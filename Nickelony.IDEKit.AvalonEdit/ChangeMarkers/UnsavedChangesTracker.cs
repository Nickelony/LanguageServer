using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Documents;
using Nickelony.IDEKit.Core.Diffing;
using Nickelony.IDEKit.Core.LineStatus;
using Nickelony.IDEKit.Core.Notifications;
using Nickelony.IDEKit.Core.Text;
using System.Collections;

namespace Nickelony.IDEKit.AvalonEdit.ChangeMarkers;

/// <summary>
/// Provides the line numbers of document lines inserted or modified relative to a recorded baseline.
/// </summary>
/// <remarks>
/// <para>
/// Until <see cref="SetBaseline"/> is called, the baseline is empty, so every line present in the
/// current document is reported as changed. Line terminators are normalized before comparison
/// (CRLF, LF, and lone CR compare equal). Marked lines are computed on demand against a line view that is
/// maintained incrementally from the document's change notifications, so the view update works on
/// the affected line window instead of replacing the whole view.
/// </para>
/// <para>
/// Content that ends with a line terminator includes its trailing empty line, matching the editor's
/// line model, so adding a final terminator marks that line.
/// Deletion-only edits, including removing a final terminator, produce no marked line because only
/// lines present in the current document are returned.
/// </para>
/// <para>
/// <see cref="SetBaseline"/> may be called from any thread: it replaces the baseline, bumps an internal
/// revision, and raises <see cref="Changed"/> on the calling thread. <see cref="GetMarkedLineNumbers"/> reads
/// the document and must be called on the document's owner thread, which is also where the change
/// subscription runs. A baseline replaced while <see cref="GetMarkedLineNumbers"/> is running is picked up by
/// the next call instead of being served from the cache.
/// </para>
/// <para>
/// The tracker subscribes to the document returned by its provider and reattaches when the provider
/// returns a different document; until <see cref="Dispose"/> is called, that subscription keeps the
/// tracker reachable from the document. The tracker and its document are typically editor-scoped
/// together, so disposal is optional and only needed when a tracker is abandoned before its document.
/// </para>
/// </remarks>
public sealed class UnsavedChangesTracker : ILineStatusSource, IChangeNotificationSource, IDisposable
{
	private readonly Func<TextDocument> _documentProvider;

	private string[] _baselineLines = [];

	private long _revision;

	private readonly DocumentVersionCache<IReadOnlyList<int>> _markedLinesCache = new();

	// The incrementally maintained line view of the tracked document; only touched on the document's
	// owner thread, which is also the thread the change notifications arrive on. The view is a gap
	// buffer positioned at the last edited line window, so a replaced window never copies the lines
	// after it.
	private TextDocument? _trackedDocument;
	private ITextSourceVersion? _syncedVersion;
	private readonly LineGapBuffer _documentLines = new();

	private bool _disposed;

	/// <summary>
	/// Raised after <see cref="SetBaseline"/> replaces the comparison baseline, and after every document
	/// change, because either can change the marked lines.
	/// </summary>
	/// <remarks>
	/// <para>
	/// An attached <see cref="ChangeMarkerMargin"/> invalidates itself when the event arrives, so a
	/// connected margin repaints after edits without the host invalidating it.
	/// </para>
	/// <para>
	/// The event is raised on the thread that caused it: the calling thread for <see cref="SetBaseline"/>
	/// and the document's owner thread for edits. An attached <see cref="ChangeMarkerMargin"/> marshals
	/// to its dispatcher; a host subscriber that touches the editor must marshal that work itself.
	/// </para>
	/// </remarks>
	public event EventHandler? Changed;

	/// <summary>
	/// Initializes a new instance of the <see cref="UnsavedChangesTracker"/> class.
	/// </summary>
	/// <param name="documentProvider">Provides the document whose lines are tracked.</param>
	/// <exception cref="ArgumentNullException"><paramref name="documentProvider"/> is <see langword="null"/>.</exception>
	public UnsavedChangesTracker(Func<TextDocument> documentProvider)
	{
		ArgumentNullException.ThrowIfNull(documentProvider);
		_documentProvider = documentProvider;
	}

	/// <summary>
	/// Replaces the comparison baseline with <paramref name="content"/>.
	/// </summary>
	/// <param name="content">The content to compare with the current document.</param>
	/// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">The instance has been disposed.</exception>
	public void SetBaseline(string content)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		ArgumentNullException.ThrowIfNull(content);

		// The baseline write precedes the revision bump, so a reader that captures the new revision
		// is guaranteed to observe the new baseline.
		_baselineLines = TextLineSplitter.Split(content);
		Interlocked.Increment(ref _revision);
		_markedLinesCache.Invalidate();

		Changed?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>
	/// Gets the current document line numbers inserted or modified relative to the baseline.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Only lines present in the current document are returned, so deletion-only changes produce
	/// no marked line, and an empty baseline marks every line. The returned numbers are one-based,
	/// ascending, and refer to the current document.
	/// </para>
	/// <para>
	/// A result is reused while the current document and its version are unchanged.
	/// Replacing the baseline clears the cache.
	/// </para>
	/// <para>
	/// A document provider that returns <see langword="null"/> yields an empty list, and a disposed
	/// tracker yields an empty list too: this read is called from a margin's render pass, so it must not
	/// throw.
	/// </para>
	/// </remarks>
	public IReadOnlyList<int> GetMarkedLineNumbers()
	{
		// The read runs inside a margin's render pass, so disposal must not turn it into a dispatcher
		// exception; a disposed tracker simply marks nothing.
		if (_disposed)
			return [];

		TextDocument? document = _documentProvider();

		if (document is null)
			return [];

		// The revision is captured before the cache decides, and stored with the result, so a baseline
		// replaced from another thread while the value is computed cannot be served from the cache.
		return _markedLinesCache.GetOrCreate(document, CollectMarkedLineNumbers, Volatile.Read(ref _revision));
	}

	/// <summary>
	/// Detaches the tracker from its document and drops the cached lines and result.
	/// </summary>
	/// <remarks>
	/// Disposal is optional: the editor typically owns the tracker and its document together. Call it to
	/// release an earlier-abandoned tracker from the document's change subscription.
	/// <see cref="SetBaseline"/> throws <see cref="ObjectDisposedException"/> after disposal, while
	/// <see cref="GetMarkedLineNumbers"/> keeps returning an empty list so an attached margin's render
	/// pass cannot fail. Repeated calls have no effect.
	/// </remarks>
	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;

		if (_trackedDocument is not null)
		{
			_trackedDocument.Changed -= Document_Changed;
			_trackedDocument = null;
		}

		_documentLines.Clear();
		_markedLinesCache.Invalidate();
	}

	private IReadOnlyList<int> CollectMarkedLineNumbers(TextDocument document)
	{
		LineGapBuffer documentLines = EnsureDocumentLines(document);
		var changedLineNumbers = new List<int>();

		foreach (int lineNumber in LineDiffer.GetChangedLineNumbers(_baselineLines, documentLines).ChangedLineNumbers)
		{
			if (lineNumber >= 1 && lineNumber <= document.LineCount)
				changedLineNumbers.Add(lineNumber);
		}

		changedLineNumbers.Sort();

		return changedLineNumbers.AsReadOnly();
	}

	/// <summary>
	/// Gets the incrementally maintained line view of <paramref name="document"/>, attaching to the
	/// document and rebuilding the view when the subscription is new or a version difference shows that
	/// changes were not observed.
	/// </summary>
	private LineGapBuffer EnsureDocumentLines(TextDocument document)
	{
		// The change subscription attaches lazily on the first read, and a version difference that
		// shows unobserved changes (for example edits made before the subscription existed) rebuilds
		// the view defensively.
		if (!ReferenceEquals(_trackedDocument, document))
		{
			if (_trackedDocument is not null)
				_trackedDocument.Changed -= Document_Changed;

			_trackedDocument = document;
			RebuildDocumentLines(document);
			document.Changed += Document_Changed;

			return _documentLines;
		}

		if (!ReferenceEquals(_syncedVersion, document.Version))
		{
			// The version changed without a change notification (for example because the subscription was
			// attached after edits), so the defensive path rebuilds the whole view.
			RebuildDocumentLines(document);
		}

		return _documentLines;
	}

	private void RebuildDocumentLines(TextDocument document)
	{
		_documentLines.Clear();
		_documentLines.AddRange(TextLineSplitter.Split(document.Text));
		_syncedVersion = document.Version;
	}

	private void Document_Changed(object? sender, DocumentChangeEventArgs e)
	{
		if (sender is not TextDocument document || !ReferenceEquals(document, _trackedDocument))
			return;

		UpdateDocumentLines(document, e);
		_syncedVersion = document.Version;

		// Edits can change the marked lines, so subscribers invalidate or repaint as well.
		Changed?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>
	/// Replaces the lines affected by one change with the lines that change produced.
	/// </summary>
	/// <remarks>
	/// The replaced window is located by line structure, not by counting line breaks in the changed
	/// fragments (a fragment that holds only half of a CRLF pair is not a complete line break): the
	/// first affected line is the line of the new document that contains the change's offset, which is
	/// the corresponding old line because all text before the offset is shared, and the last one is
	/// the line that contains the position where the inserted text ends. The matching old window ends
	/// at the same line index plus the line-count difference of the change, because the unchanged
	/// lines after the window number the same in both versions.
	/// </remarks>
	private void UpdateDocumentLines(TextDocument document, DocumentChangeEventArgs e)
	{
		int startIndex = document.GetLineByOffset(e.Offset).LineNumber - 1;
		int newEndIndex = document.GetLineByOffset(e.Offset + e.InsertionLength).LineNumber - 1;
		int oldEndIndex = newEndIndex + (_documentLines.Count - document.LineCount);
		int removedLineCount = Math.Max(0, oldEndIndex - startIndex + 1);
		var replacementLines = new List<string>(newEndIndex - startIndex + 1);

		for (int lineIndex = startIndex; lineIndex <= newEndIndex; lineIndex++)
			replacementLines.Add(document.GetText(document.GetLineByNumber(lineIndex + 1)));

		_documentLines.ReplaceRange(startIndex, removedLineCount, replacementLines);
	}

	/// <summary>
	/// Stores the tracked document lines as two lists separated by a gap that follows the most recent
	/// edit, so replacing the affected line window costs the window size instead of copying every line
	/// after it. Reads are indexed like a plain list; moving the gap to an edit position costs the
	/// distance to that position, which typing in one place amortizes to nothing.
	/// </summary>
	private sealed class LineGapBuffer : IReadOnlyList<string>
	{
		// Lines before the gap in document order, and lines after the gap in reverse document order,
		// so the line right after the gap is the last element of the after-gap list.
		private readonly List<string> _beforeGap = [];
		private readonly List<string> _afterGap = [];

		public int Count => _beforeGap.Count + _afterGap.Count;

		public string this[int index]
			=> index < _beforeGap.Count
				? _beforeGap[index]
				: _afterGap[_afterGap.Count - 1 - (index - _beforeGap.Count)];

		/// <summary>
		/// Replaces the lines in the window that starts at <paramref name="startIndex"/> and spans
		/// <paramref name="removeCount"/> lines with <paramref name="replacementLines"/>.
		/// </summary>
		public void ReplaceRange(int startIndex, int removeCount, List<string> replacementLines)
		{
			MoveGapTo(startIndex);

			// After the move, the first line after the gap is the last element of the after-gap list.
			for (int index = 0; index < removeCount; index++)
				_afterGap.RemoveAt(_afterGap.Count - 1);

			_beforeGap.AddRange(replacementLines);
		}

		public void Clear()
		{
			_beforeGap.Clear();
			_afterGap.Clear();
		}

		public void AddRange(IEnumerable<string> lines)
			=> _beforeGap.AddRange(lines);

		private void MoveGapTo(int lineIndex)
		{
			while (_beforeGap.Count > lineIndex)
			{
				string line = _beforeGap[^1];

				_beforeGap.RemoveAt(_beforeGap.Count - 1);
				_afterGap.Add(line);
			}

			while (_beforeGap.Count < lineIndex)
			{
				string line = _afterGap[^1];

				_afterGap.RemoveAt(_afterGap.Count - 1);
				_beforeGap.Add(line);
			}
		}

		public IEnumerator<string> GetEnumerator()
		{
			foreach (string line in _beforeGap)
				yield return line;

			for (int index = _afterGap.Count - 1; index >= 0; index--)
				yield return _afterGap[index];
		}

		IEnumerator IEnumerable.GetEnumerator()
			=> GetEnumerator();
	}
}

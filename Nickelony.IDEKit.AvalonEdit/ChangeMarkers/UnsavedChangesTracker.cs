using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.Core.Diffing;

namespace Nickelony.IDEKit.AvalonEdit.ChangeMarkers;

/// <summary>
/// Provides document lines inserted or modified relative to a recorded baseline.
/// </summary>
/// <remarks>
/// Until <see cref="SetBaseline"/> is called, the baseline is empty.
/// Line endings are normalized before comparison. Changes are computed on demand.
/// This type does not subscribe to document changes or request view redraws.
/// </remarks>
public sealed class UnsavedChangesTracker : IChangeMarkerSource
{
	private readonly Func<TextDocument> _documentProvider;
	private readonly Action? _onChanged;

	private string[] _baselineLines = [];
	private IReadOnlyList<DocumentLine>? _cachedLines;
	private string? _cachedText;

	/// <summary>
	/// Initializes a tracker for the document returned by <paramref name="documentProvider"/>.
	/// </summary>
	/// <param name="documentProvider">Provides the document whose lines are tracked.</param>
	/// <param name="onChanged">
	/// Invoked synchronously after <see cref="SetBaseline"/> replaces the comparison baseline and invalidates the cached result.
	/// Passing <see langword="null"/> disables the callback.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="documentProvider"/> is <see langword="null"/>.</exception>
	public UnsavedChangesTracker(Func<TextDocument> documentProvider, Action? onChanged = null)
	{
		ArgumentNullException.ThrowIfNull(documentProvider);

		_documentProvider = documentProvider;
		_onChanged = onChanged;
	}

	/// <summary>
	/// Replaces the comparison baseline with <paramref name="content"/>.
	/// </summary>
	/// <param name="content">The content to compare with the current document.</param>
	/// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
	public void SetBaseline(string content)
	{
		ArgumentNullException.ThrowIfNull(content);

		_baselineLines = SplitIntoLines(content);

		_cachedLines = null;
		_cachedText = null;

		_onChanged?.Invoke();
	}

	/// <summary>
	/// Gets the current document lines inserted or modified relative to the baseline.
	/// </summary>
	/// <remarks>
	/// Only lines present in the current document are returned.
	/// Deletion-only changes therefore produce no marked line.
	/// A result is reused when the current document text matches the cached text.
	/// Replacing the baseline clears the cache.
	/// </remarks>
	public IReadOnlyList<DocumentLine> GetMarkedLines()
	{
		TextDocument document = _documentProvider();
		ArgumentNullException.ThrowIfNull(document);

		string currentText = document.Text;

		if (_cachedLines is not null && string.Equals(_cachedText, currentText, StringComparison.Ordinal))
			return _cachedLines;

		var changedLines = new List<DocumentLine>();

		foreach (int lineNumber in LineDiffer.GetChangedLines(_baselineLines, SplitIntoLines(currentText)))
		{
			if (lineNumber >= 1 && lineNumber <= document.LineCount)
				changedLines.Add(document.GetLineByNumber(lineNumber));
		}

		changedLines.Sort((left, right) => left.LineNumber.CompareTo(right.LineNumber));

		_cachedLines = changedLines;
		_cachedText = currentText;

		return changedLines;
	}

	private static string[] SplitIntoLines(string content)
	{
		if (content.Length == 0)
			return [];

		string normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');

		if (normalized.EndsWith('\n'))
			normalized = normalized[..^1];

		return normalized.Split('\n');
	}
}

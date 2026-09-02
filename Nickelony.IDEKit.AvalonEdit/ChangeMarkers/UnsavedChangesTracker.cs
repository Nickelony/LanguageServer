using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.Core.Diffing;

namespace Nickelony.IDEKit.AvalonEdit.ChangeMarkers;

/// <summary>
/// An <see cref="IChangeMarkerSource"/> that marks the lines whose content differs from a recorded
/// baseline, identifying the lines with unsaved changes after the document is edited.
/// </summary>
/// <remarks>
/// Before <see cref="SetBaseline"/> is called, the current document is compared with an empty
/// baseline. The tracker computes changes when <see cref="GetMarkedLines"/> is called; it does not
/// subscribe to document changes or request view redraws.
/// </remarks>
public sealed class UnsavedChangesTracker : IChangeMarkerSource
{
	private readonly Func<TextDocument> _documentProvider;
	private readonly Action? _onChanged;

	private string[] _baselineLines = [];
	private IReadOnlyList<DocumentLine>? _cachedLines;
	private string? _cachedText;

	/// <summary>
	/// Initializes a new instance of the <see cref="UnsavedChangesTracker"/> class.
	/// </summary>
	/// <param name="documentProvider">Provides the document whose lines are tracked.</param>
	/// <param name="onChanged">The callback invoked when the baseline is replaced, or <see langword="null"/> for none.</param>
	public UnsavedChangesTracker(Func<TextDocument> documentProvider, Action? onChanged = null)
	{
		_documentProvider = documentProvider;
		_onChanged = onChanged;
	}

	/// <summary>
	/// Records the supplied content as the saved baseline that later edits are compared against.
	/// </summary>
	/// <param name="content">The content as persisted.</param>
	public void SetBaseline(string content)
	{
		_baselineLines = SplitIntoLines(content);

		_cachedLines = null;
		_cachedText = null;

		_onChanged?.Invoke();
	}

	/// <summary>
	/// Gets the lines with unsaved changes, computed by diffing the current document against the
	/// baseline. The result is cached until the document text or the baseline changes.
	/// </summary>
	public IReadOnlyList<DocumentLine> GetMarkedLines()
	{
		TextDocument document = _documentProvider();
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

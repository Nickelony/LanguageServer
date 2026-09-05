using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Provides a precomputed table of line start offsets and lengths for document content.
/// LF, CRLF, and lone CR are treated as line breaks.
/// </summary>
public sealed class TextLineMap
{
	private readonly ITextSnapshot _snapshot;
	private readonly int[] _lineStartOffsets;
	private readonly int[] _lineLengths;

	private TextLineMap(ITextSnapshot snapshot, int[] lineStartOffsets, int[] lineLengths)
	{
		_snapshot = snapshot;
		_lineStartOffsets = lineStartOffsets;
		_lineLengths = lineLengths;
	}

	/// <summary>
	/// Gets the number of logical lines in the document.
	/// </summary>
	public int LineCount => _lineLengths.Length;

	/// <summary>
	/// Gets the total document length in UTF-16 code units.
	/// </summary>
	public int TextLength => _snapshot.TextLength;

	/// <summary>
	/// Gets the length of the specified zero-based line. The line index is clamped to the available
	/// line range.
	/// </summary>
	/// <param name="lineIndex">The zero-based line index.</param>
	/// <returns>The line length in UTF-16 code units.</returns>
	public int GetLineLength(int lineIndex) => _lineLengths[ClampLineIndex(lineIndex)];

	/// <summary>
	/// Gets the absolute document offset where the specified zero-based line starts. The line index
	/// is clamped to the available line range.
	/// </summary>
	/// <param name="lineIndex">The zero-based line index.</param>
	/// <returns>The absolute document offset.</returns>
	public int GetLineStartOffset(int lineIndex) => _lineStartOffsets[ClampLineIndex(lineIndex)];

	/// <summary>
	/// Returns the document offset for the supplied zero-based line and character indices,
	/// clamping the character index to the line length.
	/// </summary>
	/// <param name="lineIndex">The zero-based line index.</param>
	/// <param name="character">The zero-based character index within the line.</param>
	/// <returns>The absolute document offset.</returns>
	public int GetOffset(int lineIndex, int character)
	{
		int safeLine = ClampLineIndex(lineIndex);
		int safeCharacter = Math.Clamp(character, 0, _lineLengths[safeLine]);
		return _lineStartOffsets[safeLine] + safeCharacter;
	}

	/// <summary>
	/// Gets the text of the specified zero-based line without its trailing newline sequence. The line
	/// index is clamped to the available line range.
	/// </summary>
	/// <param name="lineIndex">The zero-based line index.</param>
	/// <returns>The line text.</returns>
	public string GetLineText(int lineIndex)
	{
		int safeLine = ClampLineIndex(lineIndex);
		return _snapshot.GetText(_lineStartOffsets[safeLine], _lineLengths[safeLine]);
	}

	/// <summary>
	/// Converts an offset into a zero-based line and character position. The offset is clamped to
	/// the document bounds.
	/// </summary>
	/// <param name="offset">The zero-based UTF-16 document offset.</param>
	/// <returns>The containing line and character position.</returns>
	public (int Line, int Character) GetPosition(int offset)
	{
		int safeOffset = Math.Clamp(offset, 0, TextLength);
		int low = 0;
		int high = LineCount - 1;

		while (low < high)
		{
			int middle = (low + high + 1) >>> 1;

			if (_lineStartOffsets[middle] <= safeOffset)
				low = middle;
			else
				high = middle - 1;
		}

		return (low, safeOffset - _lineStartOffsets[low]);
	}

	private int ClampLineIndex(int lineIndex) => Math.Clamp(lineIndex, 0, _lineLengths.Length - 1);

	/// <summary>
	/// Builds a line map from the supplied content using Core snapshot line metadata.
	/// </summary>
	/// <param name="content">The document text to analyze. A <see langword="null"/> value is treated as empty.</param>
	/// <returns>A line map for the supplied content.</returns>
	public static TextLineMap Build(string? content)
	{
		StringTextSnapshot snapshot = new(content);
		var startOffsets = new List<int>(snapshot.LineCount);
		var lengths = new List<int>(snapshot.LineCount);

		foreach (ITextLine line in snapshot.Lines)
		{
			startOffsets.Add(line.Offset);
			lengths.Add(line.Length);
		}

		return new(snapshot, [.. startOffsets], [.. lengths]);
	}
}

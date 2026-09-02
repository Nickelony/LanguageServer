namespace Nickelony.IDEKit.Core.Text;

/// <summary>
/// An <see cref="ITextSnapshot"/> backed by an immutable string.
/// Line metadata is precomputed at construction time and recognizes LF, CRLF, and CR line endings.
/// </summary>
public sealed class StringTextSnapshot : ITextSnapshot
{
	private readonly string _text;
	private readonly StringTextLine[] _lines;
	private readonly int[] _lineStartOffsets;

	/// <summary>
	/// Initializes a new instance of the <see cref="StringTextSnapshot"/> class.
	/// </summary>
	/// <param name="text">The source text. A <see langword="null"/> value is treated as an empty string.</param>
	/// <param name="fileName">An optional file name associated with this text.</param>
	public StringTextSnapshot(string? text, string? fileName = null)
	{
		_text = text ?? string.Empty;

		FileName = fileName;

		(_lines, _lineStartOffsets) = BuildLines(_text);
	}

	/// <inheritdoc/>
	public string? FileName { get; }

	/// <inheritdoc/>
	public int TextLength => _text.Length;

	/// <inheritdoc/>
	public int LineCount => _lines.Length;

	/// <inheritdoc/>
	public IEnumerable<ITextLine> Lines => _lines;

	/// <inheritdoc/>
	public char GetCharAt(int offset)
	{
		if (offset < 0 || offset >= _text.Length)
			throw new ArgumentOutOfRangeException(nameof(offset));

		return _text[offset];
	}

	/// <inheritdoc/>
	public string GetText(int offset, int length)
	{
		if (offset < 0 || offset > _text.Length || length < 0 || length > _text.Length - offset)
			throw new ArgumentOutOfRangeException(nameof(offset));

		return _text.Substring(offset, length);
	}

	/// <inheritdoc/>
	public ITextLine GetLineByOffset(int offset)
	{
		if (offset < 0 || offset > _text.Length)
			throw new ArgumentOutOfRangeException(nameof(offset));

		if (offset == _text.Length && _lines.Length > 0)
			return _lines[_lines.Length - 1];

		int low = 0;
		int high = _lineStartOffsets.Length - 1;

		while (low <= high)
		{
			int middle = (low + high) / 2;
			int lineStart = _lineStartOffsets[middle];

			int nextLineStart = middle + 1 < _lineStartOffsets.Length
				? _lineStartOffsets[middle + 1]
				: _text.Length;

			if (offset < lineStart)
				high = middle - 1;
			else if (offset >= nextLineStart)
				low = middle + 1;
			else
				return _lines[middle];
		}

		throw new ArgumentOutOfRangeException(nameof(offset));
	}

	/// <inheritdoc/>
	public ITextLine GetLineByNumber(int lineNumber)
	{
		if (lineNumber < 1 || lineNumber > _lines.Length)
			throw new ArgumentOutOfRangeException(nameof(lineNumber));

		return _lines[lineNumber - 1];
	}

	private static (StringTextLine[] lines, int[] lineStartOffsets) BuildLines(string text)
	{
		if (text.Length == 0)
			return ([new StringTextLine(0, 0, 1)], [0]);

		var lines = new List<StringTextLine>();
		var lineStartOffsets = new List<int>();
		int lineStart = 0;
		int lineNumber = 1;
		int index = 0;

		while (index < text.Length)
		{
			char character = text[index];

			if (character == '\r')
			{
				int delimiterLength = index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;

				lines.Add(new StringTextLine(lineStart, index - lineStart, lineNumber));
				lineStartOffsets.Add(lineStart);

				lineNumber++;
				index += delimiterLength;

				lineStart = index;
			}
			else if (character == '\n')
			{
				lines.Add(new StringTextLine(lineStart, index - lineStart, lineNumber));
				lineStartOffsets.Add(lineStart);

				lineNumber++;
				index++;

				lineStart = index;
			}
			else
			{
				index++;
			}
		}

		lines.Add(new StringTextLine(lineStart, text.Length - lineStart, lineNumber));
		lineStartOffsets.Add(lineStart);

		return (lines.ToArray(), lineStartOffsets.ToArray());
	}

	private sealed class StringTextLine : ITextLine
	{
		public StringTextLine(int offset, int length, int lineNumber)
		{
			Offset = offset;
			Length = length;
			EndOffset = offset + length;
			LineNumber = lineNumber;
		}

		public int Offset { get; }

		public int Length { get; }

		public int EndOffset { get; }

		public int LineNumber { get; }
	}
}

namespace Nickelony.IDEKit.Core.Text;

/// <summary>
/// Represents an immutable snapshot of text used by editor and language-tooling services.
/// Offsets are zero-based UTF-16 positions and line numbers are one-based.
/// Implementations capture their content at construction time.
/// </summary>
public interface ITextSnapshot
{
	/// <summary>
	/// Gets the optional file name associated with this text source.
	/// </summary>
	string? FileName { get; }

	/// <summary>
	/// Gets the total number of UTF-16 code units in the text.
	/// </summary>
	int TextLength { get; }

	/// <summary>
	/// Gets the total number of lines in the text.
	/// </summary>
	int LineCount { get; }

	/// <summary>
	/// Gets the character at the specified zero-based UTF-16 offset.
	/// </summary>
	/// <param name="offset">The zero-based UTF-16 offset of the character to retrieve.</param>
	/// <returns>The character at the specified offset.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="offset"/> is negative or not less than <see cref="TextLength"/>.
	/// </exception>
	char GetCharAt(int offset);

	/// <summary>
	/// Retrieves the text within the specified zero-based UTF-16 range.
	/// </summary>
	/// <param name="offset">The zero-based start offset of the text to retrieve.</param>
	/// <param name="length">The number of UTF-16 code units to retrieve.</param>
	/// <returns>The text in the specified range.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The requested range is negative or extends beyond <see cref="TextLength"/>.
	/// </exception>
	string GetText(int offset, int length);

	/// <summary>
	/// Gets the line that contains the specified zero-based UTF-16 offset.
	/// </summary>
	/// <param name="offset">The zero-based UTF-16 offset to locate.</param>
	/// <returns>The line containing the specified offset.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="offset"/> is negative or greater than <see cref="TextLength"/>.
	/// </exception>
	ITextLine GetLineByOffset(int offset);

	/// <summary>
	/// Gets the line with the specified one-based line number.
	/// </summary>
	/// <param name="lineNumber">The one-based line number to retrieve.</param>
	/// <returns>The line at the specified line number.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="lineNumber"/> is less than 1 or greater than <see cref="LineCount"/>.
	/// </exception>
	ITextLine GetLineByNumber(int lineNumber);

	/// <summary>
	/// Enumerates all lines in the text, in order.
	/// </summary>
	IEnumerable<ITextLine> Lines { get; }
}

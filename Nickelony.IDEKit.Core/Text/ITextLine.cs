namespace Nickelony.IDEKit.Core.Text;

/// <summary>
/// Represents a single line within an <see cref="ITextSnapshot"/>.
/// Offsets are zero-based. Line numbers are one-based. <see cref="Length"/>
/// and <see cref="EndOffset"/> exclude line terminators.
/// </summary>
public interface ITextLine
{
	/// <summary>
	/// Gets the zero-based offset of the first character of this line within the source text.
	/// </summary>
	int Offset { get; }

	/// <summary>
	/// Gets the length of the line text, excluding any line terminator.
	/// </summary>
	int Length { get; }

	/// <summary>
	/// Gets the zero-based offset of the first character after this line's text.
	/// </summary>
	int EndOffset { get; }

	/// <summary>
	/// Gets the one-based line number of this line within the source text.
	/// </summary>
	int LineNumber { get; }
}

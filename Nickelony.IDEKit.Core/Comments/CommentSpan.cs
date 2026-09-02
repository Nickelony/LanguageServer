using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Comments;

/// <summary>
/// Describes the location of a comment found while scanning text.
/// </summary>
public readonly struct CommentSpan
{
	/// <summary>
	/// Gets the offset at which the comment span begins. For a line comment this
	/// includes any whitespace immediately preceding the delimiter (the span that
	/// removal and masking operate on); for a block comment it equals
	/// <see cref="DelimiterStart"/> because block comments do not consume preceding
	/// whitespace.
	/// </summary>
	public int Start { get; }

	/// <summary>
	/// Gets the offset of the comment delimiter itself (the opener), which is always
	/// at or after <see cref="Start"/>.
	/// </summary>
	public int DelimiterStart { get; }

	/// <summary>
	/// Gets the offset one past the end of the comment: the offset of the next LF line break
	/// for a line comment (or the end of the text), or the offset just after the
	/// closer for a block comment (or the end of the text when the comment is unclosed).
	/// </summary>
	public int End { get; }

	/// <summary>
	/// Gets a value indicating whether this is a line comment, as opposed to a block comment.
	/// </summary>
	public bool IsLineComment { get; }

	/// <summary>
	/// Gets a value indicating whether this is a block comment, as opposed to a line comment.
	/// </summary>
	public bool IsBlockComment => !IsLineComment;

	/// <summary>
	/// Gets the length of the comment span in UTF-16 code units.
	/// </summary>
	public int Length => End - Start;

	/// <summary>
	/// Initializes a new instance of the <see cref="CommentSpan"/> struct.
	/// </summary>
	/// <param name="start">The offset at which the reported span begins.</param>
	/// <param name="delimiterStart">The offset of the comment opener.</param>
	/// <param name="end">The offset one past the end of the comment.</param>
	/// <param name="isLineComment">Whether the comment is a line comment.</param>
	public CommentSpan(int start, int delimiterStart, int end, bool isLineComment)
	{
		Start = start;
		DelimiterStart = delimiterStart;
		End = end;
		IsLineComment = isLineComment;
	}

	/// <summary>
	/// Converts the span to a <see cref="TextRange"/> covering the reported comment span.
	/// </summary>
	/// <returns>A <see cref="TextRange"/> from <see cref="Start"/> with length <see cref="Length"/>.</returns>
	public TextRange ToTextRange()
		=> new(Start, Length);
}

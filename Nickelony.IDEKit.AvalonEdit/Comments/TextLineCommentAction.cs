namespace Nickelony.IDEKit.AvalonEdit.Comments;

/// <summary>
/// Specifies the line-comment operation applied to lines touched by a selection.
/// </summary>
public enum TextLineCommentAction
{
	/// <summary>
	/// Adds the line-comment delimiter to each non-whitespace selected line.
	/// </summary>
	Comment,

	/// <summary>
	/// Removes the line-comment delimiter from each selected line that has one.
	/// </summary>
	Uncomment,

	/// <summary>
	/// Uncomments when at least one non-whitespace selected line exists and all such lines have the delimiter;
	/// otherwise, comments the selected lines.
	/// </summary>
	Toggle
}

namespace Nickelony.IDEKit.AvalonEdit.Comments;

/// <summary>
/// Describes the line-comment transformation applied by <see cref="TextLineCommentService"/>.
/// </summary>
public enum TextLineCommentAction
{
	/// <summary>Comments the selected lines.</summary>
	Comment,

	/// <summary>Uncomments the selected lines.</summary>
	Uncomment,

	/// <summary>Comments or uncomments the selected lines depending on their current state.</summary>
	Toggle
}

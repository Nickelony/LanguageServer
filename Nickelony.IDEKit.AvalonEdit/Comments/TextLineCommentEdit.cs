namespace Nickelony.IDEKit.AvalonEdit.Comments;

/// <summary>
/// Describes a line-comment edit and the selection to apply after it.
/// </summary>
/// <remarks>Offsets are zero-based. Offsets and lengths are measured in UTF-16 code units.</remarks>
/// <param name="ReplaceOffset">The zero-based start offset of the range to replace.</param>
/// <param name="ReplaceLength">The length of the range to replace, in UTF-16 code units.</param>
/// <param name="ReplacementText">The text that replaces the range.</param>
/// <param name="SelectionStart">The post-edit selection's zero-based start offset.</param>
/// <param name="SelectionLength">The post-edit selection length, in UTF-16 code units.</param>
public readonly record struct TextLineCommentEdit(
	int ReplaceOffset,
	int ReplaceLength,
	string ReplacementText,
	int SelectionStart,
	int SelectionLength);

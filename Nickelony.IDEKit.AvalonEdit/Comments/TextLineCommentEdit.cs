namespace Nickelony.IDEKit.AvalonEdit.Comments;

/// <summary>
/// Describes a line-comment transformation as a replaceable document range.
/// </summary>
/// <remarks>Offsets and lengths are measured in UTF-16 code units.</remarks>
/// <param name="ReplaceOffset">The zero-based inclusive start offset of the replaced range.</param>
/// <param name="ReplaceLength">The length of the replaced range in UTF-16 code units.</param>
/// <param name="ReplacementText">The replacement text.</param>
/// <param name="SelectionStart">The zero-based inclusive start offset of the selection after the edit.</param>
/// <param name="SelectionLength">The selection length in UTF-16 code units after the edit.</param>
public readonly record struct TextLineCommentEdit(
	int ReplaceOffset,
	int ReplaceLength,
	string ReplacementText,
	int SelectionStart,
	int SelectionLength);

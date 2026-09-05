namespace Nickelony.IDEKit.Core.Text;

/// <summary>
/// Describes one text replacement using zero-based UTF-16 offsets.
/// </summary>
/// <param name="StartOffset">The zero-based inclusive source offset.</param>
/// <param name="EndOffset">The zero-based exclusive source offset.</param>
/// <param name="NewText">The replacement text.</param>
/// <param name="SourceIndex">The source index used for deterministic ordering and diagnostics.</param>
public readonly record struct TextEditOperation(
	int StartOffset,
	int EndOffset,
	string NewText,
	int SourceIndex)
{
	/// <summary>
	/// Gets the number of source UTF-16 code units replaced by the operation.
	/// </summary>
	public int Length => EndOffset - StartOffset;
}

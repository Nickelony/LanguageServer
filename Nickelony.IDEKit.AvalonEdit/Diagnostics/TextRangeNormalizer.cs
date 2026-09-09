using ICSharpCode.AvalonEdit.Document;

namespace Nickelony.IDEKit.AvalonEdit.Diagnostics;

/// <summary>
/// Normalizes document offset ranges for the diagnostic renderer.
/// </summary>
internal static class TextRangeNormalizer
{
	/// <summary>
	/// Tries to normalize an offset range into a non-empty range within the document's character range.
	/// </summary>
	/// <remarks>
	/// For a non-empty document, empty or reversed ranges become a range of length <c>1</c> that starts
	/// at the clamped start offset: the start is clamped to the last character, and an end offset at or
	/// before the start is raised to one character later. An empty document produces no range. Callers
	/// that filter before drawing (for example a render pass that discards invisible ranges) normalize
	/// through this method so the filter uses the offsets the underline is drawn for.
	/// </remarks>
	/// <param name="document">The document whose character range constrains the offsets.</param>
	/// <param name="startOffset">The zero-based inclusive start offset before clamping.</param>
	/// <param name="endOffset">The zero-based exclusive end offset before clamping.</param>
	/// <param name="normalizedStartOffset">The clamped zero-based inclusive start offset.</param>
	/// <param name="normalizedEndOffset">The clamped zero-based exclusive end offset.</param>
	/// <returns>
	/// <see langword="true"/> when a range is produced;
	/// <see langword="false"/> when the document is empty.
	/// </returns>
	public static bool TryNormalizeRange(
		TextDocument document,
		int startOffset,
		int endOffset,
		out int normalizedStartOffset,
		out int normalizedEndOffset)
	{
		ArgumentNullException.ThrowIfNull(document);

		normalizedStartOffset = 0;
		normalizedEndOffset = 0;

		if (document.TextLength == 0)
			return false;

		normalizedStartOffset = Math.Max(0, Math.Min(startOffset, document.TextLength - 1));
		normalizedEndOffset = Math.Max(normalizedStartOffset + 1, Math.Min(endOffset, document.TextLength));

		return true;
	}
}

using System.Diagnostics.CodeAnalysis;
using ICSharpCode.AvalonEdit.Document;

namespace Nickelony.IDEKit.AvalonEdit.Documents;

/// <summary>
/// Creates non-empty <see cref="TextSegment"/> values from offset ranges clamped to a document's bounds.
/// </summary>
/// <remarks>
/// To keep returned segments non-empty, an empty or reversed range is expanded to one character at
/// the clamped start offset.
/// </remarks>
public static class TextSegmentFactory
{
	/// <summary>
	/// Creates a segment for the supplied range, clamped to the document bounds.
	/// </summary>
	/// <param name="document">The document the segment is created for, or <see langword="null"/> for none.</param>
	/// <param name="startOffset">The zero-based inclusive start offset.</param>
	/// <param name="endOffset">The zero-based exclusive end offset.</param>
	/// <param name="segment">The created segment, when the range is non-empty.</param>
	/// <returns>
	/// <see langword="true"/> when <paramref name="document"/> is non-empty and a segment was created;
	/// otherwise, <see langword="false"/>.
	/// </returns>
	public static bool TryCreate(
		TextDocument? document,
		int startOffset,
		int endOffset,
		[NotNullWhen(true)] out TextSegment? segment)
	{
		segment = null;

		if (document is null || document.TextLength == 0)
			return false;

		int safeStartOffset = Math.Max(0, Math.Min(startOffset, document.TextLength - 1));
		int safeEndOffset = Math.Max(safeStartOffset + 1, Math.Min(endOffset, document.TextLength));

		if (safeEndOffset <= safeStartOffset)
			return false;

		segment = new TextSegment
		{
			StartOffset = safeStartOffset,
			EndOffset = safeEndOffset
		};

		return true;
	}
}

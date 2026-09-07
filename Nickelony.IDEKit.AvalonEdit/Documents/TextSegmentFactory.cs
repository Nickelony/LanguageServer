using ICSharpCode.AvalonEdit.Document;
using System.Diagnostics.CodeAnalysis;

namespace Nickelony.IDEKit.AvalonEdit.Documents;

/// <summary>
/// Provides helpers for converting document offset ranges into non-empty AvalonEdit <see cref="TextSegment"/> instances.
/// </summary>
public static class TextSegmentFactory
{
	/// <summary>
	/// Tries to create a non-empty <see cref="TextSegment"/> from the specified offsets,
	/// clamping them to the document's character range.
	/// </summary>
	/// <remarks>
	/// For a non-empty document, empty or reversed ranges become a segment of length <c>1</c> at the
	/// nearest valid character offset. An empty document produces no segment.
	/// </remarks>
	/// <param name="document">The document whose character range constrains the offsets.</param>
	/// <param name="startOffset">The zero-based inclusive start offset before clamping.</param>
	/// <param name="endOffset">The zero-based exclusive end offset before clamping.</param>
	/// <param name="segment">
	/// The created segment when the method returns <see langword="true"/>; otherwise, <see langword="null"/>.
	/// </param>
	/// <returns>
	/// <see langword="true"/> when a segment is created;
	/// <see langword="false"/> when the document is empty.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
	public static bool TryCreate(
		TextDocument document,
		int startOffset,
		int endOffset,
		[NotNullWhen(true)] out TextSegment? segment)
	{
		ArgumentNullException.ThrowIfNull(document);

		segment = null;

		if (document.TextLength == 0)
			return false;

		int safeStartOffset = Math.Max(0, Math.Min(startOffset, document.TextLength - 1));
		int safeEndOffset = Math.Max(safeStartOffset + 1, Math.Min(endOffset, document.TextLength));

		segment = new TextSegment
		{
			StartOffset = safeStartOffset,
			EndOffset = safeEndOffset
		};

		return true;
	}
}

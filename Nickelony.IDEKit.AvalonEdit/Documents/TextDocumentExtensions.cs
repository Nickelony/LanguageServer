using ICSharpCode.AvalonEdit.Document;

namespace Nickelony.IDEKit.AvalonEdit.Documents;

/// <summary>
/// Extension methods for AvalonEdit <see cref="TextDocument"/> instances.
/// </summary>
public static class TextDocumentExtensions
{
	/// <summary>
	/// Clamps a zero-based document offset to the document's character range.
	/// </summary>
	/// <param name="document">
	/// The AvalonEdit <see cref="TextDocument"/> whose <see cref="TextDocument.TextLength"/> defines the upper bound.
	/// </param>
	/// <param name="offset">The zero-based offset to clamp.</param>
	/// <returns>The offset clamped to the inclusive range from <c>0</c> through <see cref="TextDocument.TextLength"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
	public static int ClampOffset(this TextDocument document, int offset)
	{
		ArgumentNullException.ThrowIfNull(document);
		return Math.Clamp(offset, 0, document.TextLength);
	}
}

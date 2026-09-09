using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Diagnostics;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

[TestClass]
public sealed class TextRangeNormalizerTests
{
	[TestMethod]
	public void TryNormalizeRange_ZeroLengthRange_PromotesToSingleCharacterRange()
	{
		var document = new TextDocument("abcdef");

		bool success = TextRangeNormalizer.TryNormalizeRange(document, 2, 2, out int startOffset, out int endOffset);

		Assert.IsTrue(success);
		Assert.AreEqual(2, startOffset);
		Assert.AreEqual(3, endOffset);
	}

	[TestMethod]
	public void TryNormalizeRange_ReversedRange_AnchorsAtClampedStart()
	{
		var document = new TextDocument("abcdef");

		bool success = TextRangeNormalizer.TryNormalizeRange(document, 4, 1, out int startOffset, out int endOffset);

		Assert.IsTrue(success);
		Assert.AreEqual(4, startOffset);
		Assert.AreEqual(5, endOffset);
	}

	[TestMethod]
	public void TryNormalizeRange_StartBeyondDocument_ClampsToLastCharacter()
	{
		var document = new TextDocument("abcdef");

		bool success = TextRangeNormalizer.TryNormalizeRange(document, 20, 30, out int startOffset, out int endOffset);

		Assert.IsTrue(success);
		Assert.AreEqual(5, startOffset);
		Assert.AreEqual(6, endOffset);
	}

	[TestMethod]
	public void TryNormalizeRange_EmptyDocument_ReturnsFalse()
	{
		var document = new TextDocument();

		// The renderer normalizes through this method before drawing; an empty
		// document has no character range to normalize into.
		Assert.IsFalse(TextRangeNormalizer.TryNormalizeRange(document, 0, 5, out _, out _));
	}

	[TestMethod]
	public void TryNormalizeRange_ZeroLengthAtDocumentEnd_PromotesToLastCharacter()
	{
		var document = new TextDocument("abcdef");

		bool success = TextRangeNormalizer.TryNormalizeRange(document, 6, 6, out int startOffset, out int endOffset);

		Assert.IsTrue(success);
		Assert.AreEqual(5, startOffset);
		Assert.AreEqual(6, endOffset);
	}

	[TestMethod]
	public void TryNormalizeRange_ReversedRangeEndingAtDocumentEnd_AnchorsAtClampedStart()
	{
		var document = new TextDocument("abcdef");

		bool success = TextRangeNormalizer.TryNormalizeRange(document, 6, 4, out int startOffset, out int endOffset);

		Assert.IsTrue(success);
		Assert.AreEqual(5, startOffset);
		Assert.AreEqual(6, endOffset);
	}
}

using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Documents;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

[TestClass]
public sealed class TextSegmentFactoryTests
{
	[TestMethod]
	public void TryCreate_WithinBounds_CreatesSegment()
	{
		var document = new TextDocument("abcdef");

		bool success = TextSegmentFactory.TryCreate(document, 1, 4, out TextSegment? segment);

		Assert.IsTrue(success);
		Assert.AreEqual(1, segment!.StartOffset);
		Assert.AreEqual(4, segment.EndOffset);
	}

	[TestMethod]
	public void TryCreate_EndBeyondDocument_ClampsToTextLength()
	{
		var document = new TextDocument("abcdef");

		bool success = TextSegmentFactory.TryCreate(document, 2, 100, out TextSegment? segment);

		Assert.IsTrue(success);
		Assert.AreEqual(2, segment!.StartOffset);
		Assert.AreEqual(6, segment.EndOffset);
	}

	[TestMethod]
	public void TryCreate_StartBeyondDocument_ClampsToLastCharacter()
	{
		var document = new TextDocument("abcdef");

		bool success = TextSegmentFactory.TryCreate(document, 20, 30, out TextSegment? segment);

		Assert.IsTrue(success);
		Assert.AreEqual(5, segment!.StartOffset);
		Assert.AreEqual(6, segment.EndOffset);
	}

	[TestMethod]
	public void TryCreate_NegativeStart_ClampsToZero()
	{
		var document = new TextDocument("abcdef");

		bool success = TextSegmentFactory.TryCreate(document, -5, 3, out TextSegment? segment);

		Assert.IsTrue(success);
		Assert.AreEqual(0, segment!.StartOffset);
		Assert.AreEqual(3, segment.EndOffset);
	}

	[TestMethod]
	public void TryCreate_ZeroLengthRange_CreatesSingleCharacterSegment()
	{
		var document = new TextDocument("abcdef");

		bool success = TextSegmentFactory.TryCreate(document, 2, 2, out TextSegment? segment);

		Assert.IsTrue(success);
		Assert.AreEqual(2, segment!.StartOffset);
		Assert.AreEqual(3, segment.EndOffset);
	}

	[TestMethod]
	public void TryCreate_EndLessThanStart_ClampsToSingleCharacterAtStart()
	{
		var document = new TextDocument("abcdef");

		bool success = TextSegmentFactory.TryCreate(document, 4, 1, out TextSegment? segment);

		Assert.IsTrue(success);
		Assert.AreEqual(4, segment!.StartOffset);
		Assert.AreEqual(5, segment.EndOffset);
	}

	[TestMethod]
	public void TryCreate_EmptyDocument_ReturnsFalse()
	{
		var document = new TextDocument();

		bool success = TextSegmentFactory.TryCreate(document, 0, 1, out TextSegment? segment);

		Assert.IsFalse(success);
		Assert.IsNull(segment);
	}
}

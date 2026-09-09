namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class TextRangeTests
{
	[TestMethod]
	public void ConstructionAndGetText_ValidateZeroBasedRange()
	{
		var range = new TextRange(1, 1);

		Assert.AreEqual(2, range.EndOffset);
		Assert.AreEqual("\u00E9", range.GetText("a\u00E9bc"));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextRange(-1, 0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => range.GetText("a"));
	}

	[TestMethod]
	public void EqualityAndFormatting_UseOffsetAndLength()
	{
		var range = new TextRange(1, 2);
		var same = new TextRange(1, 2);
		var different = new TextRange(2, 1);

		Assert.IsTrue(range == same);
		Assert.IsFalse(range != same);
		Assert.IsTrue(range != different);
		Assert.AreEqual(range, same);
		Assert.AreEqual(range.GetHashCode(), same.GetHashCode());
		Assert.AreEqual("[1..3)", range.ToString());
	}

	[TestMethod]
	public void GetText_OutOfRange_NamesSourceAndOffendingComponent()
	{
		var beyondText = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextRange(3, 0).GetText("ab"));
		var beyondRange = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextRange(1, 5).GetText("ab"));

		// The source is the argument that cannot satisfy the range, and each message identifies
		// the offending component with its value.
		Assert.AreEqual("source", beyondText.ParamName);
		Assert.AreEqual("source", beyondRange.ParamName);
		StringAssert.Contains(beyondText.Message, "offset (3)");
		StringAssert.Contains(beyondText.Message, "length 2");
		StringAssert.Contains(beyondRange.Message, "end (6)");
		StringAssert.Contains(beyondRange.Message, "length 2");
	}

	[TestMethod]
	public void Constructor_RangeEndBeyondMaximumOffset_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextRange(int.MaxValue, 1));

		// A range whose end is exactly int.MaxValue is still representable.
		var range = new TextRange(int.MaxValue - 1, 1);

		Assert.AreEqual(int.MaxValue, range.EndOffset);
	}

	[TestMethod]
	public void GetTextFromSnapshot_ReturnsTheSnapshotText()
	{
		var snapshot = new StringTextSnapshot("ab\ncd");
		var range = new TextRange(1, 3);

		Assert.AreEqual("b\nc", range.GetText(snapshot));
	}

	[TestMethod]
	public void GetTextFromSnapshot_OutOfRange_NamesSnapshotAndOffendingComponent()
	{
		var snapshot = new StringTextSnapshot("ab");
		var beyondText = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextRange(3, 0).GetText(snapshot));
		var beyondRange = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextRange(1, 5).GetText(snapshot));

		// The snapshot overload carries the same component-and-value message contract as the string
		// overload, naming the snapshot as the argument that cannot satisfy the range.
		Assert.AreEqual("snapshot", beyondText.ParamName);
		Assert.AreEqual("snapshot", beyondRange.ParamName);
		StringAssert.Contains(beyondText.Message, "offset (3)");
		StringAssert.Contains(beyondText.Message, "length 2");
		StringAssert.Contains(beyondRange.Message, "end (6)");
	}

	[TestMethod]
	public void GetTextFromSnapshot_NullSnapshot_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextRange(0, 0).GetText((ITextSnapshot)null!));
	}
}

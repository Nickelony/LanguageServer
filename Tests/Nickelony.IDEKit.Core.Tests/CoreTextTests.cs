namespace Nickelony.IDEKit.Core.Text.Tests;

[TestClass]
public sealed class CoreTextTests
{
	[TestMethod]
	public void StringTextSnapshot_RecognizesAllSupportedLineEndings()
	{
		var snapshot = new StringTextSnapshot("one\r\ntwo\nthree\rfour", "script.lua");

		Assert.AreEqual("script.lua", snapshot.FileName);
		Assert.AreEqual(4, snapshot.LineCount);
		Assert.AreEqual((0, 3, 3), GetLineShape(snapshot.GetLineByNumber(1)));
		Assert.AreEqual((5, 3, 8), GetLineShape(snapshot.GetLineByNumber(2)));
		Assert.AreEqual((9, 5, 14), GetLineShape(snapshot.GetLineByNumber(3)));
		Assert.AreEqual((15, 4, 19), GetLineShape(snapshot.GetLineByNumber(4)));
		Assert.AreEqual(4, snapshot.GetLineByOffset(snapshot.TextLength).LineNumber);
		Assert.AreEqual(19, snapshot.GetText(0, snapshot.TextLength).Length);
	}

	[TestMethod]
	public void StringTextSnapshot_EmptyAndTrailingLineHaveStableMetadata()
	{
		var emptySnapshot = new StringTextSnapshot(null);
		var trailingSnapshot = new StringTextSnapshot("one\n");

		Assert.AreEqual(1, emptySnapshot.LineCount);
		Assert.AreEqual(0, emptySnapshot.GetLineByNumber(1).Length);
		Assert.AreEqual(2, trailingSnapshot.LineCount);
		Assert.AreEqual(4, trailingSnapshot.GetLineByNumber(2).Offset);
		Assert.AreEqual(0, trailingSnapshot.GetLineByNumber(2).Length);
		Assert.AreEqual(2, trailingSnapshot.GetLineByOffset(trailingSnapshot.TextLength).LineNumber);
	}

	[TestMethod]
	public void TextRange_ValidatesAndExtractsZeroBasedRanges()
	{
		var range = new TextRange(1, 1);

		Assert.AreEqual(2, range.EndOffset);
		Assert.AreEqual("é", range.GetText("aébc"));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextRange(-1, 0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => range.GetText("a"));
	}

	private static (int Offset, int Length, int EndOffset) GetLineShape(ITextLine line)
		=> (line.Offset, line.Length, line.EndOffset);
}

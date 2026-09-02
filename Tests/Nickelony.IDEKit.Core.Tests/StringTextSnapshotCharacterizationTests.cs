namespace Nickelony.IDEKit.Core.Text.Tests;

[TestClass]
[TestCategory("TextEditorBaseModernization")]
public sealed class StringTextSnapshotCharacterizationTests
{
	[TestMethod]
	public void EmptyAndNullText_HaveOneEmptyLine()
	{
		var nullSnapshot = new StringTextSnapshot(null);
		var emptySnapshot = new StringTextSnapshot(string.Empty);

		Assert.AreEqual(0, nullSnapshot.TextLength);
		Assert.AreEqual(1, nullSnapshot.LineCount);
		Assert.AreEqual(0, emptySnapshot.TextLength);
		Assert.AreEqual(1, emptySnapshot.LineCount);

		ITextLine line = emptySnapshot.GetLineByNumber(1);
		Assert.AreEqual(0, line.Offset);
		Assert.AreEqual(0, line.Length);
		Assert.AreEqual(0, line.EndOffset);
	}

	[TestMethod]
	public void TextLength_CharactersAndRanges_UseUtf16Offsets()
	{
		var snapshot = new StringTextSnapshot("A\U0001F600B", "script.lua");

		Assert.AreEqual("script.lua", snapshot.FileName);
		Assert.AreEqual(4, snapshot.TextLength);
		Assert.AreEqual('A', snapshot.GetCharAt(0));
		Assert.AreEqual('\uD83D', snapshot.GetCharAt(1));
		Assert.AreEqual('\uDE00', snapshot.GetCharAt(2));
		Assert.AreEqual("\U0001F600", snapshot.GetText(1, 2));
		Assert.AreEqual("B", snapshot.GetText(3, 1));
	}

	[TestMethod]
	public void MixedLineEndings_PreserveLineOffsetsAndLengths()
	{
		var snapshot = new StringTextSnapshot("one\r\ntwo\nthree\rfour");
		ITextLine[] lines = snapshot.Lines.ToArray();

		Assert.AreEqual(4, snapshot.LineCount);
		AssertLine(lines[0], 1, 0, 3);
		AssertLine(lines[1], 2, 5, 3);
		AssertLine(lines[2], 3, 9, 5);
		AssertLine(lines[3], 4, 15, 4);
		Assert.AreEqual(9, snapshot.GetLineByOffset(14).Offset);
		Assert.AreEqual(15, snapshot.GetLineByOffset(15).Offset);
		Assert.AreEqual(4, snapshot.GetLineByOffset(snapshot.TextLength).LineNumber);
	}

	[TestMethod]
	public void InvalidOffsetsAndLineNumbers_Throw()
	{
		var snapshot = new StringTextSnapshot("text");

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetCharAt(-1));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetCharAt(snapshot.TextLength));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetText(3, 2));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetLineByOffset(snapshot.TextLength + 1));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetLineByNumber(0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetLineByNumber(snapshot.LineCount + 1));
	}

	private static void AssertLine(ITextLine line, int lineNumber, int offset, int length)
	{
		Assert.AreEqual(lineNumber, line.LineNumber);
		Assert.AreEqual(offset, line.Offset);
		Assert.AreEqual(length, line.Length);
		Assert.AreEqual(offset + length, line.EndOffset);
	}
}

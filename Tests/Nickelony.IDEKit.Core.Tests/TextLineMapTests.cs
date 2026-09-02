namespace Nickelony.IDEKit.Core.Editing.Tests;

[TestClass]
public sealed class TextLineMapTests
{
	[TestMethod]
	public void Accessors_ClampOutOfRangeLineIndices()
	{
		TextLineMap lineMap = TextLineMap.Build("one\ntwo");

		Assert.AreEqual(3, lineMap.GetLineLength(-1));
		Assert.AreEqual(0, lineMap.GetLineStartOffset(-1));
		Assert.AreEqual(3, lineMap.GetLineLength(99));
		Assert.AreEqual(4, lineMap.GetLineStartOffset(99));
		Assert.AreEqual("two", lineMap.GetLineText(99));
	}

	[TestMethod]
	public void Build_TreatsCrLfAndCrAsLineBreaks()
	{
		TextLineMap lineMap = TextLineMap.Build("one\r\ntwo\rthree");

		Assert.AreEqual(3, lineMap.LineCount);
		Assert.AreEqual("one", lineMap.GetLineText(0));
		Assert.AreEqual("two", lineMap.GetLineText(1));
		Assert.AreEqual("three", lineMap.GetLineText(2));
		Assert.AreEqual(9, lineMap.GetLineStartOffset(2));
	}

	[TestMethod]
	public void GetPosition_MapsEndOfLineAndEndOfDocument()
	{
		TextLineMap lineMap = TextLineMap.Build("one\ntwo");

		Assert.AreEqual((0, 3), lineMap.GetPosition(3));
		Assert.AreEqual((1, 3), lineMap.GetPosition(7));
	}
}

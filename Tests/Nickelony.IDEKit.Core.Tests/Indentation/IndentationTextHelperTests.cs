namespace Nickelony.IDEKit.Core.Indentation.Tests;

[TestClass]
public sealed class IndentationTextHelperTests
{
	[TestMethod]
	public void CreateIndentationUnit_TabMode_ReturnsTab()
	{
		Assert.AreEqual("\t", IndentationTextHelper.CreateIndentationUnit(convertTabsToSpaces: false, 4, 4));
	}

	[TestMethod]
	public void CreateIndentationUnit_Spaces_UsesIndentationSize()
	{
		Assert.AreEqual("    ", IndentationTextHelper.CreateIndentationUnit(convertTabsToSpaces: true, 4, 8));
	}

	[TestMethod]
	public void CreateIndentationUnit_FallsBackToTabSize_WhenIndentationSizeIsZero()
	{
		Assert.AreEqual("        ", IndentationTextHelper.CreateIndentationUnit(convertTabsToSpaces: true, 0, 8));
	}

	[TestMethod]
	public void CreateIndentationUnit_FallsBackToFour_WhenBothSizesAreZero()
	{
		Assert.AreEqual("    ", IndentationTextHelper.CreateIndentationUnit(convertTabsToSpaces: true, 0, 0));
	}

	[TestMethod]
	public void GetLeadingWhitespace_ReturnsWhitespacePrefix()
	{
		Assert.AreEqual(string.Empty, IndentationTextHelper.GetLeadingWhitespace(string.Empty));
		Assert.AreEqual(string.Empty, IndentationTextHelper.GetLeadingWhitespace("x"));
		Assert.AreEqual("   ", IndentationTextHelper.GetLeadingWhitespace("   x"));
		Assert.AreEqual("\t ", IndentationTextHelper.GetLeadingWhitespace("\t x"));
		Assert.AreEqual(string.Empty, IndentationTextHelper.GetLeadingWhitespace("x\n"));
	}

	[TestMethod]
	public void GetLeadingWhitespaceLength_CountsWhitespaceBeforeContent()
	{
		Assert.AreEqual(0, IndentationTextHelper.GetLeadingWhitespaceLength(string.Empty));
		Assert.AreEqual(0, IndentationTextHelper.GetLeadingWhitespaceLength("x"));
		Assert.AreEqual(3, IndentationTextHelper.GetLeadingWhitespaceLength("   x"));
		Assert.AreEqual(1, IndentationTextHelper.GetLeadingWhitespaceLength("\tx"));
	}

	[TestMethod]
	public void RemoveSingleIndentLevel_RemovesOneUnit()
	{
		Assert.AreEqual(string.Empty, IndentationTextHelper.RemoveSingleIndentLevel(string.Empty, "    "));
		Assert.AreEqual(string.Empty, IndentationTextHelper.RemoveSingleIndentLevel("  ", "    "));
		Assert.AreEqual("\t", IndentationTextHelper.RemoveSingleIndentLevel("\t\t", "\t"));
		Assert.AreEqual("    ", IndentationTextHelper.RemoveSingleIndentLevel("        ", "    "));
	}

	[TestMethod]
	public void BuildIndentation_ComposesBaseAndLevels()
	{
		Assert.AreEqual("  ", IndentationTextHelper.BuildIndentation("  ", "    ", 0));
		Assert.AreEqual("          ", IndentationTextHelper.BuildIndentation("  ", "    ", 2));
	}

	[TestMethod]
	public void ContainsLineBreak_DetectsCrLfAndLoneBreaks()
	{
		Assert.IsFalse(IndentationTextHelper.ContainsLineBreak(string.Empty));
		Assert.IsFalse(IndentationTextHelper.ContainsLineBreak("abc"));
		Assert.IsTrue(IndentationTextHelper.ContainsLineBreak("a\rb"));
		Assert.IsTrue(IndentationTextHelper.ContainsLineBreak("a\nb"));
		Assert.IsTrue(IndentationTextHelper.ContainsLineBreak("a\r\nb"));
	}

	[TestMethod]
	public void SplitLines_PreservesDelimitersAndOffsets()
	{
		IReadOnlyList<IndentationTextLine> lines = IndentationTextHelper.SplitLines("a\r\nb\nc");

		Assert.AreEqual(3, lines.Count);
		Assert.AreEqual(new IndentationTextLine("a", "\r\n", 0), lines[0]);
		Assert.AreEqual(new IndentationTextLine("b", "\n", 3), lines[1]);
		Assert.AreEqual(new IndentationTextLine("c", string.Empty, 5), lines[2]);
	}

	[TestMethod]
	public void SplitLines_TrailingLineBreak_IncludesFinalEmptyLine()
	{
		IReadOnlyList<IndentationTextLine> lines = IndentationTextHelper.SplitLines("a\n");

		Assert.AreEqual(2, lines.Count);
		Assert.AreEqual(new IndentationTextLine("a", "\n", 0), lines[0]);
		Assert.AreEqual(new IndentationTextLine(string.Empty, string.Empty, 2), lines[1]);
	}
}

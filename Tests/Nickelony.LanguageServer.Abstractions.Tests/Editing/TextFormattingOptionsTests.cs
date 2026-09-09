namespace Nickelony.LanguageServer.Abstractions.Tests.Editing;

[TestClass]
public sealed class TextFormattingOptionsTests
{
	[TestMethod]
	public void Constructor_PositiveTabSize_IsPreserved()
	{
		var options = new TextFormattingOptions(8, insertSpaces: true);

		Assert.AreEqual(8, options.TabSize);
		Assert.IsTrue(options.InsertSpaces);
	}

	[TestMethod]
	public void Constructor_ZeroTabSize_UsesDefaultWidth()
	{
		var options = new TextFormattingOptions(0, insertSpaces: false);

		Assert.AreEqual(4, options.TabSize);
		Assert.IsFalse(options.InsertSpaces);
	}

	[TestMethod]
	public void Constructor_NegativeTabSize_UsesDefaultWidth()
	{
		var options = new TextFormattingOptions(-6, insertSpaces: false);

		Assert.AreEqual(4, options.TabSize);
	}
}

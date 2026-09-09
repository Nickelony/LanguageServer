namespace Nickelony.LanguageServer.Abstractions.Tests.Editing;

[TestClass]
public sealed class TextRenameRequestTests
{
	[TestMethod]
	public void Constructor_NegativePosition_ClampsToZero()
	{
		var request = new TextRenameRequest("doc.lua", "content", -3, -7, "newName");

		Assert.AreEqual(0, request.Line);
		Assert.AreEqual(0, request.Column);
		Assert.AreEqual("newName", request.NewName);
	}

	[TestMethod]
	public void Constructor_ValidPosition_IsPreserved()
	{
		var request = new TextRenameRequest("doc.lua", "content", 4, 9, "newName");

		Assert.AreEqual("doc.lua", request.FilePath);
		Assert.AreEqual("content", request.DocumentText);
		Assert.AreEqual(4, request.Line);
		Assert.AreEqual(9, request.Column);
	}
}

using Nickelony.IDEKit.Core.Text;

namespace Nickelony.LanguageServer.Abstractions.Tests.CodeActions;

[TestClass]
public sealed class TextCodeActionRequestTests
{
	[TestMethod]
	public void Constructor_ClampsNegativeCoordinatesToZero()
	{
		var request = new TextCodeActionRequest("a.lua", "text",
			new TextPositionRange(new TextPosition(-2, -3), new TextPosition(1, 4)));

		Assert.AreEqual(new TextPositionRange(new TextPosition(0, 0), new TextPosition(1, 4)), request.Range);
	}

	[TestMethod]
	public void Constructor_StoresFilePathAndDocumentText()
	{
		var request = new TextCodeActionRequest("a.lua", "text", default);

		Assert.AreEqual("a.lua", request.FilePath);
		Assert.AreEqual("text", request.DocumentText);
	}

	[TestMethod]
	public void Constructor_NullArguments_Throw()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeActionRequest(null!, "text", default));
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeActionRequest("a.lua", null!, default));
	}
}

namespace Nickelony.LanguageServer.Abstractions.Tests.Navigation;

[TestClass]
public sealed class TextReferenceRequestTests
{
	[TestMethod]
	public void Constructor_NegativePosition_ClampsToZero()
	{
		var request = new TextReferenceRequest("doc.lua", "content", -1, -4);

		Assert.AreEqual(0, request.Line);
		Assert.AreEqual(0, request.Column);
	}

	[TestMethod]
	public void Constructor_DefaultsIncludeDeclarationToTrue()
	{
		var request = new TextReferenceRequest("doc.lua", "content", 2, 5);

		Assert.IsTrue(request.IncludeDeclaration);
	}

	[TestMethod]
	public void Constructor_ExplicitIncludeDeclaration_IsPreserved()
	{
		var request = new TextReferenceRequest("doc.lua", "content", 2, 5, includeDeclaration: false);

		Assert.IsFalse(request.IncludeDeclaration);
	}
}

using Nickelony.LanguageServer.Abstractions.Editing;
using Nickelony.LanguageServer.Abstractions.Signatures;

namespace Nickelony.LanguageServer.Lua.Tests;

[TestClass]
public sealed class ContractPayloadOwnershipTests
{
	[TestMethod]
	public void TextWorkspaceEdit_StoresOwnedDocumentAndTextEditSnapshots()
	{
		var textEdits = new List<TextEdit>
		{
			new(new TextDocumentRange(1, 1, 1, 2), "replacement")
		};

		var documentEdits = new List<TextDocumentEdit>
		{
			new("C:\\Workspace\\test.lua", textEdits)
		};

		var workspaceEdit = new TextWorkspaceEdit(documentEdits);

		textEdits.Clear();
		documentEdits.Clear();

		Assert.AreEqual(1, workspaceEdit.DocumentEdits.Count);
		Assert.AreEqual(1, workspaceEdit.DocumentEdits[0].TextEdits.Count);
		Assert.AreEqual("replacement", workspaceEdit.DocumentEdits[0].TextEdits[0].NewText);
	}

	[TestMethod]
	public void TextSignatureHelpInfo_StoresOwnedParameterSnapshot()
	{
		var parameters = new List<TextSignatureParameterInfo>
		{
			new("value")
		};

		var signature = new TextSignatureHelpInfo("fn(value)", parameters: parameters);
		parameters.Clear();

		Assert.AreEqual(1, signature.Parameters.Count);
		Assert.AreEqual("value", signature.Parameters[0].Label);
	}

	[TestMethod]
	public void LuaSemanticToken_StoresOwnedModifierSnapshot()
	{
		var modifiers = new List<string> { "readonly" };
		var token = new LuaSemanticToken(0, 0, 1, "variable", modifiers);

		modifiers[0] = "changed";
		modifiers.Add("static");

		Assert.AreEqual(1, token.Modifiers.Count);
		Assert.AreEqual("readonly", token.Modifiers[0]);
		Assert.IsTrue(token.HasModifier("readonly"));
		Assert.IsFalse(token.HasModifier("changed"));
	}
}

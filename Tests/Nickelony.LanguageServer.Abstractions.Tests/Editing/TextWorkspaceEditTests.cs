using Nickelony.IDEKit.Core.Text;

namespace Nickelony.LanguageServer.Abstractions.Tests.Editing;

[TestClass]
public sealed class TextWorkspaceEditTests
{
	[TestMethod]
	public void HasEdits_EmptyDocumentList_IsFalse()
	{
		var workspaceEdit = new TextWorkspaceEdit([]);

		Assert.IsFalse(workspaceEdit.HasEdits);
	}

	[TestMethod]
	public void HasEdits_DocumentsWithoutEdits_IsFalse()
	{
		var workspaceEdit = new TextWorkspaceEdit(
		[
			new TextDocumentEdit("a.lua", []),
			new TextDocumentEdit("b.lua", [])
		]);

		Assert.IsFalse(workspaceEdit.HasEdits);
	}

	[TestMethod]
	public void HasEdits_AnyDocumentWithEdits_IsTrue()
	{
		var workspaceEdit = new TextWorkspaceEdit(
		[
			new TextDocumentEdit("a.lua", []),
			new TextDocumentEdit("b.lua", [new TextEdit(new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 1)), "replacement")])
		]);

		Assert.IsTrue(workspaceEdit.HasEdits);
	}

	[TestMethod]
	public void Constructor_StoresOwnedDocumentAndTextEditSnapshots()
	{
		var textEdits = new List<TextEdit>
		{
			new(new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 1)), "replacement")
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
}

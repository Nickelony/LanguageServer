using Nickelony.IDEKit.Core.Text;

namespace Nickelony.LanguageServer.Abstractions.Tests.Editing;

[TestClass]
public sealed class TextDocumentEditTests
{
	[TestMethod]
	public void Constructor_StoresOwnedEditSnapshot()
	{
		var textEdits = new List<TextEdit>
		{
			new(new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 1)), "replacement")
		};

		var documentEdit = new TextDocumentEdit("doc.lua", textEdits);

		textEdits.Clear();

		Assert.AreEqual("doc.lua", documentEdit.FilePath);
		Assert.AreEqual(1, documentEdit.TextEdits.Count);
		Assert.AreEqual("replacement", documentEdit.TextEdits[0].NewText);
	}
}

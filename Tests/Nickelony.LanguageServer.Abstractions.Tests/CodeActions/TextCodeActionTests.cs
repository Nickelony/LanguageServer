using Nickelony.IDEKit.Core.Text;

namespace Nickelony.LanguageServer.Abstractions.Tests.CodeActions;

[TestClass]
public sealed class TextCodeActionTests
{
	[TestMethod]
	public void Constructor_StoresTitleKindPreferredAndEdit()
	{
		var edit = new TextWorkspaceEdit(
		[
			new TextDocumentEdit("a.lua",
			[
				new TextEdit(new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 1)), "x")
			])
		]);

		var action = new TextCodeAction("Fix it", "quickfix", isPreferred: true, edit);

		Assert.AreEqual("Fix it", action.Title);
		Assert.AreEqual("quickfix", action.Kind);
		Assert.IsTrue(action.IsPreferred);
		Assert.AreSame(edit, action.Edit);
	}

	[TestMethod]
	public void Constructor_BlankKind_IsStoredAsNull()
	{
		var action = new TextCodeAction("Fix it", "   ", isPreferred: false, new TextWorkspaceEdit([]));

		Assert.IsNull(action.Kind);
	}

	[TestMethod]
	public void Constructor_NullArguments_Throw()
	{
		var edit = new TextWorkspaceEdit([]);

		Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeAction(null!, "quickfix", isPreferred: false, edit));
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeAction("Fix it", "quickfix", isPreferred: false, null!));
	}
}

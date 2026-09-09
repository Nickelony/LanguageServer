using Nickelony.IDEKit.IntelliSense.CodeActions;

namespace Nickelony.IDEKit.IntelliSense.Tests.CodeActions;

/// <summary>
/// Verifies the code-action request and presentation records: storage, equality, and construction
/// guards.
/// </summary>
[TestClass]
public sealed class TextCodeActionRecordTests
{
	[TestMethod]
	public void Item_StoresEveryComponent()
	{
		var payload = new object();
		var item = new TextCodeActionItem("Fix it", "quickfix", isPreferred: true, payload);

		Assert.AreEqual("Fix it", item.Title);
		Assert.AreEqual("quickfix", item.Kind);
		Assert.IsTrue(item.IsPreferred);
		Assert.AreSame(payload, item.Payload);
	}

	[TestMethod]
	public void Item_NullTitle_Throws()
		=> Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeActionItem(null!, null, false));

	[TestMethod]
	public void Item_NullKindAndPayload_AreAllowed()
	{
		var item = new TextCodeActionItem("Fix it", null, false);

		Assert.IsNull(item.Kind);
		Assert.IsNull(item.Payload);
	}

	[TestMethod]
	public void Item_BlankKind_IsNormalizedAndDefaultsAreOptional()
	{
		var item = new TextCodeActionItem("Fix it", "   ");

		Assert.IsNull(item.Kind);
		Assert.IsFalse(item.IsPreferred);
		Assert.IsNull(item.Payload);

		var trimmed = new TextCodeActionItem("Fix it", "  quickfix  ");

		Assert.AreEqual("quickfix", trimmed.Kind);
	}

	[TestMethod]
	public void Item_Equality_ComparesEveryComponent()
	{
		var item = new TextCodeActionItem("Fix it", "quickfix", true);

		Assert.AreEqual(item, new TextCodeActionItem("Fix it", "quickfix", true));
		Assert.AreNotEqual(item, new TextCodeActionItem("Other", "quickfix", true));
		Assert.AreNotEqual(item, new TextCodeActionItem("Fix it", "refactor", true));
		Assert.AreNotEqual(item, new TextCodeActionItem("Fix it", "quickfix", false));
	}

	[TestMethod]
	public void Context_StoresEditorState()
	{
		var context = new TextCodeActionContext("text", caretOffset: 2, selectionStartOffset: 1, selectionEndOffset: 3);

		Assert.AreEqual("text", context.DocumentText);
		Assert.AreEqual(2, context.CaretOffset);
		Assert.AreEqual(1, context.SelectionStartOffset);
		Assert.AreEqual(3, context.SelectionEndOffset);
	}

	[TestMethod]
	public void Context_Equality_ComparesEveryComponent()
	{
		var context = new TextCodeActionContext("text", 2, 1, 3);

		Assert.AreEqual(context, new TextCodeActionContext("text", 2, 1, 3));
		Assert.AreNotEqual(context, new TextCodeActionContext("other", 2, 1, 3));
		Assert.AreNotEqual(context, new TextCodeActionContext("text", 3, 1, 3));
	}

	[TestMethod]
	public void RequestState_StoresRequestedRange()
	{
		var state = new TextCodeActionRequestState("text", 1, 3);

		Assert.AreEqual("text", state.DocumentText);
		Assert.AreEqual(1, state.StartOffset);
		Assert.AreEqual(3, state.EndOffset);
	}

	[TestMethod]
	public void RequestState_Equality_ComparesEveryComponent()
	{
		var state = new TextCodeActionRequestState("text", 1, 3);

		Assert.AreEqual(state, new TextCodeActionRequestState("text", 1, 3));
		Assert.AreNotEqual(state, new TextCodeActionRequestState("text", 1, 4));
	}

	[TestMethod]
	public void Context_InvalidOffsets_Throw()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeActionContext(null!, 0, 0, 0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextCodeActionContext("text", 5, 0, 0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextCodeActionContext("text", 0, 3, 2));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextCodeActionContext("text", 0, -1, 0));
	}

	[TestMethod]
	public void RequestState_InvalidRange_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeActionRequestState(null!, 0, 0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextCodeActionRequestState("text", 3, 2));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextCodeActionRequestState("text", 0, 5));
	}
}

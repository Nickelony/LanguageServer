using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.IDEKit.IntelliSense.Tests.Completion;

[TestClass]
public sealed class TextCompletionSessionDecisionTests
{
	[TestMethod]
	public void None_LeavesSessionUnchanged()
	{
		Assert.IsFalse(TextCompletionSessionDecision.None.ShouldClose);
		Assert.IsNull(TextCompletionSessionDecision.None.Items);
		Assert.IsNull(TextCompletionSessionDecision.None.StartOffset);
		Assert.IsNull(TextCompletionSessionDecision.None.EndOffset);
	}

	[TestMethod]
	public void Close_DismissesSessionWithoutItems()
	{
		Assert.IsTrue(TextCompletionSessionDecision.Close.ShouldClose);
		Assert.IsNull(TextCompletionSessionDecision.Close.Items);
	}

	[TestMethod]
	public void Open_CarriesItemsAndReplacementRange()
	{
		var item = new TextCompletionItem("label");

		TextCompletionSessionDecision decision = TextCompletionSessionDecision.Open([item], startOffset: 2, endOffset: 6);

		Assert.IsFalse(decision.ShouldClose);
		Assert.IsNotNull(decision.Items);
		Assert.AreEqual(1, decision.Items.Count);
		Assert.AreSame(item, decision.Items[0]);
		Assert.AreEqual(2, decision.StartOffset);
		Assert.AreEqual(6, decision.EndOffset);
	}

	[TestMethod]
	public void Open_CapturesCurrentItemsWithoutObservingLaterChanges()
	{
		var item = new TextCompletionItem("first");
		var items = new List<TextCompletionItem> { item };

		TextCompletionSessionDecision decision = TextCompletionSessionDecision.Open(items, startOffset: 0, endOffset: 5);

		items.Clear();
		items.Add(new TextCompletionItem("second"));

		Assert.IsNotNull(decision.Items);
		Assert.AreEqual(1, decision.Items.Count);
		Assert.AreSame(item, decision.Items[0]);
	}

	[TestMethod]
	public void Equality_ComparesItemsByListReference()
	{
		// The documented equality contract: two decisions that carry equal item sequences are not
		// equal unless they share the same list instance.
		var item = new TextCompletionItem("label");
		TextCompletionSessionDecision first = TextCompletionSessionDecision.Open([item], startOffset: 0, endOffset: 5);
		TextCompletionSessionDecision second = TextCompletionSessionDecision.Open([item], startOffset: 0, endOffset: 5);
		var sharedItems = new List<TextCompletionItem> { item };
		var third = new TextCompletionSessionDecision(false, sharedItems, 0, 5);

		Assert.AreNotEqual(first, second);
		Assert.AreEqual(third, new TextCompletionSessionDecision(false, sharedItems, 0, 5));
	}

	[TestMethod]
	public void Open_NegativeStartOffset_Throws()
		=> Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => TextCompletionSessionDecision.Open([new TextCompletionItem("label")], startOffset: -1, endOffset: 3));

	[TestMethod]
	public void Open_EndOffsetBeforeStart_Throws()
		=> Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => TextCompletionSessionDecision.Open([new TextCompletionItem("label")], startOffset: 4, endOffset: 2));

	[TestMethod]
	public void Constructor_ItemsWithoutOffsets_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(
			() => new TextCompletionSessionDecision(false, [new TextCompletionItem("label")], null, null));

	[TestMethod]
	public void Constructor_OffsetsWithoutItems_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => new TextCompletionSessionDecision(false, null, 2, 6));

	[TestMethod]
	public void Constructor_CloseWithItems_IsAllowed()
	{
		// A decision may request a close and carry a replacement; a host applies the close first.
		TextCompletionSessionDecision decision = TextCompletionSessionDecision
			.Open([new TextCompletionItem("label")], startOffset: 0, endOffset: 5)
			with
		{ ShouldClose = true };

		Assert.IsTrue(decision.ShouldClose);
		Assert.AreEqual(1, decision.Items!.Count);
		Assert.AreEqual(0, decision.StartOffset);
		Assert.AreEqual(5, decision.EndOffset);
	}

	[TestMethod]
	public void Deconstruct_ReportsTheStateComponents()
	{
		var items = new List<TextCompletionItem> { new("label") };
		var decision = new TextCompletionSessionDecision(false, items, 1, 4);

		(bool shouldClose, IReadOnlyList<TextCompletionItem>? decisionItems, int? startOffset, int? endOffset, bool allItemsFilteredOut) = decision;

		Assert.IsFalse(shouldClose);
		Assert.AreSame(items, decisionItems);
		Assert.AreEqual(1, startOffset);
		Assert.AreEqual(4, endOffset);
		Assert.IsFalse(allItemsFilteredOut);

		(bool _, IReadOnlyList<TextCompletionItem>? _, int? _, int? _, bool filteredOut) = TextCompletionSessionDecision.NoMatches;

		Assert.IsTrue(filteredOut);
	}

	[TestMethod]
	public void NoMatches_ReportsTheFilteredEmptyState()
	{
		TextCompletionSessionDecision decision = TextCompletionSessionDecision.NoMatches;

		Assert.IsFalse(decision.ShouldClose);
		Assert.IsNull(decision.Items);
		Assert.IsNull(decision.StartOffset);
		Assert.IsNull(decision.EndOffset);
		Assert.IsTrue(decision.AllItemsFilteredOut);
		Assert.AreNotEqual(TextCompletionSessionDecision.None, decision);
	}

	[TestMethod]
	public void None_IsValueEqualToDefault()
	{
		Assert.AreEqual(default(TextCompletionSessionDecision), TextCompletionSessionDecision.None);
		Assert.IsFalse(TextCompletionSessionDecision.None.AllItemsFilteredOut);
	}

	[TestMethod]
	public void Open_EmptyItems_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => TextCompletionSessionDecision.Open([], 0, 5));

	[TestMethod]
	public void Constructor_EmptyItems_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => new TextCompletionSessionDecision(false, [], 0, 5));

	[TestMethod]
	public void Constructor_FilteredOutWithItems_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(
			() => new TextCompletionSessionDecision(false, [new TextCompletionItem("label")], 0, 5, allItemsFilteredOut: true));

	[TestMethod]
	public void Constructor_MissingEndOffset_ReportsTheEndOffsetParameter()
	{
		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(
			() => new TextCompletionSessionDecision(false, [new TextCompletionItem("label")], 1, null));

		Assert.AreEqual("endOffset", exception.ParamName);
	}
}

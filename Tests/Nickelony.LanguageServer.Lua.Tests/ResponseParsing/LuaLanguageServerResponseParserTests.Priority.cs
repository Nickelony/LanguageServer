using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.LanguageServer.Lua.Tests;

public sealed partial class LuaLanguageServerResponseParserTests
{
	[TestMethod]
	public void ParseCompletionItem_AppliesPreselectionAndResponseOrderBranches()
	{
		CompletionItemPayload plain = DeserializeCompletionItemPayload(new { label = "item", kind = 1 });
		CompletionItemPayload preselected = DeserializeCompletionItemPayload(new { label = "item", kind = 1, preselect = true });

		TextCompletionItem? plainItem = LuaLanguageServerResponseParser.ParseCompletionItem(plain, 0, "text");
		TextCompletionItem? preselectedItem = LuaLanguageServerResponseParser.ParseCompletionItem(preselected, 0, "text");
		TextCompletionItem? laterItem = LuaLanguageServerResponseParser.ParseCompletionItem(plain, 3, "text");

		Assert.IsNotNull(plainItem);
		Assert.IsNotNull(preselectedItem);
		Assert.IsNotNull(laterItem);

		// Ordering semantics only: a preselected variant outranks a plain one, and a better protocol
		// rank outranks a worse one; the exact weights stay internal.
		Assert.IsTrue(preselectedItem.Priority > plainItem.Priority);
		Assert.IsTrue(plainItem.Priority > laterItem.Priority);
	}

	[TestMethod]
	public void ParseCompletionItem_CarriesProtocolSortTextAndPreselection()
	{
		CompletionItemPayload itemElement = DeserializeCompletionItemPayload(new
		{
			label = "item",
			kind = 6,
			sortText = "0002",
			preselect = true
		});

		TextCompletionItem? item = LuaLanguageServerResponseParser.ParseCompletionItem(itemElement, 0, "text");

		Assert.IsNotNull(item);
		Assert.AreEqual("0002", item.SortText);
		Assert.IsTrue(item.IsPreselected);
	}

	[TestMethod]
	public void ParseCompletionItem_BlankSortText_IsNull()
	{
		CompletionItemPayload itemElement = DeserializeCompletionItemPayload(new { label = "item", kind = 6, sortText = "  " });

		TextCompletionItem? item = LuaLanguageServerResponseParser.ParseCompletionItem(itemElement, 0, "text");

		Assert.IsNotNull(item);
		Assert.IsNull(item.SortText);
		Assert.IsFalse(item.IsPreselected);
	}

	[TestMethod]
	public void ParseCompletionItems_RanksPrioritiesByProtocolSortText()
	{
		CompletionItemPayload late = DeserializeCompletionItemPayload(new { label = "late", kind = 6, sortText = "0002" });
		CompletionItemPayload early = DeserializeCompletionItemPayload(new { label = "early", kind = 6, sortText = "0001" });

		IReadOnlyList<TextCompletionItem> items = LuaLanguageServerResponseParser.ParseCompletionItems([late, early], "text");

		Assert.AreEqual(2, items.Count);
		Assert.AreEqual("late", items[0].Label);
		Assert.AreEqual("early", items[1].Label);

		// The priority hint agrees with the protocol ordering key even when the response order differs.
		Assert.IsTrue(items[1].Priority > items[0].Priority);
	}

	[TestMethod]
	public void ParseCompletionItems_FallsBackToLabelWhenSortTextIsMissing()
	{
		CompletionItemPayload beta = DeserializeCompletionItemPayload(new { label = "beta", kind = 6 });
		CompletionItemPayload alpha = DeserializeCompletionItemPayload(new { label = "alpha", kind = 6 });

		IReadOnlyList<TextCompletionItem> items = LuaLanguageServerResponseParser.ParseCompletionItems([beta, alpha], "text");

		Assert.AreEqual(2, items.Count);
		Assert.IsTrue(items[1].Priority > items[0].Priority);
		Assert.AreEqual("alpha", items[1].Label);
	}

	[TestMethod]
	public void ParseCompletionItems_KeepsResponseOrderForEqualSortKeys()
	{
		CompletionItemPayload first = DeserializeCompletionItemPayload(new { label = "alpha", kind = 6, sortText = "0001" });
		CompletionItemPayload second = DeserializeCompletionItemPayload(new { label = "beta", kind = 6, sortText = "0001" });

		IReadOnlyList<TextCompletionItem> items = LuaLanguageServerResponseParser.ParseCompletionItems([first, second], "text");

		Assert.AreEqual(2, items.Count);
		Assert.AreEqual("alpha", items[0].Label);
		Assert.AreEqual("beta", items[1].Label);
		Assert.IsTrue(items[0].Priority > items[1].Priority);
	}

	[TestMethod]
	public void ParseCompletionItems_SurfacesPreselectedItemAboveProtocolRank()
	{
		CompletionItemPayload first = DeserializeCompletionItemPayload(new { label = "first", kind = 6, sortText = "0001" });
		CompletionItemPayload preselected = DeserializeCompletionItemPayload(new { label = "preselected", kind = 6, sortText = "0002", preselect = true });

		IReadOnlyList<TextCompletionItem> items = LuaLanguageServerResponseParser.ParseCompletionItems([first, preselected], "text");

		Assert.AreEqual(2, items.Count);
		Assert.IsTrue(items[1].Priority > items[0].Priority);
	}

	[TestMethod]
	public void ParseCompletionItems_MergedDuplicateRanksByTheAdoptedSortText()
	{
		// The duplicate's sort text is adopted by the retained item, so its priority hint must rank
		// by that key instead of keeping the retained label rank (which would invert the hint).
		CompletionItemPayload retained = DeserializeCompletionItemPayload(new { label = "alpha", kind = 6 });
		CompletionItemPayload middle = DeserializeCompletionItemPayload(new { label = "beta", kind = 6, sortText = "0001" });
		CompletionItemPayload duplicate = DeserializeCompletionItemPayload(new { label = "alpha", kind = 6, sortText = "0000" });

		IReadOnlyList<TextCompletionItem> items = LuaLanguageServerResponseParser.ParseCompletionItems([retained, middle, duplicate], "text");

		Assert.AreEqual(2, items.Count);

		TextCompletionItem merged = items.Single(item => item.Label == "alpha");
		TextCompletionItem other = items.Single(item => item.Label == "beta");

		Assert.AreEqual("0000", merged.SortText);
		Assert.IsTrue(merged.Priority > other.Priority);
	}
}

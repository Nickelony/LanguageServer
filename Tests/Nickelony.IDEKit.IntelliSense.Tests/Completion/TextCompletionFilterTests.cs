using Nickelony.IDEKit.Core.Identifiers;
using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.IDEKit.IntelliSense.Tests.Completion;

[TestClass]
public sealed class TextCompletionFilterTests
{
	private static readonly IReadOnlyList<TextCompletionItem> s_items =
	[
		new TextCompletionItem("Alpha", "Alpha"),
		new TextCompletionItem("Beta", "Beta: "),
		new TextCompletionItem("Gamma", "Gamma")
	];

	[TestMethod]
	public void FilterByCurrentWord_EmptyWord_ReturnsAllItems()
	{
		IReadOnlyList<TextCompletionItem> result = TextCompletionFilter.FilterByCurrentWord(s_items, new TextCompletionContext(string.Empty, 0));

		Assert.AreEqual(3, result.Count);
	}

	[TestMethod]
	public void FilterByCurrentWord_KeepsItemsContainingWord()
	{
		IReadOnlyList<TextCompletionItem> result = TextCompletionFilter.FilterByCurrentWord(s_items, new TextCompletionContext("Bet", 3));

		Assert.AreEqual(1, result.Count);
		Assert.AreEqual("Beta: ", result[0].InsertText);
	}

	[TestMethod]
	public void FilterByCurrentWord_IsCaseInsensitive()
	{
		IReadOnlyList<TextCompletionItem> result = TextCompletionFilter.FilterByCurrentWord(s_items, new TextCompletionContext("gam", 3));

		Assert.AreEqual(1, result.Count);
		Assert.AreEqual("Gamma", result[0].InsertText);
	}

	[TestMethod]
	public void FilterByCurrentWord_NoMatch_ReturnsEmpty()
	{
		IReadOnlyList<TextCompletionItem> result = TextCompletionFilter.FilterByCurrentWord(s_items, new TextCompletionContext("Zzz", 3));

		Assert.AreEqual(0, result.Count);
	}

	[TestMethod]
	public void FilterByWord_EmptyWord_ReturnsAllItems()
	{
		IReadOnlyList<TextCompletionItem> result = TextCompletionFilter.FilterByWord(s_items, string.Empty);

		Assert.AreEqual(3, result.Count);
	}

	[TestMethod]
	public void FilterByWord_ContainsMatch_IsCaseInsensitive()
	{
		IReadOnlyList<TextCompletionItem> result = TextCompletionFilter.FilterByWord(s_items, "ET");

		Assert.AreEqual(1, result.Count);
		Assert.AreEqual("Beta: ", result[0].InsertText);
	}

	[TestMethod]
	public void FilterByWord_NoMatch_ReturnsEmpty()
	{
		IReadOnlyList<TextCompletionItem> result = TextCompletionFilter.FilterByWord(s_items, "Zzz");

		Assert.AreEqual(0, result.Count);
	}

	[TestMethod]
	public void GetPrefix_ReturnsPrefixAtCaret()
	{
		Assert.AreEqual("Beta_2", IdentifierHelper.GetPrefix("Beta_2: value", 6));
	}

	[TestMethod]
	public void GetPrefix_InvalidCaret_ReturnsEmpty()
	{
		Assert.AreEqual(string.Empty, IdentifierHelper.GetPrefix("Beta", 5));
	}
}

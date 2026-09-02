using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.IDEKit.IntelliSense.Tests.Completion;

[TestClass]
public sealed class CompletionSessionKernelTests
{
	private static readonly IReadOnlyList<TextCompletionItem> s_items =
	[
		new TextCompletionItem("Alpha", "Alpha"),
		new TextCompletionItem("Beta", "Beta: "),
		new TextCompletionItem("Gamma", "Gamma")
	];

	[TestMethod]
	public void GetDecision_DefaultLocator_ComputesWordAndReplacementRange()
	{
		var kernel = new CompletionSessionKernel();
		var source = new StringTextSnapshot("Bet");

		TextCompletionSessionDecision decision = kernel.GetDecision(source, 3, new StubProvider(s_items));

		Assert.IsNotNull(decision.Items);
		Assert.AreEqual(1, decision.Items.Count);
		Assert.AreEqual("Beta: ", decision.Items[0].InsertText);
		Assert.AreEqual(0, decision.StartOffset);
		Assert.AreEqual(3, decision.EndOffset);
	}

	[TestMethod]
	public void GetDecision_CaretAtDocumentStart_KeepsAllItemsAtCaretRange()
	{
		var kernel = new CompletionSessionKernel();
		var source = new StringTextSnapshot("T");

		TextCompletionSessionDecision decision = kernel.GetDecision(source, 0, new StubProvider(s_items));

		Assert.IsNotNull(decision.Items);
		Assert.AreEqual(3, decision.Items.Count);
		Assert.AreEqual(0, decision.StartOffset);
		Assert.AreEqual(0, decision.EndOffset);
	}

	[TestMethod]
	public void GetDecision_EmptySnapshot_KeepsAllItems()
	{
		var kernel = new CompletionSessionKernel();
		var source = new StringTextSnapshot(string.Empty);

		TextCompletionSessionDecision decision = kernel.GetDecision(source, 0, new StubProvider(s_items));

		Assert.IsNotNull(decision.Items);
		Assert.AreEqual(3, decision.Items.Count);
	}

	[TestMethod]
	public void GetDecision_NoMatchingItems_ReturnsNone()
	{
		var kernel = new CompletionSessionKernel();
		var source = new StringTextSnapshot("Zzz");

		TextCompletionSessionDecision decision = kernel.GetDecision(source, 3, new StubProvider(s_items));

		Assert.AreEqual(TextCompletionSessionDecision.None, decision);
	}

	[TestMethod]
	public void GetDecision_ProviderReturnsNoItems_ReturnsNone()
	{
		var kernel = new CompletionSessionKernel();
		var source = new StringTextSnapshot("Alpha");

		TextCompletionSessionDecision decision = kernel.GetDecision(source, 5, new StubProvider());

		Assert.AreEqual(TextCompletionSessionDecision.None, decision);
	}

	[TestMethod]
	public void GetDecision_ExplicitWordInfo_OverridesDefaultLocator()
	{
		var kernel = new CompletionSessionKernel();
		var source = new StringTextSnapshot("alpha beta");
		var wordInfo = new CompletionWordInfo(string.Empty, new TextRange(6, 4));

		TextCompletionSessionDecision decision = kernel.GetDecision(source, 10, new StubProvider(s_items), wordInfo: wordInfo);

		Assert.IsNotNull(decision.Items);
		Assert.AreEqual(3, decision.Items.Count);
		Assert.AreEqual(6, decision.StartOffset);
		Assert.AreEqual(10, decision.EndOffset);
	}

	[TestMethod]
	public void GetDecision_CustomFilter_IsUsed()
	{
		static IReadOnlyList<TextCompletionItem> OnlyAlpha(IReadOnlyList<TextCompletionItem> items, string word)
			=> [items[0]];

		var kernel = new CompletionSessionKernel(filter: OnlyAlpha);
		var source = new StringTextSnapshot("Bet");

		TextCompletionSessionDecision decision = kernel.GetDecision(source, 3, new StubProvider(s_items));

		Assert.IsNotNull(decision.Items);
		Assert.AreEqual(1, decision.Items.Count);
		Assert.AreEqual("Alpha", decision.Items[0].InsertText);
	}

	[TestMethod]
	public void GetDecision_CustomLocator_IsUsed()
	{
		static CompletionWordInfo LocateAfterColon(ITextSnapshot snapshot, int caretOffset)
		{
			int start = snapshot.GetText(0, caretOffset).LastIndexOf(':') + 1;
			return new CompletionWordInfo(snapshot.GetText(start, caretOffset - start), new TextRange(start, caretOffset - start));
		}

		var kernel = new CompletionSessionKernel(LocateAfterColon);
		var source = new StringTextSnapshot("key:Bet");

		TextCompletionSessionDecision decision = kernel.GetDecision(source, 7, new StubProvider(s_items));

		Assert.IsNotNull(decision.Items);
		Assert.AreEqual(1, decision.Items.Count);
		Assert.AreEqual(4, decision.StartOffset);
		Assert.AreEqual(7, decision.EndOffset);
	}

	[TestMethod]
	public void GetDecision_ProviderReceivesSnapshotTextAndTrigger()
	{
		var kernel = new CompletionSessionKernel();
		var provider = new StubProvider(s_items);
		var source = new StringTextSnapshot("Gamma");

		kernel.GetDecision(source, 5, provider, TextCompletionTrigger.Word);

		Assert.IsNotNull(provider.LastContext);
		Assert.AreEqual("Gamma", provider.LastContext.DocumentText);
		Assert.AreEqual(5, provider.LastContext.CaretOffset);
		Assert.AreEqual(TextCompletionTrigger.Word, provider.LastContext.Trigger);
		Assert.AreEqual(-1, provider.LastContext.ArgumentIndex);
	}

	[TestMethod]
	public async Task GetDecisionAsync_ReturnsFilteredDecision()
	{
		var kernel = new CompletionSessionKernel();
		var provider = new StubProvider(s_items);
		var source = new StringTextSnapshot("Bet");

		TextCompletionSessionDecision decision = await kernel.GetDecisionAsync(source, 3, provider).ConfigureAwait(false);

		Assert.IsNotNull(decision.Items);
		Assert.AreEqual(1, decision.Items.Count);
		Assert.AreEqual(0, decision.StartOffset);
		Assert.AreEqual(3, decision.EndOffset);
		Assert.IsNotNull(provider.LastContext);
	}

	[TestMethod]
	public async Task GetDecisionAsync_CancelledToken_Throws()
	{
		var kernel = new CompletionSessionKernel();
		var source = new StringTextSnapshot("Bet");

		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();

		await Assert.ThrowsExactlyAsync<TaskCanceledException>(
			() => kernel.GetDecisionAsync(source, 3, new StubProvider(s_items), cancellationToken: cancellation.Token)).ConfigureAwait(false);
	}

	private sealed class StubProvider(IReadOnlyList<TextCompletionItem>? items = null) : ITextCompletionProvider
	{
		private readonly IReadOnlyList<TextCompletionItem> _items = items ?? [];

		public TextCompletionContext? LastContext { get; private set; }

		public IReadOnlyList<TextCompletionItem> GetCompletionItems(TextCompletionContext context)
		{
			LastContext = context;
			return _items;
		}
	}
}

using Nickelony.LanguageServer.Abstractions.Editing;
using Nickelony.LanguageServer.Abstractions.Hover;
using Nickelony.LanguageServer.Abstractions.Navigation;
using System.Text.Json;

namespace Nickelony.LanguageServer.Lua.Tests;

public partial class LuaLanguageServerIntelliSenseProviderTests
{
	[TestMethod]
	public async Task CanceledRequestsBeforeStart_PropagateCancellationAcrossAllRequestKinds()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider(workspaceRoot, client);
		using var cancellationTokenSource = new CancellationTokenSource();

		cancellationTokenSource.Cancel();

		Task[] canceledRequests =
		[
			provider.GetCompletionItemsAsync(filePath, content, 0, 0, cancellationToken: cancellationTokenSource.Token),
			provider.GetHoverAsync(filePath, content, 0, 0, cancellationTokenSource.Token),
			provider.GetDefinitionAsync(filePath, content, 0, 0, cancellationTokenSource.Token),
			provider.GetSignatureHelpAsync(filePath, content, 0, 0, cancellationTokenSource.Token),
			provider.GetReferencesAsync(new TextReferenceRequest(filePath, content, 0, 0), cancellationTokenSource.Token),
			provider.RenameSymbolAsync(new TextRenameRequest(filePath, content, 0, 0, "renamed"), cancellationTokenSource.Token),
			provider.FormatDocumentAsync(new TextFormatRequest(filePath, content, new TextFormattingOptions(4, true)), cancellationTokenSource.Token)
		];

		for (int i = 0; i < canceledRequests.Length; i++)
			await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => canceledRequests[i]).ConfigureAwait(false);

		Assert.AreEqual(0, client.StartCallCount);
		Assert.AreEqual(0, client.GetSentMethodNames().Length);
	}

	[TestMethod]
	public async Task CancellationAfterResponseBeforePublication_PropagatesAndLeavesProviderUsable()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			HoverResponse = JsonSerializer.SerializeToElement(new
			{
				contents = new
				{
					kind = "markdown",
					value = "Hover docs."
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider(workspaceRoot, client);
		using var cancellationTokenSource = new CancellationTokenSource();

		client.BeforeReturningHoverResponse = cancellationTokenSource.Cancel;

		Task<TextHoverInfo?> canceledHoverTask = provider.GetHoverAsync(filePath, content, 0, 0, cancellationTokenSource.Token);

		await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => canceledHoverTask).ConfigureAwait(false);

		client.BeforeReturningHoverResponse = null;
		TextHoverInfo? recoveredHover = await provider.GetHoverAsync(filePath, content, 0, 0).ConfigureAwait(false);

		Assert.IsNotNull(recoveredHover);
		Assert.AreEqual("Hover docs.", recoveredHover.Content);
		Assert.AreEqual(0, client.MarkTransportUnhealthyCallCount);
	}
}

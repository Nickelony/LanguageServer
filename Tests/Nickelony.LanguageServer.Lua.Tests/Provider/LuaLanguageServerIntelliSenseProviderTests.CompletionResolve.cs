using Nickelony.IDEKit.IntelliSense.Completion;
using System.Text.Json;

namespace Nickelony.LanguageServer.Lua.Tests;

/// <summary>
/// Covers the completion resolve contract: when a callback is attached, transport retries, and how
/// failures and skipped resolutions fall back to the unresolved item.
/// </summary>
public sealed partial class LuaLanguageServerIntelliSenseProviderTests
{
	[TestMethod]
	public async Task GetCompletionItemsAsync_WhenResolveIsUnsupported_LeavesItemsUnresolvable()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";

		using var client = new FakeLanguageServerClient
		{
			CompletionResponse = JsonSerializer.SerializeToElement(new
			{
				items = new object[]
				{
					new { label = "spawn", kind = 3 }
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		IReadOnlyList<TextCompletionItem> items = await provider.GetCompletionItemsAsync(filePath, "spa", 0, 3);

		Assert.AreEqual(1, items.Count);
		Assert.IsFalse(items[0].CanResolve);
		Assert.AreSame(items[0], await items[0].ResolveAsync());
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_ItemWithDetailAndDocumentation_IsNotResolved()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";

		using var client = new FakeLanguageServerClient
		{
			SupportsCompletionResolve = true,
			CompletionResponse = JsonSerializer.SerializeToElement(new
			{
				items = new object[]
				{
					new
					{
						label = "spawn",
						kind = 3,
						detail = "function spawn(room, objectName)",
						documentation = "Spawns an object."
					}
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		IReadOnlyList<TextCompletionItem> items = await provider.GetCompletionItemsAsync(filePath, "spa", 0, 3);

		// The item already carries everything resolve could add, so no resolve callback is attached and
		// no round trip is scheduled.
		Assert.AreEqual(1, items.Count);
		Assert.IsFalse(items[0].CanResolve);
		CollectionAssert.DoesNotContain(client.GetSentMethodNames(), "completionItem/resolve");
	}

	[TestMethod]
	public async Task ResolveAsync_AfterTransportChangeFailure_RetriesOnceAndAdoptsTheResolvedContent()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";

		using var client = new FakeLanguageServerClient
		{
			SupportsCompletionResolve = true,
			CompletionResponse = JsonSerializer.SerializeToElement(new
			{
				items = new object[]
				{
					new { label = "spawn", kind = 3 }
				}
			}),
			CompletionResolveResponse = JsonSerializer.SerializeToElement(new
			{
				label = "spawn",
				kind = 3,
				detail = "function spawn(room, objectName)",
				documentation = "Spawns an object."
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		IReadOnlyList<TextCompletionItem> items = await provider.GetCompletionItemsAsync(filePath, "spa", 0, 3);

		Assert.AreEqual(1, items.Count);
		Assert.IsTrue(items[0].CanResolve);

		// The resolve request crosses a transport boundary; the dispatcher restarts and retries once.
		client.TransportChangedRequestFailuresRemaining = 1;

		TextCompletionItem resolvedItem = await items[0].ResolveAsync();

		// The transport change is retried once and the resolved content is adopted.
		Assert.AreEqual("function spawn(room, objectName)", resolvedItem.Detail);
		Assert.AreEqual("Spawns an object.", resolvedItem.Documentation);
		Assert.AreEqual(2, CountSentMethods(client, "completionItem/resolve"));
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_ResolveFailure_FallsBackToTheUnresolvedItem()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";

		using var client = new FakeLanguageServerClient
		{
			SupportsCompletionResolve = true,
			ThrowInvalidOperationOnNextRequestMethod = "completionItem/resolve",
			CompletionResponse = JsonSerializer.SerializeToElement(new
			{
				items = new object[]
				{
					new { label = "spawn", kind = 3 }
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		IReadOnlyList<TextCompletionItem> items = await provider.GetCompletionItemsAsync(filePath, "spa", 0, 3);

		Assert.AreEqual(1, items.Count);
		Assert.IsTrue(items[0].CanResolve);

		TextCompletionItem resolvedItem = await items[0].ResolveAsync();

		// The failure is contained: the caller receives an item equivalent to the unresolved one and
		// carrying no resolved content.
		Assert.AreEqual(items[0].Label, resolvedItem.Label);
		Assert.AreEqual("spawn", resolvedItem.InsertText);
		Assert.IsNull(resolvedItem.Detail);
		Assert.IsNull(resolvedItem.Documentation);
	}
}

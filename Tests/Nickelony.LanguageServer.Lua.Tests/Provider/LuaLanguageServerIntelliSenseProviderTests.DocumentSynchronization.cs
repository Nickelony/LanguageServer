using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Hover;
using Nickelony.LanguageServer.Provider;
using Nickelony.LanguageServer.Testing;
using System.Text.Json;

namespace Nickelony.LanguageServer.Lua.Tests;

public sealed partial class LuaLanguageServerIntelliSenseProviderTests
{
	[TestMethod]
	public async Task PublishDiagnostics_EmptyList_ClearsTheCachedDiagnosticsAndRaisesAnEmptyUpdate()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		var publishedDiagnostics = new TaskCompletionSource<IReadOnlyList<TextDiagnostic>>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		provider.DiagnosticsUpdated += (_, eventArgs) =>
		{
			if (string.Equals(eventArgs.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
				publishedDiagnostics.TrySetResult(eventArgs.Diagnostics);
		};

		client.PublishDiagnostics(CreateDiagnostics(filePath, 1, 6, 11, "Current warning."));

		Task firstCompletedTask = await Task.WhenAny(publishedDiagnostics.Task, Task.Delay(TestPolling.DefaultTimeout)).ConfigureAwait(false);
		Assert.AreSame(publishedDiagnostics.Task, firstCompletedTask);
		Assert.AreEqual(1, provider.GetDiagnostics(filePath).Count);

		// A server that fixed the problem publishes an empty list; the cache clears and the empty update
		// is announced so consumers can drop the squiggles.
		var clearedDiagnostics = new TaskCompletionSource<IReadOnlyList<TextDiagnostic>>(TaskCreationOptions.RunContinuationsAsynchronously);
		provider.DiagnosticsUpdated += (_, eventArgs) => clearedDiagnostics.TrySetResult(eventArgs.Diagnostics);

		client.PublishDiagnostics(new PublishDiagnosticsParams(new Uri(filePath).AbsoluteUri, 1, []));

		Task clearedCompletedTask = await Task.WhenAny(clearedDiagnostics.Task, Task.Delay(TestPolling.DefaultTimeout)).ConfigureAwait(false);
		Assert.AreSame(clearedDiagnostics.Task, clearedCompletedTask);
		Assert.AreEqual(0, (await clearedDiagnostics.Task.ConfigureAwait(false)).Count);
		Assert.AreEqual(0, provider.GetDiagnostics(filePath).Count);
	}

	[TestMethod]
	public async Task RenameDocument_MovesDiagnosticsAndSemanticTokensToNewPath()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string oldFilePath = @"C:\Workspace\Scripts\test.lua";
		const string newFilePath = @"C:\Workspace\Scripts\renamed.lua";
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"]
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		var semanticTokensUpdated = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);
		var semanticTokensUpdatedForNewPath = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.SemanticTokensUpdated += (_, eventArgs) =>
		{
			if (string.Equals(eventArgs.FilePath, oldFilePath, StringComparison.OrdinalIgnoreCase))
				semanticTokensUpdated.TrySetResult(eventArgs.SemanticTokens);
			else if (string.Equals(eventArgs.FilePath, newFilePath, StringComparison.OrdinalIgnoreCase))
				semanticTokensUpdatedForNewPath.TrySetResult(eventArgs.SemanticTokens);
		};

		provider.OpenDocument(oldFilePath, content);

		Task completedTask = await Task.WhenAny(semanticTokensUpdated.Task, Task.Delay(TestPolling.DefaultTimeout)).ConfigureAwait(false);
		Assert.AreSame(semanticTokensUpdated.Task, completedTask);

		client.PublishDiagnostics(CreateDiagnostics(oldFilePath, 1, 6, 11, "Current warning."));

		Assert.AreEqual(1, provider.GetDiagnostics(oldFilePath).Count);
		Assert.AreEqual(1, provider.GetSemanticTokens(oldFilePath).Count);

		provider.RenameDocument(oldFilePath, newFilePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, TestPolling.DefaultTimeout));
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, TestPolling.DefaultTimeout));

		// The rename re-keys the cached tokens to the new path and announces them for that path without
		// waiting for the fresh server response.
		Task newPathCompletedTask = await Task.WhenAny(semanticTokensUpdatedForNewPath.Task, Task.Delay(TestPolling.DefaultTimeout)).ConfigureAwait(false);
		Assert.AreSame(semanticTokensUpdatedForNewPath.Task, newPathCompletedTask);

		IReadOnlyList<SemanticToken> movedTokens = await semanticTokensUpdatedForNewPath.Task.ConfigureAwait(false);

		Assert.AreEqual(1, movedTokens.Count);
		Assert.AreEqual(6, movedTokens[0].Character);
		Assert.AreEqual("variable", movedTokens[0].Type);

		Assert.AreEqual(0, provider.GetDiagnostics(oldFilePath).Count);
		Assert.AreEqual(0, provider.GetSemanticTokens(oldFilePath).Count);
		Assert.AreEqual(1, provider.GetDiagnostics(newFilePath).Count);
		Assert.AreEqual(1, provider.GetSemanticTokens(newFilePath).Count);

		CollectionAssert.AreEqual(
			new[]
			{
				"textDocument/didOpen",
				"textDocument/semanticTokens/full",
				"textDocument/didClose",
				"textDocument/didOpen"
			},
			client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task RenameDocument_ContentChangingRename_ClearsCachedServerStateOnTheNewPath()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string oldFilePath = @"C:\Workspace\Scripts\test.lua";
		const string newFilePath = @"C:\Workspace\Scripts\renamed.lua";

		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"]
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		var semanticTokensUpdated = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.SemanticTokensUpdated += (_, eventArgs) =>
		{
			if (string.Equals(eventArgs.FilePath, oldFilePath, StringComparison.OrdinalIgnoreCase))
				semanticTokensUpdated.TrySetResult(eventArgs.SemanticTokens);
		};

		provider.OpenDocument(oldFilePath, "local value = 1");

		Task completedTask = await Task.WhenAny(semanticTokensUpdated.Task, Task.Delay(TestPolling.DefaultTimeout)).ConfigureAwait(false);
		Assert.AreSame(semanticTokensUpdated.Task, completedTask);

		client.PublishDiagnostics(CreateDiagnostics(oldFilePath, 1, 6, 11, "Current warning."));

		Assert.AreEqual(1, provider.GetDiagnostics(oldFilePath).Count);
		Assert.AreEqual(1, provider.GetSemanticTokens(oldFilePath).Count);

		provider.RenameDocument(oldFilePath, newFilePath, "local value = 2");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, TestPolling.DefaultTimeout));
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, TestPolling.DefaultTimeout));

		// A rename that changes the content drops the renamed document's cached server state instead of
		// carrying it to the new path: the old path reports nothing and the new path starts empty until a
		// fresh server response arrives.
		Assert.AreEqual(0, provider.GetDiagnostics(oldFilePath).Count);
		Assert.AreEqual(0, provider.GetSemanticTokens(oldFilePath).Count);
		Assert.AreEqual(0, provider.GetDiagnostics(newFilePath).Count);
		Assert.AreEqual(0, provider.GetSemanticTokens(newFilePath).Count);
	}

	[TestMethod]
	public async Task PublishSemanticTokensRefreshRequested_AfterTransportFailure_KeepsTokensAndRequestsFullRefresh()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";

		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"],
			ThrowIOExceptionOnNextDidChange = true
		};

		client.EnqueueSemanticTokensFullResponse(JsonSerializer.SerializeToElement(new
		{
			data = new[] { 0, 6, 5, 0, 0 },
			resultId = "tokens-1"
		}));

		client.EnqueueSemanticTokensFullResponse(JsonSerializer.SerializeToElement(new
		{
			data = new[] { 0, 6, 5, 0, 0 },
			resultId = "tokens-2"
		}));

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		var firstTokens = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.SemanticTokensUpdated += (_, eventArgs) =>
		{
			if (string.Equals(eventArgs.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
				firstTokens.TrySetResult(eventArgs.SemanticTokens);
		};

		// An open reference keeps the document reachable for the refresh path after the reopen; the
		// open also fetches the full token set and stores its delta state.
		provider.OpenDocument(filePath, "local value = 1");

		Task firstCompletedTask = await Task.WhenAny(firstTokens.Task, Task.Delay(TestPolling.DefaultTimeout)).ConfigureAwait(false);
		Assert.AreSame(firstTokens.Task, firstCompletedTask);
		Assert.AreEqual(1, provider.GetSemanticTokens(filePath).Count);

		// The failed incremental update invalidates the tracked server synchronization: the reopened
		// document keeps its last decoded tokens, and the next refresh requests a full payload again.
		provider.UpdateDocument(filePath, "local value = 2");
		Assert.IsTrue(await client.WaitForNotificationAsync("textDocument/didChange", TestPolling.DefaultTimeout));

		// The failed send's recovery bookkeeping completes asynchronously relative to the recorded
		// notification, so re-issue the trigger until the provider re-establishes the document
		// instead of pinning one interleaving.
		await TestPolling.WaitForAsync(
			async () =>
			{
				await provider.GetHoverAsync(filePath, "local value = 2", 0, 0).ConfigureAwait(false);
				return CountSentMethods(client, "textDocument/didOpen");
			},
			static didOpenCount => didOpenCount >= 2,
			TestPolling.DefaultTimeout,
			"Expected the provider to re-establish the document after the transport failure.",
			static didOpenCount => $"didOpenCount={didOpenCount}");

		Assert.AreEqual(1, provider.GetSemanticTokens(filePath).Count);

		client.PublishSemanticTokensRefreshRequested();

		Assert.IsTrue(
			await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 2, TestPolling.DefaultTimeout),
			$"Sent methods: {string.Join(", ", client.GetSentMethodNames())}");
	}

	[TestMethod]
	public async Task SemanticTokensRefreshRequested_AfterDispose_DoesNotSend()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";

		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"]
		};

		var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		provider.OpenDocument(filePath, "local value = 1");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 1, TestPolling.DefaultTimeout));

		provider.Dispose();
		client.PublishSemanticTokensRefreshRequested();

		// Disposal unsubscribes the client event, so a refresh published afterwards produces no traffic.
		Assert.IsFalse(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 2, TimeSpan.FromMilliseconds(250)));
	}

	[TestMethod]
	public async Task RenameDocument_CaseOnlyRename_FollowsHostPathIdentity()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string oldFilePath = @"C:\Workspace\Scripts\test.lua";
		const string newFilePath = @"C:\Workspace\Scripts\Test.lua";
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		provider.OpenDocument(oldFilePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		provider.RenameDocument(oldFilePath, newFilePath, content);

		if (LanguageServerPaths.UsesCaseSensitiveLocalPaths)
		{
			// On case-sensitive hosts the rename is observable: the server sees the close and the reopen.
			Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, TestPolling.DefaultTimeout));
			Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, TestPolling.DefaultTimeout));
		}
		else
		{
			// On case-insensitive hosts a case-only rename is the same tracked document, so the
			// provider must not send rename traffic; the identity check is synchronous, so nothing can
			// arrive later either.
			Assert.IsFalse(await client.WaitForMethodCountAsync("textDocument/didClose", 1, TimeSpan.FromMilliseconds(250)));
			Assert.AreEqual(1, CountSentMethods(client, "textDocument/didOpen"));
		}
	}

	[TestMethod]
	public async Task RenameDocument_PreservesOpenReferenceCountsAcrossMultipleTabs()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string oldFilePath = @"C:\Workspace\Scripts\test.lua";
		const string newFilePath = @"C:\Workspace\Scripts\renamed.lua";
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		provider.OpenDocument(oldFilePath, content);
		provider.OpenDocument(oldFilePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		provider.RenameDocument(oldFilePath, newFilePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, TestPolling.DefaultTimeout));
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, TestPolling.DefaultTimeout));

		provider.CloseDocument(newFilePath);
		Assert.IsFalse(await client.WaitForMethodCountAsync("textDocument/didClose", 2, TimeSpan.FromMilliseconds(250)));

		provider.CloseDocument(newFilePath);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 2, TestPolling.DefaultTimeout));
	}

	[TestMethod]
	public async Task RenameDocument_UpdateOnNewPath_WaitsForRenameReopenToFinish()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string oldFilePath = @"C:\Workspace\Scripts\test.lua";
		const string newFilePath = @"C:\Workspace\Scripts\renamed.lua";
		const string originalContent = "local value = 1";
		const string updatedContent = "local value = 2";

		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = false
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		provider.OpenDocument(oldFilePath, originalContent);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		client.BlockNextOpenNotification();

		provider.RenameDocument(oldFilePath, newFilePath, originalContent);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, TestPolling.DefaultTimeout));
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, TestPolling.DefaultTimeout));

		provider.UpdateDocument(newFilePath, updatedContent);
		Assert.IsFalse(await client.WaitForMethodCountAsync("textDocument/didChange", 1, TimeSpan.FromMilliseconds(250)));

		client.ReleaseOpenNotification();
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 1, TestPolling.DefaultTimeout));

		CollectionAssert.AreEqual(
			new[]
			{
				"textDocument/didOpen",
				"textDocument/didClose",
				"textDocument/didOpen",
				"textDocument/didChange"
			},
			client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task RenameDocument_DisposeDuringBlockedRenameReopen_DoesNotRaiseDiagnosticsUpdatedForNewPath()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string oldFilePath = @"C:\Workspace\Scripts\test.lua";
		const string newFilePath = @"C:\Workspace\Scripts\renamed.lua";
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = false
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		int movedDiagnosticsUpdatedCount = 0;

		provider.DiagnosticsUpdated += (_, eventArgs) =>
		{
			if (string.Equals(eventArgs.FilePath, newFilePath, StringComparison.OrdinalIgnoreCase))
				movedDiagnosticsUpdatedCount++;
		};

		provider.OpenDocument(oldFilePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout).ConfigureAwait(false));

		client.PublishDiagnostics(CreateDiagnostics(oldFilePath, 1, 6, 11, "Current warning."));
		Assert.AreEqual(1, provider.GetDiagnostics(oldFilePath).Count);

		client.BlockNextOpenNotification();

		provider.RenameDocument(oldFilePath, newFilePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, TestPolling.DefaultTimeout).ConfigureAwait(false));
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, TestPolling.DefaultTimeout).ConfigureAwait(false));

		provider.Dispose();
		client.ReleaseOpenNotification();

		// Negative check: no observable signal exists for "no callback arrived", so the bounded window is deliberate.
		await Task.Delay(250).ConfigureAwait(false);

		Assert.AreEqual(0, movedDiagnosticsUpdatedCount);
	}

	[TestMethod]
	public async Task GetHoverAsync_RestartsAfterConsecutiveTimeoutsOnSameTransportGeneration()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			TimedOutHoverRequestsRemaining = 2,
			HoverResponse = JsonSerializer.SerializeToElement(new
			{
				contents = new
				{
					kind = "markdown",
					value = "Hover docs."
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider(
			[workspaceRoot],
			client,
			providerOptions: new LanguageServerProviderOptions
			{
				RequestTimeout = TimeSpan.FromMilliseconds(50),
				RequestTimeoutRestartThreshold = 2
			});

		TextHoverInfo? firstHover = await provider.GetHoverAsync(filePath, content, 0, 0);
		TextHoverInfo? secondHover = await provider.GetHoverAsync(filePath, content, 0, 0);
		TextHoverInfo? thirdHover = await provider.GetHoverAsync(filePath, content, 0, 0);

		Assert.IsNull(firstHover);
		Assert.IsNull(secondHover);
		Assert.IsNotNull(thirdHover);
		Assert.AreEqual("Hover docs.", thirdHover.Content);
		Assert.AreEqual(1, client.MarkTransportUnhealthyCallCount);
		Assert.AreEqual(2, client.StartCallCount);

		client.TimedOutHoverRequestsRemaining = 1;

		TextHoverInfo? fourthHover = await provider.GetHoverAsync(filePath, content, 0, 0);

		Assert.IsNull(fourthHover);
		Assert.AreEqual(1, client.MarkTransportUnhealthyCallCount);

		CollectionAssert.AreEqual(
			new[]
			{
				"textDocument/didOpen",
				"textDocument/hover",
				"textDocument/hover",
				"textDocument/didOpen",
				"textDocument/hover",
				"textDocument/hover"
			},
			client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task GetHoverAsync_TimeoutFromSupersededGeneration_DoesNotInvalidateReplacementTransport()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			TimedOutHoverRequestsRemaining = 1,
			HoverResponse = JsonSerializer.SerializeToElement(new
			{
				contents = new
				{
					kind = "markdown",
					value = "Hover docs."
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider(
			[workspaceRoot],
			client,
			providerOptions: new LanguageServerProviderOptions
			{
				RequestTimeout = TimeSpan.FromMilliseconds(200),
				RequestTimeoutRestartThreshold = 1
			});

		Task<TextHoverInfo?> timedOutHoverTask = provider.GetHoverAsync(filePath, content, 0, 0);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/hover", 1, TestPolling.DefaultTimeout));

		client.TryMarkTransportUnhealthy(client.TransportGeneration);

		TextHoverInfo? restartedHover = await provider.GetHoverAsync(filePath, content, 0, 0);
		TextHoverInfo? timedOutHover = await timedOutHoverTask;
		TextHoverInfo? thirdHover = await provider.GetHoverAsync(filePath, content, 0, 0);

		Assert.IsNotNull(restartedHover);
		Assert.AreEqual("Hover docs.", restartedHover.Content);
		Assert.IsNull(timedOutHover);
		Assert.IsNotNull(thirdHover);
		Assert.AreEqual("Hover docs.", thirdHover.Content);
		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(2, client.StartCallCount);

		CollectionAssert.AreEqual(
			new[]
			{
				"textDocument/didOpen",
				"textDocument/hover",
				"textDocument/didOpen",
				"textDocument/hover",
				"textDocument/hover"
			},
			client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task GetHoverAsync_RequestCancellationAfterConnectionDrop_DoesNotForceRestartAttempt()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			FailStartWhenCancellationRequested = true,
			HoverResponse = JsonSerializer.SerializeToElement(new
			{
				contents = new
				{
					kind = "markdown",
					value = "Hover docs."
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		await provider.GetHoverAsync(filePath, content, 0, 0);

		client.IsReady = false;

		using var cancellationTokenSource = new CancellationTokenSource();
		cancellationTokenSource.Cancel();

		Task<TextHoverInfo?> hoverTask = provider.GetHoverAsync(filePath, content, 0, 0, cancellationTokenSource.Token);

		await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => hoverTask);

		Assert.AreEqual(1, client.StartCallCount);
		Assert.AreEqual(1, client.StartCancellationTokenCanBeCanceled.Count);
	}

	[TestMethod]
	public async Task GetHoverAsync_InternalRequestCancellation_DoesNotCountAsTimeoutOrRestart()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			CancelNextHoverRequestWithoutTimeout = true,
			HoverResponse = JsonSerializer.SerializeToElement(new
			{
				contents = new
				{
					kind = "markdown",
					value = "Hover docs."
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider(
			[workspaceRoot],
			client,
			providerOptions: new LanguageServerProviderOptions
			{
				RequestTimeout = TimeSpan.FromMilliseconds(50),
				RequestTimeoutRestartThreshold = 1
			});

		TextHoverInfo? canceledHover = await provider.GetHoverAsync(filePath, content, 0, 0);
		TextHoverInfo? recoveredHover = await provider.GetHoverAsync(filePath, content, 0, 0);

		Assert.IsNull(canceledHover);
		Assert.IsNotNull(recoveredHover);
		Assert.AreEqual("Hover docs.", recoveredHover.Content);
		Assert.AreEqual(0, client.MarkTransportUnhealthyCallCount);
		Assert.AreEqual(1, client.StartCallCount);

		CollectionAssert.AreEqual(
			new[]
			{
				"textDocument/didOpen",
				"textDocument/hover",
				"textDocument/hover"
			},
			client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task GetHoverAsync_UserCancellation_DoesNotBlockClosingOpenDocument()
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

		client.BlockNextHoverRequest();

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		using var cancellationTokenSource = new CancellationTokenSource();

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout).ConfigureAwait(false));

		Task<TextHoverInfo?> hoverTask = provider.GetHoverAsync(filePath, content, 0, 0, cancellationTokenSource.Token);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/hover", 1, TestPolling.DefaultTimeout).ConfigureAwait(false));

		cancellationTokenSource.Cancel();
		await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => hoverTask).ConfigureAwait(false);

		provider.CloseDocument(filePath);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, TestPolling.DefaultTimeout).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task UpdateDocument_SendsFullTextChangeWhenServerAdvertisesFullSync()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";

		using var client = new FakeLanguageServerClient
		{
			TextDocumentSyncKind = TextDocumentSyncKind.Full
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		provider.OpenDocument(filePath, "local value = 1");
		provider.UpdateDocument(filePath, "local value = 2");
		Assert.IsTrue(await client.WaitForNotificationAsync("textDocument/didChange", TestPolling.DefaultTimeout));

		JsonElement parameters = client.GetLastNotificationParameters("textDocument/didChange");
		JsonElement change = parameters.GetProperty("contentChanges")[0];

		Assert.AreEqual("local value = 2", change.GetProperty("text").GetString());
		Assert.IsFalse(change.TryGetProperty("range", out _));
	}

	[TestMethod]
	public async Task UpdateDocument_SendsIncrementalChangeRangeWhenServerAdvertisesIncrementalSync()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";

		using var client = new FakeLanguageServerClient
		{
			TextDocumentSyncKind = TextDocumentSyncKind.Incremental
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		provider.OpenDocument(filePath, "local value = 1");
		provider.UpdateDocument(filePath, "local value = 2");
		Assert.IsTrue(await client.WaitForNotificationAsync("textDocument/didChange", TestPolling.DefaultTimeout));

		JsonElement parameters = client.GetLastNotificationParameters("textDocument/didChange");
		JsonElement change = parameters.GetProperty("contentChanges")[0];

		// The change carries the replaced span as a protocol range plus the replacement text, so the
		// server can apply an incremental edit instead of resynchronizing the whole document.
		Assert.AreEqual("2", change.GetProperty("text").GetString());

		JsonElement range = change.GetProperty("range");

		Assert.AreEqual(0, range.GetProperty("start").GetProperty("line").GetInt32());
		Assert.AreEqual(14, range.GetProperty("start").GetProperty("character").GetInt32());
		Assert.AreEqual(0, range.GetProperty("end").GetProperty("line").GetInt32());
		Assert.AreEqual(15, range.GetProperty("end").GetProperty("character").GetInt32());
	}

	[TestMethod]
	public async Task UpdateDocument_WithUnchangedContent_DoesNotSendDidChange()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		provider.UpdateDocument(filePath, content);
		Assert.IsFalse(await client.WaitForNotificationAsync("textDocument/didChange", TimeSpan.FromMilliseconds(250)));

		CollectionAssert.AreEqual(
			new[] { "textDocument/didOpen" },
			client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task UpdateDocument_CoalescesSupersededQueuedChanges()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";

		using var client = new FakeLanguageServerClient
		{
			TextDocumentSyncKind = TextDocumentSyncKind.Full
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		provider.OpenDocument(filePath, "local value = 1");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		client.BlockNextChangeNotification();

		provider.UpdateDocument(filePath, "local value = 2");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 1, TestPolling.DefaultTimeout));

		provider.UpdateDocument(filePath, "local value = 3");
		provider.UpdateDocument(filePath, "local value = 4");

		client.ReleaseChangeNotification();

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 2, TestPolling.DefaultTimeout));
		Assert.IsFalse(await client.WaitForMethodCountAsync("textDocument/didChange", 3, TimeSpan.FromMilliseconds(250)));

		JsonElement parameters = client.GetLastNotificationParameters("textDocument/didChange");
		JsonElement change = parameters.GetProperty("contentChanges")[0];

		Assert.AreEqual("local value = 4", change.GetProperty("text").GetString());

		CollectionAssert.AreEqual(
			new[] { "textDocument/didOpen", "textDocument/didChange", "textDocument/didChange" },
			client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task UpdateDocument_BlockedChangeOnOneFile_DoesNotStallOtherFileHover()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string firstFilePath = @"C:\Workspace\Scripts\first.lua";
		const string secondFilePath = @"C:\Workspace\Scripts\second.lua";

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

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		provider.OpenDocument(firstFilePath, "local first = 1");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		client.BlockNextChangeNotification();

		provider.UpdateDocument(firstFilePath, "local first = 2");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 1, TestPolling.DefaultTimeout));

		Task<TextHoverInfo?> hoverTask = provider.GetHoverAsync(secondFilePath, "local second = 1", 0, 0);
		Task completedTask = await Task.WhenAny(hoverTask, Task.Delay(TestPolling.DefaultTimeout)).ConfigureAwait(false);

		Assert.AreSame(hoverTask, completedTask);
		Assert.IsNotNull(await hoverTask.ConfigureAwait(false));
		Assert.AreEqual(2, CountSentMethods(client, "textDocument/didOpen"));
		Assert.AreEqual(1, CountSentMethods(client, "textDocument/hover"));

		client.ReleaseChangeNotification();

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 1, TestPolling.DefaultTimeout));
	}

	[TestMethod]
	public async Task UpdateDocument_WithUnchangedContentAfterTransportFailure_ReopensWithFullSemanticTokensRefresh()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";

		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"]
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		provider.OpenDocument(filePath, "local value = 1");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 1, TestPolling.DefaultTimeout));

		client.ThrowIOExceptionOnNextDidChange = true;

		provider.UpdateDocument(filePath, "local value = 2");
		Assert.IsTrue(await client.WaitForNotificationAsync("textDocument/didChange", TestPolling.DefaultTimeout));

		// The failed send's recovery bookkeeping completes asynchronously relative to the recorded
		// notification, so re-issue the content-unchanged update until the provider re-establishes
		// the document instead of pinning one interleaving.
		await TestPolling.WaitForAsync(
			() =>
			{
				provider.UpdateDocument(filePath, "local value = 2");
				return Task.FromResult(CountSentMethods(client, "textDocument/didOpen"));
			},
			static didOpenCount => didOpenCount >= 2,
			TestPolling.DefaultTimeout,
			"Expected the provider to re-establish the document after the transport failure.",
			static didOpenCount => $"didOpenCount={didOpenCount}");

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 2, TestPolling.DefaultTimeout));

		CollectionAssert.AreEqual(
			new[]
			{
				"textDocument/didOpen",
				"textDocument/semanticTokens/full",
				"textDocument/didChange",
				"textDocument/didOpen",
				"textDocument/semanticTokens/full"
			},
			client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task GetHoverAsync_ReplaysTrackedDocumentsAfterLanguageServerRestart()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		await provider.GetHoverAsync(filePath, content, 0, 0);

		CollectionAssert.AreEqual(
			new[] { "textDocument/didOpen", "textDocument/hover" },
			client.GetSentMethodNames());

		Assert.AreEqual(1, client.StartCallCount);

		client.IsReady = false;

		await provider.GetHoverAsync(filePath, content, 0, 0);

		CollectionAssert.AreEqual(
			new[]
			{
				"textDocument/didOpen",
				"textDocument/hover",
				"textDocument/didOpen",
				"textDocument/hover"
			},
			client.GetSentMethodNames());

		Assert.AreEqual(2, client.StartCallCount);
	}

	[TestMethod]
	public async Task OpenDocument_DuringStartupFailure_ReplaysTrackedDocumentAfterRecovery()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string openedFilePath = @"C:\Workspace\Scripts\opened.lua";
		const string requestFilePath = @"C:\Workspace\Scripts\request.lua";
		const string openedContent = "local opened = 1";

		using var client = new FakeLanguageServerClient
		{
			StartResult = false,
			HoverResponse = JsonSerializer.SerializeToElement(new
			{
				contents = new
				{
					kind = "markdown",
					value = "Hover docs."
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		provider.OpenDocument(openedFilePath, openedContent);

		// Deliberate negative-assertion window: no observable signal exists for "the startup attempt
		// completed with a failure and the document open was dropped". The bounded wait follows the
		// recorded negative-assertion-window stance used by the Client test suite.
		await Task.Delay(100).ConfigureAwait(false);

		Assert.AreEqual(1, client.StartCallCount);
		Assert.AreEqual(0, CountSentMethods(client, "textDocument/didOpen"));

		client.StartResult = true;

		TextHoverInfo? hover = await provider.GetHoverAsync(requestFilePath, "local request = 1", 0, 0);

		Assert.IsNotNull(hover);
		Assert.AreEqual(2, client.StartCallCount);

		CollectionAssert.AreEqual(
			new[]
			{
				"textDocument/didOpen",
				"textDocument/didOpen",
				"textDocument/hover"
			},
			client.GetSentMethodNames());

		JsonElement firstDidOpen = client.GetLastNotificationParameters("textDocument/didOpen");
		Assert.AreEqual(new Uri(requestFilePath).AbsoluteUri, firstDidOpen.GetProperty("textDocument").GetProperty("uri").GetString());
	}

	[TestMethod]
	public async Task GetHoverAsync_ReplaysUntouchedTrackedDocumentsAfterFailedRestartRetry()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string firstFilePath = @"C:\Workspace\Scripts\first.lua";
		const string secondFilePath = @"C:\Workspace\Scripts\second.lua";
		const string firstContent = "local first = 1";
		const string secondContent = "local second = 2";

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

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		provider.OpenDocument(firstFilePath, firstContent);
		provider.OpenDocument(secondFilePath, secondContent);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, TestPolling.DefaultTimeout));
		Assert.AreEqual(1, client.StartCallCount);

		client.IsReady = false;
		client.StartResult = false;

		TextHoverInfo? failedHover = await provider.GetHoverAsync(firstFilePath, firstContent, 0, 0);

		Assert.IsNull(failedHover);
		Assert.AreEqual(2, client.StartCallCount);

		client.StartResult = true;

		TextHoverInfo? recoveredHover = await provider.GetHoverAsync(firstFilePath, firstContent, 0, 0);

		Assert.IsNotNull(recoveredHover);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 4, TestPolling.DefaultTimeout));
		Assert.AreEqual(3, client.StartCallCount);

		CollectionAssert.AreEqual(
			new[]
			{
				"textDocument/didOpen",
				"textDocument/didOpen",
				"textDocument/didOpen",
				"textDocument/didOpen",
				"textDocument/hover"
			},
			client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task DiagnosticsPublished_IgnoresVersionMismatchAndStoresMatchingVersion()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";
		const string firstContent = "local value = 1";
		const string secondContent = "local second = 2";

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		int diagnosticsUpdatedCount = 0;
		provider.DiagnosticsUpdated += (_, _) => diagnosticsUpdatedCount++;

		await provider.GetHoverAsync(filePath, firstContent, 0, 0);
		await provider.GetHoverAsync(filePath, secondContent, 0, 0);

		client.PublishDiagnostics(CreateDiagnostics(filePath, 1, 6, 12, "Stale warning."));

		Assert.AreEqual(0, diagnosticsUpdatedCount);
		Assert.AreEqual(0, provider.GetDiagnostics(filePath).Count);

		client.PublishDiagnostics(CreateDiagnostics(filePath, 3, 6, 12, "Future warning."));

		Assert.AreEqual(0, diagnosticsUpdatedCount);
		Assert.AreEqual(0, provider.GetDiagnostics(filePath).Count);

		client.PublishDiagnostics(CreateDiagnostics(filePath, 2, 6, 12, "Current warning."));

		IReadOnlyList<TextDiagnostic> diagnostics = provider.GetDiagnostics(filePath);

		Assert.AreEqual(1, diagnosticsUpdatedCount);
		Assert.AreEqual(1, diagnostics.Count);
		Assert.AreEqual(6, diagnostics[0].StartOffset);
		Assert.AreEqual(12, diagnostics[0].EndOffset);
	}

	[TestMethod]
	public async Task DiagnosticsPublished_WithoutVersion_StoresFallbackDiagnosticsForTrackedDocument()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		int diagnosticsUpdatedCount = 0;

		provider.DiagnosticsUpdated += (_, _) => diagnosticsUpdatedCount++;

		await provider.GetHoverAsync(filePath, content, 0, 0);

		client.PublishDiagnostics(CreateDiagnostics(filePath, version: null, 6, 11, "Fallback warning."));

		IReadOnlyList<TextDiagnostic> diagnostics = provider.GetDiagnostics(filePath);

		Assert.AreEqual(1, diagnosticsUpdatedCount);
		Assert.AreEqual(1, diagnostics.Count);
		Assert.AreEqual(TextDiagnosticSeverity.Warning, diagnostics[0].Severity);
		Assert.AreEqual(6, diagnostics[0].StartOffset);
		Assert.AreEqual(11, diagnostics[0].EndOffset);
	}

	[TestMethod]
	public async Task DiagnosticsPublished_OneSubscriberExceptionDoesNotSuppressLaterSubscribers()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		int notifiedSubscribers = 0;

		provider.DiagnosticsUpdated += (_, _) =>
		{
			notifiedSubscribers++;
			throw new InvalidOperationException("Simulated diagnostics subscriber failure.");
		};

		provider.DiagnosticsUpdated += (_, _) => notifiedSubscribers++;

		await provider.GetHoverAsync(filePath, content, 0, 0);
		client.PublishDiagnostics(CreateDiagnostics(filePath, 1, 6, 12, "Current warning."));

		Assert.AreEqual(2, notifiedSubscribers);
	}

	[TestMethod]
	public async Task DiagnosticsPublished_DisposeInFirstSubscriber_DoesNotNotifyLaterSubscribers()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		int firstSubscriberCalls = 0;
		int secondSubscriberCalls = 0;

		provider.DiagnosticsUpdated += (_, _) =>
		{
			firstSubscriberCalls++;
			provider.Dispose();
		};

		provider.DiagnosticsUpdated += (_, _) => secondSubscriberCalls++;

		await provider.GetHoverAsync(filePath, content, 0, 0);
		client.PublishDiagnostics(CreateDiagnostics(filePath, 1, 6, 12, "Current warning."));

		Assert.AreEqual(1, firstSubscriberCalls);
		Assert.AreEqual(0, secondSubscriberCalls);
	}

	[TestMethod]
	public async Task DiagnosticsPublished_DisposeWhileSubscriberIsRunning_DoesNotAdmitLaterSubscribers()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		var firstSubscriberEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var releaseFirstSubscriber = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		int secondSubscriberCalls = 0;

		provider.DiagnosticsUpdated += (_, _) =>
		{
			firstSubscriberEntered.TrySetResult(true);
			releaseFirstSubscriber.Task.GetAwaiter().GetResult();
		};

		provider.DiagnosticsUpdated += (_, _) => secondSubscriberCalls++;

		await provider.GetHoverAsync(filePath, content, 0, 0);

		Task publishTask = Task.Run(() => client.PublishDiagnostics(CreateDiagnostics(filePath, 1, 6, 12, "Current warning.")));
		Task enteredTask = await Task.WhenAny(firstSubscriberEntered.Task, Task.Delay(TestPolling.DefaultTimeout)).ConfigureAwait(false);
		Assert.AreSame(firstSubscriberEntered.Task, enteredTask);

		provider.Dispose();
		releaseFirstSubscriber.TrySetResult(true);
		await publishTask.ConfigureAwait(false);

		Assert.AreEqual(0, secondSubscriberCalls);
	}

	[TestMethod]
	public async Task UpdateDocument_WaitsForEarlierOpenNotificationToFinish()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		client.BlockNextOpenNotification();

		provider.OpenDocument(filePath, "local value = 1");
		provider.UpdateDocument(filePath, "local value = 2");

		Assert.IsFalse(await client.WaitForNotificationAsync("textDocument/didChange", TimeSpan.FromMilliseconds(250)));

		client.ReleaseOpenNotification();

		Assert.IsTrue(await client.WaitForNotificationAsync("textDocument/didChange", TestPolling.DefaultTimeout));

		CollectionAssert.AreEqual(
			new[] { "textDocument/didOpen", "textDocument/didChange" },
			client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task UpdateDocument_DisposeBeforeQueuedLatestUpdateRuns_DoesNotSendLateDidChange()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		client.BlockNextOpenNotification();

		provider.OpenDocument(filePath, "local value = 1");
		provider.UpdateDocument(filePath, "local value = 2");

		Assert.IsFalse(await client.WaitForNotificationAsync("textDocument/didChange", TimeSpan.FromMilliseconds(250)).ConfigureAwait(false));

		provider.Dispose();
		client.ReleaseOpenNotification();

		Assert.IsFalse(await client.WaitForNotificationAsync("textDocument/didChange", TimeSpan.FromMilliseconds(250)).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task GetHoverAsync_ReopensDocumentAfterIncrementalChangeTransportFailure()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";

		using var client = new FakeLanguageServerClient
		{
			ThrowIOExceptionOnNextDidChange = true
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		await provider.GetHoverAsync(filePath, "local value = 1", 0, 0);
		provider.UpdateDocument(filePath, "local value = 2");

		Assert.IsTrue(await client.WaitForNotificationAsync("textDocument/didChange", TestPolling.DefaultTimeout));

		// Wait for the failed update's transport-failure cleanup to land before hovering: the didChange record
		// is observable before the provider has marked the transport unavailable, and a hover inside that
		// window observes a transiently unavailable transport (documented fallback) instead of the reopen
		// under test. The next request after the cleanup restarts the transport and reopens the document.
		await TestPolling.WaitForAsync(
			() => Task.FromResult(provider.State),
			static state => state == LanguageServerProviderState.Unavailable,
			TestPolling.DefaultTimeout,
			"Expected the failed update to mark the transport unavailable.",
			static state => $"state={state}").ConfigureAwait(false);

		await provider.GetHoverAsync(filePath, "local value = 2", 0, 0);

		CollectionAssert.AreEqual(
			new[]
			{
				"textDocument/didOpen",
				"textDocument/hover",
				"textDocument/didChange",
				"textDocument/didOpen",
				"textDocument/hover"
			},
			client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task CloseDocument_WaitsForQueuedOpenNotificationToFinish()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		client.BlockNextOpenNotification();

		provider.OpenDocument(filePath, "local value = 1");
		provider.CloseDocument(filePath);

		Assert.IsFalse(await client.WaitForNotificationAsync("textDocument/didClose", TimeSpan.FromMilliseconds(250)));

		client.ReleaseOpenNotification();

		Assert.IsTrue(await client.WaitForNotificationAsync("textDocument/didClose", TestPolling.DefaultTimeout));

		CollectionAssert.AreEqual(
			new[] { "textDocument/didOpen", "textDocument/didClose" },
			client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task CloseDocument_SendsDidClosePayloadWithDocumentUri()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		provider.OpenDocument(filePath, "local value = 1");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		provider.CloseDocument(filePath);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, TestPolling.DefaultTimeout));

		JsonElement parameters = client.GetLastNotificationParameters("textDocument/didClose");
		Assert.AreEqual(new Uri(filePath).AbsoluteUri, parameters.GetProperty("textDocument").GetProperty("uri").GetString());
	}

	[TestMethod]
	public async Task GetHoverAsync_TwoRoots_ReplaysTrackedDocumentsFromEveryRootAfterRestart()
	{
		const string primaryRoot = @"C:\Workspace";
		const string secondaryRoot = @"C:\Secondary";
		const string primaryFilePath = @"C:\Workspace\Scripts\primary.lua";
		const string secondaryFilePath = @"C:\Secondary\Scripts\secondary.lua";
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([primaryRoot, secondaryRoot], client);

		provider.OpenDocument(primaryFilePath, content);
		provider.OpenDocument(secondaryFilePath, content);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, TestPolling.DefaultTimeout));
		Assert.AreEqual(1, client.StartCallCount);

		client.IsReady = false;

		await provider.GetHoverAsync(secondaryFilePath, content, 0, 0);

		Assert.AreEqual(2, client.StartCallCount);
		Assert.AreEqual(4, CountSentMethods(client, "textDocument/didOpen"));

		JsonElement[] didOpenNotifications = client.GetNotificationParameters("textDocument/didOpen");
		var reopenedDocumentUris = new List<string>();

		for (int i = 2; i < didOpenNotifications.Length; i++)
			reopenedDocumentUris.Add(didOpenNotifications[i].GetProperty("textDocument").GetProperty("uri").GetString()!);

		CollectionAssert.AreEquivalent(
			new[]
			{
				LanguageServerPaths.CreateFileUri(primaryFilePath),
				LanguageServerPaths.CreateFileUri(secondaryFilePath)
			},
			reopenedDocumentUris);
	}

	[TestMethod]
	public async Task UpdateDocument_WithNoneTextDocumentSyncKind_LogsAndSkipsTheChange()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";
		const string content = "local value = 1";

		using var logScope = new TestLoggerScope(LogLevel.Warning);
		using var client = new FakeLanguageServerClient
		{
			// None is the value a server reports before initialization completes: the provider must not
			// send a change it cannot express and must contain the fault through the shared observer.
			TextDocumentSyncKind = TextDocumentSyncKind.None
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client, logger: logScope.CreateLogger<LuaLanguageServerIntelliSenseProvider>());

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		provider.UpdateDocument(filePath, content + "\nprint(value)");

		// The contained background fault is observable as a recorded warning; its message shape is
		// pinned by the BackgroundTaskObserver tests, not here. The poll runs against the shared
		// timeout budget instead of a fixed 1-second loop, so a loaded machine cannot turn the
		// recorded-warning observation into a spurious failure.
		await TestPolling.WaitForAsync(
			() => Task.FromResult(logScope.Logs.Count),
			static count => count > 0,
			TestPolling.DefaultTimeout,
			"Expected the contained background fault to be logged.",
			static count => $"recordedWarnings={count}").ConfigureAwait(false);
		Assert.AreEqual(0, CountSentMethods(client, "textDocument/didChange"));
	}

	[TestMethod]
	public async Task DiagnosticsPublished_ForUntrackedDocument_IsIgnored()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string trackedFilePath = @"C:\Workspace\Scripts\tracked.lua";
		const string untrackedFilePath = @"C:\Workspace\Scripts\untracked.lua";

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		var updates = new List<(string FilePath, int Count)>();
		var trackedUpdate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.DiagnosticsUpdated += (_, eventArgs) =>
		{
			lock (updates)
				updates.Add((eventArgs.FilePath, eventArgs.Diagnostics.Count));

			if (string.Equals(eventArgs.FilePath, trackedFilePath, StringComparison.OrdinalIgnoreCase))
				trackedUpdate.TrySetResult(true);
		};

		await provider.GetHoverAsync(trackedFilePath, "local value = 1", 0, 0);

		client.PublishDiagnostics(CreateDiagnostics(untrackedFilePath, 1, 0, 1, "Untracked warning."));
		client.PublishDiagnostics(CreateDiagnostics(trackedFilePath, 1, 6, 11, "Tracked warning."));

		Task completedTask = await Task.WhenAny(trackedUpdate.Task, Task.Delay(TestPolling.DefaultTimeout)).ConfigureAwait(false);
		Assert.AreSame(trackedUpdate.Task, completedTask);

		lock (updates)
		{
			CollectionAssert.AreEqual(
				new[] { trackedFilePath },
				updates.Select(update => update.FilePath).ToArray());
		}
	}
}

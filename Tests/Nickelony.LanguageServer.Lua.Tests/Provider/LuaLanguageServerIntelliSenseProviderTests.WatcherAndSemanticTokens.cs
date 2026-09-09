using System.Text.Json;

namespace Nickelony.LanguageServer.Lua.Tests;

public sealed partial class LuaLanguageServerIntelliSenseProviderTests
{
	[TestMethod]
	public async Task GetHoverAsync_RetriesWorkspaceWatcherStartAfterWorkspaceDirectoryAppears()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "LuaWatcherRetry_" + Guid.NewGuid().ToString("N"));
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.lua");
		const string content = "local value = 1";

		try
		{
			using var client = new FakeLanguageServerClient();
			using var provider = CreateProviderWithWatcherCapture(workspaceRoot, client, out _);
			var failures = new List<WorkspaceWatcherFailure>();

			provider.WorkspaceWatcherFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

			await provider.GetHoverAsync(filePath, content, 0, 0);

			Assert.IsNull(GetWorkspaceWatcher(provider));
			Assert.AreEqual(0, failures.Count);

			Directory.CreateDirectory(workspaceRoot);

			await provider.GetHoverAsync(filePath, content, 0, 0);

			Assert.IsNotNull(GetWorkspaceWatcher(provider));
			Assert.AreEqual(0, failures.Count);
		}
		finally
		{
			TestTempDirectories.Delete(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task GetHoverAsync_WatcherStartupFailure_RaisesWorkspaceWatcherFailureOnce()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "LuaWatcherStartupFailure_" + Guid.NewGuid().ToString("N"));
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.lua");
		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(workspaceRoot);

			using var client = new FakeLanguageServerClient();

			using var provider = new LuaLanguageServerIntelliSenseProvider(
				[workspaceRoot],
				client,
				workspaceFileWatcherFactory: (rootPath, dispatchAsync, onWatcherFailed) => new WorkspaceFileWatcher(
					rootPath,
					dispatchAsync,
					LuaWorkspaceConventions.WatchSpecifications,
					onWatcherFailed,
					static (_, _) => throw new InvalidOperationException("Simulated watcher creation failure.")));

			var failures = new List<WorkspaceWatcherFailure>();

			provider.WorkspaceWatcherFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

			await provider.GetHoverAsync(filePath, content, 0, 0);
			await provider.GetHoverAsync(filePath, content, 0, 0);

			Assert.IsNull(GetWorkspaceWatcher(provider));
			Assert.AreEqual(1, failures.Count);
			Assert.IsTrue(failures[0].Message.Contains("could not be started", StringComparison.OrdinalIgnoreCase));
		}
		finally
		{
			TestTempDirectories.Delete(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task WorkspaceWatcherFailure_AutomaticallyRestartsWithoutRaisingFailure()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "LuaWatcherFailure_" + Guid.NewGuid().ToString("N"));
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.lua");
		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(workspaceRoot);

			using var client = new FakeLanguageServerClient();
			using var provider = CreateProviderWithWatcherCapture(workspaceRoot, client, out _);
			var failures = new List<WorkspaceWatcherFailure>();

			provider.WorkspaceWatcherFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

			await provider.GetHoverAsync(filePath, content, 0, 0);

			WorkspaceFileWatcher watcher = GetWorkspaceWatcher(provider)
				?? throw new AssertFailedException("Expected the workspace watcher to start.");

			Assert.IsFalse(watcher.IsDisposed);

			SimulateWatcherFailure(watcher, new IOException("Simulated watcher failure."));

			WorkspaceFileWatcher replacementWatcher = GetWorkspaceWatcher(provider)
				?? throw new AssertFailedException("Expected the workspace watcher to restart.");

			Assert.AreNotSame(watcher, replacementWatcher);
			Assert.IsTrue(watcher.IsDisposed);
			Assert.IsFalse(replacementWatcher.IsDisposed);
			Assert.AreEqual(0, failures.Count);
		}
		finally
		{
			TestTempDirectories.Delete(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task WorkspaceWatcherFailure_WhenReplacementRestartFails_DisposesBothWatchersAndRaisesFailure()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "LuaWatcherFailure_" + Guid.NewGuid().ToString("N"));
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.lua");
		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(workspaceRoot);

			using var client = new FakeLanguageServerClient();
			var createdWatchers = new List<WorkspaceFileWatcher>();
			int watcherCreationCount = 0;
			Action<WorkspaceFileWatcher, Exception?>? onWatcherFailed = null;

			using var provider = new LuaLanguageServerIntelliSenseProvider(
				[workspaceRoot],
				client,
				workspaceFileWatcherFactory: (rootPath, dispatchAsync, registeredOnWatcherFailed) =>
				{
					onWatcherFailed = registeredOnWatcherFailed;

					var watcher = new WorkspaceFileWatcher(
						rootPath,
						dispatchAsync,
						LuaWorkspaceConventions.WatchSpecifications,
						registeredOnWatcherFailed,
						watcherCreationCount++ == 0
							? null
							: static (_, _) => throw new InvalidOperationException("Simulated watcher creation failure."));
					createdWatchers.Add(watcher);
					return watcher;
				});

			var failures = new List<WorkspaceWatcherFailure>();
			provider.WorkspaceWatcherFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

			await provider.GetHoverAsync(filePath, content, 0, 0);

			WorkspaceFileWatcher watcher = createdWatchers[0];

			onWatcherFailed!(watcher, new IOException("Simulated watcher failure."));

			Assert.AreEqual(2, createdWatchers.Count);
			Assert.IsTrue(createdWatchers[0].IsDisposed);
			Assert.IsTrue(createdWatchers[1].IsDisposed);
			Assert.IsNull(GetWorkspaceWatcher(provider));
			Assert.AreEqual(1, failures.Count);
		}
		finally
		{
			TestTempDirectories.Delete(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task WorkspaceWatcherFailure_RepeatedAutomaticRestartsContinueWithoutRaisingFailure()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "LuaWatcherRepeatedFailure_" + Guid.NewGuid().ToString("N"));
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.lua");
		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(workspaceRoot);

			using var client = new FakeLanguageServerClient();
			using var provider = CreateProviderWithWatcherCapture(workspaceRoot, client, out _);
			var failures = new List<WorkspaceWatcherFailure>();

			provider.WorkspaceWatcherFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

			await provider.GetHoverAsync(filePath, content, 0, 0);

			WorkspaceFileWatcher firstWatcher = GetWorkspaceWatcher(provider)
				?? throw new AssertFailedException("Expected the initial workspace watcher to start.");

			SimulateWatcherFailure(firstWatcher, new IOException("Simulated watcher failure 1."));

			WorkspaceFileWatcher secondWatcher = GetWorkspaceWatcher(provider)
				?? throw new AssertFailedException("Expected the first replacement watcher to start.");

			SimulateWatcherFailure(secondWatcher, new IOException("Simulated watcher failure 2."));

			WorkspaceFileWatcher thirdWatcher = GetWorkspaceWatcher(provider)
				?? throw new AssertFailedException("Expected the second replacement watcher to start.");

			Assert.AreNotSame(firstWatcher, secondWatcher);
			Assert.AreNotSame(secondWatcher, thirdWatcher);
			Assert.IsTrue(firstWatcher.IsDisposed);
			Assert.IsTrue(secondWatcher.IsDisposed);
			Assert.IsFalse(thirdWatcher.IsDisposed);
			Assert.AreEqual(0, failures.Count);
		}
		finally
		{
			TestTempDirectories.Delete(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task WorkspaceWatcherRecovery_ReconcilesLuaFilesCreatedWhileWatcherWasDown()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "LuaWatcherRecoveryCreate_" + Guid.NewGuid().ToString("N"));
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.lua");
		string missedFilePath = Path.Combine(workspaceRoot, "Scripts", "missed.lua");
		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? workspaceRoot);

			using var client = new FakeLanguageServerClient();
			using var provider = CreateProviderWithWatcherCapture(workspaceRoot, client, out _);

			await provider.GetHoverAsync(filePath, content, 0, 0);

			WorkspaceFileWatcher watcher = GetWorkspaceWatcher(provider)
				?? throw new AssertFailedException("Expected the workspace watcher to start.");

			File.WriteAllText(missedFilePath, "return 1");

			SimulateWatcherFailure(watcher, new IOException("Simulated watcher failure."));
			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 1, TestPolling.DefaultTimeout));

			JsonElement changes = client.GetLastNotificationParameters("workspace/didChangeWatchedFiles").GetProperty("changes");
			Assert.AreEqual(1, changes.GetArrayLength());
			Assert.AreEqual(new Uri(missedFilePath).AbsoluteUri, changes[0].GetProperty("uri").GetString());
			Assert.AreEqual((int)FileChangeKind.Created, changes[0].GetProperty("type").GetInt32());
		}
		finally
		{
			TestTempDirectories.Delete(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task WorkspaceWatcherRecovery_ReplaysConfigurationRefreshForMissedWorkspaceConfigChanges()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "LuaWatcherRecoveryConfig_" + Guid.NewGuid().ToString("N"));
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.lua");
		string configFilePath = Path.Combine(workspaceRoot, ".luarc.json");
		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? workspaceRoot);

			using var client = new FakeLanguageServerClient();
			using var provider = CreateProviderWithWatcherCapture(workspaceRoot, client, out _);

			await provider.GetHoverAsync(filePath, content, 0, 0);

			WorkspaceFileWatcher watcher = GetWorkspaceWatcher(provider)
				?? throw new AssertFailedException("Expected the workspace watcher to start.");

			File.WriteAllText(configFilePath, "{\"Lua.workspace.maxPreload\": 1000}");

			SimulateWatcherFailure(watcher, new IOException("Simulated watcher failure."));
			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeConfiguration", 1, TestPolling.DefaultTimeout));
			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 1, TestPolling.DefaultTimeout));

			string[] sentMethods = client.GetSentMethodNames();

			CollectionAssert.AreEqual(
				new[] { "textDocument/didOpen", "textDocument/hover", "workspace/didChangeConfiguration", "workspace/didChangeWatchedFiles" },
				sentMethods);

			JsonElement changes = client.GetLastNotificationParameters("workspace/didChangeWatchedFiles").GetProperty("changes");
			Assert.AreEqual(1, changes.GetArrayLength());
			Assert.AreEqual(new Uri(configFilePath).AbsoluteUri, changes[0].GetProperty("uri").GetString());
			Assert.AreEqual((int)FileChangeKind.Created, changes[0].GetProperty("type").GetInt32());
		}
		finally
		{
			TestTempDirectories.Delete(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task WorkspaceWatcherRecovery_DoesNotReplayChangesAlreadyForwardedBeforeTheOutage()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "LuaWatcherRecoveryDuplicate_" + Guid.NewGuid().ToString("N"));
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.lua");
		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? workspaceRoot);
			File.WriteAllText(filePath, content);

			using var client = new FakeLanguageServerClient();
			using var provider = CreateProviderWithWatcherCapture(workspaceRoot, client, out _);

			await provider.GetHoverAsync(filePath, content, 0, 0);

			File.WriteAllText(filePath, content + Environment.NewLine + "return value");

			var batch = new FileChangeBatch(
			[
				new WorkspaceFileChange(filePath, FileChangeKind.Changed)
			]);

			await DispatchWorkspaceFileChangesAsync(provider, batch, CancellationToken.None);

			Assert.AreEqual(1, CountSentMethods(client, "workspace/didChangeWatchedFiles"));

			WorkspaceFileWatcher watcher = GetWorkspaceWatcher(provider)
				?? throw new AssertFailedException("Expected the workspace watcher to start.");

			SimulateWatcherFailure(watcher, new IOException("Simulated watcher failure."));

			// Negative check: no observable signal exists for "no replay happened", so the bounded window is deliberate.
			await Task.Delay(150).ConfigureAwait(false);

			Assert.AreEqual(1, CountSentMethods(client, "workspace/didChangeWatchedFiles"));
		}
		finally
		{
			TestTempDirectories.Delete(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task WorkspaceWatcherRecovery_ReconcilesDroppedChangesFromUnexpectedForwardingFailure()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "LuaWatcherRecoveryDropped_" + Guid.NewGuid().ToString("N"));
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.lua");
		const string content = "local value = 1";
		const string updatedContent = "local value = 2";

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? workspaceRoot);
			File.WriteAllText(filePath, content);

			using var client = new FakeLanguageServerClient
			{
				ThrowInvalidOperationOnNextWatchedFilesNotification = true,
				HoverResponse = JsonSerializer.SerializeToElement(new
				{
					contents = new
					{
						kind = "markdown",
						value = "Hover docs."
					}
				})
			};

			using var provider = CreateProviderWithWatcherCapture(workspaceRoot, client, out _);

			await provider.GetHoverAsync(filePath, content, 0, 0);

			WorkspaceFileWatcher watcher = GetWorkspaceWatcher(provider)
				?? throw new AssertFailedException("Expected the workspace watcher to start.");

			File.WriteAllText(filePath, updatedContent);

			await DispatchWorkspaceFileChangesAsync(
				provider,
				new FileChangeBatch(
				[
					new WorkspaceFileChange(filePath, FileChangeKind.Changed)
				]),
				CancellationToken.None);

			Assert.AreEqual(1, CountSentMethods(client, "workspace/didChangeWatchedFiles"));

			SimulateWatcherFailure(watcher, new IOException("Simulated watcher failure."));
			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 2, TestPolling.DefaultTimeout));

			JsonElement changes = client.GetLastNotificationParameters("workspace/didChangeWatchedFiles").GetProperty("changes");
			Assert.AreEqual(1, changes.GetArrayLength());
			Assert.AreEqual(new Uri(filePath).AbsoluteUri, changes[0].GetProperty("uri").GetString());
			Assert.AreEqual((int)FileChangeKind.Changed, changes[0].GetProperty("type").GetInt32());
		}
		finally
		{
			TestTempDirectories.Delete(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task WorkspaceWatcherRecovery_ConcurrentDispatchDuringRecovery_ConvergesWithoutExtraReplayOnNextRecovery()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "LuaWatcherRecoveryConcurrent_" + Guid.NewGuid().ToString("N"));
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.lua");
		string reconciledFilePath = Path.Combine(workspaceRoot, "Scripts", "reconciled.lua");
		string liveFilePath = Path.Combine(workspaceRoot, "Scripts", "live.lua");
		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? workspaceRoot);

			using var client = new FakeLanguageServerClient();
			using var provider = CreateProviderWithWatcherCapture(workspaceRoot, client, out _);

			await provider.GetHoverAsync(filePath, content, 0, 0);

			WorkspaceFileWatcher watcher = GetWorkspaceWatcher(provider)
				?? throw new AssertFailedException("Expected the workspace watcher to start.");

			File.WriteAllText(reconciledFilePath, "return 1");
			File.WriteAllText(liveFilePath, "return 2");

			client.BlockNextWatchedFilesNotification();

			Task liveDispatchTask = DispatchWorkspaceFileChangesAsync(
				provider,
				new FileChangeBatch(
				[
					new WorkspaceFileChange(liveFilePath, FileChangeKind.Created)
				]),
				CancellationToken.None);

			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 1, TestPolling.DefaultTimeout));

			SimulateWatcherFailure(watcher, new IOException("Simulated watcher failure."));

			client.ReleaseWatchedFilesNotification();
			await liveDispatchTask.ConfigureAwait(false);

			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 2, TestPolling.DefaultTimeout));

			WorkspaceFileWatcher replacementWatcher = GetWorkspaceWatcher(provider)
				?? throw new AssertFailedException("Expected the replacement workspace watcher to start.");

			SimulateWatcherFailure(replacementWatcher, new IOException("Simulated watcher failure."));

			// Negative check: no observable signal exists for "no extra replay happened", so the bounded window is deliberate.
			await Task.Delay(150).ConfigureAwait(false);

			Assert.AreEqual(2, CountSentMethods(client, "workspace/didChangeWatchedFiles"));
		}
		finally
		{
			TestTempDirectories.Delete(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task SemanticTokensRefreshRequested_RefreshesTheTrackedDocument()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"]
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		var semanticTokensUpdated = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.SemanticTokensUpdated += (_, eventArgs) =>
		{
			if (string.Equals(eventArgs.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
				semanticTokensUpdated.TrySetResult(eventArgs.SemanticTokens);
		};

		await provider.GetHoverAsync(filePath, content, 0, 0);

		client.PublishSemanticTokensRefreshRequested();

		Task completedTask = await Task.WhenAny(semanticTokensUpdated.Task, Task.Delay(TestPolling.DefaultTimeout)).ConfigureAwait(false);
		Assert.AreSame(semanticTokensUpdated.Task, completedTask);

		IReadOnlyList<SemanticToken> semanticTokens = await semanticTokensUpdated.Task.ConfigureAwait(false);

		Assert.AreEqual(1, semanticTokens.Count);
		Assert.AreEqual("variable", semanticTokens[0].Type);

		CollectionAssert.AreEqual(
			new[] { "textDocument/didOpen", "textDocument/hover", "textDocument/semanticTokens/full" },
			client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task SemanticTokensRefreshRequested_SupersededRequest_AppliesOnlyTheFreshResponse()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"]
		};

		client.EnqueueSemanticTokensFullResponse(JsonSerializer.SerializeToElement(new
		{
			data = new[] { 0, 6, 5, 0, 0 },
			resultId = "tokens-1"
		}));

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		var semanticTokenUpdates = new List<IReadOnlyList<SemanticToken>>();
		var initialUpdate = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);
		var freshUpdate = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.SemanticTokensUpdated += (_, eventArgs) =>
		{
			if (!string.Equals(eventArgs.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
				return;

			lock (semanticTokenUpdates)
				semanticTokenUpdates.Add(eventArgs.SemanticTokens);

			if (!initialUpdate.Task.IsCompleted)
				initialUpdate.TrySetResult(eventArgs.SemanticTokens);
			else
				freshUpdate.TrySetResult(eventArgs.SemanticTokens);
		};

		provider.OpenDocument(filePath, content);

		Task initialCompletedTask = await Task.WhenAny(initialUpdate.Task, Task.Delay(TestPolling.DefaultTimeout)).ConfigureAwait(false);
		Assert.AreSame(initialUpdate.Task, initialCompletedTask);
		Assert.AreEqual(1, provider.GetSemanticTokens(filePath).Count);

		// Two refreshes are in flight while both full-token requests are parked on their response
		// gates: the second refresh supersedes (cancels) the first before either response arrives.
		// Each gate pins its request's response so the assertion cannot depend on resume order.
		client.BlockNextSemanticTokensFullRequest(JsonSerializer.SerializeToElement(new
		{
			data = new[] { 0, 7, 2, 0, 0 },
			resultId = "tokens-2"
		}));

		client.BlockNextSemanticTokensFullRequest(JsonSerializer.SerializeToElement(new
		{
			data = new[] { 0, 9, 3, 0, 0 },
			resultId = "tokens-3"
		}));

		client.PublishSemanticTokensRefreshRequested();
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 2, TestPolling.DefaultTimeout));

		client.PublishSemanticTokensRefreshRequested();
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 3, TestPolling.DefaultTimeout));

		// Releasing the gates resumes the superseded request first; it must discard its response, then
		// the fresh request applies its own.
		client.ReleaseSemanticTokensFullRequest();
		client.ReleaseSemanticTokensFullRequest();

		Task freshCompletedTask = await Task.WhenAny(freshUpdate.Task, Task.Delay(TestPolling.DefaultTimeout)).ConfigureAwait(false);
		Assert.AreSame(freshUpdate.Task, freshCompletedTask);

		IReadOnlyList<SemanticToken> freshTokens = await freshUpdate.Task.ConfigureAwait(false);

		Assert.AreEqual(1, freshTokens.Count);
		Assert.AreEqual(9, freshTokens[0].Character);
		Assert.AreEqual(3, freshTokens[0].Length);

		Assert.AreEqual(1, provider.GetSemanticTokens(filePath).Count);
		Assert.AreEqual(9, provider.GetSemanticTokens(filePath)[0].Character);

		// Negative check: the superseded payload never surfaces; the bounded window is deliberate
		// because "no extra update happened" has no completion signal.
		await Task.Delay(150).ConfigureAwait(false);

		lock (semanticTokenUpdates)
			Assert.AreEqual(2, semanticTokenUpdates.Count);
	}

	[TestMethod]
	public async Task OpenDocument_DisposeDuringInFlightSemanticTokensRequest_DoesNotRaiseSemanticTokensUpdated()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"]
		};

		client.BlockNextSemanticTokensFullRequest();

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		var semanticTokensUpdated = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.SemanticTokensUpdated += (_, eventArgs) => semanticTokensUpdated.TrySetResult(eventArgs.SemanticTokens);

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 1, TestPolling.DefaultTimeout).ConfigureAwait(false));

		provider.Dispose();
		client.ReleaseSemanticTokensFullRequest();

		Task completedTask = await Task.WhenAny(semanticTokensUpdated.Task, Task.Delay(TimeSpan.FromMilliseconds(250))).ConfigureAwait(false);

		Assert.AreNotSame(semanticTokensUpdated.Task, completedTask);
	}

	[TestMethod]
	public async Task SemanticTokensRefreshRequested_RequestFailure_KeepsCachedSemanticTokens()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"]
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		var tokenUpdates = new List<IReadOnlyList<SemanticToken>>();
		var initialTokensUpdated = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.SemanticTokensUpdated += (_, eventArgs) =>
		{
			if (!string.Equals(eventArgs.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
				return;

			lock (tokenUpdates)
				tokenUpdates.Add(eventArgs.SemanticTokens);

			initialTokensUpdated.TrySetResult(eventArgs.SemanticTokens);
		};

		provider.OpenDocument(filePath, content);

		Task initialCompletedTask = await Task.WhenAny(initialTokensUpdated.Task, Task.Delay(TestPolling.DefaultTimeout)).ConfigureAwait(false);
		Assert.AreSame(initialTokensUpdated.Task, initialCompletedTask);
		Assert.AreEqual(1, provider.GetSemanticTokens(filePath).Count);

		client.ThrowInvalidOperationOnNextRequestMethod = "textDocument/semanticTokens/full";
		client.PublishSemanticTokensRefreshRequested();

		// The failed request is sent (second full request), and because it fails the refresh keeps the
		// last known token set instead of clearing the highlighting.
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 2, TestPolling.DefaultTimeout));

		// Negative check: a failed refresh produces no observable update event, so the bounded window
		// is deliberate.
		await Task.Delay(150).ConfigureAwait(false);

		lock (tokenUpdates)
			Assert.AreEqual(1, tokenUpdates.Count);

		IReadOnlyList<SemanticToken> cachedTokens = provider.GetSemanticTokens(filePath);

		Assert.AreEqual(1, cachedTokens.Count);
		Assert.AreEqual("variable", cachedTokens[0].Type);

		CollectionAssert.AreEqual(
			new[]
			{
				"textDocument/didOpen",
				"textDocument/semanticTokens/full",
				"textDocument/semanticTokens/full"
			},
			client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task OpenDocument_SemanticTokenModifiers_AreDecodedFromTheLegend()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";

		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"],
			SemanticTokenModifiers = ["declaration", "readonly"]
		};

		// The first token carries modifier bit 0, which the legend maps back to "declaration".
		client.EnqueueSemanticTokensFullResponse(JsonSerializer.SerializeToElement(new
		{
			data = new[] { 0, 6, 5, 0, 1 },
			resultId = "tokens-1"
		}));

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		var semanticTokensUpdated = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.SemanticTokensUpdated += (_, eventArgs) =>
		{
			if (string.Equals(eventArgs.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
				semanticTokensUpdated.TrySetResult(eventArgs.SemanticTokens);
		};

		provider.OpenDocument(filePath, "local value = 1");

		Task completedTask = await Task.WhenAny(semanticTokensUpdated.Task, Task.Delay(TestPolling.DefaultTimeout)).ConfigureAwait(false);
		Assert.AreSame(semanticTokensUpdated.Task, completedTask);

		IReadOnlyList<SemanticToken> semanticTokens = await semanticTokensUpdated.Task.ConfigureAwait(false);

		Assert.AreEqual(1, semanticTokens.Count);
		Assert.AreEqual("variable", semanticTokens[0].Type);
		CollectionAssert.AreEqual(new[] { "declaration" }, semanticTokens[0].Modifiers.ToArray());
	}

	[TestMethod]
	public async Task SemanticTokensRefreshRequested_WithManyDocuments_BoundsTheConcurrentFanOut()
	{
		const string workspaceRoot = @"C:\Workspace";
		string[] filePaths = [.. Enumerable.Range(0, 6).Select(index => $@"C:\Workspace\Scripts\fanout_{index}.lua")];

		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"]
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		foreach (string filePath in filePaths)
			provider.OpenDocument(filePath, "local value = 1");

		// Wait until every open-triggered refresh completed and cached its token set, so no in-flight
		// request can consume a fan-out gate queued below.
		await TestPolling.WaitForAsync(
			() => Task.FromResult(filePaths.All(filePath => provider.GetSemanticTokens(filePath).Count > 0)),
			static allCached => allCached,
			TestPolling.DefaultTimeout,
			"Expected every opened document to cache its semantic tokens.",
			static allCached => $"allCached={allCached}").ConfigureAwait(false);

		// Park the next four requests - the fan-out bound - and trigger a refresh for all six documents.
		for (int i = 0; i < 4; i++)
			client.BlockNextSemanticTokensFullRequest();

		client.PublishSemanticTokensRefreshRequested();

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", filePaths.Length + 4, TestPolling.DefaultTimeout));

		// Negative check: while all four permits are parked, the remaining documents wait instead of
		// issuing an unbounded burst, so the bounded window is deliberate.
		await Task.Delay(250).ConfigureAwait(false);
		Assert.AreEqual(filePaths.Length + 4, CountSentMethods(client, "textDocument/semanticTokens/full"));

		client.ReleaseSemanticTokensFullRequest();
		client.ReleaseSemanticTokensFullRequest();
		client.ReleaseSemanticTokensFullRequest();
		client.ReleaseSemanticTokensFullRequest();

		// Releasing the parked requests admits the remaining documents.
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", filePaths.Length + 6, TestPolling.DefaultTimeout));
	}

	[TestMethod]
	public async Task UpdateDocument_WhileSemanticTokensResponseIsStalled_DeliversChangeNotification()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";

		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"]
		};

		client.BlockNextSemanticTokensFullRequest();

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		provider.OpenDocument(filePath, "local value = 1");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 1, TestPolling.DefaultTimeout));

		// The first edit's token refresh is parked on its response gate; the next edit must still reach
		// the server instead of queuing behind the stalled semantic-token round trip.
		provider.UpdateDocument(filePath, "local value = 2");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 1, TestPolling.DefaultTimeout));

		provider.UpdateDocument(filePath, "local value = 3");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 2, TestPolling.DefaultTimeout));

		client.ReleaseSemanticTokensFullRequest();
	}

	[TestMethod]
	public async Task OpenDocument_OneSemanticTokenSubscriberExceptionDoesNotSuppressLaterSubscribers()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";

		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"]
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		var observedTokens = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.SemanticTokensUpdated += (_, _) => throw new InvalidOperationException("Subscriber failure.");

		provider.SemanticTokensUpdated += (_, eventArgs) =>
		{
			if (string.Equals(eventArgs.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
				observedTokens.TrySetResult(eventArgs.SemanticTokens);
		};

		provider.OpenDocument(filePath, "local value = 1");

		// A throwing subscriber is isolated: later subscribers still receive the token set.
		Task completedTask = await Task.WhenAny(observedTokens.Task, Task.Delay(TestPolling.DefaultTimeout)).ConfigureAwait(false);
		Assert.AreSame(observedTokens.Task, completedTask);

		IReadOnlyList<SemanticToken> semanticTokens = await observedTokens.Task.ConfigureAwait(false);

		Assert.AreEqual(1, semanticTokens.Count);
		Assert.AreEqual("variable", semanticTokens[0].Type);
	}

	[TestMethod]
	public async Task OpenDocument_SemanticTokensFullRequest_OmitsPreviousResultId()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"]
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 1, TestPolling.DefaultTimeout));

		JsonElement parameters = client.GetLastRequestParameters("textDocument/semanticTokens/full");

		Assert.AreEqual(new Uri(filePath).AbsoluteUri, parameters.GetProperty("textDocument").GetProperty("uri").GetString());
		Assert.IsFalse(parameters.TryGetProperty("previousResultId", out _));
	}

	[TestMethod]
	public async Task OpenDocument_WithSemanticTokenLegendButNoFullSupport_DoesNotRequestSemanticTokens()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = false,
			SemanticTokenTypes = ["variable"]
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout).ConfigureAwait(false));

		CollectionAssert.DoesNotContain(client.GetSentMethodNames(), "textDocument/semanticTokens/full");
	}

	[TestMethod]
	public async Task GetHoverAsync_TwoRoots_StartsAndDisposesOneWatcherPerRoot()
	{
		string primaryRoot = Path.Combine(Path.GetTempPath(), "LuaMultiRootPrimary_" + Guid.NewGuid().ToString("N"));
		string secondaryRoot = Path.Combine(Path.GetTempPath(), "LuaMultiRootSecondary_" + Guid.NewGuid().ToString("N"));
		string filePath = Path.Combine(primaryRoot, "Scripts", "test.lua");
		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(primaryRoot);
			Directory.CreateDirectory(secondaryRoot);

			using var client = new FakeLanguageServerClient();
			using var provider = CreateProviderWithWatcherCapture([primaryRoot, secondaryRoot], client, out _);

			await provider.GetHoverAsync(filePath, content, 0, 0);

			WorkspaceFileWatcher primaryWatcher = GetWorkspaceWatcher(provider, primaryRoot)
				?? throw new AssertFailedException("Expected the primary root's workspace watcher to start.");
			WorkspaceFileWatcher secondaryWatcher = GetWorkspaceWatcher(provider, secondaryRoot)
				?? throw new AssertFailedException("Expected the secondary root's workspace watcher to start.");

			Assert.AreNotSame(primaryWatcher, secondaryWatcher);

			provider.Dispose();

			Assert.IsTrue(primaryWatcher.IsDisposed);
			Assert.IsTrue(secondaryWatcher.IsDisposed);
		}
		finally
		{
			TestTempDirectories.Delete(primaryRoot);
			TestTempDirectories.Delete(secondaryRoot);
		}
	}

	[TestMethod]
	public async Task GetHoverAsync_TwoRoots_IsolatesWatcherStartupFailureToItsOwnRoot()
	{
		string healthyRoot = Path.Combine(Path.GetTempPath(), "LuaMultiRootHealthy_" + Guid.NewGuid().ToString("N"));
		string failingRoot = Path.Combine(Path.GetTempPath(), "LuaMultiRootFailing_" + Guid.NewGuid().ToString("N"));
		string filePath = Path.Combine(healthyRoot, "Scripts", "test.lua");
		string forwardedFilePath = Path.Combine(healthyRoot, "Scripts", "generated.lua");
		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(healthyRoot);
			Directory.CreateDirectory(failingRoot);

			using var client = new FakeLanguageServerClient();
			Func<FileChangeBatch, CancellationToken, Task>? healthyRootDispatch = null;

			using var provider = new LuaLanguageServerIntelliSenseProvider(
				[healthyRoot, failingRoot],
				client,
				workspaceFileWatcherFactory: (rootPath, dispatchAsync, onWatcherFailed) =>
				{
					if (LanguageServerPaths.AreLocalPathsEqual(rootPath, healthyRoot))
						healthyRootDispatch = dispatchAsync;

					return new WorkspaceFileWatcher(
						rootPath,
						dispatchAsync,
						LuaWorkspaceConventions.WatchSpecifications,
						onWatcherFailed,
						LanguageServerPaths.AreLocalPathsEqual(rootPath, failingRoot)
							? static (_, _) => throw new InvalidOperationException("Simulated watcher creation failure.")
							: null);
				});
			var failures = new List<WorkspaceWatcherFailure>();

			provider.WorkspaceWatcherFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

			await provider.GetHoverAsync(filePath, content, 0, 0);

			Assert.AreEqual(1, failures.Count);
			StringAssert.Contains(failures[0].Message, failingRoot);
			Assert.IsFalse(failures[0].Message.Contains(healthyRoot, StringComparison.Ordinal));

			Func<FileChangeBatch, CancellationToken, Task> dispatch = healthyRootDispatch
				?? throw new AssertFailedException("Expected the healthy root's dispatch delegate to be captured.");

			await dispatch(
				new FileChangeBatch(
				[
					new WorkspaceFileChange(forwardedFilePath, FileChangeKind.Changed)
				]),
				CancellationToken.None);

			Assert.AreEqual(1, CountSentMethods(client, "workspace/didChangeWatchedFiles"));
		}
		finally
		{
			TestTempDirectories.Delete(healthyRoot);
			TestTempDirectories.Delete(failingRoot);
		}
	}
}

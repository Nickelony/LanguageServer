using Nickelony.IDEKit.IntelliSense.Hover;
using System.Text.Json;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Covers the provider lifecycle: disposal ordering and idempotency, callback admission closure, and the
/// transport-unavailable wiring with restart and tracked-document reopen.
/// </summary>
[TestClass]
public sealed class LanguageServerIntelliSenseProviderBaseLifecycleTests
{
	private const string Content = "line one";

	private static readonly string s_workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-lifecycle-" + Guid.NewGuid().ToString("N"));
	private static readonly string s_filePath = Path.Combine(s_workspaceRoot, "Scripts", "test.test");
	private static readonly TimeSpan s_waitTimeout = TimeSpan.FromSeconds(2);

	[TestMethod]
	public void Dispose_IsIdempotent_AndDisposesTheClientOnce()
	{
		var client = new FakeLanguageServerClient();
		var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		provider.Dispose();
		provider.Dispose();

		Assert.AreEqual(1, client.DisposeCallCount);
		Assert.AreEqual(1, provider.OnDisposingCallCount);
		Assert.AreEqual(false, provider.ClientWasDisposedAtOnDisposing);
		Assert.AreEqual(LanguageServerProviderState.Disposed, provider.State);
		Assert.IsFalse(provider.IsAvailable);
	}

	[TestMethod]
	public async Task Dispose_ClosesCallbackAdmissionAndDetachesClientEvents()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		var provider = new TestLanguageServerProvider([s_workspaceRoot], client);
		int diagnosticsUpdates = 0;

		provider.DiagnosticsUpdated += (_, _) => diagnosticsUpdates++;

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));

		client.PublishDiagnostics(new PublishDiagnosticsParams(new Uri(s_filePath).AbsoluteUri, null, []));
		Assert.IsTrue(await TestWait.ForConditionAsync(() => diagnosticsUpdates == 1, s_waitTimeout).ConfigureAwait(false));

		provider.Dispose();

		// Raising diagnostics after disposal must neither invoke subscribers nor throw, and a late
		// subscription is dropped by the closed admission path.
		client.PublishDiagnostics(new PublishDiagnosticsParams(new Uri(s_filePath).AbsoluteUri, null, []));
		provider.DiagnosticsUpdated += (_, _) => diagnosticsUpdates++;

		await Task.Delay(100).ConfigureAwait(false);

		Assert.AreEqual(1, diagnosticsUpdates);
	}

	[TestMethod]
	public async Task TransportUnavailable_MarksUnavailable_AndNextRequestRestartsWithReopen()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);
		int capabilitiesChangedCount = 0;

		provider.CapabilitiesChanged += (_, _) => capabilitiesChangedCount++;

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);
		Assert.IsTrue(provider.IsAvailable);

		long lostGeneration = client.TransportGeneration;
		client.RaiseTransportUnavailable(lostGeneration);

		Assert.AreEqual(LanguageServerProviderState.Unavailable, provider.State);
		Assert.IsFalse(provider.IsAvailable);
		Assert.IsFalse(provider.SupportsReferences);

		// The next request completes the documented fallback value and restarts the transport; the tracked
		// document is reopened on the new generation.
		Assert.IsNull(await provider.GetHoverAsync(s_filePath, Content, 0, 0).ConfigureAwait(false));

		Assert.AreEqual(2, client.StartCallCount);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);
		Assert.IsTrue(capabilitiesChangedCount > 0);
	}

	[TestMethod]
	public async Task MissingClient_ReportsThePersistentFailureWithoutThrowingOrTraffic()
	{
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client: null);

		provider.OpenDocument(s_filePath, Content);
		provider.UpdateDocument(s_filePath, "line two");
		provider.CloseDocument(s_filePath);

		// The members are fire-and-forget: they must not throw, and the provider reports the persistent
		// failure instead of any document traffic.
		Assert.IsTrue(await TestWait.ForConditionAsync(
			() => provider.State == LanguageServerProviderState.Failed,
			TimeSpan.FromSeconds(2)).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task FailedRestartReplay_IsResumedByTheNextStartEvenWhileTheClientStaysReady()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));

		client.RaiseTransportUnavailable(client.TransportGeneration);
		Assert.AreEqual(LanguageServerProviderState.Unavailable, provider.State);

		bool failReopenSend = true;

		client.SendNotificationHandler = (method, _, _) =>
		{
			if (failReopenSend && string.Equals(method, "textDocument/didOpen", StringComparison.Ordinal))
				throw new IOException("Simulated reopen failure.");

			return Task.CompletedTask;
		};

		// The restart succeeds but the replay fails: the provider reports the transient failure and keeps the
		// document for a later resume instead of leaving a record that claims a server-open document.
		Assert.IsNull(await provider.GetHoverAsync(s_filePath, Content, 0, 0).ConfigureAwait(false));

		Assert.AreEqual(1, client.GetAttemptedNotificationMethodNames().Count(method => string.Equals(method, "textDocument/didOpen", StringComparison.Ordinal)));

		failReopenSend = false;

		// The next start resumes the replay even though the client is already ready again.
		Assert.IsNull(await provider.GetHoverAsync(s_filePath, Content, 0, 0).ConfigureAwait(false));

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);
		Assert.IsNotNull(provider.GetTrackedSnapshot(s_filePath));
	}

	[TestMethod]
	public async Task CloseDuringRestartReplay_StillClosesAReopenedServerDocument()
	{
		string secondFilePath = Path.Combine(s_workspaceRoot, "Scripts", "second.test");

		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		provider.OpenDocument(s_filePath, Content);
		provider.OpenDocument(secondFilePath, "line two");

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout).ConfigureAwait(false));

		client.RaiseTransportUnavailable(client.TransportGeneration);

		var replayGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		int reopenAttempts = 0;

		client.SendNotificationHandler = async (method, _, cancellationToken) =>
		{
			if (string.Equals(method, "textDocument/didOpen", StringComparison.Ordinal)
				&& Interlocked.Increment(ref reopenAttempts) == 2)
			{
				// Hold the second reopen so the close below lands while the replay still owns the startup flow.
				await replayGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
			}
		};

		Task<TextHoverInfo?> hoverTask = provider.GetHoverAsync(s_filePath, Content, 0, 0);

		// Wait until the first reopened document was delivered; the second reopen is blocked.
		Assert.IsTrue(await TestWait.ForConditionAsync(() => client.GetSentMethodCount("textDocument/didOpen") == 3, s_waitTimeout).ConfigureAwait(false));

		string reopenedUri = client.GetLastNotificationParameters("textDocument/didOpen").GetProperty("textDocument").GetProperty("uri").GetString()
			?? throw new AssertFailedException("Expected a reopened document URI.");
		string reopenedPath = string.Equals(reopenedUri, new Uri(s_filePath).AbsoluteUri, StringComparison.Ordinal) ? s_filePath : secondFilePath;

		provider.CloseDocument(reopenedPath);

		// The close must close the server copy the replay reopened, instead of being suppressed while the startup
		// flow is still running.
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, s_waitTimeout).ConfigureAwait(false));

		replayGate.TrySetResult(true);
		Assert.IsNull(await hoverTask.ConfigureAwait(false));

		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);
	}

	[TestMethod]
	public async Task UpdateDocument_AfterTransportLoss_RestartsAndReopensWithoutEscapingTheUpdatePath()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));

		client.RaiseTransportUnavailable(client.TransportGeneration);
		Assert.AreEqual(LanguageServerProviderState.Unavailable, provider.State);

		// The update entry point ensures the transport outside the document scheduler slot, so the restart
		// replay can reopen the tracked document without violating the scheduler's reentrancy contract.
		provider.UpdateDocument(s_filePath, "line two");

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout).ConfigureAwait(false));
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 1, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);
		Assert.IsTrue(provider.IsAvailable);

		JsonElement change = client.GetLastNotificationParameters("textDocument/didChange");

		Assert.AreEqual("two", change.GetProperty("contentChanges")[0].GetProperty("text").GetString());
	}

	[TestMethod]
	public async Task OpenDocument_AfterTransportLoss_StartsTheRestartAndOpensTheNewDocument()
	{
		string secondFilePath = Path.Combine(s_workspaceRoot, "Scripts", "second.test");

		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));

		client.RaiseTransportUnavailable(client.TransportGeneration);
		Assert.AreEqual(LanguageServerProviderState.Unavailable, provider.State);

		// The open entry point restarts the transport outside the per-document scheduler slot, replays the first
		// document, and then opens the second one on the restarted transport.
		provider.OpenDocument(secondFilePath, "line two");

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 3, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);

		string? lastOpenedUri = client.GetLastNotificationParameters("textDocument/didOpen")
			.GetProperty("textDocument").GetProperty("uri").GetString();

		Assert.AreEqual(new Uri(secondFilePath).AbsoluteUri, lastOpenedUri);
	}

	[TestMethod]
	public async Task UpdateDocument_DuringTransportRestartReplay_CompletesWithoutDeadlockingTheDocumentChain()
	{
		string secondFilePath = Path.Combine(s_workspaceRoot, "Scripts", "second.test");

		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		provider.OpenDocument(s_filePath, Content);
		provider.OpenDocument(secondFilePath, "line two");

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout).ConfigureAwait(false));

		client.RaiseTransportUnavailable(client.TransportGeneration);

		var replayGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		int reopenAttempts = 0;

		client.SendNotificationHandler = async (method, _, cancellationToken) =>
		{
			if (string.Equals(method, "textDocument/didOpen", StringComparison.Ordinal)
				&& Interlocked.Increment(ref reopenAttempts) == 1)
			{
				// Hold the first replayed reopen so the concurrent updates below land while the replay still owns
				// the startup flow and the startup lock.
				await replayGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
			}
		};

		// A request starts the restart; its replay blocks on the first tracked document.
		Task<TextHoverInfo?> hoverTask = provider.GetHoverAsync(secondFilePath, "line four", 0, 0);

		Assert.IsTrue(await TestWait.ForConditionAsync(() => Volatile.Read(ref reopenAttempts) == 1, s_waitTimeout).ConfigureAwait(false));

		// Both updates must queue behind the startup flow instead of deadlocking their document chains.
		provider.UpdateDocument(s_filePath, "line three");
		provider.UpdateDocument(secondFilePath, "line four");

		replayGate.TrySetResult(true);

		Assert.IsNull(await hoverTask.ConfigureAwait(false));
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 2, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);
	}

	[TestMethod]
	public async Task RestartStartedByACanceledRequest_StillCompletesAndReopensTheDocument()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));

		client.RaiseTransportUnavailable(client.TransportGeneration);
		Assert.AreEqual(LanguageServerProviderState.Unavailable, provider.State);

		var startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		client.StartAsyncHandler = async cancellationToken =>
		{
			await startGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
			return true;
		};

		using var cancellationTokenSource = new CancellationTokenSource();
		Task<TextHoverInfo?> hoverTask = provider.GetHoverAsync(s_filePath, Content, 0, 0, cancellationTokenSource.Token);

		// Wait until the restart is in flight, then cancel the triggering request: the restart serves every
		// consumer of the provider, so it must still complete and reopen the tracked document, while the request
		// itself surfaces the caller's cancellation.
		Assert.IsTrue(await TestWait.ForConditionAsync(() => client.StartCallCount == 2, s_waitTimeout).ConfigureAwait(false));

		cancellationTokenSource.Cancel();
		startGate.TrySetResult(true);

		await Assert.ThrowsAsync<OperationCanceledException>(async () => await hoverTask.ConfigureAwait(false)).ConfigureAwait(false);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);
	}

	[TestMethod]
	public async Task FailedRestartReplay_ResumesWithTheRemainingDocumentsOnly()
	{
		string secondFilePath = Path.Combine(s_workspaceRoot, "Scripts", "second.test");
		string thirdFilePath = Path.Combine(s_workspaceRoot, "Scripts", "third.test");

		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		provider.OpenDocument(s_filePath, Content);
		provider.OpenDocument(secondFilePath, "line two");
		provider.OpenDocument(thirdFilePath, "line three");

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 3, s_waitTimeout).ConfigureAwait(false));

		client.RaiseTransportUnavailable(client.TransportGeneration);

		int reopenAttempts = 0;

		client.SendNotificationHandler = (method, _, _) =>
		{
			if (string.Equals(method, "textDocument/didOpen", StringComparison.Ordinal)
				&& Interlocked.Increment(ref reopenAttempts) == 2)
			{
				throw new IOException("Simulated reopen failure.");
			}

			return Task.CompletedTask;
		};

		// The restart replay reopens the first document and fails on the second one: the failed document and
		// everything after it stay pending, and the provider does not report ready.
		Assert.IsNull(await provider.GetHoverAsync(s_filePath, Content, 0, 0).ConfigureAwait(false));
		Assert.IsFalse(provider.IsAvailable);

		// The next start resumes the replay with the remaining documents only and reports ready.
		Assert.IsNull(await provider.GetHoverAsync(s_filePath, Content, 0, 0).ConfigureAwait(false));

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 6, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);
	}

	[TestMethod]
	public async Task FailedRestartReplay_WhenTheSessionIsReplaced_ReopensEveryTrackedDocument()
	{
		string secondFilePath = Path.Combine(s_workspaceRoot, "Scripts", "second.test");

		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		provider.OpenDocument(s_filePath, Content);
		provider.OpenDocument(secondFilePath, "line two");

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout).ConfigureAwait(false));

		client.RaiseTransportUnavailable(client.TransportGeneration);

		int reopenAttempts = 0;

		client.SendNotificationHandler = (method, _, _) =>
		{
			if (string.Equals(method, "textDocument/didOpen", StringComparison.Ordinal)
				&& Interlocked.Increment(ref reopenAttempts) == 2)
			{
				throw new IOException("Simulated reopen failure.");
			}

			return Task.CompletedTask;
		};

		// The restart replay reopens one document and fails on the second one, leaving a partial pending list
		// while the transport stays ready.
		Assert.IsNull(await provider.GetHoverAsync(s_filePath, Content, 0, 0).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerProviderState.Unavailable, provider.State);

		// A start handler always runs and activates a fresh generation, so the resume below replaces the session
		// the partial list was captured for. The framework must capture the complete set again: both tracked
		// documents need a didOpen on the new session.
		client.StartAsyncHandler = static _ => Task.FromResult(true);

		var reopenedUris = new List<string>();

		client.SendNotificationHandler = (method, parameters, _) =>
		{
			if (string.Equals(method, "textDocument/didOpen", StringComparison.Ordinal))
			{
				reopenedUris.Add(JsonSerializer.SerializeToElement(parameters)
					.GetProperty("textDocument").GetProperty("uri").GetString()!);
			}

			return Task.CompletedTask;
		};

		Assert.IsNull(await provider.GetHoverAsync(s_filePath, Content, 0, 0).ConfigureAwait(false));

		Assert.IsTrue(await TestWait.ForConditionAsync(() => reopenedUris.Count >= 2, s_waitTimeout).ConfigureAwait(false),
			$"Expected both tracked documents to be reopened on the replacement session. Reopened: {string.Join(", ", reopenedUris)}");

		CollectionAssert.AreEquivalent(
			new[] { new Uri(s_filePath).AbsoluteUri, new Uri(secondFilePath).AbsoluteUri },
			reopenedUris);

		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);
	}

	[TestMethod]
	public async Task DisposeAsync_DisposesTheClientOnce()
	{
		var client = new FakeLanguageServerClient();
		var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		await provider.DisposeAsync().ConfigureAwait(false);
		await provider.DisposeAsync().ConfigureAwait(false);
		provider.Dispose();

		Assert.AreEqual(1, client.DisposeCallCount);
		Assert.IsTrue(client.IsDisposed);
		Assert.AreEqual(LanguageServerProviderState.Disposed, provider.State);
		Assert.IsFalse(provider.IsAvailable);
	}
}

using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

public partial class LanguageServerClientTests
{
	private const string ReadyCapabilitiesJson = """
		{
		  "capabilities": {
		    "textDocumentSync": 2,
		    "referencesProvider": true
		  }
		}
		""";

	private sealed class ScriptedStartupAttempt
	{
		public required RecordingStream Recording { get; init; }

		public required DeferredPersistentJsonRpcResponseStream Responses { get; init; }
	}

	private static LanguageServerClient CreateScriptedStartupClient(List<ScriptedStartupAttempt> attempts, LanguageServerClientOptions? options = null,
		Func<CancellationToken, Task>? beforeInitializeRequestTestHook = null)
	{
		return new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", options ?? s_defaultClientOptions,
			logger: null,
			processStartedTestHook: null,
			sessionActivatedTestHook: null,
			beforeInitializeRequestTestHook: beforeInitializeRequestTestHook,
			transportSessionTestHook: (client, _) =>
			{
				var attempt = new ScriptedStartupAttempt
				{
					Recording = new RecordingStream(),
					Responses = new DeferredPersistentJsonRpcResponseStream()
				};

				attempts.Add(attempt);

				return Task.FromResult(new LanguageServerTransportSession(
					client.CapabilityStore.NextTransportGeneration(),
					process: null,
					attempt.Responses,
					attempt.Recording));
			});
	}

	private static async Task<ScriptedStartupAttempt> CompleteScriptedStartupAsync(
		Task<bool> startTask,
		List<ScriptedStartupAttempt> attempts,
		int attemptIndex,
		string initializeResultJson)
	{
		DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);

		while (attempts.Count <= attemptIndex && DateTime.UtcNow < deadline)
			await Task.Delay(25).ConfigureAwait(false);

		if (attempts.Count <= attemptIndex)
			throw new AssertFailedException("Timed out waiting for the scripted startup attempt to begin.");

		ScriptedStartupAttempt attempt = attempts[attemptIndex];
		int requestId = await WaitForRequestIdAsync(attempt.Recording).ConfigureAwait(false);

		attempt.Responses.SetPayload(CreateJsonRpcResponse(requestId, initializeResultJson));

		Assert.IsTrue(await startTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));

		return attempt;
	}

	[TestMethod]
	public async Task StartAsync_WithScriptedTransportSession_CompletesHandshakeAndBecomesReady()
	{
		List<ScriptedStartupAttempt> attempts = [];

		using var client = CreateScriptedStartupClient(attempts);

		Task<bool> startTask = client.StartAsync(CancellationToken.None);

		ScriptedStartupAttempt attempt = await CompleteScriptedStartupAsync(startTask, attempts, 0, ReadyCapabilitiesJson).ConfigureAwait(false);

		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(1L, client.TransportGeneration);
		Assert.AreEqual(TextDocumentSyncKind.Incremental, client.TextDocumentSyncKind);
		Assert.IsTrue(client.SupportsReferences);

		// The scripted transport records writes asynchronously; wait for the settings push to reach the
		// recorder before pinning the sequence (same synchronization as the handshake test).
		await TestWait.UntilAsync(
			() => attempt.Recording.GetWrittenText().Contains("workspace/didChangeConfiguration", StringComparison.Ordinal),
			TimeSpan.FromSeconds(5),
			"The handshake should push the settings notification after initialization completes.").ConfigureAwait(false);

		// The full handshake sequence must reach the server in this exact order.
		List<(string? Method, string BodyJson)> writtenMessages = ExtractWrittenMessages(attempt.Recording.GetWrittenText());

		CollectionAssert.AreEqual(
			new[] { "initialize", "initialized", "workspace/didChangeConfiguration" },
			writtenMessages.Where(message => message.Method is not null).Select(message => message.Method!).ToArray());
	}

	[TestMethod]
	public void BuildInitializeParams_WhenCapabilitiesProviderReturnsNonObject_SendsEmptyCapabilities()
	{
		var options = new LanguageServerClientOptions(static () => new { })
		{
			ClientCapabilitiesProvider = static _ => new[] { 1 }
		};

		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", options);

		JsonElement initializeParams = JsonSerializer.SerializeToElement(client.BuildInitializeParams());
		JsonElement capabilities = initializeParams.GetProperty("capabilities");

		// A non-object payload cannot receive the enforced overrides and must not reach the server as a scalar
		// or array; the handshake stays protocol-valid with empty capabilities instead.
		Assert.AreEqual(JsonValueKind.Object, capabilities.ValueKind);
		Assert.AreEqual(0, capabilities.EnumerateObject().Count());
	}

	[TestMethod]
	public async Task StartAsync_WhenTheSessionIsInvalidatedDuringHandshake_ReturnsFalseWithoutBecomingReady()
	{
		List<ScriptedStartupAttempt> attempts = [];
		bool invalidated = false;
		LanguageServerClient? client = null;

		try
		{
			client = CreateScriptedStartupClient(attempts, options: null,
				beforeInitializeRequestTestHook: _ =>
				{
					invalidated = true;
					Assert.IsTrue(client!.TryMarkTransportUnhealthy(client.TransportGeneration));
					return Task.CompletedTask;
				});

			Task<bool> startTask = client.StartAsync(CancellationToken.None);

			DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);

			while (attempts.Count == 0 && DateTime.UtcNow < deadline)
				await Task.Delay(25).ConfigureAwait(false);

			ScriptedStartupAttempt attempt = attempts[0];
			int requestId = await WaitForRequestIdAsync(attempt.Recording).ConfigureAwait(false);

			attempt.Responses.SetPayload(CreateJsonRpcResponse(requestId, ReadyCapabilitiesJson));

			Assert.IsFalse(await startTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
			Assert.IsTrue(invalidated);
			Assert.IsFalse(client.IsReady);
			Assert.AreEqual(0L, client.TransportGeneration);
		}
		finally
		{
			client?.Dispose();
		}
	}

	[TestMethod]
	public async Task StartAsync_AfterTransportInvalidation_RestartsWithNewSessionAndReopensReady()
	{
		List<ScriptedStartupAttempt> attempts = [];

		using var client = CreateScriptedStartupClient(attempts);

		Task<bool> firstStartTask = client.StartAsync(CancellationToken.None);

		await CompleteScriptedStartupAsync(firstStartTask, attempts, 0, ReadyCapabilitiesJson).ConfigureAwait(false);

		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(1L, client.TransportGeneration);

		Assert.IsTrue(client.TryMarkTransportUnhealthy(client.TransportGeneration));
		Assert.IsFalse(client.IsReady);

		Task<bool> restartTask = client.StartAsync(CancellationToken.None);

		await CompleteScriptedStartupAsync(restartTask, attempts, 1, ReadyCapabilitiesJson).ConfigureAwait(false);

		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(2L, client.TransportGeneration);
		Assert.AreEqual(TextDocumentSyncKind.Incremental, client.TextDocumentSyncKind);

		// Both scripted sessions received an initialize request; only the new one may still accept requests.
		Assert.AreEqual(1, CountSentMethods(attempts[0].Recording.GetWrittenText(), "initialize"));
		Assert.AreEqual(1, CountSentMethods(attempts[1].Recording.GetWrittenText(), "initialize"));
	}

	[TestMethod]
	public async Task StartAsync_WhenSyncIsNotRequired_AcceptsServerWithoutTextSynchronization()
	{
		List<ScriptedStartupAttempt> attempts = [];
		var options = new LanguageServerClientOptions(static () => new { })
		{
			RequireTextDocumentSynchronization = false,
			ShutdownRequestTimeout = TimeSpan.FromMilliseconds(1500),
			DisposeWaitTimeout = TimeSpan.FromMilliseconds(2000)
		};

		using var client = CreateScriptedStartupClient(attempts, options);

		Task<bool> startTask = client.StartAsync(CancellationToken.None);

		await CompleteScriptedStartupAsync(startTask, attempts, 0, """{ "capabilities": { } }""").ConfigureAwait(false);

		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(TextDocumentSyncKind.None, client.TextDocumentSyncKind);
	}

	[TestMethod]
	public async Task StartAsync_WhenServerAdvertisesNoSyncAndSyncIsRequired_FailsStartup()
	{
		List<ScriptedStartupAttempt> attempts = [];

		using var client = CreateScriptedStartupClient(attempts);

		Task<bool> startTask = client.StartAsync(CancellationToken.None);

		await TestWait.UntilAsync(
			() => attempts.Count > 0,
			TimeSpan.FromSeconds(5),
			"The scripted startup attempt should begin.").ConfigureAwait(false);

		ScriptedStartupAttempt attempt = attempts[0];
		int requestId = await WaitForRequestIdAsync(attempt.Recording).ConfigureAwait(false);

		attempt.Responses.SetPayload(CreateJsonRpcResponse(requestId, """{ "capabilities": { "textDocumentSync": 0 } }"""));

		Assert.IsFalse(await startTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
		Assert.IsFalse(client.IsReady);
		Assert.IsNull(client.CapabilityStore.ActiveSession);
	}

	[TestMethod]
	public void CaptureServerCapabilitiesForGeneration_AfterTransportInvalidation_DoesNotRepublishCapabilities()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		LanguageServerTransportSession session = CreateTransportSession(client, 12, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);

		InitializeResponse initializeResponse = DeserializeInitializeResponse(ReadyCapabilitiesJson);

		CaptureServerCapabilities(client, initializeResponse);
		Assert.AreEqual(TextDocumentSyncKind.Incremental, client.TextDocumentSyncKind);
		Assert.IsTrue(client.SupportsReferences);

		Assert.IsTrue(client.TryMarkTransportUnhealthy(client.TransportGeneration));
		Assert.AreEqual(TextDocumentSyncKind.None, client.TextDocumentSyncKind);

		// A late initialize completion for the invalidated generation must not restore the capabilities.
		CaptureServerCapabilities(client, initializeResponse);
		Assert.AreEqual(TextDocumentSyncKind.None, client.TextDocumentSyncKind);
		Assert.IsFalse(client.SupportsReferences);
	}

	[TestMethod]
	public async Task PumpDiagnosticsAsync_DropsPayloadsQueuedBeforeTransportInvalidation()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		LanguageServerTransportSession session = CreateTransportSession(client, 5, process: null, Stream.Null, Stream.Null);
		int publishedCount = 0;

		SetActiveSession(client, session);
		SetReadyState(client, true);

		// The callback pump must run so a payload that survives the invalidation gate would actually reach the
		// subscriber; without it this test could not fail even if the drop logic were removed.
		StartCallbackPump(client);

		client.DiagnosticsPublished += (_, _) => publishedCount++;

		client.DiagnosticsRouter.RaiseDiagnosticsPublished(
			GetTransportGeneration(session),
			CreateDiagnosticsParameters("file:///C:/Workspace/queued.ext", "Queued warning."));

		Assert.IsTrue(client.TryMarkTransportUnhealthy(GetTransportGeneration(session)));

		Task diagnosticsPumpTask = client.DiagnosticsRouter.PumpDiagnosticsAsync();

		await Task.Delay(200).ConfigureAwait(false);

		client.CancelLifetime();
		await diagnosticsPumpTask.ConfigureAwait(false);

		// A payload accepted before invalidation must not reach subscribers after the transport was marked unavailable.
		Assert.AreEqual(0, publishedCount);
	}

	[TestMethod]
	public void BuildInitializeParams_WithoutWorkspaceRoots_OmitsRootsAndPinsUtf16()
	{
		using var client = new LanguageServerClient([], "example-language-server.exe", s_defaultClientOptions);

		System.Text.Json.JsonElement payload = System.Text.Json.JsonSerializer.SerializeToElement(client.BuildInitializeParams());

		Assert.AreEqual(System.Text.Json.JsonValueKind.Null, payload.GetProperty("rootUri").ValueKind);
		Assert.AreEqual(System.Text.Json.JsonValueKind.Null, payload.GetProperty("workspaceFolders").ValueKind);
		Assert.AreEqual("utf-16",
			payload.GetProperty("capabilities").GetProperty("general").GetProperty("positionEncodings")[0].GetString());
	}

	[TestMethod]
	public void BuildInitializeParams_ConsumerAdvertisesUtf8_OverridesEncodingAndWorkDoneProgress()
	{
		var options = new LanguageServerClientOptions
		{
			ClientCapabilitiesProvider = _ => new
			{
				General = new { PositionEncodings = new[] { "utf-8" } },
				Window = new { WorkDoneProgress = true }
			}
		};

		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", options);

		System.Text.Json.JsonElement capabilities = System.Text.Json.JsonSerializer.SerializeToElement(client.BuildInitializeParams())
			.GetProperty("capabilities");

		Assert.AreEqual("utf-16", capabilities.GetProperty("general").GetProperty("positionEncodings")[0].GetString());
		Assert.IsFalse(capabilities.GetProperty("window").GetProperty("workDoneProgress").GetBoolean());
	}

	private static int CountSentMethods(string writtenText, string method)
	{
		int count = 0;

		foreach (var message in ExtractWrittenMessages(writtenText))
		{
			if (string.Equals(message.Method, method, StringComparison.Ordinal))
				count++;
		}

		return count;
	}
}

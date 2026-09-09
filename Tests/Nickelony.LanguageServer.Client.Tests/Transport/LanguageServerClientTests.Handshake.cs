using System.Text;
using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

public partial class LanguageServerClientTests
{
	[TestMethod]
	public async Task CompleteHandshakeAsync_WithScriptedResponses_PublishesCapabilitiesAndPushesSettingsInOrder()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", new LanguageServerClientOptions(static () => new
		{
			example = new
			{
				runtime = new
				{
					version = "4.0"
				}
			}
		}));

		using var recordingStream = new RecordingStream();
		using var responseStream = new DeferredPersistentJsonRpcResponseStream();

		// The helper's first stream is the stream the client reads from; the second stream is the one it writes to.
		LanguageServerTransportSession session = CreateTransportSession(client, 12, process: null, responseStream, recordingStream, startListening: true);

		SetActiveSession(client, session);

		Task<bool> handshakeTask = client.CompleteHandshakeAsync(session, CancellationToken.None, CancellationToken.None);

		int requestId = await WaitForRequestIdAsync(recordingStream).ConfigureAwait(false);

		responseStream.SetPayload(CreateJsonRpcResponse(requestId,
			"""
			{
			  "capabilities": {
			    "textDocumentSync": 2,
			    "referencesProvider": true,
			    "semanticTokensProvider": {
			      "full": { "delta": true },
			      "legend": {
			        "tokenTypes": [ "keyword" ],
			        "tokenModifiers": [ "declaration" ]
			      }
			    }
			  }
			}
			"""));

		Assert.IsTrue(await handshakeTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));

		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(TextDocumentSyncKind.Incremental, client.TextDocumentSyncKind);
		Assert.IsTrue(client.SupportsReferences);
		Assert.IsTrue(client.SupportsSemanticTokensFull);
		Assert.IsTrue(client.SupportsSemanticTokensDelta);
		CollectionAssert.AreEqual(new[] { "keyword" }, client.SemanticTokenTypes.ToArray());
		CollectionAssert.AreEqual(new[] { "declaration" }, client.SemanticTokenModifiers.ToArray());

		await TestWait.UntilAsync(
			() => recordingStream.GetWrittenText().Contains("workspace/didChangeConfiguration", StringComparison.Ordinal),
			TimeSpan.FromSeconds(5),
			"The handshake should push the settings notification after initialization completes.").ConfigureAwait(false);

		string writtenText = recordingStream.GetWrittenText();
		List<(string? Method, string BodyJson)> writtenMessages = ExtractWrittenMessages(writtenText);

		CollectionAssert.AreEqual(
			new[] { "initialize", "initialized", "workspace/didChangeConfiguration" },
			writtenMessages.Where(message => message.Method is not null).Select(message => message.Method!).ToArray());

		(string? Method, string BodyJson) initializeMessage = writtenMessages.Single(message => message.Method == "initialize");
		using JsonDocument initializeDocument = JsonDocument.Parse(initializeMessage.BodyJson);
		JsonElement initializeParameters = initializeDocument.RootElement.GetProperty("params");

		Assert.IsTrue(initializeParameters.TryGetProperty("processId", out JsonElement processIdElement));
		Assert.AreEqual(JsonValueKind.Number, processIdElement.ValueKind);
		Assert.AreEqual(Environment.ProcessId, processIdElement.GetInt32());
		Assert.AreEqual(LanguageServerPaths.CreateFileUri(@"C:\Workspace"), initializeParameters.GetProperty("rootUri").GetString());
		Assert.AreEqual(JsonValueKind.Object, initializeParameters.GetProperty("capabilities").ValueKind);

		(string? Method, string BodyJson) configurationMessage = writtenMessages.Single(message => message.Method == "workspace/didChangeConfiguration");
		using JsonDocument configurationDocument = JsonDocument.Parse(configurationMessage.BodyJson);
		JsonElement settings = configurationDocument.RootElement.GetProperty("params").GetProperty("settings");

		Assert.AreEqual("4.0", settings.GetProperty("example").GetProperty("runtime").GetProperty("version").GetString());
	}

	[TestMethod]
	public async Task CompleteHandshakeAsync_WithPascalCaseSettingsProvider_PushesCamelCaseSettings()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", new LanguageServerClientOptions(static () => new
		{
			Example = new
			{
				Runtime = new
				{
					Version = "4.0"
				}
			}
		}));

		using var recordingStream = new RecordingStream();
		using var responseStream = new DeferredPersistentJsonRpcResponseStream();

		LanguageServerTransportSession session = CreateTransportSession(client, 12, process: null, responseStream, recordingStream, startListening: true);
		SetActiveSession(client, session);

		Task<bool> handshakeTask = client.CompleteHandshakeAsync(session, CancellationToken.None, CancellationToken.None);

		int requestId = await WaitForRequestIdAsync(recordingStream).ConfigureAwait(false);

		responseStream.SetPayload(CreateJsonRpcResponse(requestId,
			"""
			{
			  "capabilities": { "textDocumentSync": 2 }
			}
			"""));

		Assert.IsTrue(await handshakeTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));

		await TestWait.UntilAsync(
			() => recordingStream.GetWrittenText().Contains("workspace/didChangeConfiguration", StringComparison.Ordinal),
			TimeSpan.FromSeconds(5),
			"The handshake should push the settings notification after initialization completes.").ConfigureAwait(false);

		List<(string? Method, string BodyJson)> writtenMessages = ExtractWrittenMessages(recordingStream.GetWrittenText());
		(string? Method, string BodyJson) configurationMessage = writtenMessages.Single(message => message.Method == "workspace/didChangeConfiguration");

		using JsonDocument configurationDocument = JsonDocument.Parse(configurationMessage.BodyJson);
		JsonElement settings = configurationDocument.RootElement.GetProperty("params").GetProperty("settings");

		// The push and the workspace/configuration replies are served from the same serialized element, so the
		// provider's PascalCase members reach the server in lower camel case on both channels.
		Assert.AreEqual("4.0", settings.GetProperty("example").GetProperty("runtime").GetProperty("version").GetString());
	}

	[TestMethod]
	public async Task CompleteHandshakeAsync_WhenInitializeResultIsNull_FailsWithTheMissingCapabilitiesDiagnostic()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", new LanguageServerClientOptions(static () => new { }));

		using var recordingStream = new RecordingStream();
		using var responseStream = new DeferredPersistentJsonRpcResponseStream();

		LanguageServerTransportSession session = CreateTransportSession(client, 12, process: null, responseStream, recordingStream, startListening: true);
		SetActiveSession(client, session);

		Task<bool> handshakeTask = client.CompleteHandshakeAsync(session, CancellationToken.None, CancellationToken.None);

		int requestId = await WaitForRequestIdAsync(recordingStream).ConfigureAwait(false);
		responseStream.SetPayload(CreateJsonRpcResponse(requestId, "null"));

		NotSupportedException exception = await Assert.ThrowsExactlyAsync<NotSupportedException>(() => handshakeTask).ConfigureAwait(false);

		Assert.IsTrue(exception.Message.Contains("server capabilities", StringComparison.OrdinalIgnoreCase));
	}

	private static List<(string? Method, string BodyJson)> ExtractWrittenMessages(string writtenText)
	{
		var messages = new List<(string? Method, string BodyJson)>();
		int searchIndex = 0;

		while (true)
		{
			int contentLengthIndex = writtenText.IndexOf("Content-Length:", searchIndex, StringComparison.OrdinalIgnoreCase);

			if (contentLengthIndex < 0)
				break;

			int headerEnd = writtenText.IndexOf("\r\n\r\n", contentLengthIndex, StringComparison.Ordinal);

			if (headerEnd < 0)
				break;

			int contentLengthValueEnd = writtenText.IndexOf("\r\n", contentLengthIndex, StringComparison.Ordinal);
			string contentLengthText = writtenText[(contentLengthIndex + "Content-Length:".Length)..contentLengthValueEnd].Trim();

			if (!int.TryParse(contentLengthText, out int contentLength) || contentLength < 0)
				break;

			int bodyStartIndex = headerEnd + 4;

			if (writtenText.Length < bodyStartIndex + contentLength)
				break;

			string bodyJson = writtenText.Substring(bodyStartIndex, contentLength);
			string? method = null;

			using (JsonDocument document = JsonDocument.Parse(bodyJson))
			{
				if (document.RootElement.TryGetProperty("method", out JsonElement methodElement))
					method = methodElement.GetString();
			}

			messages.Add((method, bodyJson));
			searchIndex = bodyStartIndex + contentLength;
		}

		return messages;
	}

	private static string CreateJsonRpcResponse(int id, string resultJson)
	{
		string payload = "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":" + resultJson + "}";
		int payloadLength = Encoding.UTF8.GetByteCount(payload);
		return "Content-Length: " + payloadLength + "\r\n\r\n" + payload;
	}
}

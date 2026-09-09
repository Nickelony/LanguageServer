using Nickelony.IDEKit.Core.Text;
using System.Text.Json;

namespace Nickelony.LanguageServer.Lua.Tests;

public sealed partial class LuaLanguageServerIntelliSenseProviderTests
{
	[TestMethod]
	public async Task GetCodeActionsAsync_SendsTheCodeActionRequestAndMapsTheResponse()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			CodeActionsResponse = JsonSerializer.SerializeToElement(new object[]
			{
				new
				{
					title = "Add semicolon",
					kind = "quickfix",
					edit = new
					{
						changes = new Dictionary<string, object[]>
						{
							[new Uri(filePath).AbsoluteUri] =
							[
								new
								{
									range = new
									{
										start = new { line = 0, character = 0 },
										end = new { line = 0, character = 5 }
									},
									newText = "local!"
								}
							]
						}
					}
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		IReadOnlyList<TextCodeAction> actions = await provider.GetCodeActionsAsync(
			new TextCodeActionRequest(filePath, content, new TextPositionRange(new TextPosition(0, 6), new TextPosition(0, 11))));

		JsonElement parameters = client.GetLastRequestParameters("textDocument/codeAction");

		Assert.AreEqual(
			Nickelony.LanguageServer.Client.LanguageServerPaths.CreateFileUri(filePath),
			parameters.GetProperty("textDocument").GetProperty("uri").GetString());
		Assert.AreEqual(6, parameters.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
		Assert.AreEqual(11, parameters.GetProperty("range").GetProperty("end").GetProperty("character").GetInt32());
		Assert.AreEqual(0, parameters.GetProperty("context").GetProperty("diagnostics").GetArrayLength());

		Assert.AreEqual(1, actions.Count);
		Assert.AreEqual("Add semicolon", actions[0].Title);
		Assert.AreEqual("quickfix", actions[0].Kind);
		Assert.IsFalse(actions[0].IsPreferred);
		Assert.AreEqual("local!", actions[0].Edit.DocumentEdits[0].TextEdits[0].NewText);
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_ContextCarriesTheCachedDiagnosticsForTheRange()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";
		const string content = "local value = 1\nprint(value)\n";

		using var client = new FakeLanguageServerClient
		{
			CodeActionsResponse = JsonSerializer.SerializeToElement(Array.Empty<object>())
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		client.PublishDiagnostics(new PublishDiagnosticsParams(
			new Uri(filePath).AbsoluteUri,
			1,
			[
				new DiagnosticPayload(
					new ProtocolRangePayload(new ProtocolPosition(0, 6), new ProtocolPosition(0, 11)),
					DiagnosticSeverity.Warning,
					"Inside range.",
					null,
					null),
				new DiagnosticPayload(
					new ProtocolRangePayload(new ProtocolPosition(0, 0), new ProtocolPosition(0, 5)),
					DiagnosticSeverity.Warning,
					"Outside range.",
					null,
					null)
			]));

		Assert.AreEqual(2, provider.GetDiagnostics(filePath).Count);

		await provider.GetCodeActionsAsync(new TextCodeActionRequest(
			filePath, content, new TextPositionRange(new TextPosition(0, 6), new TextPosition(0, 11))));

		JsonElement diagnostics = client.GetLastRequestParameters("textDocument/codeAction")
			.GetProperty("context")
			.GetProperty("diagnostics");

		// Only the diagnostic that intersects the requested range belongs to the request context.
		Assert.AreEqual(1, diagnostics.GetArrayLength());

		JsonElement diagnostic = diagnostics[0];

		Assert.AreEqual("Inside range.", diagnostic.GetProperty("message").GetString());
		Assert.AreEqual(2, diagnostic.GetProperty("severity").GetInt32());
		Assert.AreEqual(6, diagnostic.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
		Assert.AreEqual(11, diagnostic.GetProperty("range").GetProperty("end").GetProperty("character").GetInt32());
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_ContextPositionsComeFromTheParsedSnapshot()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";
		const string trackedContent = "local value = 1";
		const string editedContent = "local\nvalue = 1";

		using var client = new FakeLanguageServerClient
		{
			CodeActionsResponse = JsonSerializer.SerializeToElement(Array.Empty<object>())
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		provider.OpenDocument(filePath, trackedContent);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		client.PublishDiagnostics(new PublishDiagnosticsParams(
			new Uri(filePath).AbsoluteUri,
			1,
			[
				new DiagnosticPayload(
					new ProtocolRangePayload(new ProtocolPosition(0, 6), new ProtocolPosition(0, 11)),
					DiagnosticSeverity.Warning,
					"Inside range.",
					null,
					null)
			]));

		Assert.AreEqual(1, provider.GetDiagnostics(filePath).Count);

		// The caller supplies newer text than the snapshot the diagnostics were parsed against; the
		// emitted context positions must keep addressing the parsed snapshot's coordinates.
		await provider.GetCodeActionsAsync(new TextCodeActionRequest(
			filePath, editedContent, new TextPositionRange(new TextPosition(0, 0), new TextPosition(1, 10))));

		JsonElement diagnostics = client.GetLastRequestParameters("textDocument/codeAction")
			.GetProperty("context")
			.GetProperty("diagnostics");

		Assert.AreEqual(1, diagnostics.GetArrayLength());

		JsonElement range = diagnostics[0].GetProperty("range");

		Assert.AreEqual(0, range.GetProperty("start").GetProperty("line").GetInt32());
		Assert.AreEqual(6, range.GetProperty("start").GetProperty("character").GetInt32());
		Assert.AreEqual(0, range.GetProperty("end").GetProperty("line").GetInt32());
		Assert.AreEqual(11, range.GetProperty("end").GetProperty("character").GetInt32());
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_ReversedRange_ProducesAnEmptyDiagnosticsContext()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			CodeActionsResponse = JsonSerializer.SerializeToElement(Array.Empty<object>())
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		client.PublishDiagnostics(CreateDiagnostics(filePath, 1, 6, 11, "Current warning."));
		Assert.AreEqual(1, provider.GetDiagnostics(filePath).Count);

		// A reversed range cannot be mapped to offsets, so the request context stays empty instead of
		// guessing which diagnostics were meant.
		await provider.GetCodeActionsAsync(new TextCodeActionRequest(
			filePath, content, new TextPositionRange(new TextPosition(0, 11), new TextPosition(0, 6))));

		JsonElement diagnostics = client.GetLastRequestParameters("textDocument/codeAction")
			.GetProperty("context")
			.GetProperty("diagnostics");

		Assert.AreEqual(0, diagnostics.GetArrayLength());
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_WhenUnsupported_ReturnsEmptyAndSendsNoRequest()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";

		using var client = new FakeLanguageServerClient { SupportsCodeActions = false };
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		IReadOnlyList<TextCodeAction> actions = await provider.GetCodeActionsAsync(
			new TextCodeActionRequest(filePath, "local value = 1", new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 5))));

		// The member is capability-gated: without a negotiated code-action provider the request is
		// skipped before the document is synchronized, so no traffic is produced at all.
		Assert.AreEqual(0, actions.Count);
		Assert.AreEqual(0, client.GetSentMethodNames().Length);
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_InvalidFilePath_ReturnsEmptyWithoutSending()
	{
		const string workspaceRoot = @"C:\Workspace";

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		IReadOnlyList<TextCodeAction> actions = await provider.GetCodeActionsAsync(
			new TextCodeActionRequest("   ", "local value = 1", default));

		Assert.AreEqual(0, actions.Count);
		Assert.AreEqual(0, client.GetSentMethodNames().Length);
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_NullArguments_Throw()
	{
		const string workspaceRoot = @"C:\Workspace";

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(
			() => provider.GetCodeActionsAsync(null!));
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_CommandOnlyActions_AreNotReturned()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";

		using var client = new FakeLanguageServerClient
		{
			CodeActionsResponse = JsonSerializer.SerializeToElement(new object[]
			{
				new
				{
					title = "Disable diagnostic",
					kind = "quickfix",
					command = new { title = "Disable", command = "lua.setConfig" }
				},
				new
				{
					title = "Add semicolon",
					kind = "quickfix",
					edit = new
					{
						changes = new Dictionary<string, object[]>
						{
							[new Uri(filePath).AbsoluteUri] =
							[
								new
								{
									range = new
									{
										start = new { line = 0, character = 0 },
										end = new { line = 0, character = 0 }
									},
									newText = ";"
								}
							]
						}
					}
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		IReadOnlyList<TextCodeAction> actions = await provider.GetCodeActionsAsync(
			new TextCodeActionRequest(filePath, "local value = 1", new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 5))));

		Assert.AreEqual(1, actions.Count);
		Assert.AreEqual("Add semicolon", actions[0].Title);
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_WhenTheCallerTextDiffers_KeepsEveryCachedDiagnostic()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";
		const string trackedContent = "local value = 1";
		const string editedContent = "local\nvalue = 1";

		using var client = new FakeLanguageServerClient
		{
			CodeActionsResponse = JsonSerializer.SerializeToElement(Array.Empty<object>())
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		provider.OpenDocument(filePath, trackedContent);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		client.PublishDiagnostics(CreateDiagnostics(filePath, 1, 6, 11, "Snapshot warning."));
		Assert.AreEqual(1, provider.GetDiagnostics(filePath).Count);

		// The caller's text differs from the parsed snapshot, so the requested range cannot be
		// intersected with the snapshot coordinates: the context keeps the cached diagnostics instead
		// of dropping entries through an inexact comparison, and the positions stay snapshot-based.
		await provider.GetCodeActionsAsync(new TextCodeActionRequest(
			filePath, editedContent, new TextPositionRange(new TextPosition(1, 0), new TextPosition(1, 5))));

		JsonElement diagnostics = client.GetLastRequestParameters("textDocument/codeAction")
			.GetProperty("context")
			.GetProperty("diagnostics");

		Assert.AreEqual(1, diagnostics.GetArrayLength());
	}
}

using System.Text.Json;

namespace Nickelony.LanguageServer.Lua.Tests;

/// <summary>
/// Pins the handshake payloads the provider advertises to LuaLS: the client capabilities and the
/// initialization options must stay aligned with the features the provider actually implements.
/// </summary>
[TestClass]
public sealed class LuaLanguageServerHandshakeFactoryTests
{
	[TestMethod]
	public void ClientCapabilities_AdvertiseSnippetsDocumentationResolveAndSemanticTokens()
	{
		JsonElement capabilities = JsonSerializer.SerializeToElement(LuaLanguageServerClientCapabilitiesFactory.Create());
		JsonElement completionItem = capabilities
			.GetProperty("textDocument")
			.GetProperty("completion")
			.GetProperty("completionItem");

		// Snippet support is advertised because the provider consumes snippet insert texts end to end.
		Assert.IsTrue(completionItem.GetProperty("snippetSupport").GetBoolean());

		string?[] resolveSupportProperties = completionItem
			.GetProperty("resolveSupport")
			.GetProperty("properties")
			.EnumerateArray()
			.Select(property => property.GetString())
			.ToArray();

		CollectionAssert.Contains(resolveSupportProperties, "detail");
		CollectionAssert.Contains(resolveSupportProperties, "documentation");

		JsonElement semanticTokens = capabilities.GetProperty("textDocument").GetProperty("semanticTokens");

		// The provider consumes the hierarchical document-symbol shape, so the client advertises it.
		Assert.IsTrue(capabilities
			.GetProperty("textDocument")
			.GetProperty("documentSymbol")
			.GetProperty("hierarchicalDocumentSymbolSupport")
			.GetBoolean());

		// The provider consumes full semantic-token requests only; LuaLS does not answer delta
		// requests, so the client does not advertise delta support.
		Assert.IsTrue(semanticTokens.GetProperty("requests").GetProperty("full").GetBoolean());

		// LuaLS only sends workspace/semanticTokens/refresh when the client advertises refresh support.
		Assert.IsTrue(capabilities
			.GetProperty("workspace")
			.GetProperty("semanticTokens")
			.GetProperty("refreshSupport")
			.GetBoolean());

		string?[] legend = semanticTokens
			.GetProperty("tokenTypes")
			.EnumerateArray()
			.Select(tokenType => tokenType.GetString())
			.ToArray();

		// The legend mirrors the shared LSP 3.17 + 3.18 vocabulary; the exact list is pinned so a
		// dropped entry cannot silently degrade token colors.
		string?[] expectedLegend =
		[
			"namespace", "type", "class", "enum", "interface", "struct", "typeParameter", "parameter",
			"variable", "property", "enumMember", "event", "function", "method", "macro", "keyword",
			"modifier", "comment", "string", "number", "regexp", "operator", "decorator", "label"
		];

		CollectionAssert.AreEqual(expectedLegend, legend);
		CollectionAssert.AllItemsAreUnique(legend);

		string?[] modifiers = semanticTokens
			.GetProperty("tokenModifiers")
			.EnumerateArray()
			.Select(modifier => modifier.GetString())
			.ToArray();

		// The modifier legend mirrors the shared vocabulary and declares LuaLS's 'global' extension.
		string?[] expectedModifiers =
		[
			"declaration", "definition", "readonly", "static", "deprecated", "abstract", "async",
			"modification", "documentation", "defaultLibrary", LuaLanguageServerClientCapabilitiesFactory.GlobalSemanticTokenModifier
		];

		CollectionAssert.AreEqual(expectedModifiers, modifiers);
		CollectionAssert.AllItemsAreUnique(modifiers);

		// Versioned diagnostics are the precondition for the cached-diagnostics version fences, and
		// label-offset support is the precondition for the offset-pair parameter labels.
		Assert.IsTrue(capabilities
			.GetProperty("textDocument")
			.GetProperty("publishDiagnostics")
			.GetProperty("versionSupport")
			.GetBoolean());
		Assert.IsTrue(capabilities
			.GetProperty("textDocument")
			.GetProperty("signatureHelp")
			.GetProperty("signatureInformation")
			.GetProperty("parameterInformation")
			.GetProperty("labelOffsetSupport")
			.GetBoolean());
	}

	[TestMethod]
	public void ClientCapabilities_AdvertiseCodeActionLiteralSupport()
	{
		JsonElement capabilities = JsonSerializer.SerializeToElement(LuaLanguageServerClientCapabilitiesFactory.Create());
		JsonElement codeAction = capabilities.GetProperty("textDocument").GetProperty("codeAction");

		// The provider consumes literal CodeAction objects with inline edits, so the client advertises
		// the literal form; without it a server may fall back to the command form it cannot execute.
		JsonElement codeActionKind = codeAction
			.GetProperty("codeActionLiteralSupport")
			.GetProperty("codeActionKind");

		string?[] kinds = codeActionKind
			.GetProperty("valueSet")
			.EnumerateArray()
			.Select(kind => kind.GetString())
			.ToArray();

		CollectionAssert.Contains(kinds, "quickfix");
		CollectionAssert.Contains(kinds, "refactor.rewrite");
		Assert.IsTrue(codeAction.GetProperty("isPreferredSupport").GetBoolean());
	}

	[TestMethod]
	public void InitializationOptions_DisableInteractiveTrustFlows()
	{
		JsonElement options = JsonSerializer.SerializeToElement(LuaLanguageServerInitializationOptionsFactory.Create());

		// A library cannot answer LuaLS's interactive prompts, so trust stays client-disabled; and the
		// client cannot serve LuaLS's configuration-modification or document-reveal flows, so those
		// flags stay disabled too (LuaLS falls back to its own configuration handling).
		Assert.IsFalse(options.GetProperty("changeConfiguration").GetBoolean());
		Assert.IsFalse(options.GetProperty("viewDocument").GetBoolean());
		Assert.IsFalse(options.GetProperty("trustByClient").GetBoolean());

		// Semantic tokens are consumed from full responses; range requests stay disabled.
		Assert.IsFalse(options.GetProperty("useSemanticByRange").GetBoolean());
	}

	[TestMethod]
	public void ProviderConstruction_WiresTheHandshakeFactoriesIntoTheRealClient()
	{
		const string workspaceRoot = @"C:\Workspace";

		// A configured executable path makes the public constructor build the real client. The wired
		// factories are read reflectively because neither the client nor the provider exposes them, and
		// a dropped registration would otherwise only surface in the opt-in integration tests.
		using var provider = new LuaLanguageServerIntelliSenseProvider(
			[workspaceRoot], @"C:\tools\lua-language-server\lua-language-server.exe");

		LanguageServerClient client = LuaLanguageServerIntelliSenseProviderTestAccess.GetProviderClient(provider);
		IReadOnlyList<string> normalizedRoots = LanguageServerPaths.NormalizeWorkspaceRoots([workspaceRoot]);

		Assert.AreEqual(
			JsonSerializer.SerializeToElement(LuaLanguageServerClientCapabilitiesFactory.Create()).GetRawText(),
			JsonSerializer.SerializeToElement(LuaLanguageServerIntelliSenseProviderTestAccess.InvokeClientCapabilitiesProvider(client, normalizedRoots)).GetRawText());
		Assert.AreEqual(
			JsonSerializer.SerializeToElement(LuaLanguageServerInitializationOptionsFactory.Create()).GetRawText(),
			JsonSerializer.SerializeToElement(LuaLanguageServerIntelliSenseProviderTestAccess.InvokeInitializationOptionsProvider(client, normalizedRoots)).GetRawText());
		Assert.AreEqual(
			JsonSerializer.SerializeToElement(LuaLanguageServerSettingsFactory.Create(LuaLanguageServerOptions.Default)).GetRawText(),
			JsonSerializer.SerializeToElement(LuaLanguageServerIntelliSenseProviderTestAccess.InvokeSettingsProvider(client)).GetRawText());
	}

	[TestMethod]
	public void Settings_ForwardLibraryFoldersAndFilterBlankEntries()
	{
		JsonElement settings = JsonSerializer.SerializeToElement(LuaLanguageServerSettingsFactory.Create(
			new LuaLanguageServerOptions
			{
				AdditionalLibraryDirectories = [@"C:\libs\a", "   ", "", @"C:\libs\b"]
			}));

		JsonElement lua = settings.GetProperty("Lua");

		string?[] library = lua
			.GetProperty("workspace")
			.GetProperty("library")
			.EnumerateArray()
			.Select(entry => entry.GetString())
			.ToArray();

		CollectionAssert.AreEqual(new[] { @"C:\libs\a", @"C:\libs\b" }, library);

		// Third-party checks stay disabled because a library cannot answer LuaLS's interactive prompts.
		Assert.AreEqual("Disable", lua.GetProperty("workspace").GetProperty("checkThirdParty").GetString());
	}

	[TestMethod]
	public void ProviderConstruction_IntegrationProcessSeamResolvesWithoutALiveSession()
	{
		const string workspaceRoot = @"C:\Workspace";

		// A configured executable path makes the public constructor build the real client. The live
		// integration tier reaches the spawned server process through this seam; resolving it here
		// fails loudly when the internal path chain is renamed, instead of surfacing only when the
		// opt-in archive is configured.
		using var provider = new LuaLanguageServerIntelliSenseProvider(
			[workspaceRoot], @"C:\tools\lua-language-server\lua-language-server.exe");

		LanguageServerClient client = LuaLanguageServerIntelliSenseProviderTestAccess.GetProviderClient(provider);

		Assert.IsNull(LuaLanguageServerIntelliSenseProviderTestAccess.GetActiveServerProcess(client));
	}
}

using System.Text.Json;

namespace Nickelony.LanguageServer.Lua.Tests;

/// <summary>
/// Covers the rename request guards: an unsupported capability, a blank new name, an unusable target
/// path, and request-coordinate clamping.
/// </summary>
public sealed partial class LuaLanguageServerIntelliSenseProviderTests
{
	[TestMethod]
	public async Task RenameSymbolAsync_WhenUnsupported_ReturnsNullAndSendsNoRename()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";

		using var client = new FakeLanguageServerClient { SupportsRename = false };
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		TextWorkspaceEdit? workspaceEdit = await provider.RenameSymbolAsync(
			new TextRenameRequest(filePath, "local value = 1", 0, 6, "renamed"));

		// The capability gate runs before document synchronization, so an unsupported request
		// produces no traffic at all.
		Assert.IsNull(workspaceEdit);
		Assert.AreEqual(0, client.GetSentMethodNames().Length);
	}

	[TestMethod]
	public async Task RenameSymbolAsync_WhitespaceNewName_ReturnsNullWithoutSendingAnything()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		TextWorkspaceEdit? workspaceEdit = await provider.RenameSymbolAsync(
			new TextRenameRequest(filePath, "local value = 1", 0, 6, "   "));

		Assert.IsNull(workspaceEdit);
		Assert.AreEqual(0, client.GetSentMethodNames().Length);
	}

	[TestMethod]
	public async Task RenameSymbolAsync_UnnormalizablePath_ReturnsNullWithoutSendingAnything()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = "C:\\bad\0name.lua";

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		TextWorkspaceEdit? workspaceEdit = await provider.RenameSymbolAsync(
			new TextRenameRequest(filePath, "local value = 1", 0, 6, "renamed"));

		Assert.IsNull(workspaceEdit);
		Assert.AreEqual(0, client.GetSentMethodNames().Length);
	}

	[TestMethod]
	public async Task RenameSymbolAsync_NegativeCoordinates_ClampToTheDocumentStart()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";

		using var client = new FakeLanguageServerClient
		{
			RenameResponse = JsonSerializer.SerializeToElement<object?>(null)
		};
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		await provider.RenameSymbolAsync(new TextRenameRequest(filePath, "local value = 1", -2, -4, "renamed"));

		// Request coordinates are clamped to zero like the shared position-based request path.
		JsonElement position = client.GetLastRequestParameters("textDocument/rename").GetProperty("position");

		Assert.AreEqual(0, position.GetProperty("line").GetInt32());
		Assert.AreEqual(0, position.GetProperty("character").GetInt32());
	}
}

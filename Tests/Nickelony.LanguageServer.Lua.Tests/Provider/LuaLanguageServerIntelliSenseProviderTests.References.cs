using System.Text.Json;

namespace Nickelony.LanguageServer.Lua.Tests;

/// <summary>
/// Covers the references request guards: the capability gate (an unsupported server produces an empty
/// result without any traffic) and request-coordinate clamping.
/// </summary>
public sealed partial class LuaLanguageServerIntelliSenseProviderTests
{
	[TestMethod]
	public async Task GetReferencesAsync_WhenUnsupported_ReturnsEmptyAndSendsNoRequest()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";

		using var client = new FakeLanguageServerClient { SupportsReferences = false };
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		IReadOnlyList<TextReferenceLocation> references = await provider.GetReferencesAsync(
			new TextReferenceRequest(filePath, "local value = 1", 0, 6));

		// The capability gate runs before document synchronization, so an unsupported request
		// produces no traffic at all.
		Assert.AreEqual(0, references.Count);
		Assert.AreEqual(0, client.GetSentMethodNames().Length);
	}

	[TestMethod]
	public async Task GetReferencesAsync_NegativeCoordinates_ClampToTheDocumentStart()
	{
		const string workspaceRoot = @"C:\Workspace";
		const string filePath = @"C:\Workspace\Scripts\test.lua";

		using var client = new FakeLanguageServerClient
		{
			ReferencesResponse = JsonSerializer.SerializeToElement(Array.Empty<object>())
		};
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		await provider.GetReferencesAsync(new TextReferenceRequest(filePath, "local value = 1", -5, -3));

		// Request coordinates are clamped to zero like the shared position-based request path.
		JsonElement position = client.GetLastRequestParameters("textDocument/references").GetProperty("position");

		Assert.AreEqual(0, position.GetProperty("line").GetInt32());
		Assert.AreEqual(0, position.GetProperty("character").GetInt32());
	}
}

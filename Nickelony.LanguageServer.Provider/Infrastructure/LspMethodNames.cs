namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Carries the LSP method names the provider framework sends, so each wire name exists once in the package.
/// </summary>
internal static class LspMethodNames
{
	/// <summary>The <c>textDocument/didOpen</c> notification method.</summary>
	internal const string DidOpen = "textDocument/didOpen";

	/// <summary>The <c>textDocument/didChange</c> notification method.</summary>
	internal const string DidChange = "textDocument/didChange";

	/// <summary>The <c>textDocument/didClose</c> notification method.</summary>
	internal const string DidClose = "textDocument/didClose";

	/// <summary>The <c>workspace/didChangeConfiguration</c> notification method.</summary>
	internal const string DidChangeConfiguration = "workspace/didChangeConfiguration";

	/// <summary>The <c>workspace/didChangeWatchedFiles</c> notification method.</summary>
	internal const string DidChangeWatchedFiles = "workspace/didChangeWatchedFiles";
}

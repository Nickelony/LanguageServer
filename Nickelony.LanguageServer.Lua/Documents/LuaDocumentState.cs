using Nickelony.IDEKit.IntelliSense.Diagnostics;

namespace Nickelony.LanguageServer.Lua;

/// <summary>
/// Extends the core tracked-document state with Lua-specific caches for diagnostics and semantic tokens.
/// </summary>
/// <remarks>
/// <para>
/// The type is public because <see cref="LuaLanguageServerIntelliSenseProvider"/> derives from the public
/// <see cref="LanguageServerIntelliSenseProviderBase{TDocumentState}"/> with this state type; it declares no
/// public members of its own, so the type is opaque to consumers and only parameterizes the provider framework.
/// </para>
/// <para>
/// The state-mutating members below forward to the protected <see cref="TrackedDocumentState"/> operations because
/// the Lua document store does not derive from this state type and cannot call them directly.
/// </para>
/// </remarks>
public sealed class LuaDocumentState : TrackedDocumentState
{
	/// <summary>
	/// Initializes a new instance of the <see cref="LuaDocumentState"/> class.
	/// </summary>
	/// <param name="initialState">The initial tracked-document state.</param>
	internal LuaDocumentState(TrackedDocumentInitialState initialState)
		: base(initialState)
	{ }

	/// <summary>Updates the access stamp used for idle-document eviction ordering.</summary>
	internal void Touch(long lastAccessStamp)
		=> SetLastAccessStamp(lastAccessStamp);

	/// <summary>Marks the document as reopened with fresh synchronized content.</summary>
	internal void Reopen(string content)
		=> ReopenDocument(content);

	/// <summary>
	/// Replaces the tracked content and advances the version, returning the previous content.
	/// </summary>
	internal string UpdateContent(string content)
		=> ReplaceContent(content);

	/// <summary>Replaces the tracked file path and URI after a rename.</summary>
	internal void RenameTo(string filePath, string uri)
		=> RenameDocument(filePath, uri);

	/// <summary>Marks the tracked document as closed locally while preserving cached state.</summary>
	internal void MarkClosed()
		=> MarkDocumentClosed();

	/// <summary>
	/// Gets the version-fenced diagnostics cache for the tracked document, including the content snapshot
	/// the diagnostic offsets refer to.
	/// </summary>
	internal VersionFencedPayloadCache<TextDiagnostic> DiagnosticsCache { get; } = new();

	/// <summary>
	/// Gets the version-fenced semantic-token cache for the tracked document.
	/// </summary>
	internal VersionFencedPayloadCache<SemanticToken> SemanticTokensCache { get; } = new();
}

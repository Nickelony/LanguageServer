namespace Nickelony.LanguageServer.Lua;

/// <summary>
/// Defines the Lua-specific language-service contract used by Lua editing clients to provide IntelliSense features.
/// </summary>
/// <remarks>
/// <para>
/// This interface extends <see cref="ILanguageServerIntelliSenseProvider"/> with Lua-specific concerns such as
/// semantic tokens. The generic contracts (the document lifecycle, diagnostics, and every IntelliSense feature),
/// including the threading, callback, and disposal rules, are defined on the base interface.
/// </para>
/// <para>
/// Diagnostics are published for documents the provider currently tracks. A document that has never been
/// synchronized (opened, edited, or requested through this provider) has no content to map server ranges against,
/// so its published diagnostics are dropped; consumers must synchronize a document before expecting an error list for it.
/// </para>
/// </remarks>
public interface ILuaLanguageServerIntelliSenseProvider : ILanguageServerIntelliSenseProvider
{
	/// <summary>
	/// Occurs when the semantic tokens that are current for a document have changed.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The threading, handler-isolation, and disposal rules are defined on the base interface; this event follows
	/// them, and the provider is the sender. The event is raised when a refreshed token set is stored, when a
	/// rename re-keys a document's cached tokens, and with an empty list when a content-changing rename clears
	/// them. A failed refresh keeps the previously cached tokens and raises no event.
	/// </para>
	/// </remarks>
	event EventHandler<SemanticTokensUpdatedEventArgs>? SemanticTokensUpdated;

	/// <summary>
	/// Gets the latest semantic tokens known for a document.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <returns>
	/// A read-only snapshot of the semantic tokens currently cached for the document; an empty list when the
	/// provider is disposed, the path cannot be resolved to a local file path, or the document has no cached tokens.
	/// </returns>
	/// <remarks>
	/// The tokens reflect the document version they were decoded for and may therefore be stale relative to the
	/// content the consumer is editing. A refresh runs after every successful open or edit synchronization (and
	/// after a restart reopen); request-driven synchronization does not issue an extra refresh. The
	/// <see cref="SemanticTokensUpdated"/> event announces each new token set.
	/// </remarks>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	IReadOnlyList<SemanticToken> GetSemanticTokens(string filePath);
}

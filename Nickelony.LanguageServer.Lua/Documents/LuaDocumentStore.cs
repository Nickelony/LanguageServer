using Nickelony.IDEKit.IntelliSense.Diagnostics;

namespace Nickelony.LanguageServer.Lua;

/// <summary>
/// Tracks local document state for LuaLS synchronization, including versions, diagnostics, and semantic-token caches.
/// </summary>
/// <remarks>
/// The store accepts any local path: cached reads normalize the supplied path internally, so callers may pass
/// an already-normalized path or a raw one.
/// </remarks>
internal sealed class LuaDocumentStore : TrackedDocumentStore<LuaDocumentState>
{
	/// <summary>
	/// Gets the cached diagnostics for the specified file path.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <returns>
	/// The cached diagnostics, ordered by start offset and then by severity, or an empty list when none are stored.
	/// </returns>
	internal IReadOnlyList<TextDiagnostic> GetDiagnostics(string filePath)
		=> WithTrackedDocument(filePath, static state => state.DiagnosticsCache.Items, fallbackValue: []);

	/// <summary>
	/// Gets the cached diagnostics together with the content snapshot their offsets refer to.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <returns>
	/// The cached diagnostics and the content snapshot the payload was parsed against; an empty list and
	/// <see langword="null"/> when nothing is stored. Consumers that convert the offsets back to positions
	/// must use the returned snapshot, because it can differ from the caller's current document text.
	/// </returns>
	internal (IReadOnlyList<TextDiagnostic> Diagnostics, string? SourceContent) GetDiagnosticsSnapshot(string filePath)
	{
		return WithTrackedDocument(filePath,
			static state => (state.DiagnosticsCache.Items, state.DiagnosticsCache.SourceContent),
			fallbackValue: ((IReadOnlyList<TextDiagnostic>)[], null));
	}

	/// <summary>
	/// Gets the cached semantic tokens for the specified file path.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <returns>The cached semantic tokens, or an empty list when none are stored.</returns>
	internal IReadOnlyList<SemanticToken> GetSemanticTokens(string filePath)
		=> WithTrackedDocument(filePath, static state => state.SemanticTokensCache.Items, fallbackValue: []);

	/// <summary>
	/// Stores a diagnostics payload when it is not stale for the tracked document version.
	/// </summary>
	/// <param name="publishedDiagnostics">The diagnostics payload to cache.</param>
	/// <param name="expectedDocumentVersion">The tracked document version observed when the payload was parsed.</param>
	/// <param name="sourceContent">The content snapshot the diagnostic offsets were resolved against.</param>
	/// <returns><see langword="true"/> when the payload was stored; otherwise, <see langword="false"/>.</returns>
	internal bool TryStoreDiagnostics(LuaPublishedDiagnostics publishedDiagnostics, int expectedDocumentVersion, string sourceContent)
	{
		// The parser compared versions against the snapshot it read; this check runs inside the
		// tracked-document lock to fence the parse-to-store window, so a payload parsed against an
		// older snapshot cannot overwrite diagnostics stored for a newer version in the meantime.
		// A payload with an unknown version (0) is accepted without advancing the cached version; a
		// positive payload is accepted when the tracked version is unknown (0) and becomes the new
		// cached version.
		return WithTrackedDocument(
			publishedDiagnostics.FilePath,
			state => LuaDocumentVersionPolicy.IsPayloadCurrent(state.Version, expectedDocumentVersion)
				&& state.DiagnosticsCache.TryStore(publishedDiagnostics.Version, publishedDiagnostics.Diagnostics, sourceContent),
			fallbackValue: false);
	}

	/// <summary>
	/// Stores semantic tokens when they are not stale for the tracked document version.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <param name="version">The document version associated with the tokens.</param>
	/// <param name="semanticTokens">The semantic tokens to cache.</param>
	/// <returns><see langword="true"/> when the token set was stored; otherwise, <see langword="false"/>.</returns>
	internal bool TryStoreSemanticTokens(string filePath, int version, IReadOnlyList<SemanticToken> semanticTokens)
	{
		return WithTrackedDocument(
			filePath,
			state => LuaDocumentVersionPolicy.IsPayloadCurrent(state.Version, version)
				&& state.SemanticTokensCache.TryStore(version, semanticTokens),
			fallbackValue: false);
	}

	/// <summary>
	/// Marks the specified document as needing a fresh server-side open/sync before incremental updates can resume.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <returns><see langword="true"/> when the document was found and invalidated; otherwise, <see langword="false"/>.</returns>
	internal bool InvalidateServerSynchronization(string filePath)
	{
		return WithTrackedDocument(filePath,
			state =>
			{
				MarkTrackedDocumentClosed(state);

				// The server-side synchronization is unknown after the invalidation, so both payload
				// caches drop their version stamps and the next payload of either kind is accepted
				// regardless of the version it reports.
				state.DiagnosticsCache.ResetVersionStamp();
				state.SemanticTokensCache.ResetVersionStamp();
				return true;
			},
			fallbackValue: false);
	}

	protected override LuaDocumentState CreateTrackedDocumentState(TrackedDocumentInitialState initialState)
		=> new(initialState);

	protected override long GetLastAccessStamp(LuaDocumentState state)
		=> state.LastAccessStamp;

	protected override void TouchTrackedDocumentState(LuaDocumentState state, long lastAccessStamp)
		=> state.Touch(lastAccessStamp);

	protected override void ReopenTrackedDocumentState(LuaDocumentState state, string content)
		=> state.Reopen(content);

	protected override string ReplaceTrackedDocumentContent(LuaDocumentState state, string content)
		=> state.UpdateContent(content);

	protected override void RenameTrackedDocumentState(LuaDocumentState state, string filePath, string uri)
		=> state.RenameTo(filePath, uri);

	protected override void MarkTrackedDocumentClosed(LuaDocumentState state)
		=> state.MarkClosed();

	protected override void OnTrackedDocumentRenamed(LuaDocumentState state, bool contentChanged)
	{
		if (contentChanged)
			ClearCachedState(state);
	}

	private static void ClearCachedState(LuaDocumentState state)
	{
		state.DiagnosticsCache.Clear();
		state.SemanticTokensCache.Clear();
	}
}

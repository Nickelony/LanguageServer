using Nickelony.IDEKit.IntelliSense.Diagnostics;

namespace Nickelony.LanguageServer.Provider;

public abstract partial class LanguageServerIntelliSenseProviderBase<TDocumentState>
{
	/// <summary>
	/// Gets the provider name used in log and diagnostic text, for example <c>"Lua"</c>.
	/// </summary>
	/// <remarks>
	/// The value is read once during base construction and must be a constant; implementations must not depend
	/// on derived instance state.
	/// </remarks>
	protected abstract string ProviderDisplayName { get; }

	/// <summary>
	/// Gets the language identifier sent as the <c>languageId</c> of opened documents.
	/// </summary>
	protected abstract string LanguageId { get; }

	/// <summary>
	/// Builds the watch specifications mirrored to the language server: the file patterns the provider mirrors
	/// to the server and uses to keep its recovery snapshots up to date.
	/// </summary>
	/// <returns>
	/// The watch specifications for the workspace, or an empty list to disable workspace watching for this
	/// provider (every root).
	/// </returns>
	/// <remarks>
	/// <para>
	/// Called during base construction; implementations must not depend on derived instance state and should
	/// simply return the specification list for the language.
	/// </para>
	/// <para>
	/// Patterns follow the <see cref="FileSystemWatcher"/> filter grammar (for example <c>*.lua</c> or
	/// <c>.luarc.*</c>) and are matched against file names only; casing follows the configured local-path
	/// comparison policy (case-insensitive on Windows and macOS, ordinal elsewhere). Matching is an implementation
	/// detail of the current watcher; do not reproduce it as a public contract.
	/// </para>
	/// </remarks>
	protected abstract IReadOnlyList<WorkspaceWatchSpecification> CreateWatchSpecifications();

	/// <summary>
	/// Creates the tracked-document store that owns the language's document state.
	/// </summary>
	/// <returns>The tracked-document store for the language.</returns>
	/// <remarks>
	/// Called once during base construction; implementations must not depend on derived instance state. The
	/// returned store is owned by the provider, which mediates all synchronization through its request pipeline.
	/// </remarks>
	protected abstract TrackedDocumentStore<TDocumentState> CreateTrackedDocumentStore();

	/// <summary>
	/// Reports whether a normalized changed workspace path requires a settings refresh before the watched-files
	/// notification is forwarded to the language server.
	/// </summary>
	/// <param name="normalizedPath">The normalized path of the changed file.</param>
	/// <returns><see langword="true"/> when the path is a configuration file for this language.</returns>
	protected abstract bool IsConfigurationPath(string normalizedPath);

	/// <summary>
	/// Creates the settings payload sent to the language server when a configuration file changes.
	/// </summary>
	/// <returns>The payload for the <c>workspace/didChangeConfiguration</c> notification.</returns>
	protected abstract object CreateSettingsPayload();

	/// <summary>
	/// Creates the startup-failure report raised through <see cref="StartupFailed"/> after the language server
	/// failed to start.
	/// </summary>
	/// <param name="isPermanentFailure">
	/// <see langword="true"/> when the consecutive-failure threshold was reached and the provider entered the
	/// failed state; otherwise, <see langword="false"/> for a transient failure that the provider retries.
	/// </param>
	/// <returns>The failure description raised to subscribers.</returns>
	/// <remarks>
	/// A throwing implementation is contained: the failure is logged and <see cref="StartupFailed"/> is not raised
	/// for that failure.
	/// </remarks>
	protected abstract LanguageServerStartupFailure CreateStartupFailure(bool isPermanentFailure);

	/// <summary>
	/// Creates the persistent startup-failure report raised when no language-server client was configured.
	/// </summary>
	/// <returns>The failure description raised to subscribers.</returns>
	/// <remarks>
	/// A throwing implementation is contained: the failure is logged and <see cref="StartupFailed"/> is not raised
	/// for that failure.
	/// </remarks>
	protected abstract LanguageServerStartupFailure CreateMissingClientFailure();

	/// <summary>
	/// Converts a published diagnostics payload for a tracked document and stores it for later retrieval.
	/// </summary>
	/// <param name="filePath">The normalized local file path that the payload was matched to.</param>
	/// <param name="parameters">The published diagnostics payload.</param>
	/// <param name="document">The tracked document snapshot the payload belongs to.</param>
	/// <returns>
	/// The diagnostics to raise through <see cref="DiagnosticsUpdated"/>, or <see langword="null"/> when the
	/// payload should be dropped, for example because it could not be parsed or is stale.
	/// </returns>
	/// <remarks>
	/// <para>
	/// The hook is responsible for storing the payload in the language's caches; the base raises
	/// <see cref="DiagnosticsUpdated"/> when the returned list is not <see langword="null"/> and the provider has
	/// not been disposed in the meantime.
	/// </para>
	/// <para>
	/// Implementations that cache the result should re-check <see cref="IsDisposed"/> (or the tracked document's
	/// version) before publishing it themselves, because the base raises the event only after the hook returns and
	/// disposal can race the raise.
	/// </para>
	/// </remarks>
	protected abstract IReadOnlyList<TextDiagnostic>? HandleDiagnosticsPayload(
		string filePath,
		PublishDiagnosticsParams parameters,
		DocumentSnapshot document);

	/// <summary>
	/// Gets the latest diagnostics cached for a normalized document path.
	/// </summary>
	/// <param name="normalizedFilePath">The normalized local file path of the document.</param>
	/// <returns>The cached diagnostics, or an empty list when none are stored.</returns>
	/// <remarks>A throwing implementation is contained: the framework reads treat the failure as an empty result.</remarks>
	protected abstract IReadOnlyList<TextDiagnostic> GetTrackedDiagnostics(string normalizedFilePath);

	/// <summary>
	/// Refreshes language-specific state for a document that was just synchronized or reopened.
	/// </summary>
	/// <param name="document">The synchronized tracked document snapshot.</param>
	/// <param name="cancellationToken">Cancels the refresh.</param>
	/// <returns>A task that completes when the refresh has finished.</returns>
	/// <remarks>
	/// Invoked after a successful open or change synchronization that requested a refresh, and after each tracked
	/// document is reopened when the provider restarts. The default implementation does nothing; request-driven
	/// synchronization deliberately does not invoke it.
	/// </remarks>
	protected virtual Task OnDocumentSynchronizedAsync(DocumentSnapshot document, CancellationToken cancellationToken)
		=> Task.CompletedTask;

	/// <summary>
	/// Marks a tracked document as no longer mirrored to the server.
	/// </summary>
	/// <param name="filePath">The normalized local file path of the document.</param>
	/// <remarks>
	/// Invoked while the document's per-document scheduler slot may still be held, directly after a transport
	/// failure invalidated its server copy, and when a rename needs the destination to be reopened lazily.
	/// Implementations must mark the record so the next synchronization treats it as not open on the server,
	/// typically by forwarding to the tracked-document store's invalidation helper; the restart replay skips
	/// records that still claim an open server copy. The implementation must only touch tracked state (no scheduler
	/// work) and must not throw for unknown documents.
	/// </remarks>
	protected abstract void InvalidateTrackedDocumentSynchronization(string filePath);

	/// <summary>
	/// Clears language-specific per-document work for a document whose tracked provider-side state was invalidated.
	/// </summary>
	/// <param name="filePath">The normalized local file path of the document.</param>
	/// <remarks>
	/// <para>
	/// Invoked when the document's provider-side state no longer supports the language-specific work in flight for
	/// it:
	/// </para>
	/// <list type="bullet">
	/// <item><description>after the document is closed by its last open reference, now that no consumer path will display its results;</description></item>
	/// <item><description>for both paths when a tracked document is renamed and rekeyed;</description></item>
	/// <item><description>for each idle document that is trimmed and closed on the server after its last request reference was released;</description></item>
	/// <item><description>while the document's per-document scheduler slot is still held, directly after its server synchronization was dropped following a transport failure.</description></item>
	/// </list>
	/// <para>
	/// Implementations should cancel language-specific in-flight work for the document, such as pending cache
	/// requests. The default implementation does nothing.
	/// </para>
	/// </remarks>
	protected virtual void OnTrackedDocumentInvalidated(string filePath)
	{ }

	/// <summary>
	/// Refreshes language-specific state after a tracked document was renamed successfully.
	/// </summary>
	/// <param name="filePath">The normalized local file path of the renamed document.</param>
	/// <remarks>
	/// Invoked after the base raised <see cref="DiagnosticsUpdated"/> for the renamed document; a throwing
	/// implementation is contained. The default implementation does nothing.
	/// </remarks>
	protected virtual void OnDocumentRenamed(string filePath)
	{ }

	/// <summary>
	/// Detaches language-specific state when the provider is disposed.
	/// </summary>
	/// <remarks>
	/// Invoked once during <see cref="Dispose"/>, after callback admission closed and before the owned
	/// language-server client is unsubscribed and disposed. Implementations should detach their own client event
	/// subscriptions and cancel language-specific work here. The default implementation does nothing.
	/// </remarks>
	protected virtual void OnDisposing()
	{ }
}

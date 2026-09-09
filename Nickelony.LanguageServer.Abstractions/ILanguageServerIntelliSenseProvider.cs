using Nickelony.IDEKit.IntelliSense.Completion;
using Nickelony.IDEKit.IntelliSense.Diagnostics;
using Nickelony.IDEKit.IntelliSense.DocumentSymbols;
using Nickelony.IDEKit.IntelliSense.Hover;
using Nickelony.IDEKit.IntelliSense.Navigation;
using Nickelony.IDEKit.IntelliSense.Signatures;

namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Defines the language-neutral provider contract used by text editors to obtain IntelliSense features.
/// </summary>
/// <remarks>
/// <para>
/// This interface contains document lifecycle operations and IntelliSense features that are not specific to any
/// single language. Language-specific concerns such as semantic tokens can be layered on top through narrower
/// interfaces. Workspace file watching is optional infrastructure: <see cref="WorkspaceWatcherFailed"/> simply
/// never fires for providers that do not watch an external workspace.
/// </para>
/// <para>
/// Implementations may raise callbacks from background threads. Consumers that access UI controls must marshal
/// those callbacks to the UI thread. Disposal stops new callbacks from being admitted. A handler that is already
/// running may finish, but disposal may stop delivery before all handlers in an admitted invocation have run.
/// Event handlers for one invocation run serially on the raising thread; a failing handler is isolated from later
/// handlers. Implementations must make <see cref="IDisposable.Dispose"/> and <see cref="IAsyncDisposable.DisposeAsync"/>
/// idempotent and share one teardown between them: the asynchronous form awaits provider-owned teardown instead of
/// blocking the calling thread. Disposal closes callback admission before releasing provider-owned resources.
/// </para>
/// <para>
/// <see cref="StartupFailed"/> reports startup failures classified by the provider; caller cancellation and provider
/// disposal are not startup failures. <see cref="WorkspaceWatcherFailed"/> reports watcher failures that leave
/// external workspace forwarding unavailable; failures that recover automatically may be silent.
/// </para>
/// <para>
/// For asynchronous request members, cancellation from the caller's <see cref="CancellationToken"/> propagates as
/// <see cref="OperationCanceledException"/> and never becomes an ordinary empty or <see langword="null"/> result.
/// Provider disposal and provider-enforced timeouts use each member's documented fallback value. Unsupported
/// capabilities likewise use their documented normal fallback and do not report caller cancellation. Completion,
/// hover, definition, and signature help are treated as best-effort and always attempted; document symbols and
/// code actions are best-effort and skipped with an empty result when the server did not advertise support.
/// Only rename, references, and formatting expose <c>Supports*</c> capability flags, so consumers should check
/// those flags before issuing
/// a request that must not run on an unsupported session.
/// </para>
/// </remarks>
public interface ILanguageServerIntelliSenseProvider : IDisposable, IAsyncDisposable, ITextEditProvider, ITextReferencesProvider
{
	/// <summary>
	/// Gets a value indicating whether the provider has a ready language-server session and negotiated capabilities.
	/// </summary>
	/// <remarks>
	/// This is a convenience projection of <see cref="State"/>: it is <see langword="true"/> exactly while the
	/// provider has a ready language-server session and negotiated capabilities.
	/// This value is <see langword="false"/> before lazy startup, while the provider is starting or restarting,
	/// when no usable language-server session is available, and after disposal. A request may transition the provider
	/// from an unavailable state to a ready state when startup succeeds.
	/// </remarks>
	bool IsAvailable { get; }

	/// <summary>
	/// Gets the current provider lifecycle state.
	/// </summary>
	LanguageServerProviderState State { get; }

	/// <summary>
	/// Occurs when the provider's cached diagnostics for a document are updated.
	/// </summary>
	/// <remarks>
	/// The provider is the sender. The diagnostics list is an owned immutable snapshot that remains valid
	/// after the callback returns.
	/// </remarks>
	event EventHandler<DiagnosticsUpdatedEventArgs>? DiagnosticsUpdated;

	/// <summary>
	/// Occurs when lazy startup, restart, or transport loss may have changed the negotiated capabilities.
	/// </summary>
	/// <remarks>
	/// Consumers should reread <see cref="IsAvailable"/> and capability properties after this event rather than caching
	/// capability values.
	/// </remarks>
	event EventHandler? CapabilitiesChanged;

	/// <summary>
	/// Occurs when the provider reports a language-server startup failure.
	/// </summary>
	/// <remarks>
	/// The provider is the sender. The event is raised at most once for each transient failure period and once for
	/// a terminal failure period. A successful start clears the failure-notification suppression state.
	/// </remarks>
	event EventHandler<StartupFailedEventArgs>? StartupFailed;

	/// <summary>
	/// Occurs when the workspace file watcher cannot be started or recovered and external changes may no longer be forwarded.
	/// </summary>
	/// <remarks>
	/// When a running watcher fails, automatic recovery is attempted first. Initial watcher startup reports an unresolved
	/// startup failure directly because there is no existing watcher to recover. A successful recovery does not raise this
	/// event; a missing workspace root is treated as temporarily unavailable; an unresolved startup or recovery failure
	/// raises it once until a later successful watcher recovery resets the notification state. Workspace watching is
	/// optional, so providers without an external workspace never raise this event. The provider is the sender.
	/// </remarks>
	event EventHandler<WorkspaceWatcherFailedEventArgs>? WorkspaceWatcherFailed;

	/// <summary>
	/// Gets the latest diagnostics known for a document.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <returns>An owned immutable snapshot of the cached diagnostics, or an empty list when no diagnostics are cached.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	IReadOnlyList<TextDiagnostic> GetDiagnostics(string filePath);

	/// <summary>
	/// Opens a document in the provider, starts tracking its contents, and synchronizes it with the underlying language service when available.
	/// </summary>
	/// <remarks>
	/// Repeated opens for the same path require matching calls to <see cref="CloseDocument"/> before the document
	/// is fully cleaned up. The provider normalizes the path and serializes operations for that document.
	/// </remarks>
	/// <param name="filePath">The local file path of the document.</param>
	/// <param name="content">The initial document content.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="content"/> is <see langword="null"/>.
	/// </exception>
	void OpenDocument(string filePath, string content);

	/// <summary>
	/// Synchronizes updated content for a document so the underlying language service can stay synchronized.
	/// </summary>
	/// <remarks>
	/// This operation does not count as an editor open, so a document tracked only by an update can be cleaned up
	/// automatically. When the language service is available, an update may cause the provider to track a document
	/// even when no editor has it open. Updates after a close or rename are serialized against the affected path and
	/// can reopen or update the resulting tracked document.
	/// </remarks>
	/// <param name="filePath">The local file path of the document.</param>
	/// <param name="content">The updated document content.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="content"/> is <see langword="null"/>.
	/// </exception>
	void UpdateDocument(string filePath, string content);

	/// <summary>
	/// Releases an editor-open reference for a tracked document and cleans up provider-side state when no references remain.
	/// </summary>
	/// <remarks>
	/// Each call releases one editor-open reference when one exists. A repeated call after the state has already been
	/// removed is a no-op; a document tracked only by an update may also be cleaned up by close. The document remains
	/// tracked while an IntelliSense request is using it; otherwise the final close releases the provider-side state
	/// and notifies the language service when it is safe to do so.
	/// </remarks>
	/// <param name="filePath">The local file path of the document.</param>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	void CloseDocument(string filePath);

	/// <summary>
	/// Rekeys a tracked document to a new path while preserving any provider-side state that still applies.
	/// </summary>
	/// <remarks>
	/// Unknown source paths, equivalent paths, and occupied destination paths are no-ops; they do not create or move
	/// destination state. A successful rename preserves references and uses the supplied content. Results for
	/// unchanged content remain valid across the rename; content changes invalidate them.
	/// </remarks>
	/// <param name="oldFilePath">The previous local file path.</param>
	/// <param name="newFilePath">The new local file path.</param>
	/// <param name="content">The current document content.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="oldFilePath"/>, <paramref name="newFilePath"/>, or <paramref name="content"/> is
	/// <see langword="null"/>.
	/// </exception>
	void RenameDocument(string oldFilePath, string newFilePath, string content);

	/// <summary>
	/// Requests completion items for a position within a document.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <param name="content">The current document content.</param>
	/// <param name="line">The zero-based line index.</param>
	/// <param name="column">The zero-based column index.</param>
	/// <param name="triggerCharacter">The optional character that triggered completion.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The available completion items, or an empty list when completion is unavailable or no items are available.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="content"/> is <see langword="null"/>.
	/// </exception>
	Task<IReadOnlyList<TextCompletionItem>> GetCompletionItemsAsync(string filePath, string content,
		int line, int column, char? triggerCharacter = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Requests hover information for a position within a document.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <param name="content">The current document content.</param>
	/// <param name="line">The zero-based line index.</param>
	/// <param name="column">The zero-based column index.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The hover information for the requested position, or <see langword="null"/> when unavailable.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="content"/> is <see langword="null"/>.
	/// </exception>
	Task<TextHoverInfo?> GetHoverAsync(string filePath, string content,
		int line, int column, CancellationToken cancellationToken = default);

	/// <summary>
	/// Requests the definition location for a symbol at a position within a document.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <param name="content">The current document content.</param>
	/// <param name="line">The zero-based line index.</param>
	/// <param name="column">The zero-based column index.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The resolved definition location, or <see langword="null"/> when no definition is available.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="content"/> is <see langword="null"/>.
	/// </exception>
	Task<TextDefinitionLocation?> GetDefinitionAsync(string filePath, string content,
		int line, int column, CancellationToken cancellationToken = default);

	/// <summary>
	/// Requests signature help for a function call at a position within a document.
	/// </summary>
	/// <remarks>
	/// The editor-side trigger context describes how the request was triggered and carries the
	/// currently shown payload; providers forward it to the language service when the transport
	/// supports it, so a server can keep the selected overload stable across retriggers.
	/// </remarks>
	/// <param name="filePath">The local file path of the document.</param>
	/// <param name="content">The current document content.</param>
	/// <param name="line">The zero-based line index.</param>
	/// <param name="column">The zero-based column index.</param>
	/// <param name="context">
	/// The trigger context, or <see langword="null"/> to issue a position-only request without one.
	/// </param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The signature help information, or <see langword="null"/> when unavailable.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="content"/> is <see langword="null"/>.
	/// </exception>
	Task<TextSignatureHelp?> GetSignatureHelpAsync(string filePath, string content,
		int line, int column, TextSignatureHelpContext? context = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Requests the document symbols (outline entries) for a document.
	/// </summary>
	/// <remarks>
	/// Document symbols are best-effort: the request is skipped with an empty result when the connected
	/// server did not advertise document-symbol support. The returned list is an owned snapshot with the
	/// provider's entry order preserved.
	/// </remarks>
	/// <param name="filePath">The local file path of the document.</param>
	/// <param name="content">The current document content.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The document symbols for the document, or an empty list when unavailable.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="content"/> is <see langword="null"/>.
	/// </exception>
	Task<IReadOnlyList<TextDocumentSymbol>> GetDocumentSymbolsAsync(string filePath, string content,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Requests the code actions (quick fixes and refactorings) available for a document range.
	/// </summary>
	/// <remarks>
	/// Code actions are best-effort: the request is skipped with an empty result when the connected
	/// server did not advertise code-action support. The returned list is an owned snapshot with the
	/// provider's entry order preserved. Actions the server expresses as a command without an edit
	/// cannot be represented by the shared edit model and are omitted by providers.
	/// </remarks>
	/// <param name="request">The document and the range to get code actions for.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The available code actions, or an empty list when unavailable.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	Task<IReadOnlyList<TextCodeAction>> GetCodeActionsAsync(TextCodeActionRequest request,
		CancellationToken cancellationToken = default);
}

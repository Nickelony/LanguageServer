using Nickelony.LanguageServer.Abstractions.Completion;
using Nickelony.LanguageServer.Abstractions.Diagnostics;
using Nickelony.LanguageServer.Abstractions.Editing;
using Nickelony.LanguageServer.Abstractions.Hover;
using Nickelony.LanguageServer.Abstractions.Infrastructure.Provider;
using Nickelony.LanguageServer.Abstractions.Navigation;
using Nickelony.LanguageServer.Abstractions.Signatures;

namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Defines the generic language-service contract used by text editors to provide IntelliSense features.
/// </summary>
/// <remarks>
/// This interface contains the parts of the IntelliSense provider contract that are not specific to any
/// single language. Language-specific concerns such as semantic tokens are layered on top through
/// narrower interfaces like <c>ILuaIntelliSenseProvider</c>.
///
/// Implementations may raise callbacks from background threads. Consumers that access UI controls must marshal
/// those callbacks to the UI thread. Once disposal begins, no further provider callbacks are raised.
/// Implementations must make <see cref="IDisposable.Dispose"/> idempotent. A callback that was already admitted before
/// disposal began may finish, but disposal closes callback admission before releasing provider-owned resources.
/// <see cref="StartupFailed"/> reports startup attempts that returned an unusable server session, including a
/// terminal missing-executable configuration; caller cancellation and provider disposal are not startup failures.
/// <see cref="WorkspaceWatcherFailed"/> reports only watcher startup or recovery failures that leave external
/// workspace forwarding unavailable. Transient watcher failures that recover automatically are intentionally silent.
///
/// For asynchronous request members, cancellation from the caller's <see cref="CancellationToken"/> propagates as
/// <see cref="OperationCanceledException"/> and never becomes an ordinary empty or <see langword="null"/> result.
/// Provider disposal, provider-enforced timeouts, and internal transport cancellation or failure use each member's
/// documented fallback result instead. Unsupported capabilities likewise use their documented normal fallback and do
/// not report caller cancellation.
/// </remarks>
public interface ILanguageServerIntelliSenseProvider : IDisposable, ITextEditProvider, ITextReferencesProvider
{
	/// <summary>
	/// Gets a value indicating whether the provider has a ready language-server session and negotiated capabilities.
	/// </summary>
	/// <remarks>
	/// This value is <see langword="false"/> before lazy startup, while the provider is starting or restarting,
	/// after a transient transport failure, after terminal startup failure, and after disposal. A request may
	/// transition the provider from an unavailable state to a ready state.
	/// </remarks>
	bool IsAvailable { get; }

	/// <summary>
	/// Gets the current provider lifecycle state.
	/// </summary>
	LanguageServerProviderState State { get; }

	/// <summary>
	/// Occurs when diagnostics for a document have changed.
	/// </summary>
	/// <remarks>
	/// This callback may be raised from a background thread. UI consumers must marshal to the UI thread before touching
	/// controls. Handlers for one event invocation run serially on the raising thread; a failing handler is isolated from
	/// later handlers. The diagnostics list is an owned immutable snapshot that remains valid after the callback returns.
	/// Once disposal begins, this event will not be raised again.
	/// </remarks>
	event Action<string, IReadOnlyList<TextEditorDiagnostic>>? DiagnosticsUpdated;

	/// <summary>
	/// Occurs when lazy startup, restart, or transport loss may have changed the negotiated capabilities.
	/// </summary>
	/// <remarks>
	/// This callback may be raised from a background thread. UI consumers must marshal to the UI thread before
	/// touching controls. Handlers for one event invocation run serially on the raising thread and a failing handler is
	/// isolated from later handlers. Consumers should reread <see cref="IsAvailable"/> and capability properties after
	/// this event rather than caching capability values. Once disposal begins, this event will not be raised again.
	/// </remarks>
	event Action? CapabilitiesChanged;

	/// <summary>
	/// Occurs when the underlying language server fails to start.
	/// </summary>
	/// <remarks>
	/// This callback may be raised from a background thread. UI consumers must marshal to the UI thread before touching
	/// controls. Handlers for one event invocation run serially on the raising thread and a failing handler is isolated
	/// from later handlers. Once disposal begins, this event will not be raised again.
	/// The event is raised at most once for a transient startup-failure period and once for a terminal failure period;
	/// a successful restart resets the transient notification state.
	/// </remarks>
	event Action<LanguageServerStartupFailure>? StartupFailed;

	/// <summary>
	/// Occurs when the workspace file watcher fails.
	/// </summary>
	/// <remarks>
	/// This callback may be raised from a background thread. UI consumers must marshal to the UI thread before touching
	/// controls. Handlers for one event invocation run serially on the raising thread and a failing handler is isolated
	/// from later handlers. Once disposal begins, this event will not be raised again.
	/// Automatic watcher recovery is attempted first. A successful recovery does not raise this event; a missing workspace
	/// root is treated as temporarily unavailable; an unresolved startup or recovery failure raises it once until a later
	/// successful watcher recovery resets the notification state.
	/// </remarks>
	event Action<WorkspaceWatcherFailure>? WorkspaceWatcherFailed;

	/// <summary>
	/// Gets the latest diagnostics known for a document.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <returns>An owned immutable snapshot of the diagnostics currently cached for the document.</returns>
	IReadOnlyList<TextEditorDiagnostic> GetDiagnostics(string filePath);

	/// <summary>
	/// Opens a document in the provider, starts tracking its contents, and synchronizes it with the underlying language service when available.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <param name="content">The initial document content.</param>
	/// <remarks>
	/// Each call acquires one editor-open reference; repeated calls for the same path require matching calls to
	/// <see cref="CloseDocument"/>. The provider normalizes the path and serializes operations for that document.
	/// </remarks>
	void OpenDocument(string filePath, string content);

	/// <summary>
	/// Pushes updated content for a document that is already open in the provider so the underlying language service can stay synchronized.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <param name="content">The updated document content.</param>
	/// <remarks>
	/// This operation does not acquire an editor-open or request reference. When the language service is available,
	/// an update may create a tracked, server-open document with no active references; that idle state is eligible for
	/// request-only trimming. Updates after a close or rename are serialized against the affected path and can reopen
	/// or update the resulting tracked document.
	/// </remarks>
	void UpdateDocument(string filePath, string content);

	/// <summary>
	/// Closes a tracked document and releases any provider-side state associated with it.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <remarks>
	/// Each call releases one editor-open reference when one exists. A repeated call after the state has already been
	/// removed is a no-op; an explicitly idle, server-open record created by an update may be cleaned up by close.
	/// The document remains tracked while a request reference is active; otherwise the final cleanup removes the local
	/// state, cached results, pending document work, and mirrored server document when it is safe to send the close.
	/// </remarks>
	void CloseDocument(string filePath);

	/// <summary>
	/// Rekeys a tracked document to a new path while preserving any provider-side state that still applies.
	/// </summary>
	/// <param name="oldFilePath">The previous local file path.</param>
	/// <param name="newFilePath">The new local file path.</param>
	/// <param name="content">The current document content.</param>
	/// <remarks>
	/// Unknown source paths, equivalent paths, and occupied destination paths are no-ops; they do not create or move
	/// destination state. A successful rename preserves references and the tracked content. Cached language results are
	/// preserved when the supplied content is unchanged and invalidated when the content changes.
	/// </remarks>
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
	/// <returns>The available completion items for the requested position.</returns>
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
	Task<TextDefinitionLocation?> GetDefinitionAsync(string filePath, string content,
		int line, int column, CancellationToken cancellationToken = default);

	/// <summary>
	/// Requests signature help for a function call at a position within a document.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <param name="content">The current document content.</param>
	/// <param name="line">The zero-based line index.</param>
	/// <param name="column">The zero-based column index.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The signature help information, or <see langword="null"/> when unavailable.</returns>
	Task<TextSignatureHelpInfo?> GetSignatureHelpAsync(string filePath, string content,
		int line, int column, CancellationToken cancellationToken = default);
}

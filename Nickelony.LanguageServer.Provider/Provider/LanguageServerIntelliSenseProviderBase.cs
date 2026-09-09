using Nickelony.IDEKit.IntelliSense.Completion;
using Nickelony.IDEKit.IntelliSense.Diagnostics;
using Nickelony.IDEKit.IntelliSense.DocumentSymbols;
using Nickelony.IDEKit.IntelliSense.Hover;
using Nickelony.IDEKit.IntelliSense.Navigation;
using Nickelony.IDEKit.IntelliSense.Signatures;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Provides the language-neutral provider lifecycle, document synchronization, workspace watching, and
/// request machinery that a language-server provider builds on.
/// </summary>
/// <typeparam name="TDocumentState">The tracked document state type produced by the provider's tracked-document store.</typeparam>
/// <remarks>
/// <para>
/// Derive from this class, implement the protected hook members, and supply a
/// <see cref="TrackedDocumentStore{TDocumentState}"/> plus an optional <see cref="ILanguageServerClient"/> to the
/// constructor. The base class owns the client and disposes it with the provider; passing <see langword="null"/>
/// means the language server is unavailable and every request returns its documented fallback value after the provider
/// reports a persistent startup failure.
/// </para>
/// <para>
/// Several hooks run during base construction (see each hook's documentation); implementations must not depend on
/// derived instance state. The remaining hooks run lazily, on the thread that triggers them (including transport
/// and scheduler threads).
/// </para>
/// <para>
/// Events are raised synchronously on the thread that reports the change, which can be a transport, watcher, or
/// scheduler thread and can be a thread that holds the framework's internal startup lock. Subscribers should
/// return promptly and must not synchronously wait on provider operations from a handler.
/// </para>
/// <para>
/// Document paths and workspace roots are absolute local file paths. A path that cannot be normalized to an
/// absolute local path makes the affected document member a no-op (or returns an empty result) and is logged at
/// debug level. Because relative input is anchored to the process working directory, passing a workspace-relative
/// path silently tracks a different file. Diagnostics published for non-file URIs are dropped. Path identity
/// (casing) follows the shared <see cref="LanguageServerPaths"/> policy, which is case-insensitive on Windows and
/// macOS and can be overridden through its AppContext switches.
/// </para>
/// <para>
/// Disposal is idempotent; <see cref="Dispose"/> and <see cref="IAsyncDisposable.DisposeAsync"/> share one teardown,
/// with the asynchronous overload awaiting the client's teardown instead of blocking the calling thread. Disposal
/// stops new callbacks, cancels provider-owned work, disposes the workspace watcher and the owned language-server
/// client, and leaves the provider unusable. The <see cref="OnDisposing"/> hook runs before the client is disposed so
/// language-specific subscriptions can be detached first.
/// </para>
/// </remarks>
public abstract partial class LanguageServerIntelliSenseProviderBase<TDocumentState> : ILanguageServerIntelliSenseProvider
	where TDocumentState : TrackedDocumentState
{
	private readonly ILogger _logger;

	private readonly string _workspaceRootsDisplayText;
	private readonly ILanguageServerClient? _client;
	private readonly IReadOnlyList<WorkspaceWatchSpecification> _workspaceWatchSpecifications;
	private readonly WorkspaceChangeCoordinator _workspaceChanges;
	private readonly LanguageServerRequestDispatcher _requestDispatcher;
	private readonly LanguageServerStartupState _startupState;
	private readonly LanguageServerProviderOptions _options;

	private readonly DocumentOperationScheduler _documentScheduler = new();
	private readonly TrackedDocumentStore<TDocumentState> _documents;

	// Snapshots whose post-restart reopen did not run to completion. Written under the startup lock; document
	// slots also read it lock-free to skip local content commits while a replay owns the tracked records, so the
	// field is volatile.
	private volatile IReadOnlyList<DocumentSnapshot>? _pendingReopenDocuments;

	private readonly object _callbackAdmissionSyncRoot = new();

	// Deliberately not disposed: SemaphoreSlim allocates a kernel handle only when AvailableWaitHandle is
	// requested, and skipping disposal avoids racing in-flight startup leases that may still release it.
	private readonly SemaphoreSlim _startLock = new(1, 1);

	private readonly CancellationTokenSource _disposeCts = new();
	private readonly CancellationToken _disposeToken;

	private EventHandler<DiagnosticsUpdatedEventArgs>? _diagnosticsUpdated;
	private EventHandler? _capabilitiesChanged;
	private EventHandler<StartupFailedEventArgs>? _startupFailed;
	private EventHandler<WorkspaceWatcherFailedEventArgs>? _workspaceWatcherFailed;

	private int _disposeStarted;
	private volatile bool _isDisposed;
	private bool _callbackAdmissionClosed;

	// Close requests that arrived while a temporary request reference kept the document tracked. The close is
	// retried when the request reference is released so the server copy does not stay open indefinitely.
	private readonly ConcurrentDictionary<string, byte> _pendingDocumentCloses = new(LanguageServerPaths.LocalPathComparer);

	/// <summary>
	/// Initializes a new instance of the <see cref="LanguageServerIntelliSenseProviderBase{TDocumentState}"/> class.
	/// </summary>
	/// <param name="workspaceRootDirectoryPaths">
	/// The root directories of the current workspace. The first entry is the primary root; every entry is watched
	/// for external changes. Entries may be nested; duplicates are rejected by normalized-path identity
	/// (case-insensitive on Windows and macOS, ordinal elsewhere). Pass absolute local directory
	/// paths; relative entries are anchored to the process working directory.
	/// </param>
	/// <param name="client">The language-server client owned by this provider, or <see langword="null"/> when the language server is unavailable.</param>
	/// <param name="options">The provider tunables, or <see langword="null"/> for <see cref="LanguageServerProviderOptions.Default"/>.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	/// <exception cref="ArgumentException">
	/// <paramref name="workspaceRootDirectoryPaths"/> is empty or contains an empty, whitespace-only, or duplicate entry.
	/// </exception>
	/// <exception cref="ArgumentNullException"><paramref name="workspaceRootDirectoryPaths"/> is <see langword="null"/>.</exception>
	/// <exception cref="PathTooLongException">A workspace root directory path exceeds the platform-specific maximum length.</exception>
	/// <exception cref="NotSupportedException">A workspace root directory path contains a colon that is not part of a volume identifier.</exception>
	/// <exception cref="ArgumentOutOfRangeException">One of the <paramref name="options"/> values is outside its supported range.</exception>
	/// <remarks>
	/// Construction calls the <see cref="CreateWatchSpecifications"/> and <see cref="CreateTrackedDocumentStore"/>
	/// hooks and reads <see cref="ProviderDisplayName"/>; implementations of those members must not depend on
	/// derived instance state. Ownership of <paramref name="client"/> transfers to the provider; when construction
	/// fails after the transfer, the constructor disposes the client as it unwinds, so a failed construction never
	/// leaks it.
	/// </remarks>
	protected LanguageServerIntelliSenseProviderBase(
		IReadOnlyList<string> workspaceRootDirectoryPaths,
		ILanguageServerClient? client,
		LanguageServerProviderOptions? options = null,
		ILogger? logger = null)
	{
		_logger = logger ?? NullLogger.Instance;
		_options = options ?? LanguageServerProviderOptions.Default;

		_options.Validate();

		string[] normalizedWorkspaceRootDirectoryPaths = LanguageServerPaths.NormalizeWorkspaceRoots(workspaceRootDirectoryPaths);

		// The provider layer requires at least one root, unlike the folderless client session the shared
		// path helper also models; the empty list is rejected here so the failure comes from this
		// constructor's own contract, before any client is taken.
		if (normalizedWorkspaceRootDirectoryPaths.Length == 0)
			throw new ArgumentException("At least one workspace root directory path is required.", nameof(workspaceRootDirectoryPaths));

		_workspaceRootsDisplayText = string.Join(", ", normalizedWorkspaceRootDirectoryPaths);
		_client = client;

		try
		{
			_workspaceWatchSpecifications = CreateWatchSpecifications();
			_documents = CreateTrackedDocumentStore();

			// Cache the token, and deliberately never dispose the source: request paths can still link
			// their timeout tokens against a canceled source, but linking against the token of a disposed
			// source throws ObjectDisposedException, which would escape to the caller instead of surfacing
			// the documented disposal fallback.
			_disposeToken = _disposeCts.Token;

			// The dispatcher validates the same timeout values again as part of its own public contract.
			_requestDispatcher = new LanguageServerRequestDispatcher(
				_workspaceRootsDisplayText,
				_client,
				_options.RequestTimeout,
				_options.RequestTimeoutRestartThreshold,
				isDisposedAccessor: () => _isDisposed,
				ensureStartedAsync: EnsureStartedAsync,
				logger: _logger,
				disposeToken: _disposeToken);
			_startupState = new LanguageServerStartupState(
				_client,
				initialState: LanguageServerProviderState.Unavailable,
				readyState: LanguageServerProviderState.Ready,
				disposedState: LanguageServerProviderState.Disposed,
				isDisposedAccessor: () => _isDisposed);
			_workspaceChanges = new WorkspaceChangeCoordinator(
				normalizedWorkspaceRootDirectoryPaths,
				_workspaceRootsDisplayText,
				ProviderDisplayName,
				_workspaceWatchSpecifications,
				CreateWorkspaceFileWatcher,
				IsConfigurationPath,
				CreateSettingsPayload,
				new WorkspaceChangeCallbacks(
					ClientAccessor: () => _client,
					IsDisposedAccessor: () => _isDisposed,
					EnsureStartedAsync: EnsureStartedAsync,
					TryMarkTransportUnhealthy: TryMarkWorkspaceTransportUnhealthy,
					RaiseWorkspaceWatcherFailed: RaiseWorkspaceWatcherFailed),
				logger: _logger);

			if (_client is not null)
			{
				_client.DiagnosticsPublished += HandleDiagnosticsPublished;
				_client.TransportUnavailable += HandleTransportUnavailable;
			}
		}
		catch
		{
			// Ownership of the client passes to the provider once it takes the reference; dispose it so a
			// construction failure after that point cannot leak it (disposal is idempotent).
			client?.Dispose();
			throw;
		}
	}

	/// <inheritdoc/>
	public bool IsAvailable
	{
		get
		{
			// The conjunction deliberately repeats conditions that look implied by State == Ready:
			// TryMarkWorkspaceTransportUnhealthy invalidates the startup-succeeded flag before the state
			// transition runs, so IsAvailable must not report availability during that transient window.
			return State == LanguageServerProviderState.Ready
				&& !_isDisposed
				&& _client is not null
				&& _client.IsReady
				&& _startupState.GetStartupSucceeded();
		}
	}

	/// <inheritdoc/>
	public LanguageServerProviderState State
		=> _startupState.State;

	/// <inheritdoc/>
	public bool SupportsReferences => IsAvailable && _client!.SupportsReferences;

	/// <inheritdoc/>
	public bool SupportsRename => IsAvailable && _client!.SupportsRename;

	/// <inheritdoc/>
	public bool SupportsFormatting => IsAvailable && _client!.SupportsFormatting;

	/// <inheritdoc/>
	public event EventHandler<DiagnosticsUpdatedEventArgs>? DiagnosticsUpdated
	{
		add => AddAdmittedCallback(ref _diagnosticsUpdated, value);
		remove => RemoveCallback(ref _diagnosticsUpdated, value);
	}

	/// <inheritdoc/>
	public event EventHandler? CapabilitiesChanged
	{
		add => AddAdmittedCallback(ref _capabilitiesChanged, value);
		remove => RemoveCallback(ref _capabilitiesChanged, value);
	}

	/// <inheritdoc/>
	public event EventHandler<StartupFailedEventArgs>? StartupFailed
	{
		add => AddAdmittedCallback(ref _startupFailed, value);
		remove => RemoveCallback(ref _startupFailed, value);
	}

	/// <inheritdoc/>
	public event EventHandler<WorkspaceWatcherFailedEventArgs>? WorkspaceWatcherFailed
	{
		add => AddAdmittedCallback(ref _workspaceWatcherFailed, value);
		remove => RemoveCallback(ref _workspaceWatcherFailed, value);
	}

	/// <summary>
	/// Gets the owned language-server client, or <see langword="null"/> when the provider was constructed without one.
	/// </summary>
	protected ILanguageServerClient? Client => _client;

	/// <summary>
	/// Gets the request dispatcher used for language-server requests.
	/// </summary>
	/// <remarks>
	/// Most requests should route through <see cref="SendDocumentRequestAsync{TResponse, TResult}"/> so the
	/// document is synchronized, the capability gate runs, and the temporary request reference is released. Use
	/// the dispatcher directly only for requests that do not operate on a tracked document (for example
	/// <c>completionItem/resolve</c> or semantic-token requests), and rely on its timeout and restart policy
	/// either way.
	/// </remarks>
	protected LanguageServerRequestDispatcher RequestDispatcher => _requestDispatcher;

	/// <summary>
	/// Gets the logger instance supplied to the constructor, or a no-op logger when none was supplied.
	/// </summary>
	protected ILogger Logger => _logger;

	/// <summary>
	/// Gets a value indicating whether the provider has started disposing.
	/// </summary>
	protected bool IsDisposed => _isDisposed;

	/// <inheritdoc/>
	public abstract Task<IReadOnlyList<TextCompletionItem>> GetCompletionItemsAsync(string filePath, string content,
		int line, int column, char? triggerCharacter = null, CancellationToken cancellationToken = default);

	/// <inheritdoc/>
	public abstract Task<TextHoverInfo?> GetHoverAsync(string filePath, string content,
		int line, int column, CancellationToken cancellationToken = default);

	/// <inheritdoc/>
	public abstract Task<TextDefinitionLocation?> GetDefinitionAsync(string filePath, string content,
		int line, int column, CancellationToken cancellationToken = default);

	/// <inheritdoc/>
	public abstract Task<TextSignatureHelp?> GetSignatureHelpAsync(string filePath, string content,
		int line, int column, TextSignatureHelpContext? context = null, CancellationToken cancellationToken = default);

	/// <inheritdoc/>
	public abstract Task<IReadOnlyList<TextDocumentSymbol>> GetDocumentSymbolsAsync(string filePath, string content,
		CancellationToken cancellationToken = default);

	/// <inheritdoc/>
	public abstract Task<IReadOnlyList<TextCodeAction>> GetCodeActionsAsync(TextCodeActionRequest request,
		CancellationToken cancellationToken = default);

	/// <inheritdoc/>
	public abstract Task<IReadOnlyList<TextReferenceLocation>> GetReferencesAsync(TextReferenceRequest request,
		CancellationToken cancellationToken = default);

	/// <inheritdoc/>
	public abstract Task<TextWorkspaceEdit?> RenameSymbolAsync(TextRenameRequest request,
		CancellationToken cancellationToken = default);

	/// <inheritdoc/>
	public abstract Task<TextWorkspaceEdit?> FormatDocumentAsync(TextFormatRequest request,
		CancellationToken cancellationToken = default);

	/// <inheritdoc/>
	public IReadOnlyList<TextDiagnostic> GetDiagnostics(string filePath)
	{
		ArgumentNullException.ThrowIfNull(filePath);

		if (_isDisposed)
			return [];

		if (!TryNormalizeDocumentEntryPath(filePath, "Diagnostics read", out string? normalizedFilePath))
			return [];

		return InvokeContainedHook(() => GetTrackedDiagnostics(normalizedFilePath), [], "tracked diagnostics");
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The document is tracked until its last open reference is closed. The current content is synchronized through
	/// the language-server document lifecycle. While the transport is starting or a restart replay owns the tracked
	/// records, the open is skipped the same way an update is (see <see cref="UpdateDocument"/>).
	/// </remarks>
	public void OpenDocument(string filePath, string content)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(content);

		if (_isDisposed)
			return;

		if (!TryNormalizeDocumentEntryPath(filePath, "Document open", out string? normalizedFilePath))
			return;

		CancelQueuedDocumentUpdate(normalizedFilePath);

		ObserveBackgroundTask(OpenDocumentAsync(normalizedFilePath, content), $"Document open '{normalizedFilePath}'");
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The change is coalesced with other pending updates for the document and synchronized as the next document
	/// change. An update for a document that is not tracked yet creates an idle tracked record (the store mirrors an
	/// open) that is closed again once it is trimmed or explicitly closed with <see cref="CloseDocument"/>.
	/// <para>
	/// While the transport is starting or a restart replay owns the tracked records, an update is skipped: the
	/// tracked content is not committed and nothing is sent, so the change is re-established by the next
	/// operation that supplies document content (every request does).
	/// </para>
	/// </remarks>
	public void UpdateDocument(string filePath, string content)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(content);

		if (_isDisposed)
			return;

		if (!TryNormalizeDocumentEntryPath(filePath, "Document update", out string? normalizedFilePath))
			return;

		ObserveBackgroundTask(UpdateDocumentAsync(normalizedFilePath, content), $"Document change '{normalizedFilePath}'");
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Each call releases one open reference. The server copy is closed when the last reference is released,
	/// including a close that is deferred because a temporary request reference still uses the document; the
	/// deferred close is completed when that reference is released.
	/// </remarks>
	public void CloseDocument(string filePath)
	{
		ArgumentNullException.ThrowIfNull(filePath);

		if (_isDisposed || _client is null)
			return;

		if (!TryNormalizeDocumentEntryPath(filePath, "Document close", out string? normalizedFilePath))
			return;

		CancelQueuedDocumentUpdate(normalizedFilePath);

		ObserveBackgroundTask(CloseDocumentAsync(normalizedFilePath, CancellationToken.None), $"Document close '{normalizedFilePath}'");
	}

	/// <inheritdoc/>
	/// <remarks>
	/// A case-only rename refers to the same document under this provider's local-path identity (for example on
	/// Windows and macOS) and is therefore a deliberate no-op. Because path identity is case-insensitive on those
	/// platforms, a directory rename that only changes casing cannot be expressed through this member.
	/// </remarks>
	public void RenameDocument(string oldFilePath, string newFilePath, string content)
	{
		ArgumentNullException.ThrowIfNull(oldFilePath);
		ArgumentNullException.ThrowIfNull(newFilePath);
		ArgumentNullException.ThrowIfNull(content);

		if (_isDisposed)
			return;

		if (!TryNormalizeDocumentEntryPath(oldFilePath, "Document rename", out string? normalizedOldFilePath))
			return;

		if (!TryNormalizeDocumentEntryPath(newFilePath, "Document rename", out string? normalizedNewFilePath))
			return;

		// A case-only rename refers to the same document on case-insensitive file systems. Return before
		// canceling queued updates so a coalesced change for that document is not dropped for a
		// rename that the tracked-document store treats as a no-op.
		if (LanguageServerPaths.AreLocalPathsEqual(normalizedOldFilePath, normalizedNewFilePath))
			return;

		CancelQueuedDocumentUpdate(normalizedOldFilePath);
		CancelQueuedDocumentUpdate(normalizedNewFilePath);

		ObserveBackgroundTask(
			RenameDocumentAsync(normalizedOldFilePath, normalizedNewFilePath, content, CancellationToken.None),
			$"Document rename '{normalizedOldFilePath}' to '{normalizedNewFilePath}'");
	}

	/// <summary>
	/// Observes a provider-owned background task so faults are logged instead of surfacing as unobserved task exceptions.
	/// </summary>
	/// <param name="task">The background task to observe.</param>
	/// <param name="operation">The operation description used in log text.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="task"/> or <paramref name="operation"/> is <see langword="null"/>.
	/// </exception>
	protected void ObserveBackgroundTask(Task task, string operation)
	{
		ArgumentNullException.ThrowIfNull(task);
		ArgumentNullException.ThrowIfNull(operation);

		BackgroundTaskObserver.Observe(_logger, task, operation);
	}

	/// <summary>
	/// Adds one handler to a callback field unless callback admission has already closed.
	/// </summary>
	/// <typeparam name="TDelegate">The callback delegate type.</typeparam>
	/// <param name="callbackField">The callback field to combine the handler into.</param>
	/// <param name="value">The handler to add.</param>
	protected void AddAdmittedCallback<TDelegate>(ref TDelegate? callbackField, TDelegate? value)
		where TDelegate : Delegate
	{
		lock (_callbackAdmissionSyncRoot)
		{
			if (!_callbackAdmissionClosed)
				callbackField = (TDelegate?)Delegate.Combine(callbackField, value);
		}
	}

	/// <summary>
	/// Removes one handler from a callback field.
	/// </summary>
	/// <typeparam name="TDelegate">The callback delegate type.</typeparam>
	/// <param name="callbackField">The callback field to remove the handler from.</param>
	/// <param name="value">The handler to remove.</param>
	protected void RemoveCallback<TDelegate>(ref TDelegate? callbackField, TDelegate? value)
		where TDelegate : Delegate
	{
		lock (_callbackAdmissionSyncRoot)
			callbackField = (TDelegate?)Delegate.Remove(callbackField, value);
	}

	/// <summary>
	/// Raises the subscribers of one callback field with handler isolation and admission checks between handlers.
	/// </summary>
	/// <param name="callbackAccessor">Returns the current callback list for the event.</param>
	/// <param name="invoke">Invokes one handler.</param>
	/// <param name="subscriberDescription">The subscriber description used in failure logs.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="callbackAccessor"/>, <paramref name="invoke"/>, or <paramref name="subscriberDescription"/> is
	/// <see langword="null"/>.
	/// </exception>
	protected void RaiseSubscribers(Func<Delegate?> callbackAccessor, Action<Delegate> invoke, string subscriberDescription)
	{
		ArgumentNullException.ThrowIfNull(callbackAccessor);
		ArgumentNullException.ThrowIfNull(invoke);
		ArgumentNullException.ThrowIfNull(subscriberDescription);

		Delegate? handlers;

		lock (_callbackAdmissionSyncRoot)
		{
			if (_callbackAdmissionClosed || _isDisposed)
				return;

			handlers = callbackAccessor();
		}

		if (handlers is null)
			return;

		foreach (Delegate handler in handlers.GetInvocationList())
		{
			if (!TryAdmitCallback())
				return;

			try
			{
				invoke(handler);
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "{SubscriberDescription} threw; later subscribers will still be notified.", subscriberDescription);
			}
		}
	}

	private bool TryAdmitCallback()
	{
		lock (_callbackAdmissionSyncRoot)
			return !_callbackAdmissionClosed && !_isDisposed;
	}

	private void RaiseDiagnosticsUpdated(string filePath, IReadOnlyList<TextDiagnostic> diagnostics)
		=> RaiseSubscribers(
			() => _diagnosticsUpdated,
			handler => ((EventHandler<DiagnosticsUpdatedEventArgs>)handler)(this, new DiagnosticsUpdatedEventArgs(filePath, diagnostics)),
			"Diagnostics subscriber");

	private void RaiseCapabilitiesChanged()
		=> RaiseSubscribers(
			() => _capabilitiesChanged,
			handler => ((EventHandler)handler)(this, EventArgs.Empty),
			"Capability-change subscriber");

	private void RaiseStartupFailed(LanguageServerStartupFailure failure)
		=> RaiseSubscribers(
			() => _startupFailed,
			handler => ((EventHandler<StartupFailedEventArgs>)handler)(this, new StartupFailedEventArgs(failure)),
			"Startup-failure subscriber");

	private void RaiseWorkspaceWatcherFailed(WorkspaceWatcherFailure failure)
		=> RaiseSubscribers(
			() => _workspaceWatcherFailed,
			handler => ((EventHandler<WorkspaceWatcherFailedEventArgs>)handler)(this, new WorkspaceWatcherFailedEventArgs(failure)),
			"Workspace-watcher subscriber");

	/// <summary>
	/// Attempts a provider-state transition and raises the capabilities-changed event when the state actually
	/// changed and the transition requested a notification.
	/// </summary>
	/// <param name="state">The requested provider state.</param>
	/// <param name="notifyCapabilitiesChanged"><see langword="true"/> to request a capabilities-changed notification when the state actually changed.</param>
	private void SetProviderState(LanguageServerProviderState state, bool notifyCapabilitiesChanged = false)
	{
		if (_startupState.TrySetState(state, notifyCapabilitiesChanged))
			RaiseCapabilitiesChanged();
	}

	private void TryMarkWorkspaceTransportUnhealthy(long transportGeneration)
	{
		ILanguageServerClient? client = _client;

		if (client is null)
			return;

		try
		{
			if (client.TryMarkTransportUnhealthy(transportGeneration))
				MarkStartupTransportUnavailable();
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "Failed to mark the {DisplayName} language server transport unhealthy after a workspace-watcher send failure.",
				ProviderDisplayName);
		}
	}

	private void MarkStartupTransportUnavailable()
	{
		_startupState.InvalidateStartupSucceeded();
		SetProviderState(LanguageServerProviderState.Unavailable, notifyCapabilitiesChanged: true);
	}

	private bool TryCompleteSuccessfulStart(long transportGeneration)
	{
		bool started = _startupState.TryCompleteSuccessfulStart(transportGeneration, out LanguageServerProviderState previousState);

		if (started
			&& previousState != LanguageServerProviderState.Ready
			&& State == LanguageServerProviderState.Ready)
		{
			RaiseCapabilitiesChanged();
		}

		return started;
	}

	private void HandleTransportUnavailable(object? sender, TransportUnavailableEventArgs eventArgs)
	{
		// The client clears its current capability snapshot before raising this event. Match the
		// event against the generation that most recently reached Ready, not the reset snapshot.
		if (_startupState.OnClientTransportUnavailable(eventArgs.Generation))
			SetProviderState(LanguageServerProviderState.Unavailable, notifyCapabilitiesChanged: true);
	}

	/// <summary>
	/// Runs a void language hook and contains an unexpected hook fault so it cannot escape framework entry points.
	/// </summary>
	/// <param name="hook">The hook invocation.</param>
	/// <param name="hookDescription">The hook description used in failure logs.</param>
	private void InvokeContainedHook(Action hook, string hookDescription)
		=> HookContainment.Invoke(_logger, ProviderDisplayName, hook, hookDescription);

	/// <summary>
	/// Runs a language hook and contains an unexpected hook fault, returning the fallback value instead.
	/// </summary>
	/// <typeparam name="TResult">The hook result type.</typeparam>
	/// <param name="hook">The hook invocation.</param>
	/// <param name="fallbackValue">The value returned when the hook throws.</param>
	/// <param name="hookDescription">The hook description used in failure logs.</param>
	/// <returns>The hook result, or <paramref name="fallbackValue"/> when the hook throws.</returns>
	private TResult InvokeContainedHook<TResult>(Func<TResult> hook, TResult fallbackValue, string hookDescription)
		=> HookContainment.Invoke(_logger, ProviderDisplayName, hook, fallbackValue, hookDescription);

	/// <summary>
	/// Runs an asynchronous language hook and contains an unexpected hook fault; cancellation is treated as expected
	/// teardown and stays silent.
	/// </summary>
	/// <param name="hook">The asynchronous hook invocation.</param>
	/// <param name="hookDescription">The hook description used in failure logs.</param>
	private Task InvokeContainedHookAsync(Func<Task> hook, string hookDescription)
		=> HookContainment.InvokeAsync(_logger, ProviderDisplayName, hook, hookDescription);

	/// <summary>
	/// Logs a document entry point rejection for a path that cannot be normalized to an absolute local file path.
	/// </summary>
	/// <param name="filePath">The rejected caller-supplied path.</param>
	/// <param name="operation">The operation description used in log text.</param>
	private void LogUnusableDocumentPath(string filePath, string operation)
	{
		_logger.LogDebug("{DisplayName} {Operation} ignored the path '{FilePath}' because it cannot be normalized to an absolute local file path.",
			ProviderDisplayName, operation, filePath);
	}

	/// <summary>
	/// Tries to normalize a caller-supplied document path for one entry point, logging the rejection when the
	/// path cannot be used.
	/// </summary>
	/// <param name="filePath">The caller-supplied path.</param>
	/// <param name="operation">The operation description used in log text.</param>
	/// <param name="normalizedFilePath">Receives the normalized path when the path is usable.</param>
	/// <returns><see langword="true"/> when the path was normalized; otherwise, <see langword="false"/>.</returns>
	private bool TryNormalizeDocumentEntryPath(string filePath, string operation, [NotNullWhen(true)] out string? normalizedFilePath)
	{
		if (LanguageServerPaths.TryNormalizeLocalPath(filePath, out normalizedFilePath))
			return true;

		LogUnusableDocumentPath(filePath, operation);
		return false;
	}

	/// <summary>
	/// Sends one <c>textDocument/didClose</c> notification for a document snapshot.
	/// </summary>
	/// <param name="document">The snapshot whose server copy should be closed.</param>
	/// <param name="cancellationToken">Cancels the send.</param>
	/// <returns>A task that completes when the notification was handed to the transport.</returns>
	private Task SendDidCloseAsync(DocumentSnapshot document, CancellationToken cancellationToken)
		=> _client!.SendNotificationAsync(LspMethodNames.DidClose,
			new DidCloseTextDocumentParams(new TextDocumentIdentifier(document.Uri)),
			cancellationToken);

	/// <summary>
	/// Logs a best-effort notification failure with the package's severity policy: transport and disposal failures
	/// are expected outcomes logged at debug level, and anything else is logged as a warning.
	/// </summary>
	/// <param name="operation">The operation description used in log text.</param>
	/// <param name="filePath">The document path the failed operation targeted.</param>
	/// <param name="exception">The failure to log.</param>
	private void LogBestEffortFailure(string operation, string filePath, Exception exception)
	{
		if (exception is IOException or ObjectDisposedException)
		{
			_logger.LogDebug(exception, "{DisplayName} best-effort {Operation} failed for '{FilePath}' because the transport is unavailable or the provider is racing disposal.",
				ProviderDisplayName, operation, filePath);
		}
		else
		{
			_logger.LogWarning(exception, "{DisplayName} best-effort {Operation} failed unexpectedly for '{FilePath}'.",
				ProviderDisplayName, operation, filePath);
		}
	}

	/// <summary>
	/// Creates the workspace file watcher for one workspace root.
	/// </summary>
	/// <param name="workspaceRootDirectoryPath">The normalized workspace root directory to watch.</param>
	/// <param name="dispatchAsync">The callback that forwards coalesced file changes to the owning coordinator.</param>
	/// <param name="onWatcherFailed">The callback that reports a watcher failure to the owning coordinator.</param>
	/// <returns>The workspace file watcher for the supplied root.</returns>
	/// <remarks>
	/// The default implementation creates the framework's <see cref="WorkspaceFileWatcher"/> with the
	/// specifications returned by <see cref="CreateWatchSpecifications"/>. Override to customize watcher creation
	/// for a host environment; the returned watcher must not be started, and any exception it throws is reported
	/// through <see cref="WorkspaceWatcherFailed"/> as a watcher startup failure instead of escaping request APIs.
	/// </remarks>
	protected virtual WorkspaceFileWatcher CreateWorkspaceFileWatcher(
		string workspaceRootDirectoryPath,
		Func<FileChangeBatch, CancellationToken, Task> dispatchAsync,
		Action<WorkspaceFileWatcher, Exception?> onWatcherFailed)
	{
		return new(workspaceRootDirectoryPath, dispatchAsync, _workspaceWatchSpecifications, onWatcherFailed, logger: new WorkspaceWatcherLogger(_logger));
	}
}

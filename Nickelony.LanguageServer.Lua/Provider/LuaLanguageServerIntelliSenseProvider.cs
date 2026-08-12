using Nickelony.LanguageServer.Abstractions.Diagnostics;
using Nickelony.LanguageServer.Abstractions.Infrastructure.Provider;
using System.Collections.Concurrent;

namespace Nickelony.LanguageServer.Lua;

/// <summary>
/// Implements the Lua IntelliSense provider by synchronizing editor documents with LuaLS and caching its responses.
/// </summary>
/// <remarks>
/// The provider owns the language-server client supplied to its constructor and the workspace watcher created by its
/// workspace coordinator. Dispose the provider when the host/editor no longer needs it; the provider detaches callbacks,
/// cancels provider work, disposes the watcher, and then disposes the client.
/// </remarks>
public sealed partial class LuaLanguageServerIntelliSenseProvider : ILuaIntelliSenseProvider
{
	private readonly ILogger _logger;

	private static readonly TimeSpan s_defaultRequestTimeout = TimeSpan.FromSeconds(10);

	private static readonly Action<ILogger, string, Exception?> s_logMissingExecutable = LoggerMessage.Define<string>(
		LogLevel.Error,
		new EventId(1, nameof(LuaLanguageServerIntelliSenseProvider)),
		"Lua language server executable is unavailable for workspace '{Workspace}'; IntelliSense is disabled until the host supplies a valid executable.");

	private const int DefaultRequestTimeoutRestartThreshold = 2;
	private const int HardStartupFailureThreshold = 3;
	private const int MaxTrackedRequestOnlyDocuments = 16;

	/// <summary>
	/// Gets the workspace file patterns mirrored to the Lua language server for external-change watching.
	/// </summary>
	internal static IReadOnlyList<WorkspaceWatchSpecification> WorkspaceWatchSpecifications { get; } = Array.AsReadOnly(
	[
		new WorkspaceWatchSpecification(".API", IncludeSubdirectories: false),
		new WorkspaceWatchSpecification("*.lua", IncludeSubdirectories: true),
		new WorkspaceWatchSpecification(".luarc.*", IncludeSubdirectories: false)
	]);

	private readonly string _workspaceRootDirectoryPath;
	private readonly ILanguageServerClient? _client;
	private readonly TimeSpan _requestTimeout;
	private readonly int _requestTimeoutRestartThreshold;
	private readonly LuaWorkspaceChangeCoordinator _workspaceChanges;

	private readonly DocumentOperationScheduler _documentScheduler = new();
	private readonly LuaDocumentStore _documents = new();
	private readonly ConcurrentDictionary<string, CancellationTokenSource> _semanticTokenRequests = new(LanguageServerPathHelper.LocalPathComparer);

	private readonly object _startupStateSyncRoot = new();
	private readonly object _requestTimeoutSyncRoot = new();
	private readonly object _callbackAdmissionSyncRoot = new();
	private readonly SemaphoreSlim _startLock = new(1, 1);
	private readonly CancellationTokenSource _disposeCts = new();

	private Action<string, IReadOnlyList<TextEditorDiagnostic>>? _diagnosticsUpdated;
	private Action<string, IReadOnlyList<LuaSemanticToken>>? _semanticTokensUpdated;
	private Action? _capabilitiesChanged;
	private Action<LanguageServerStartupFailure>? _startupFailed;
	private Action<WorkspaceWatcherFailure>? _workspaceWatcherFailed;

	private bool _startupSucceeded;
	private long _readyTransportGeneration;
	private long _lastUnavailableTransportGeneration;
	private int _consecutiveStartupFailures;
	private int _consecutiveRequestTimeouts;
	private long _timedOutRequestGeneration = -1;
	private long _restartRequestedGeneration = -1;
	private bool _permanentStartupFailureReported;
	private bool _transientStartupFailureReported;

	private int _disposeStarted;
	private volatile bool _isDisposed;
	private bool _callbackAdmissionClosed;
	private int _providerState = (int)LanguageServerProviderState.Unavailable;

	/// <inheritdoc/>
	public bool IsAvailable
		=> State == LanguageServerProviderState.Ready
			&& !_isDisposed
			&& _client is not null
			&& _client.IsReady
			&& GetStartupSucceeded();

	/// <inheritdoc/>
	public LanguageServerProviderState State
		=> (LanguageServerProviderState)Volatile.Read(ref _providerState);

	/// <inheritdoc/>
	public bool SupportsReferences => IsAvailable && _client is not null && _client.SupportsReferences;

	/// <inheritdoc/>
	public bool SupportsRename => IsAvailable && _client is not null && _client.SupportsRename;

	/// <inheritdoc/>
	public bool SupportsFormatting => IsAvailable && _client is not null && _client.SupportsFormatting;

	/// <inheritdoc/>
	public event Action<string, IReadOnlyList<TextEditorDiagnostic>>? DiagnosticsUpdated
	{
		add
		{
			lock (_callbackAdmissionSyncRoot)
			{
				if (!_callbackAdmissionClosed)
					_diagnosticsUpdated += value;
			}
		}
		remove
		{
			lock (_callbackAdmissionSyncRoot)
				_diagnosticsUpdated -= value;
		}
	}

	/// <inheritdoc/>
	public event Action<string, IReadOnlyList<LuaSemanticToken>>? SemanticTokensUpdated
	{
		add
		{
			lock (_callbackAdmissionSyncRoot)
			{
				if (!_callbackAdmissionClosed)
					_semanticTokensUpdated += value;
			}
		}
		remove
		{
			lock (_callbackAdmissionSyncRoot)
				_semanticTokensUpdated -= value;
		}
	}

	/// <inheritdoc/>
	public event Action? CapabilitiesChanged
	{
		add
		{
			lock (_callbackAdmissionSyncRoot)
			{
				if (!_callbackAdmissionClosed)
					_capabilitiesChanged += value;
			}
		}
		remove
		{
			lock (_callbackAdmissionSyncRoot)
				_capabilitiesChanged -= value;
		}
	}

	/// <summary>
	/// Occurs when repeated language-server startup failures should be surfaced to the user.
	/// </summary>
	/// <remarks>
	/// Startup-failure notifications may be delivered from background work. Consumers that touch UI controls must marshal
	/// to the UI thread. Handlers for one event invocation run serially on the raising thread and a failing handler is
	/// isolated from later handlers. Once disposal begins, this event will not be raised again.
	/// </remarks>
	public event Action<LanguageServerStartupFailure>? StartupFailed
	{
		add
		{
			lock (_callbackAdmissionSyncRoot)
			{
				if (!_callbackAdmissionClosed)
					_startupFailed += value;
			}
		}
		remove
		{
			lock (_callbackAdmissionSyncRoot)
				_startupFailed -= value;
		}
	}

	/// <summary>
	/// Occurs when the external workspace watcher becomes unavailable for the rest of the session.
	/// </summary>
	/// <remarks>
	/// Workspace-watcher notifications may be delivered from background work. Consumers that touch UI controls must
	/// marshal to the UI thread. Handlers for one event invocation run serially on the raising thread and a failing handler
	/// is isolated from later handlers. Once disposal begins, this event will not be raised again.
	/// </remarks>
	public event Action<WorkspaceWatcherFailure>? WorkspaceWatcherFailed
	{
		add
		{
			lock (_callbackAdmissionSyncRoot)
			{
				if (!_callbackAdmissionClosed)
					_workspaceWatcherFailed += value;
			}
		}
		remove
		{
			lock (_callbackAdmissionSyncRoot)
				_workspaceWatcherFailed -= value;
		}
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="LuaLanguageServerIntelliSenseProvider"/> class.
	/// </summary>
	/// <param name="workspaceRootDirectoryPath">The root directory of the current Lua script workspace.</param>
	/// <param name="serverExecutablePath">The LuaLS executable path, or <see langword="null"/> when unavailable.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	public LuaLanguageServerIntelliSenseProvider(string workspaceRootDirectoryPath, string? serverExecutablePath, ILogger<LuaLanguageServerIntelliSenseProvider>? logger = null)
		: this(workspaceRootDirectoryPath,
			CreateClient(workspaceRootDirectoryPath, serverExecutablePath),
			s_defaultRequestTimeout,
			DefaultRequestTimeoutRestartThreshold,
			logger: logger)
	{ }

	/// <summary>
	/// Initializes a new instance of the <see cref="LuaLanguageServerIntelliSenseProvider"/> class.
	/// for testing and dependency injection.
	/// </summary>
	/// <param name="workspaceRootDirectoryPath">The root directory of the current Lua script workspace.</param>
	/// <param name="client">The language server client, or <see langword="null"/> when unavailable.</param>
	/// <param name="requestTimeout">The per-request timeout, or <see langword="null"/> for the default.</param>
	/// <param name="requestTimeoutRestartThreshold">The consecutive timeout count that triggers an automatic restart.</param>
	/// <param name="workspaceFileWatcherFactory">A factory for creating workspace file watchers, used for testing.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	/// <remarks>
	/// Ownership of <paramref name="client"/> transfers to the provider. The client is disposed when this provider is
	/// disposed, including when construction completed only partially and startup never succeeded.
	/// </remarks>
	internal LuaLanguageServerIntelliSenseProvider(string workspaceRootDirectoryPath, ILanguageServerClient? client,
		TimeSpan? requestTimeout = null,
		int requestTimeoutRestartThreshold = DefaultRequestTimeoutRestartThreshold,
		Func<string, Func<FileChangeBatch, CancellationToken, Task>, Action<WorkspaceFileWatcher, Exception?>, WorkspaceFileWatcher>? workspaceFileWatcherFactory = null,
		ILogger<LuaLanguageServerIntelliSenseProvider>? logger = null)
	{
		_logger = logger ?? NullLogger<LuaLanguageServerIntelliSenseProvider>.Instance;

		_workspaceRootDirectoryPath = LanguageServerPathHelper.NormalizeLocalPath(workspaceRootDirectoryPath);
		_client = client;
		_requestTimeout = requestTimeout ?? s_defaultRequestTimeout;
		_requestTimeoutRestartThreshold = Math.Max(1, requestTimeoutRestartThreshold);
		_workspaceChanges = new LuaWorkspaceChangeCoordinator(
			_workspaceRootDirectoryPath,
			WorkspaceWatchSpecifications,
			workspaceFileWatcherFactory ?? CreateWorkspaceFileWatcher,
			() => _client,
			() => _isDisposed,
			EnsureStartedAsync,
			MarkWorkspaceTransportUnavailable,
			RaiseWorkspaceWatcherFailed,
			_logger);

		if (_client is not null)
		{
			_client.DiagnosticsPublished += HandleDiagnosticsPublished;
			_client.SemanticTokensRefreshRequested += HandleSemanticTokensRefreshRequested;
			_client.TransportUnavailable += HandleTransportUnavailable;
		}
	}

	private static ILanguageServerClient? CreateClient(string workspaceRootDirectoryPath, string? serverExecutablePath)
	{
		if (string.IsNullOrWhiteSpace(serverExecutablePath))
			return null;

		string normalizedRoot = LanguageServerPathHelper.NormalizeLocalPath(workspaceRootDirectoryPath);

		return new LanguageServerClient(normalizedRoot, serverExecutablePath, new LanguageServerClientOptions(
			() => LuaLanguageServerSettingsFactory.Create(normalizedRoot))
		{
			ClientCapabilitiesProvider = _ => LuaLanguageServerClientCapabilitiesFactory.Create(),
			InitializationOptionsProvider = _ => LuaLanguageServerInitializationOptionsFactory.Create()
		});
	}

	private static WorkspaceFileWatcher CreateWorkspaceFileWatcher(
		string workspaceRootDirectoryPath,
		Func<FileChangeBatch, CancellationToken, Task> dispatchAsync,
		Action<WorkspaceFileWatcher, Exception?> watcherFailed)
	{
		return new(workspaceRootDirectoryPath, dispatchAsync, WorkspaceWatchSpecifications, watcherFailed);
	}

	/// <inheritdoc/>
	public IReadOnlyList<TextEditorDiagnostic> GetDiagnostics(string filePath)
	{
		if (_isDisposed)
			return [];

		if (!LanguageServerPathHelper.TryNormalizeLocalPath(filePath, out string normalizedFilePath))
			return [];

		return _documents.GetDiagnostics(normalizedFilePath);
	}

	/// <inheritdoc/>
	public IReadOnlyList<LuaSemanticToken> GetSemanticTokens(string filePath)
	{
		if (_isDisposed)
			return [];

		if (!LanguageServerPathHelper.TryNormalizeLocalPath(filePath, out string normalizedFilePath))
			return [];

		return _documents.GetSemanticTokens(normalizedFilePath);
	}

	/// <inheritdoc/>
	public void OpenDocument(string filePath, string content)
	{
		if (!LanguageServerPathHelper.TryNormalizeLocalPath(filePath, out string normalizedFilePath))
			return;

		CancelQueuedDocumentUpdate(normalizedFilePath);

		ObserveBackgroundTask(SynchronizeDocumentAsync(normalizedFilePath, content,
			acquireOpenReference: true,
			acquireRequestReference: false,
			refreshSemanticTokens: true,
			CancellationToken.None), $"Document open '{normalizedFilePath}'");
	}

	/// <inheritdoc/>
	public void UpdateDocument(string filePath, string content)
	{
		if (!LanguageServerPathHelper.TryNormalizeLocalPath(filePath, out string normalizedFilePath))
			return;

		ObserveBackgroundTask(QueueLatestDocumentUpdateAsync(normalizedFilePath, content), $"Document change '{normalizedFilePath}'");
	}

	/// <inheritdoc/>
	public void CloseDocument(string filePath)
	{
		if (_isDisposed || _client is null || !LanguageServerPathHelper.TryNormalizeLocalPath(filePath, out string normalizedFilePath))
			return;

		CancelQueuedDocumentUpdate(normalizedFilePath);

		ObserveBackgroundTask(CloseDocumentAsync(normalizedFilePath, CancellationToken.None), $"Document close '{normalizedFilePath}'");
	}

	private void MarkWorkspaceTransportUnavailable(long transportGeneration)
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
			_logger.LogDebug(exception, "Failed to mark the Lua language server transport unhealthy after a workspace-watcher send failure.");
		}
	}

	private void RaiseDiagnosticsUpdated(string filePath, IReadOnlyList<TextEditorDiagnostic> diagnostics)
	{
		if (!TrySnapshotCallbackHandlers(() => _diagnosticsUpdated, out Delegate? handlers))
			return;

		InvokeSubscribersSafely(
			handlers,
			handler => ((Action<string, IReadOnlyList<TextEditorDiagnostic>>)handler)(filePath, diagnostics),
			"Lua diagnostics subscriber");
	}

	private void HandleTransportUnavailable(long transportGeneration)
	{
		// The client clears its current capability snapshot before raising this event. Match the
		// event against the generation that most recently reached Ready, not the reset snapshot.
		lock (_startupStateSyncRoot)
		{
			if (_isDisposed)
				return;

			if (transportGeneration > _lastUnavailableTransportGeneration)
				_lastUnavailableTransportGeneration = transportGeneration;

			if (!_startupSucceeded
				|| _readyTransportGeneration != transportGeneration)
			{
				return;
			}

			_startupSucceeded = false;
		}

		SetProviderState(LanguageServerProviderState.Unavailable, notifyCapabilitiesChanged: true);
	}

	private void RaiseSemanticTokensUpdated(string filePath, IReadOnlyList<LuaSemanticToken> semanticTokens)
	{
		if (!TrySnapshotCallbackHandlers(() => _semanticTokensUpdated, out Delegate? handlers))
			return;

		InvokeSubscribersSafely(
			handlers,
			handler => ((Action<string, IReadOnlyList<LuaSemanticToken>>)handler)(filePath, semanticTokens),
			"Lua semantic token subscriber");
	}

	private void RaiseCapabilitiesChanged()
	{
		if (!TrySnapshotCallbackHandlers(() => _capabilitiesChanged, out Delegate? handlers))
			return;

		InvokeSubscribersSafely(
			handlers,
			handler => ((Action)handler)(),
			"Lua capability-change subscriber");
	}

	private void RaiseStartupFailed(LanguageServerStartupFailure failure)
	{
		if (!TrySnapshotCallbackHandlers(() => _startupFailed, out Delegate? handlers))
			return;

		InvokeSubscribersSafely(
			handlers,
			handler => ((Action<LanguageServerStartupFailure>)handler)(failure),
			"Lua IntelliSense startup-failure subscriber");
	}

	private void RaiseWorkspaceWatcherFailed(WorkspaceWatcherFailure failure)
	{
		if (!TrySnapshotCallbackHandlers(() => _workspaceWatcherFailed, out Delegate? handlers))
			return;

		InvokeSubscribersSafely(
			handlers,
			handler => ((Action<WorkspaceWatcherFailure>)handler)(failure),
			"Lua workspace-watcher subscriber");
	}

	private bool TrySnapshotCallbackHandlers(Func<Delegate?> callbackAccessor, out Delegate? handlers)
	{
		lock (_callbackAdmissionSyncRoot)
		{
			if (_callbackAdmissionClosed || _isDisposed)
			{
				handlers = null;
				return false;
			}

			handlers = callbackAccessor();
			return handlers is not null;
		}
	}

	private bool TryAdmitCallback()
	{
		lock (_callbackAdmissionSyncRoot)
			return !_callbackAdmissionClosed && !_isDisposed;
	}

	private void InvokeSubscribersSafely(Delegate? handlers, Action<Delegate> invoke, string subscriberDescription)
	{
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

	private void SetProviderState(LanguageServerProviderState state, bool notifyCapabilitiesChanged = false)
	{
		LanguageServerProviderState previousState;

		lock (_startupStateSyncRoot)
		{
			if (_isDisposed && state != LanguageServerProviderState.Disposed)
				return;

			previousState = (LanguageServerProviderState)_providerState;

			if (previousState == LanguageServerProviderState.Disposed && state != LanguageServerProviderState.Disposed)
				return;

			Volatile.Write(ref _providerState, (int)state);
		}

		if (notifyCapabilitiesChanged && previousState != state)
			RaiseCapabilitiesChanged();
	}

	private int GetConsecutiveStartupFailures()
	{
		lock (_startupStateSyncRoot)
			return _consecutiveStartupFailures;
	}

	private bool GetStartupSucceeded()
	{
		lock (_startupStateSyncRoot)
			return _startupSucceeded;
	}

	private bool TryMarkStartupFailureReported(bool isPermanentFailure)
	{
		lock (_startupStateSyncRoot)
		{
			if (isPermanentFailure)
			{
				if (_permanentStartupFailureReported)
					return false;

				_permanentStartupFailureReported = true;
				return true;
			}

			if (_transientStartupFailureReported)
				return false;

			_transientStartupFailureReported = true;
			return true;
		}
	}

	private void MarkStartupTransportUnavailable()
	{
		lock (_startupStateSyncRoot)
			_startupSucceeded = false;

		SetProviderState(LanguageServerProviderState.Unavailable, notifyCapabilitiesChanged: true);
	}

	private bool TryCompleteSuccessfulStart(long transportGeneration)
	{
		LanguageServerProviderState previousState;

		lock (_startupStateSyncRoot)
		{
			if (_isDisposed
				|| transportGeneration == 0
				|| _lastUnavailableTransportGeneration == transportGeneration
				|| _client is null
				|| !_client.IsReady
				|| _client.TransportGeneration != transportGeneration)
			{
				_startupSucceeded = false;
				return false;
			}

			_startupSucceeded = true;
			_readyTransportGeneration = transportGeneration;
			_consecutiveStartupFailures = 0;
			_transientStartupFailureReported = false;
			_permanentStartupFailureReported = false;

			previousState = (LanguageServerProviderState)_providerState;
			Volatile.Write(ref _providerState, (int)LanguageServerProviderState.Ready);
		}

		if (previousState != LanguageServerProviderState.Ready
			&& State == LanguageServerProviderState.Ready)
		{
			RaiseCapabilitiesChanged();
		}

		return true;
	}

	private int RegisterStartupFailure()
	{
		lock (_startupStateSyncRoot)
		{
			_startupSucceeded = false;
			return ++_consecutiveStartupFailures;
		}
	}
}

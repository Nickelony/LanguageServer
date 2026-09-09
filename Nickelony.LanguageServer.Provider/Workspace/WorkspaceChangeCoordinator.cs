using System.Diagnostics.CodeAnalysis;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Owns one workspace-watch scope per workspace root and forwards external workspace changes to the
/// language server through a shared change forwarder.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>Per-root watcher lifecycle, snapshot capture, and recovery reconciliation are delegated to
/// the watch scopes. Each delivery names its scope, and a path is normally forwarded only by the scope that
/// owns it (longest prefix wins when roots nest), so an outer root's watcher never duplicates a nested root's
/// notifications; while the owning scope's watcher is inactive, the delivering watcher covers the root
/// instead.</description></item>
/// <item><description>Change buffering is reserved for recoverable transport or startup gaps after a client
/// exists; when there is no client or the provider is disposed, workspace changes are intentionally ignored, so
/// external-change replay never covers pre-start noise.</description></item>
/// <item><description>Unexpected forwarding failures are dropped by the forwarder because a partially observed
/// batch cannot be replayed unambiguously; a later snapshot capture or watcher recovery can reconcile missed
/// file-system state.</description></item>
/// <item><description>An empty watch-specification list disables workspace watching entirely: no watch scope is
/// created, no snapshot is tracked, and no replay is produced.</description></item>
/// </list>
/// </remarks>
internal sealed class WorkspaceChangeCoordinator : IDisposable
{
	private readonly ILogger _logger;

	private readonly string _providerDisplayName;
	private readonly string _workspaceRootsDisplayText;
	private readonly Func<string, bool> _isConfigurationPath;
	private readonly Func<object> _createSettingsPayload;

	private readonly Func<ILanguageServerClient?> _clientAccessor;
	private readonly Func<bool> _isDisposedAccessor;
	private readonly Action<long> _tryMarkTransportUnhealthy;

	private readonly WorkspaceWatchScope[] _watchScopes;
	private readonly WorkspaceFileChangeForwarder _workspaceFileChangeForwarder;

	/// <summary>
	/// Initializes a new instance of the <see cref="WorkspaceChangeCoordinator"/> class.
	/// </summary>
	/// <param name="workspaceRootDirectoryPaths">The normalized workspace root directories to watch.</param>
	/// <param name="workspaceRootsDisplayText">The comma-joined normalized workspace root paths used in log text.</param>
	/// <param name="providerDisplayName">The provider name used in log text.</param>
	/// <param name="watchSpecifications">
	/// The file patterns that should be mirrored to the language server, or an empty list to disable workspace
	/// watching for all roots.
	/// </param>
	/// <param name="workspaceFileWatcherFactory">Builds the low-level workspace watcher for one root.</param>
	/// <param name="isConfigurationPath">Reports whether a normalized changed path requires a settings refresh.</param>
	/// <param name="createSettingsPayload">Creates the settings payload sent when a configuration file changes.</param>
	/// <param name="callbacks">The owner callbacks used for client access, disposal probing, startup, transport marking, and failure reporting.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	internal WorkspaceChangeCoordinator(
		IReadOnlyList<string> workspaceRootDirectoryPaths,
		string workspaceRootsDisplayText,
		string providerDisplayName,
		IReadOnlyList<WorkspaceWatchSpecification> watchSpecifications,
		WorkspaceFileWatcherFactory workspaceFileWatcherFactory,
		Func<string, bool> isConfigurationPath,
		Func<object> createSettingsPayload,
		WorkspaceChangeCallbacks callbacks,
		ILogger? logger = null)
	{
		_logger = logger ?? NullLogger.Instance;

		_providerDisplayName = providerDisplayName;
		_workspaceRootsDisplayText = workspaceRootsDisplayText;
		_isConfigurationPath = isConfigurationPath;
		_createSettingsPayload = createSettingsPayload;
		_clientAccessor = callbacks.ClientAccessor;
		_isDisposedAccessor = callbacks.IsDisposedAccessor;
		_tryMarkTransportUnhealthy = callbacks.TryMarkTransportUnhealthy;

		// An empty specification list disables workspace watching: no watch scope is created, no snapshot is
		// tracked, and no replay is produced, so a host that bridges file events itself can opt out by returning
		// an empty list from CreateWatchSpecifications.
		if (watchSpecifications.Count == 0)
		{
			_watchScopes = [];
		}
		else
		{
			_watchScopes = new WorkspaceWatchScope[workspaceRootDirectoryPaths.Count];

			for (int i = 0; i < workspaceRootDirectoryPaths.Count; i++)
			{
				_watchScopes[i] = new WorkspaceWatchScope(
					new WorkspaceWatchScopeContext(
						workspaceRootDirectoryPaths[i],
						providerDisplayName,
						watchSpecifications,
						workspaceFileWatcherFactory,
						DispatchWorkspaceFileChangesAsync,
						_logger),
					callbacks);
			}
		}

		_workspaceFileChangeForwarder = new WorkspaceFileChangeForwarder(
			// Buffering covers recoverable transport/startup gaps only (see the type remarks).
			canForwardAccessor: () => _clientAccessor() is not null && !_isDisposedAccessor(),
			isDisposedAccessor: _isDisposedAccessor,
			ensureStartedAsync: callbacks.EnsureStartedAsync,
			// The forwarder's own transport marking is intentionally disabled: SendWorkspaceFileChangesAsync
			// marks the generation it captured before the send, which fences stale notifications, so
			// transport marking is owned by that path only.
			tryMarkTransportUnhealthy: static () => { },
			logForwardingFailure: failure =>
			{
				string firstPath = string.IsNullOrWhiteSpace(failure.FirstPath) ? "<unknown>" : failure.FirstPath;

				if (failure.WasDropped)
				{
					_logger.LogWarning(failure.Exception,
						"Dropped {BatchCount} {DisplayName} workspace file change(s) for '{Workspace}' after an unexpected forwarding failure. First path: '{FirstPath}'.",
						failure.BatchCount,
						_providerDisplayName,
						_workspaceRootsDisplayText,
						firstPath);
				}
				else
				{
					_logger.LogDebug(failure.Exception,
						"Failed to forward {BatchCount} {DisplayName} workspace file change(s) for '{Workspace}' starting at '{FirstPath}'; the batch was buffered for replay.",
						failure.BatchCount,
						_providerDisplayName,
						_workspaceRootsDisplayText,
						firstPath);
				}
			},
			bufferChangesWhileForwardingDisabled: false);
	}

	/// <summary>
	/// Starts the external workspace watcher of every root when the language-server client is available.
	/// </summary>
	internal void EnsureWorkspaceFileWatcherStarted()
	{
		for (int i = 0; i < _watchScopes.Length; i++)
			_watchScopes[i].EnsureStarted();
	}

	/// <summary>
	/// Normalizes and forwards a coalesced batch of external workspace changes to the language server.
	/// </summary>
	/// <param name="deliveringScope">The scope whose watcher or recovery reconciliation produced the batch.</param>
	/// <param name="batch">The coalesced file change batch.</param>
	/// <param name="cancellationToken">Cancels the forwarding operation.</param>
	/// <remarks>
	/// Batch entries are already normalized local paths: the workspace watcher and the snapshot tracker normalize
	/// every path before queuing it, so the batch is not re-normalized here. A path is forwarded when its owning
	/// scope is the delivering scope (longest prefix wins when roots nest), so overlapping watchers produce one
	/// notification: the outer root's watcher observes a nested root's subtree too, but the nested root's own
	/// watcher is the single forwarder for its paths. A non-owned path is still forwarded (and tracked) while the
	/// owning scope's watcher is inactive - not started yet, or being recovered - so a root that is temporarily
	/// unwatched stays covered by the delivering watcher instead of losing its changes; once the owning scope's
	/// watcher is active again, its recovery reconciliation owns the missed-change detection. A path outside every
	/// root is forwarded as delivered.
	/// </remarks>
	internal async Task DispatchWorkspaceFileChangesAsync(WorkspaceWatchScope deliveringScope, FileChangeBatch batch, CancellationToken cancellationToken)
	{
		if (_clientAccessor() is null || _isDisposedAccessor() || batch.Count == 0)
			return;

		var changes = new List<WorkspaceFileChange>(batch.Count);

		foreach ((string path, FileChangeKind kind) in batch.Entries)
		{
			WorkspaceWatchScope? owningScope = FindOwningScope(path);

			if (owningScope is not null && !ReferenceEquals(owningScope, deliveringScope) && owningScope.IsWatcherActive)
				continue;

			changes.Add(new WorkspaceFileChange(path, kind));
		}

		if (changes.Count == 0)
			return;

		bool forwarded = await _workspaceFileChangeForwarder.DispatchAsync(changes, SendWorkspaceFileChangesAsync, cancellationToken).ConfigureAwait(false);

		if (forwarded)
			ApplyTrackedChangesToOwningScopes(changes);
	}

	/// <summary>
	/// Replays any buffered workspace changes once the language server is ready again.
	/// </summary>
	/// <param name="cancellationToken">Cancels the replay operation.</param>
	internal async Task ReplayDeferredWorkspaceFileChangesAsync(CancellationToken cancellationToken)
	{
		if (_clientAccessor() is null || _isDisposedAccessor())
			return;

		IReadOnlyList<WorkspaceFileChange> replayedChanges = await _workspaceFileChangeForwarder.ReplayDeferredAsync(SendWorkspaceFileChangesAsync, cancellationToken).ConfigureAwait(false);

		if (replayedChanges.Count > 0)
			ApplyTrackedChangesToOwningScopes(replayedChanges);
	}

	/// <summary>
	/// Disposes every root's watch scope and any buffered forwarding state.
	/// </summary>
	public void Dispose()
	{
		// Disposal-order invariant: every watcher and the forwarding buffer are stopped before the owner
		// disposes the language-server client, so a queued dispatch never observes a half-disposed
		// transport. The coordinator is always disposed before the provider's client.
		for (int i = 0; i < _watchScopes.Length; i++)
			_watchScopes[i].Dispose();

		_workspaceFileChangeForwarder.Dispose();
	}

	/// <summary>
	/// Sends one forwarded batch: a configuration refresh (when the batch touched a configuration path and the
	/// settings hook produced a payload) precedes the watched-files notification, and a transport failure marks
	/// the generation observed before the send unhealthy.
	/// </summary>
	/// <param name="changes">The forwarded file changes.</param>
	/// <param name="cancellationToken">Cancels the sends.</param>
	private async Task SendWorkspaceFileChangesAsync(IReadOnlyList<WorkspaceFileChange> changes, CancellationToken cancellationToken)
	{
		ILanguageServerClient? client = _clientAccessor();

		if (client is null || _isDisposedAccessor() || changes.Count == 0)
			return;

		long transportGeneration = client.TransportGeneration;

		bool shouldRefreshConfiguration = false;
		var payloads = new List<FileEventPayload>(changes.Count);

		for (int i = 0; i < changes.Count; i++)
		{
			WorkspaceFileChange change = changes[i];
			shouldRefreshConfiguration |= IsConfigurationPathContained(change.Path);
			payloads.Add(new FileEventPayload(LanguageServerPaths.CreateFileUri(change.Path), change.Kind));
		}

		try
		{
			if (shouldRefreshConfiguration && TryCreateSettingsPayload(out object? settingsPayload))
			{
				await client.SendNotificationAsync(LspMethodNames.DidChangeConfiguration,
					new DidChangeConfigurationParams(settingsPayload),
					cancellationToken).ConfigureAwait(false);
			}

			await client.SendNotificationAsync(LspMethodNames.DidChangeWatchedFiles,
				new DidChangeWatchedFilesParams([.. payloads]), cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			_tryMarkTransportUnhealthy(transportGeneration);
			throw;
		}
		catch (IOException)
		{
			_tryMarkTransportUnhealthy(transportGeneration);
			throw;
		}
		catch (ObjectDisposedException) when (!_isDisposedAccessor())
		{
			_tryMarkTransportUnhealthy(transportGeneration);
			throw;
		}
	}

	/// <summary>
	/// Invokes the configuration-path hook, containing failures so a provider defect cannot break file forwarding.
	/// </summary>
	/// <param name="normalizedPath">The normalized path of the changed file.</param>
	/// <returns><see langword="true"/> when the path requires a settings refresh; otherwise, <see langword="false"/>.</returns>
	private bool IsConfigurationPathContained(string normalizedPath)
		=> HookContainment.Invoke(_logger, _providerDisplayName, () => _isConfigurationPath(normalizedPath), fallbackValue: false, "configuration-path");

	/// <summary>
	/// Invokes the settings-payload hook, containing failures so a provider defect cannot block the watched-files
	/// notification.
	/// </summary>
	/// <param name="settingsPayload">The created settings payload when the hook succeeded.</param>
	/// <returns><see langword="true"/> when the payload was created; otherwise, <see langword="false"/>.</returns>
	private bool TryCreateSettingsPayload([NotNullWhen(true)] out object? settingsPayload)
	{
		settingsPayload = HookContainment.Invoke<object?>(_logger, _providerDisplayName, _createSettingsPayload, fallbackValue: null, "settings-payload");
		return settingsPayload is not null;
	}

	/// <summary>
	/// Applies forwarded changes to the tracked snapshot of the scope that owns each path.
	/// </summary>
	/// <remarks>
	/// Roots may nest; the longest matching root wins, so a nested subtree's changes are applied to its own
	/// scope even when they arrive through the outer root's watcher.
	/// </remarks>
	/// <param name="changes">The normalized forwarded changes.</param>
	private void ApplyTrackedChangesToOwningScopes(IReadOnlyList<WorkspaceFileChange> changes)
	{
		Dictionary<WorkspaceWatchScope, List<WorkspaceFileChange>>? ownedChanges = null;

		for (int i = 0; i < changes.Count; i++)
		{
			WorkspaceFileChange change = changes[i];
			WorkspaceWatchScope? owningScope = FindOwningScope(change.Path);

			if (owningScope is null)
				continue;

			ownedChanges ??= new Dictionary<WorkspaceWatchScope, List<WorkspaceFileChange>>(_watchScopes.Length);

			if (!ownedChanges.TryGetValue(owningScope, out List<WorkspaceFileChange>? scopeChanges))
			{
				scopeChanges = [];
				ownedChanges.Add(owningScope, scopeChanges);
			}

			scopeChanges.Add(change);
		}

		if (ownedChanges is null)
			return;

		foreach ((WorkspaceWatchScope scope, List<WorkspaceFileChange> scopeChanges) in ownedChanges)
			scope.ApplyTrackedChanges(scopeChanges);
	}

	/// <summary>
	/// Finds the scope that owns a normalized path: the scope whose root is the longest prefix of the path.
	/// </summary>
	/// <param name="normalizedPath">The normalized path to route.</param>
	/// <returns>The owning scope, or <see langword="null"/> when no root contains the path.</returns>
	private WorkspaceWatchScope? FindOwningScope(string normalizedPath)
	{
		WorkspaceWatchScope? owningScope = null;
		int longestRootLength = -1;

		for (int i = 0; i < _watchScopes.Length; i++)
		{
			WorkspaceWatchScope scope = _watchScopes[i];

			if (scope.WorkspaceRootDirectoryPath.Length > longestRootLength
				&& IsPathWithinRoot(normalizedPath, scope.WorkspaceRootDirectoryPath))
			{
				owningScope = scope;
				longestRootLength = scope.WorkspaceRootDirectoryPath.Length;
			}
		}

		return owningScope;
	}

	/// <summary>
	/// Reports whether a normalized path lies within a normalized root directory.
	/// </summary>
	/// <param name="normalizedPath">The normalized path to test.</param>
	/// <param name="normalizedRoot">The normalized root directory.</param>
	/// <returns><see langword="true"/> when the path is the root itself or lies beneath it; otherwise, <see langword="false"/>.</returns>
	private static bool IsPathWithinRoot(string normalizedPath, string normalizedRoot)
	{
		if (!normalizedPath.StartsWith(normalizedRoot, LanguageServerPaths.LocalPathComparison))
			return false;

		if (normalizedPath.Length == normalizedRoot.Length)
			return true;

		// A drive or share root already ends with a separator; any other root requires a separator boundary
		// so sibling directories that share a name prefix cannot match.
		return Path.EndsInDirectorySeparator(normalizedRoot)
			|| normalizedPath[normalizedRoot.Length] == Path.DirectorySeparatorChar;
	}
}

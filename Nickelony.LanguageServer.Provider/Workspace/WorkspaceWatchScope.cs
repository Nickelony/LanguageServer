namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Owns the workspace-watching lifecycle for one workspace root: its file watcher, the tracked snapshot
/// used for recovery reconciliation, the watcher recovery state, and the unrecoverable-failure latch.
/// </summary>
/// <remarks>
/// The coordinator creates one scope per workspace root and routes forwarded or replayed changes to the
/// owning scope. A watcher failure is contained to its own root: the scope restarts its watcher, reconciles
/// any changes missed during the outage, and reports an unrecoverable failure only for its own root.
/// </remarks>
internal sealed class WorkspaceWatchScope : IDisposable
{
	private readonly ILogger _logger;

	private enum WorkspaceWatcherRecoveryResult
	{
		Recovered,
		Unavailable,
		Failed
	}

	private readonly string _providerDisplayName;
	private readonly string _workspaceRootDirectoryPath;
	private readonly WorkspaceFileWatcherFactory _workspaceFileWatcherFactory;
	private readonly Func<WorkspaceWatchScope, FileChangeBatch, CancellationToken, Task> _dispatchAsync;
	private readonly Func<ILanguageServerClient?> _clientAccessor;
	private readonly Func<bool> _isDisposedAccessor;
	private readonly Action<WorkspaceWatcherFailure> _raiseWorkspaceWatcherFailed;
	private readonly WorkspaceSnapshotTracker _workspaceSnapshotTracker;

	private readonly object _watcherSyncRoot = new();

	private WorkspaceFileWatcher? _workspaceFileWatcher;
	private int _workspaceWatcherFailureReported;

	/// <summary>
	/// Initializes a new instance of the <see cref="WorkspaceWatchScope"/> class.
	/// </summary>
	/// <param name="context">The scope context describing the root, watcher factory, dispatch callback, and log identity.</param>
	/// <param name="callbacks">The owner callbacks used for client access, disposal probing, and failure reporting.</param>
	internal WorkspaceWatchScope(WorkspaceWatchScopeContext context, WorkspaceChangeCallbacks callbacks)
	{
		_logger = context.Logger;
		_providerDisplayName = context.ProviderDisplayName;
		_workspaceRootDirectoryPath = context.WorkspaceRootDirectoryPath;
		_workspaceFileWatcherFactory = context.WorkspaceFileWatcherFactory;
		_dispatchAsync = context.DispatchAsync;
		_clientAccessor = callbacks.ClientAccessor;
		_isDisposedAccessor = callbacks.IsDisposedAccessor;
		_raiseWorkspaceWatcherFailed = callbacks.RaiseWorkspaceWatcherFailed;
		_workspaceSnapshotTracker = new WorkspaceSnapshotTracker(context.WorkspaceRootDirectoryPath, context.WatchSpecifications, context.Logger);
	}

	/// <summary>
	/// Gets the normalized workspace root directory this scope owns.
	/// </summary>
	internal string WorkspaceRootDirectoryPath => _workspaceRootDirectoryPath;

	/// <summary>
	/// Gets a value indicating whether this scope currently has a started workspace file watcher.
	/// </summary>
	/// <remarks>
	/// The coordinator uses this to keep a nested root covered: while this scope's watcher is inactive (not
	/// started yet, or being recovered), the delivering outer watcher forwards and tracks the root's changes
	/// instead of filtering them away.
	/// </remarks>
	internal bool IsWatcherActive
	{
		get
		{
			lock (_watcherSyncRoot)
				return _workspaceFileWatcher is not null;
		}
	}

	/// <summary>
	/// Starts this root's workspace watcher when the language-server client is available.
	/// </summary>
	internal void EnsureStarted()
	{
		WorkspaceFileWatcher? watcherToDispose = null;
		Exception? startupException = null;
		bool shouldReportFailure = false;

		lock (_watcherSyncRoot)
		{
			if (_workspaceFileWatcher is not null || _clientAccessor() is null || string.IsNullOrEmpty(_workspaceRootDirectoryPath) || _isDisposedAccessor())
				return;

			bool watcherStarted = TryStartWorkspaceFileWatcher(out WorkspaceFileWatcher? watcher, out WorkspaceWatcherStartStatus startStatus, out startupException);

			if (!watcherStarted)
			{
				watcherToDispose = watcher;
				shouldReportFailure = startStatus == WorkspaceWatcherStartStatus.StartupFailed;
			}
			else
			{
				_workspaceFileWatcher = watcher;
				_workspaceSnapshotTracker.CaptureTrackedSnapshot();
				Interlocked.Exchange(ref _workspaceWatcherFailureReported, 0);
			}
		}

		DisposeWatcher(watcherToDispose, $"Failed to dispose an unstarted {_providerDisplayName} workspace file watcher.");

		if (shouldReportFailure)
			ReportWorkspaceWatcherStartupFailure(startupException);
	}

	/// <summary>
	/// Handles a failure reported by this root's watcher: replaces the watcher, reconciles any missed tracked
	/// changes, and reports an unrecoverable failure for this root when recovery cannot continue.
	/// </summary>
	/// <param name="watcher">The failed watcher.</param>
	/// <param name="exception">The watcher error, if one was provided.</param>
	internal void HandleWatcherFailed(WorkspaceFileWatcher watcher, Exception? exception)
	{
		if (_isDisposedAccessor())
			return;

		_logger.LogWarning(exception,
			"{DisplayName} workspace watching failed for '{Workspace}'. Attempting to restart the watcher automatically and replay any missed tracked changes.",
			_providerDisplayName,
			_workspaceRootDirectoryPath);

		WorkspaceWatcherRecoveryResult recoveryResult = RecoverWorkspaceFileWatcher(watcher);

		if (recoveryResult is WorkspaceWatcherRecoveryResult.Recovered or WorkspaceWatcherRecoveryResult.Unavailable)
			return;

		if (Interlocked.Exchange(ref _workspaceWatcherFailureReported, 1) != 0)
			return;

		_raiseWorkspaceWatcherFailed(new WorkspaceWatcherFailure(
			CreateWatcherFailureMessage($"The {_providerDisplayName} workspace file watcher encountered an internal error and automatic recovery failed for the workspace root '{_workspaceRootDirectoryPath}'.")));
	}

	/// <summary>
	/// Applies already forwarded file changes to this root's tracked snapshot.
	/// </summary>
	/// <param name="changes">The normalized forwarded file changes owned by this scope.</param>
	internal void ApplyTrackedChanges(IReadOnlyList<WorkspaceFileChange> changes)
		=> _workspaceSnapshotTracker.ApplyChanges(changes);

	/// <summary>
	/// Disposes this root's active watcher.
	/// </summary>
	public void Dispose()
	{
		WorkspaceFileWatcher? watcher;

		lock (_watcherSyncRoot)
		{
			watcher = _workspaceFileWatcher;
			_workspaceFileWatcher = null;
		}

		DisposeWatcher(watcher, $"Failed to dispose the {_providerDisplayName} workspace file watcher.");
	}

	/// <summary>
	/// Attempts to replace a failed watcher, reconcile any missed tracked changes, and classify the recovery outcome.
	/// </summary>
	private WorkspaceWatcherRecoveryResult RecoverWorkspaceFileWatcher(WorkspaceFileWatcher failedWatcher)
	{
		bool watcherRecovered = false;
		bool replacementWatcherStarted = false;
		bool startupFailed = false;
		bool workspaceUnavailable = false;
		Exception? startupException = null;
		WorkspaceFileWatcher? failedWatcherToDispose = failedWatcher;
		WorkspaceFileWatcher? replacementWatcherToDispose = null;
		Dictionary<string, WorkspaceSnapshotEntry>? previousSnapshot = null;
		Dictionary<string, WorkspaceSnapshotEntry>? currentSnapshot = null;

		// Decide under the lock whether recovery is needed and, if so, capture the snapshots required for reconciliation.
		lock (_watcherSyncRoot)
		{
			if (!ReferenceEquals(_workspaceFileWatcher, failedWatcher))
			{
				watcherRecovered = _workspaceFileWatcher is not null;
			}
			else
			{
				previousSnapshot = _workspaceSnapshotTracker.CloneTrackedSnapshot();
				_workspaceFileWatcher = null;
			}

			if (!watcherRecovered && !_isDisposedAccessor() && _clientAccessor() is not null && !string.IsNullOrEmpty(_workspaceRootDirectoryPath))
			{
				bool replacementStarted = TryStartWorkspaceFileWatcher(out WorkspaceFileWatcher? replacementWatcher, out WorkspaceWatcherStartStatus startStatus, out startupException);

				if (replacementStarted)
				{
					_workspaceFileWatcher = replacementWatcher;
					currentSnapshot = _workspaceSnapshotTracker.ReplaceTrackedSnapshotWithCurrent();
					watcherRecovered = true;
					replacementWatcherStarted = true;
					Interlocked.Exchange(ref _workspaceWatcherFailureReported, 0);
				}
				else
				{
					startupFailed = startStatus == WorkspaceWatcherStartStatus.StartupFailed;
					workspaceUnavailable = startStatus == WorkspaceWatcherStartStatus.WorkspaceRootMissing;
					replacementWatcherToDispose = replacementWatcher;
				}
			}
		}

		// Dispose watcher instances outside the lock so recovery bookkeeping stays responsive.
		DisposeWatcher(failedWatcherToDispose, $"Failed to dispose a {_providerDisplayName} workspace file watcher while recovering from a watcher error.");
		DisposeWatcher(replacementWatcherToDispose, $"Failed to dispose a {_providerDisplayName} workspace file watcher while recovering from a watcher error.");

		if (watcherRecovered)
		{
			_logger.LogInformation("{DisplayName} workspace watching recovered successfully for '{Workspace}'.",
				_providerDisplayName,
				_workspaceRootDirectoryPath);

			// Reconcile any tracked changes that may have happened while the watcher was unavailable.
			if (replacementWatcherStarted && previousSnapshot is not null && currentSnapshot is not null)
				BackgroundTaskObserver.Observe(_logger, ReconcileWorkspaceSnapshotAsync(previousSnapshot, currentSnapshot), $"{_providerDisplayName} workspace watcher recovery reconciliation");

			return WorkspaceWatcherRecoveryResult.Recovered;
		}

		if (workspaceUnavailable)
		{
			_logger.LogInformation(
				"{DisplayName} workspace watching remains unavailable for '{Workspace}' because the workspace path does not exist.",
				_providerDisplayName,
				_workspaceRootDirectoryPath);

			return WorkspaceWatcherRecoveryResult.Unavailable;
		}

		// Log startup failures separately so the caller can surface the right watcher-failure message.
		if (startupFailed)
		{
			_logger.LogWarning(startupException,
				"{DisplayName} workspace watching could not be restarted for '{Workspace}' because watcher startup failed.",
				_providerDisplayName,
				_workspaceRootDirectoryPath);
		}

		return WorkspaceWatcherRecoveryResult.Failed;
	}

	/// <summary>
	/// Reports a watcher startup failure at most once per scope: logs the warning and raises the
	/// workspace-watcher-failure report when none was raised yet.
	/// </summary>
	/// <param name="exception">The startup exception reported by the watcher, if any.</param>
	private void ReportWorkspaceWatcherStartupFailure(Exception? exception)
	{
		if (Interlocked.Exchange(ref _workspaceWatcherFailureReported, 1) != 0)
			return;

		_logger.LogWarning(exception,
			"{DisplayName} workspace watching could not start for '{Workspace}'. External workspace changes will not be forwarded until the watcher can be started successfully.",
			_providerDisplayName,
			_workspaceRootDirectoryPath);

		_raiseWorkspaceWatcherFailed(new WorkspaceWatcherFailure(
			CreateWatcherFailureMessage($"The {_providerDisplayName} workspace file watcher could not be started for the workspace root '{_workspaceRootDirectoryPath}'.")));
	}

	/// <summary>
	/// Replays the tracked workspace delta detected while this root's watcher was unavailable.
	/// </summary>
	private async Task ReconcileWorkspaceSnapshotAsync(
		Dictionary<string, WorkspaceSnapshotEntry> previousSnapshot,
		Dictionary<string, WorkspaceSnapshotEntry> currentSnapshot)
	{
		if (_isDisposedAccessor())
			return;

		FileChangeBatch batch = WorkspaceSnapshotTracker.BuildDeltaBatch(previousSnapshot, currentSnapshot);

		if (batch.Count == 0)
			return;

		_logger.LogInformation(
			"Replaying {Count} reconciled workspace file change(s) after {DisplayName} workspace watcher recovery for '{Workspace}'.",
			batch.Count,
			_providerDisplayName,
			_workspaceRootDirectoryPath);

		await _dispatchAsync(this, batch, CancellationToken.None).ConfigureAwait(false);
	}

	/// <summary>
	/// Creates a watcher through the factory and starts it, returning the watcher, the start status,
	/// and the startup exception reported by the watcher.
	/// </summary>
	/// <param name="startStatus">Receives the watcher start status.</param>
	/// <param name="startupException">Receives the exception reported by a failed start, when any.</param>
	/// <returns>The created watcher instance.</returns>
	private WorkspaceFileWatcher CreateAndStartWorkspaceFileWatcher(out WorkspaceWatcherStartStatus startStatus, out Exception? startupException)
	{
		WorkspaceFileWatcher watcher = _workspaceFileWatcherFactory(
			_workspaceRootDirectoryPath,
			(batch, cancellationToken) => _dispatchAsync(this, batch, cancellationToken),
			HandleWatcherFailed);

		ArgumentNullException.ThrowIfNull(watcher);

		startStatus = watcher.Start(out startupException);
		return watcher;
	}

	/// <summary>
	/// Builds one user-facing watcher-failure message with the shared host-neutral consequence sentence.
	/// </summary>
	/// <param name="firstSentence">The context sentence describing the failure and its workspace root.</param>
	/// <returns>The complete failure message.</returns>
	private string CreateWatcherFailureMessage(string firstSentence)
		=> $"{firstSentence}\n\n{_providerDisplayName} IntelliSense will continue to work for documents synchronized through this provider, but external workspace changes may not be forwarded until the watcher is available again.";

	/// <summary>
	/// Attempts to create and start a workspace file watcher, classifying a throwing factory as a startup failure
	/// instead of letting it escape.
	/// </summary>
	/// <param name="watcher">Receives the created watcher, when any.</param>
	/// <param name="startStatus">Receives the watcher start status.</param>
	/// <param name="startupException">Receives the exception reported by a failed start or a throwing factory.</param>
	/// <returns><see langword="true"/> when the watcher started; otherwise, <see langword="false"/>.</returns>
	private bool TryStartWorkspaceFileWatcher(out WorkspaceFileWatcher? watcher, out WorkspaceWatcherStartStatus startStatus, out Exception? startupException)
	{
		watcher = null;

		try
		{
			watcher = CreateAndStartWorkspaceFileWatcher(out startStatus, out startupException);
		}
		catch (Exception exception)
		{
			// A throwing watcher factory must not escape into request APIs; classify it as a startup failure so it
			// surfaces through the watcher-failure report instead.
			startStatus = WorkspaceWatcherStartStatus.StartupFailed;
			startupException = exception;
		}

		return startStatus is WorkspaceWatcherStartStatus.Started or WorkspaceWatcherStartStatus.AlreadyRunning;
	}

	private void DisposeWatcher(WorkspaceFileWatcher? watcher, string message)
	{
		if (watcher is null)
			return;

		try
		{
			watcher.Dispose();
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "{Message}", message);
		}
	}
}

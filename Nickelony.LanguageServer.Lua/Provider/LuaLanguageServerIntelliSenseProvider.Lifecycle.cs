using Nickelony.LanguageServer.Abstractions.Infrastructure.Provider;

namespace Nickelony.LanguageServer.Lua;

public sealed partial class LuaLanguageServerIntelliSenseProvider
{
	/// <summary>
	/// Ensures the language-server transport is running and that tracked documents and workspace watching are restored after reconnects.
	/// </summary>
	private async Task<bool> EnsureStartedAsync(CancellationToken cancellationToken)
	{
		if (_isDisposed || _client is null)
			return false;

		if (GetConsecutiveStartupFailures() >= HardStartupFailureThreshold)
		{
			SetProviderState(LanguageServerProviderState.Failed);
			return false;
		}

		using var fastPathDisposeAwareCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);

		// Fast path: once the client is healthy, keep the workspace watcher alive and avoid taking the startup lock.
		if (GetStartupSucceeded() && _client.IsReady)
		{
			_workspaceChanges.EnsureWorkspaceFileWatcherStarted();
			await _workspaceChanges.ReplayDeferredWorkspaceFileChangesAsync(fastPathDisposeAwareCts.Token).ConfigureAwait(false);

			return true;
		}

		SetProviderState(LanguageServerProviderState.Starting);

		bool shieldCancellationForRestart = GetStartupSucceeded() && !_client.IsReady;

		CancellationToken startupCancellationToken = shieldCancellationForRestart
			? CancellationToken.None
			: cancellationToken;

		using var disposeAwareStartupCts = CancellationTokenSource.CreateLinkedTokenSource(startupCancellationToken, _disposeCts.Token);
		CancellationToken effectiveStartupCancellationToken = disposeAwareStartupCts.Token;
		bool startLockHeld = false;

		try
		{
			// Serialize startup and restart work so concurrent callers share one recovery flow.
			await _startLock.WaitAsync(effectiveStartupCancellationToken).ConfigureAwait(false);
			startLockHeld = true;

			IReadOnlyList<DocumentSnapshot> documentsToReopen = [];

			// Re-check state after taking the lock so concurrent callers share the same restart/startup work.
			if (GetConsecutiveStartupFailures() >= HardStartupFailureThreshold)
			{
				SetProviderState(LanguageServerProviderState.Failed);
				return false;
			}

			if (GetStartupSucceeded() && _client.IsReady)
			{
				SetProviderState(LanguageServerProviderState.Ready, notifyCapabilitiesChanged: true);

				_workspaceChanges.EnsureWorkspaceFileWatcherStarted();
				await _workspaceChanges.ReplayDeferredWorkspaceFileChangesAsync(effectiveStartupCancellationToken).ConfigureAwait(false);

				return true;
			}

			if (!_client.IsReady)
			{
				documentsToReopen = _documents.PrepareForRestart();

				if (GetStartupSucceeded())
				{
					_logger.LogInformation("Lua language server connection dropped for workspace '{Workspace}'; restarting and reopening {DocumentCount} tracked document(s).",
						_workspaceRootDirectoryPath,
						documentsToReopen.Count);
				}
			}

			// Start the transport, then replay tracked documents when this is a restart rather than a cold start.
			bool startupSucceeded = await _client.StartAsync(effectiveStartupCancellationToken).ConfigureAwait(false);
			long startedTransportGeneration = startupSucceeded ? _client.TransportGeneration : 0;

			if (startupSucceeded && documentsToReopen.Count > 0)
			{
				startupSucceeded = await ReopenTrackedDocumentsAsync(documentsToReopen, effectiveStartupCancellationToken).ConfigureAwait(false);

				if (!startupSucceeded)
				{
					_logger.LogWarning("Failed to replay {DocumentCount} tracked document(s) after Lua language server restart for workspace '{Workspace}'.",
						documentsToReopen.Count,
						_workspaceRootDirectoryPath);
				}
			}

			if (startupSucceeded)
				startupSucceeded = TryCompleteSuccessfulStart(startedTransportGeneration);

			if (startupSucceeded)
			{
				ResetRequestTimeoutTracking(startedTransportGeneration);

				_workspaceChanges.EnsureWorkspaceFileWatcherStarted();
				await _workspaceChanges.ReplayDeferredWorkspaceFileChangesAsync(effectiveStartupCancellationToken).ConfigureAwait(false);
			}
			else
			{
				int consecutiveStartupFailures = RegisterStartupFailure();
				bool isPermanentFailure = consecutiveStartupFailures >= HardStartupFailureThreshold;

				SetProviderState(
					isPermanentFailure ? LanguageServerProviderState.Failed : LanguageServerProviderState.Unavailable,
					notifyCapabilitiesChanged: true);

				// Record repeated failures so IntelliSense eventually stops advertising availability until restart.
				if (isPermanentFailure)
				{
					_logger.LogError("Lua language server failed to start {Count} times consecutively for workspace '{Workspace}'; IntelliSense is now disabled until the editor is restarted.",
						consecutiveStartupFailures, _workspaceRootDirectoryPath);
				}
				else
				{
					_logger.LogWarning("Failed to start the Lua language server for workspace '{Workspace}' (attempt {Attempt}/{Threshold}).",
						_workspaceRootDirectoryPath, consecutiveStartupFailures, HardStartupFailureThreshold);
				}

				ReportStartupFailure(isPermanentFailure);
			}

			return startupSucceeded;
		}
		catch (OperationCanceledException) when (_isDisposed)
		{
			return false;
		}
		catch (OperationCanceledException)
		{
			SetProviderState(LanguageServerProviderState.Unavailable, notifyCapabilitiesChanged: true);
			throw;
		}
		catch
		{
			SetProviderState(LanguageServerProviderState.Unavailable, notifyCapabilitiesChanged: true);
			throw;
		}
		finally
		{
			if (startLockHeld)
				_startLock.Release();
		}
	}

	private void ReportStartupFailure(bool isPermanentFailure)
	{
		if (!TryMarkStartupFailureReported(isPermanentFailure))
			return;

		LanguageServerStartupFailure failure = isPermanentFailure
			? new LanguageServerStartupFailure(
				"The configured Lua language server failed to start repeatedly and Lua IntelliSense is now disabled until the application is restarted. See the log for technical details.",
				true)
			: new LanguageServerStartupFailure(
				"The configured Lua language server failed to start. Lua IntelliSense will remain unavailable until the application can start the server successfully. The application will retry automatically when Lua IntelliSense is requested again.",
				false);

		RaiseStartupFailed(failure);
	}

	private void ReportMissingClientFailure()
	{
		if (_isDisposed || _client is not null)
			return;

		SetProviderState(LanguageServerProviderState.Failed, notifyCapabilitiesChanged: true);

		if (!TryMarkStartupFailureReported(isPermanentFailure: true))
			return;

		s_logMissingExecutable(_logger, _workspaceRootDirectoryPath, null);

		RaiseStartupFailed(new LanguageServerStartupFailure(
			"The Lua language server executable is unavailable, so Lua IntelliSense is disabled until the application provides a valid server installation.",
			IsPersistent: true));
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Disposal is idempotent. It first closes callback admission and detaches provider subscribers, then cancels
	/// provider-owned work, disposes the workspace watcher/coordinator, and finally disposes the Lua language-server
	/// client owned by this provider. A callback already admitted before disposal began may finish; no later callback
	/// starts. The provider must not be used after disposal.
	/// </remarks>
	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
			return;

		CloseCallbackAdmission();
		SetProviderState(LanguageServerProviderState.Disposed);

		if (_client is not null)
		{
			_client.DiagnosticsPublished -= HandleDiagnosticsPublished;
			_client.SemanticTokensRefreshRequested -= HandleSemanticTokensRefreshRequested;
			_client.TransportUnavailable -= HandleTransportUnavailable;
		}

		try
		{
			_disposeCts.Cancel();
		}
		catch (ObjectDisposedException)
		{ }

		_workspaceChanges.Dispose();

		CancelAllQueuedDocumentUpdates();
		CancelAllSemanticTokenRequests();

		try
		{
			_client?.Dispose();
		}
		finally
		{
			_disposeCts.Dispose();
		}
	}

	private void CloseCallbackAdmission()
	{
		lock (_callbackAdmissionSyncRoot)
		{
			_callbackAdmissionClosed = true;
			_isDisposed = true;

			_diagnosticsUpdated = null;
			_semanticTokensUpdated = null;
			_capabilitiesChanged = null;
			_startupFailed = null;
			_workspaceWatcherFailed = null;
		}
	}
}

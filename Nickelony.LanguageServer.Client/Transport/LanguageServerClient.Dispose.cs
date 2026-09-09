using System.Diagnostics;

namespace Nickelony.LanguageServer.Client;

public sealed partial class LanguageServerClient
{
	private async Task WaitForQueuedFailedSessionDisposalsAsync(Stopwatch disposeStopwatch)
	{
		// The loop is bounded by the shared disposal budget: producers may keep replacing the queued
		// cleanup task, and waiting for each replacement must not extend teardown indefinitely.
		while (GetRemainingDisposeBudget(disposeStopwatch) > TimeSpan.Zero)
		{
			Task pendingDisposal = _transportHost.QueuedFailedSessionCleanupTask;

			await WaitWithDisposeBudgetAsync(
				pendingDisposal,
				disposeStopwatch,
				"detached session cleanup").ConfigureAwait(false);

			if (ReferenceEquals(pendingDisposal, _transportHost.QueuedFailedSessionCleanupTask))
				return;
		}
	}

	/// <summary>
	/// Disposes the currently active transport session, if any.
	/// </summary>
	private async Task DisposeActiveSessionAsync()
	{
		LanguageServerTransportSession? session = _capabilityStore.DetachActiveSession();

		if (session is not null)
			await _transportHost.DisposeSessionAsync(session).ConfigureAwait(false);
	}

	/// <summary>
	/// Begins disposal for the client if it has not already started.
	/// </summary>
	/// <returns><see langword="true"/> when the current caller should continue disposal; otherwise, <see langword="false"/>.</returns>
	internal bool TryBeginDispose()
	{
		if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
			return false;

		_isDisposed = true;
		return true;
	}

	/// <summary>
	/// Cancels the client lifetime token without starting teardown, stopping background pumps that observe it.
	/// </summary>
	internal void CancelLifetime()
	{
		try
		{
			_lifetimeCts.Cancel();
		}
		catch (ObjectDisposedException)
		{ }
		catch (Exception exception)
		{
			// CancellationTokenSource.Cancel wraps throwing cancellation callbacks in an AggregateException;
			// a callback failure must not abort disposal before the process and session are torn down.
			_logger.LogDebug(exception, "Language server lifetime cancellation for workspace '{Workspace}' reported one or more callback failures.", _workspaceRootsDisplayText);
		}
	}

	/// <summary>
	/// Runs the shared teardown sequence for the first dispose caller.
	/// </summary>
	/// <returns>A task that completes when disposal finishes.</returns>
	internal async Task DisposeCoreAsync()
	{
		var disposeStopwatch = Stopwatch.StartNew();

		_diagnosticsRouter.CompleteSignalChannels();
		CancelLifetime();

		await WaitWithDisposeBudgetAsync(
			DisposeActiveSessionAsync(),
			disposeStopwatch,
			"active session disposal").ConfigureAwait(false);

		await WaitForQueuedFailedSessionDisposalsAsync(disposeStopwatch).ConfigureAwait(false);

		if (!ReferenceEquals(_diagnosticsRouter.DiagnosticsPumpTask, Task.CompletedTask))
		{
			await WaitWithDisposeBudgetAsync(
				_diagnosticsRouter.DiagnosticsPumpTask,
				disposeStopwatch,
				"diagnostics pump").ConfigureAwait(false);
		}

		await WaitWithDisposeBudgetAsync(
			_diagnosticsRouter.CallbackPumpTask,
			disposeStopwatch,
			"callback dispatcher").ConfigureAwait(false);

		await DisposeStartLockAsync(GetRemainingDisposeBudget(disposeStopwatch)).ConfigureAwait(false);
		_lifetimeCts.Dispose();
	}

	/// <summary>
	/// Waits for one teardown task while spending from the caller's remaining disposal budget.
	/// </summary>
	/// <param name="task">The teardown task to await.</param>
	/// <param name="disposeStopwatch">Tracks the elapsed disposal time.</param>
	/// <param name="stage">The teardown stage reported in disposal diagnostics.</param>
	private async Task WaitWithDisposeBudgetAsync(Task task, Stopwatch disposeStopwatch, string stage)
	{
		TimeSpan remainingDisposeBudget = GetRemainingDisposeBudget(disposeStopwatch);

		if (remainingDisposeBudget <= TimeSpan.Zero)
		{
			if (!task.IsCompleted)
				_logger.LogWarning(
					"Language server teardown stage '{Stage}' was skipped because the disposal budget was exhausted.",
					stage);

			return;
		}

		try
		{
			await task.WaitAsync(remainingDisposeBudget).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
			_logger.LogWarning(
				"Language server teardown stage '{Stage}' did not complete within {TimeoutMs} ms during disposal.",
				stage,
				(int)remainingDisposeBudget.TotalMilliseconds);
		}
		catch (Exception exception)
		{
			if (_diagnosticsRouter.WasObservedBackgroundLoopTermination(task))
				return;

			_logger.LogWarning(exception, "Language server teardown stage '{Stage}' raised an exception during disposal.", stage);
		}
	}

	/// <summary>
	/// Gets the remaining disposal budget shared by the teardown stages.
	/// </summary>
	/// <param name="disposeStopwatch">Tracks the elapsed disposal time.</param>
	/// <returns>The remaining shared disposal budget.</returns>
	private TimeSpan GetRemainingDisposeBudget(Stopwatch disposeStopwatch)
	{
		TimeSpan remainingDisposeBudget = _disposeWaitTimeout - disposeStopwatch.Elapsed;
		return remainingDisposeBudget > TimeSpan.Zero ? remainingDisposeBudget : TimeSpan.Zero;
	}

	/// <summary>
	/// Waits for the startup gate to become available and then disposes it.
	/// </summary>
	/// <returns>A task that completes when the startup gate has been disposed or when cleanup timed out.</returns>
	internal async Task DisposeStartLockAsync(TimeSpan waitTimeout)
	{
		bool startLockHeld = false;

		if (waitTimeout <= TimeSpan.Zero)
		{
			_logger.LogWarning("Language server startup gate cleanup was skipped because the disposal budget was already exhausted.");

			return;
		}

		try
		{
			startLockHeld = await _startLock.WaitAsync(waitTimeout).ConfigureAwait(false);
		}
		catch (ObjectDisposedException)
		{
			return;
		}

		if (!startLockHeld)
		{
			_logger.LogWarning("Language server startup gate did not become available within {TimeoutMs} ms during disposal.",
				(int)waitTimeout.TotalMilliseconds);

			return;
		}

		// Dispose the gate while still holding it. A release-then-dispose window would let a concurrent startup
		// acquire the semaphore and release it after disposal, replacing the documented StartAsync result with an
		// ObjectDisposedException from its finally block. Concurrent waiters now observe ObjectDisposedException
		// from the wait itself, which the startup path already handles as a disposal race.
		try
		{
			_startLock.Dispose();
		}
		catch (ObjectDisposedException)
		{ }
	}

	/// <summary>
	/// Throws when the client has been disposed, unless disposed access is allowed.
	/// </summary>
	/// <param name="allowDisposed">Whether disposed access should be allowed.</param>
	private void ThrowIfDisposed(bool allowDisposed)
	{
		if (!allowDisposed)
			ObjectDisposedException.ThrowIf(_isDisposed, nameof(LanguageServerClient));
	}

	/// <summary>
	/// Stops the language-server process, completes pending requests, and releases transport resources.
	/// </summary>
	/// <remarks>
	/// Blocks the calling thread until teardown finishes; prefer <see cref="DisposeAsync"/> on UI threads.
	/// Only the first dispose caller performs teardown; later callers return immediately without waiting for it.
	/// Do not call this method, or block on <see cref="DisposeAsync"/>, from a
	/// <see cref="ILanguageServerClient.TransportUnavailable"/> handler or another client callback; see the
	/// interface remarks for the shared disposal and delivery contracts.
	/// </remarks>
	public void Dispose()
	{
		if (!TryBeginDispose())
			return;

		try
		{
			DisposeCoreAsync().GetAwaiter().GetResult();
		}
		finally
		{
			GC.SuppressFinalize(this);
		}
	}

	/// <summary>
	/// Stops the language-server process, completes pending requests, and releases transport resources asynchronously.
	/// </summary>
	/// <returns>
	/// A task that completes when disposal finishes for the first caller; later callers receive an already-completed
	/// task because only the first caller performs teardown.
	/// </returns>
	public async ValueTask DisposeAsync()
	{
		if (!TryBeginDispose())
			return;

		try
		{
			await DisposeCoreAsync().ConfigureAwait(false);
		}
		finally
		{
			GC.SuppressFinalize(this);
		}
	}
}

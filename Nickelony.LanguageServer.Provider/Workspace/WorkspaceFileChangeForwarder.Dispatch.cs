namespace Nickelony.LanguageServer.Provider;

public sealed partial class WorkspaceFileChangeForwarder
{
	/// <summary>
	/// Attempts to forward a new change set immediately.
	/// </summary>
	/// <remarks>
	/// The change set is buffered when the startup callback reports no usable transport or when a recoverable live
	/// transport failure occurs. When forwarding is not currently allowed, the change set is either buffered or ignored
	/// based on construction options. A change set that arrives after disposal was requested is dropped.
	/// </remarks>
	/// <param name="changes">The file changes to forward.</param>
	/// <param name="forwardAsync">The transport forwarding callback.</param>
	/// <param name="cancellationToken">Cancels the forwarding operation.</param>
	/// <returns><see langword="true"/> when the batch was forwarded immediately; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="changes"/> or <paramref name="forwardAsync"/> is <see langword="null"/>.
	/// </exception>
	public async Task<bool> DispatchAsync(
		IReadOnlyList<WorkspaceFileChange> changes,
		Func<IReadOnlyList<WorkspaceFileChange>, CancellationToken, Task> forwardAsync,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(changes);
		ArgumentNullException.ThrowIfNull(forwardAsync);

		if (!TryEnterOperation())
			return false;

		bool forwardingGateHeld = false;

		try
		{
			if (changes.Count == 0)
				return false;

			if (!_canForwardAccessor())
			{
				BufferChangesWhenForwardingDisabled(changes);
				return false;
			}

			bool started = await _ensureStartedAsync(cancellationToken).ConfigureAwait(false);

			if (IsDisposeRequested())
				return false;

			if (!started)
			{
				_pendingChanges.AddRange(changes);
				return false;
			}

			await _forwardingGate.WaitAsync(cancellationToken).ConfigureAwait(false);
			forwardingGateHeld = true;

			if (IsDisposeRequested())
				return false;

			if (!_canForwardAccessor())
			{
				BufferChangesWhenForwardingDisabled(changes);
				return false;
			}

			return await TryForwardAsync(changes, forwardAsync, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			if (forwardingGateHeld)
				_forwardingGate.Release();

			ExitOperation();
		}
	}

	private void BufferChangesWhenForwardingDisabled(IReadOnlyList<WorkspaceFileChange> changes)
	{
		if (_bufferChangesWhileForwardingDisabled)
			_pendingChanges.AddRange(changes);
	}

	/// <summary>
	/// Replays any previously buffered changes now that forwarding is allowed again.
	/// </summary>
	/// <remarks>If forwarding is still not allowed, the buffered set is preserved for a later replay attempt. A batch that failed
	/// a dispatch attempt is replayed before the changes that were buffered while the failed attempt was in flight.</remarks>
	/// <param name="forwardAsync">The transport forwarding callback.</param>
	/// <param name="cancellationToken">Cancels the replay operation.</param>
	/// <returns>The buffered changes that were replayed successfully, or an empty list when none were forwarded.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="forwardAsync"/> is <see langword="null"/>.</exception>
	public async Task<IReadOnlyList<WorkspaceFileChange>> ReplayDeferredAsync(
		Func<IReadOnlyList<WorkspaceFileChange>, CancellationToken, Task> forwardAsync,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(forwardAsync);

		if (!TryEnterOperation())
			return [];

		bool forwardingGateHeld = false;
		IReadOnlyList<WorkspaceFileChange>? deferredChanges = null;

		try
		{
			// The early check runs inside the try so a throwing host accessor cannot leak the operation count.
			if (!_canForwardAccessor() || (_pendingChanges.IsEmpty && _replayedChanges.IsEmpty))
				return [];

			await _forwardingGate.WaitAsync(cancellationToken).ConfigureAwait(false);
			forwardingGateHeld = true;

			if (IsDisposeRequested())
				return [];

			if (!_canForwardAccessor() || (_pendingChanges.IsEmpty && _replayedChanges.IsEmpty))
				return [];

			deferredChanges = DrainBufferedChanges();

			if (deferredChanges.Count == 0)
				return [];

			if (IsDisposeRequested())
				return [];

			bool forwarded = await TryForwardAsync(deferredChanges, forwardAsync, cancellationToken).ConfigureAwait(false);

			return forwarded ? deferredChanges : [];
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// The gate wait can be canceled before any changes were drained; a batch that was already drained is
			// re-buffered by TryForwardAsync instead of propagating out of it, so nothing has to be restored here.
			return [];
		}
		finally
		{
			if (forwardingGateHeld)
				_forwardingGate.Release();

			ExitOperation();
		}
	}

	/// <summary>
	/// Forwards a change set and converts recoverable transient live-forwarding failures into buffered replay state.
	/// </summary>
	/// <param name="changes">The file changes to forward.</param>
	/// <param name="forwardAsync">The transport forwarding callback.</param>
	/// <param name="cancellationToken">Cancels the forwarding operation.</param>
	/// <returns><see langword="true"/> when the batch was forwarded successfully; otherwise, <see langword="false"/>.</returns>
	private async Task<bool> TryForwardAsync(
		IReadOnlyList<WorkspaceFileChange> changes,
		Func<IReadOnlyList<WorkspaceFileChange>, CancellationToken, Task> forwardAsync,
		CancellationToken cancellationToken)
	{
		try
		{
			await forwardAsync(changes, cancellationToken).ConfigureAwait(false);
			return true;
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			_replayedChanges.AddRange(changes);
			_tryMarkTransportUnhealthy();
			return false;
		}
		catch (IOException exception)
		{
			_replayedChanges.AddRange(changes);
			_tryMarkTransportUnhealthy();

			LogForwardingFailure(exception, changes, wasDropped: false);
			return false;
		}
		catch (ObjectDisposedException)
		{
			if (!_isDisposedAccessor())
			{
				_replayedChanges.AddRange(changes);
				_tryMarkTransportUnhealthy();
			}

			return false;
		}
		catch (OperationCanceledException)
		{
			if (!_isDisposedAccessor())
				_replayedChanges.AddRange(changes);

			return false;
		}
		catch (Exception exception)
		{
			// Intentional: unexpected failures are treated as logic/protocol defects rather than
			// transient transport gaps. Replaying here risks duplicating a partially observed batch
			// during later recovery, so the batch is logged and dropped on purpose.
			LogForwardingFailure(exception, changes, wasDropped: true);
			return false;
		}
	}

	private void LogForwardingFailure(Exception exception, IReadOnlyList<WorkspaceFileChange> changes, bool wasDropped)
	{
		string? firstPath = changes.Count > 0 ? changes[0].Path : null;
		_logForwardingFailure?.Invoke(new WorkspaceFileForwardingFailure(exception, changes.Count, firstPath, wasDropped));
	}

	/// <summary>
	/// Drains both buffered accumulators into the next forwarding batch.
	/// </summary>
	/// <remarks>
	/// The failed-replay buffer is drained first so a batch that failed to dispatch keeps its original order ahead
	/// of the changes that were buffered while the failed attempt was in flight. Draining both buffers into one
	/// batch preserves the replacement semantics of a re-buffered delete followed by a newer create for the same
	/// path: the two entries are forwarded in order instead of canceling each other out.
	/// </remarks>
	/// <returns>The buffered changes in forwarding order.</returns>
	private IReadOnlyList<WorkspaceFileChange> DrainBufferedChanges()
	{
		if (_replayedChanges.IsEmpty)
			return _pendingChanges.DrainBatch().Entries;

		FileChangeBatch replayedBatch = _replayedChanges.DrainBatch();

		if (_pendingChanges.IsEmpty)
			return replayedBatch.Entries;

		FileChangeBatch pendingBatch = _pendingChanges.DrainBatch();

		var combinedChanges = new List<WorkspaceFileChange>(replayedBatch.Count + pendingBatch.Count);
		combinedChanges.AddRange(replayedBatch.Entries);
		combinedChanges.AddRange(pendingBatch.Entries);

		return combinedChanges;
	}
}

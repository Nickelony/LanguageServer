namespace Nickelony.LanguageServer.Client;

public sealed partial class DocumentOperationScheduler
{
	/// <summary>
	/// Replaces any queued latest-only update for the specified document path so only the newest pending update remains active.
	/// </summary>
	/// <remarks>
	/// The update is queued behind the operations already queued for the document, including a running update, and is
	/// skipped when it was superseded before it started. Queued delegates must not call back into the scheduler; that
	/// pattern is rejected with an <see cref="InvalidOperationException"/> instead of risking a deadlock.
	/// </remarks>
	/// <param name="filePath">The document path whose latest update should be replaced.</param>
	/// <param name="operation">The latest-only update delegate to execute.</param>
	/// <returns>
	/// A task that represents the queued update. A superseded update completes successfully without running its
	/// delegate; delegate faults and cancellation propagate to the caller.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="operation"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="filePath"/> is empty or whitespace-only, or the path is invalid on the current platform.</exception>
	/// <exception cref="InvalidOperationException">
	/// The caller executes inside a queued scheduler operation and must queue the update after awaiting it instead.
	/// </exception>
	public Task EnqueueLatestUpdateAsync(string filePath, Func<CancellationToken, Task> operation)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
		ArgumentNullException.ThrowIfNull(operation);

		string normalizedFilePath = LanguageServerPaths.NormalizeLocalPath(filePath);
		EnsureNotInsideSchedulerOperation(nameof(EnqueueLatestUpdateAsync));
		var replacementRegistration = new QueuedUpdateRegistration();
		var callerCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var enqueueGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		QueuedUpdateRegistration? previousRegistration = null;

		try
		{
			lock (_syncRoot)
			{
				DocumentQueue queue = GetOrCreateDocumentQueueUnderLock(normalizedFilePath);

				previousRegistration = queue._updateSlot;
				queue._updateSlot = replacementRegistration;

				long generation = ++queue._chainGeneration;

				queue._chainTail = RunLatestUpdateNodeAsync(
					normalizedFilePath,
					queue,
					enqueueGate.Task,
					[queue._chainTail],
					replacementRegistration,
					operation,
					callerCompletion,
					generation);
			}
		}
		finally
		{
			enqueueGate.TrySetResult(true);
		}

		// The supersede-cancel runs outside the scheduler lock: the slot identity check in TryMarkQueuedUpdateStarted
		// already prevents a superseded update from running its delegate, so no cancellation callback can run while
		// scheduler state is locked.
		CancelSupersededQueuedUpdate(previousRegistration);

		return callerCompletion.Task;
	}

	/// <summary>
	/// Cancels the current latest-only update for the specified document path, including one that is already running.
	/// A pending update is skipped and completes without running its delegate.
	/// </summary>
	/// <remarks>
	/// Cancellation reaches both the newest queued update and the update that is currently running, so an enqueue
	/// that superseded a running update cannot hide that update from this call.
	/// </remarks>
	/// <param name="filePath">The document path whose queued update should be canceled.</param>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="filePath"/> is empty or whitespace-only, or the path is invalid on the current platform.</exception>
	public void CancelQueuedUpdate(string filePath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

		string normalizedFilePath = LanguageServerPaths.NormalizeLocalPath(filePath);
		QueuedUpdateRegistration? queuedRegistration;
		QueuedUpdateRegistration? runningRegistration;

		lock (_syncRoot)
		{
			if (!_documentQueues.TryGetValue(normalizedFilePath, out DocumentQueue? queue))
				return;

			queuedRegistration = queue._updateSlot;
			runningRegistration = queue._runningRegistration;
			queue._updateSlot = null;

			if (ReferenceEquals(queuedRegistration, runningRegistration))
				queuedRegistration = null;
		}

		queuedRegistration?.Cancel();
		runningRegistration?.Cancel();
	}

	/// <summary>
	/// Cancels all currently active latest-only updates, both queued and running.
	/// </summary>
	public void CancelAllQueuedUpdates()
	{
		QueuedUpdateRegistration[] registrations;

		lock (_syncRoot)
		{
			var activeRegistrations = new List<QueuedUpdateRegistration>();

			foreach (DocumentQueue queue in _documentQueues.Values)
			{
				QueuedUpdateRegistration? queuedRegistration = queue._updateSlot;
				QueuedUpdateRegistration? runningRegistration = queue._runningRegistration;

				queue._updateSlot = null;

				if (queuedRegistration is not null)
					activeRegistrations.Add(queuedRegistration);

				if (runningRegistration is not null && !ReferenceEquals(runningRegistration, queuedRegistration))
					activeRegistrations.Add(runningRegistration);
			}

			if (activeRegistrations.Count == 0)
				return;

			registrations = [.. activeRegistrations];
		}

		for (int i = 0; i < registrations.Length; i++)
			registrations[i].Cancel();
	}

	/// <summary>
	/// Executes a latest-only update once its predecessors have settled and it is still the queued update for its
	/// document path.
	/// </summary>
	/// <param name="normalizedFilePath">The normalized document path associated with the update.</param>
	/// <param name="queue">The queue that owns the document path.</param>
	/// <param name="enqueueGate">Completes when the enqueuing call released the scheduler lock; the node starts only afterwards.</param>
	/// <param name="previousOperations">The operations that must settle before the update may run.</param>
	/// <param name="registration">The active queued-update registration.</param>
	/// <param name="operation">The update delegate to execute.</param>
	/// <param name="callerCompletion">The task exposed to the caller; the node itself never faults.</param>
	/// <param name="generation">The chain generation captured when the node was enqueued.</param>
	/// <returns>A task that completes when the update node settled.</returns>
	private async Task RunLatestUpdateNodeAsync(
		string normalizedFilePath,
		DocumentQueue queue,
		Task enqueueGate,
		Task[] previousOperations,
		QueuedUpdateRegistration registration,
		Func<CancellationToken, Task> operation,
		TaskCompletionSource<object?> callerCompletion,
		long generation)
	{
		try
		{
			// The enqueuing call completes the gate only after it released the scheduler lock, so the delegate never
			// starts while that lock is held.
			await enqueueGate.ConfigureAwait(false);

			await WaitForQueuedOperationsAsync(previousOperations).ConfigureAwait(false);

			if (!TryMarkQueuedUpdateStarted(queue, registration, out CancellationTokenSource? source) || source is null)
			{
				callerCompletion.TrySetResult(null);
				return;
			}

			await RunWithActiveContextAsync(
				new ActiveDocumentContext(normalizedFilePath, secondFilePath: null),
				async () =>
				{
					await operation(source.Token).ConfigureAwait(false);
					return true;
				}).ConfigureAwait(false);

			callerCompletion.TrySetResult(null);
		}
		catch (OperationCanceledException exception)
		{
			callerCompletion.TrySetCanceled(exception.CancellationToken);
		}
		catch (Exception exception)
		{
			// The node task doubles as the document chain tail; keep it fault-suppressed and surface the failure
			// through the caller's task instead so an ignored chain tail cannot fault unobserved.
			callerCompletion.TrySetException(exception);
		}
		finally
		{
			CompleteLatestUpdateNode(normalizedFilePath, queue, registration, generation);
		}
	}

	/// <summary>
	/// Clears the update slot once the completed node still owns it, clears the chain tail once it is still the
	/// current tail, removes the queue once it drained, and releases the registration's cancellation source.
	/// </summary>
	/// <param name="normalizedFilePath">The normalized document path whose update node completed.</param>
	/// <param name="queue">The queue that owns the document path.</param>
	/// <param name="registration">The registration of the completed update.</param>
	/// <param name="generation">The chain generation captured when the node was enqueued.</param>
	private void CompleteLatestUpdateNode(string normalizedFilePath, DocumentQueue queue, QueuedUpdateRegistration registration, long generation)
	{
		lock (_syncRoot)
		{
			if (ReferenceEquals(queue._updateSlot, registration))
				queue._updateSlot = null;

			if (ReferenceEquals(queue._runningRegistration, registration))
				queue._runningRegistration = null;

			if (queue._chainGeneration == generation)
				queue._chainTail = Task.CompletedTask;

			PruneDocumentQueueUnderLock(normalizedFilePath, queue);
		}

		registration.Dispose();
	}

	/// <summary>
	/// Marks a queued update as running when it is still the current registration for its document path.
	/// </summary>
	/// <param name="queue">The queue that owns the document path whose latest-only slot is checked under the scheduler lock.</param>
	/// <param name="registration">The queued-update registration to mark as running.</param>
	/// <param name="source">Receives the cancellation source of the running update.</param>
	/// <returns><see langword="true"/> when the update may run; otherwise, <see langword="false"/>.</returns>
	private bool TryMarkQueuedUpdateStarted(DocumentQueue queue, QueuedUpdateRegistration registration, out CancellationTokenSource? source)
	{
		lock (_syncRoot)
		{
			// A registration that is no longer the current slot was superseded: it must not run its delegate even
			// when its pending start mark races the enqueue that replaced it.
			if (!ReferenceEquals(queue._updateSlot, registration))
			{
				source = null;
				return false;
			}

			if (!registration.TryMarkStarted(out source))
				return false;

			// Track the running registration so CancelQueuedUpdate can reach an update that a superseding enqueue
			// removed from the slot while it kept running.
			queue._runningRegistration = registration;
			return true;
		}
	}

	/// <summary>
	/// Cancels a superseded queued update only while it is still pending.
	/// </summary>
	/// <param name="registration">The registration to cancel, or <see langword="null"/>.</param>
	private static void CancelSupersededQueuedUpdate(QueuedUpdateRegistration? registration)
		=> registration?.TryCancelIfNotStarted();

	/// <summary>
	/// Tracks one latest-only update registration and lazily owns the cancellation source of a running update.
	/// </summary>
	private sealed class QueuedUpdateRegistration : IDisposable
	{
		private readonly object _stateLock = new();
		private CancellationTokenSource? _source;
		private bool _hasStarted;
		private bool _cancelRequested;

		/// <summary>
		/// Marks the update as running and creates its cancellation source unless the registration was already
		/// superseded or canceled.
		/// </summary>
		/// <param name="source">Receives the cancellation source when the update may run.</param>
		/// <returns><see langword="true"/> when the update may run; otherwise, <see langword="false"/>.</returns>
		public bool TryMarkStarted(out CancellationTokenSource? source)
		{
			// The start mark and the supersede-cancel share this lock so a superseded update can never be
			// canceled between its last pending check and its running state.
			lock (_stateLock)
			{
				if (_hasStarted || _cancelRequested)
				{
					source = null;
					return false;
				}

				_hasStarted = true;
				_source = new CancellationTokenSource();
				source = _source;
				return true;
			}
		}

		/// <summary>
		/// Cancels the update only while it has not started running yet.
		/// </summary>
		/// <returns><see langword="true"/> when a pending update was canceled; otherwise, <see langword="false"/>.</returns>
		public bool TryCancelIfNotStarted()
		{
			lock (_stateLock)
			{
				if (_hasStarted || _cancelRequested)
					return false;

				_cancelRequested = true;
				return true;
			}
		}

		/// <summary>
		/// Cancels the update, including one that is already running.
		/// </summary>
		public void Cancel()
		{
			CancellationTokenSource? source;

			lock (_stateLock)
				source = _source;

			if (source is null)
				return;

			try
			{
				source.Cancel();
			}
			catch (ObjectDisposedException)
			{
				// The update completed and disposed its source while the cancel was in flight.
			}
			catch (AggregateException)
			{
				// Cancellation callbacks registered on the running update's token source can throw arbitrary exceptions;
				// a callback failure must not escape the public cancel surface.
			}
		}

		/// <summary>
		/// Releases the cancellation source once the update settled.
		/// </summary>
		public void Dispose()
		{
			CancellationTokenSource? source;

			lock (_stateLock)
			{
				source = _source;
				_source = null;
			}

			source?.Dispose();

			GC.SuppressFinalize(this);
		}
	}
}

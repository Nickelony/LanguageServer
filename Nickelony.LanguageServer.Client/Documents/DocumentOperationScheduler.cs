namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Serializes per-document operations, coalesces pending latest-only updates, and runs exclusive operations that
/// temporarily take ownership of one or two document paths.
/// </summary>
/// <remarks>
/// <para>
/// Every document path owns a single ordered operation chain; idle chains are pruned once they drain. Per-document
/// operations append to their chain, latest-only updates append a coalescable node that is skipped when it was
/// superseded before it started, and an exclusive operation inserts one node into the affected chains while also
/// joining the global chain. Work queued for a path after an exclusive operation observes that operation as its
/// predecessor, so later work never overtakes an exclusive operation.
/// </para>
/// <para>
/// Enqueue methods only register work: a queued delegate never starts while the scheduler's internal lock is held by
/// the enqueuing call, and enqueuing never blocks on the running operation. Each enqueue call releases its start gate
/// only after it left the lock, so the ordering is enforced instead of approximated.
/// </para>
/// <para>
/// Queued delegates must not call back into this scheduler while they are executing: every enqueue and wait method
/// rejects flow-local re-entrancy, including work started with <c>Task.Run</c> from inside the delegate, with an
/// <see cref="InvalidOperationException"/> instead of risking a deadlock. The rejection covers the delegate's own
/// document path, other document paths, and global or exclusive work, because a concurrent exclusive operation can
/// make any nested enqueue wait on the running delegate's chain; a different task that queues behind the running
/// delegate without sharing its flow can still block until the delegate completes. Inline nested work into the
/// delegate, or await the outer operation from the caller and queue follow-up work afterwards.
/// </para>
/// </remarks>
public sealed partial class DocumentOperationScheduler
{
	// Scheduler state: one queue per normalized document path plus the global operation chain.
	// All state is guarded by _syncRoot.
	private readonly object _syncRoot = new();

	private readonly AsyncLocal<ActiveDocumentContext?> _activeDocumentContext = new();

	private readonly Dictionary<string, DocumentQueue> _documentQueues = new(LanguageServerPaths.LocalPathComparer);
	private Task _queuedGlobalOperation = Task.CompletedTask;

	/// <summary>
	/// Awaits a queued operation while suppressing its failure so later operations can continue.
	/// </summary>
	/// <param name="queuedOperation">The previously scheduled operation.</param>
	/// <returns>A task that completes after the queued operation settles.</returns>
	private static async Task WaitForQueuedOperationAsync(Task queuedOperation)
	{
		try
		{
			await queuedOperation.ConfigureAwait(false);
		}
		catch
		{ }
	}

	/// <summary>
	/// Awaits queued operations while suppressing failures so later operations can continue.
	/// </summary>
	/// <param name="queuedOperations">The previously scheduled operations.</param>
	/// <returns>A task that completes after the queued operations settle.</returns>
	private static async Task WaitForQueuedOperationsAsync(Task[] queuedOperations)
	{
		for (int i = 0; i < queuedOperations.Length; i++)
			await WaitForQueuedOperationAsync(queuedOperations[i]).ConfigureAwait(false);
	}

	/// <summary>
	/// Tracks the document paths whose chain is currently executing an operation so that re-entrant scheduler calls
	/// can be rejected instead of deadlocking.
	/// </summary>
	/// <param name="firstFilePath">The first normalized document path whose operation is executing, if any.</param>
	/// <param name="secondFilePath">The second normalized document path whose operation is executing, if any.</param>
	private sealed class ActiveDocumentContext(string? firstFilePath, string? secondFilePath)
	{
		/// <summary>Gets the first normalized document path whose operation is executing, if any.</summary>
		public string? FirstFilePath { get; } = firstFilePath;

		/// <summary>Gets the second normalized document path whose operation is executing, if any.</summary>
		public string? SecondFilePath { get; } = secondFilePath;

		/// <summary>Gets or sets a value indicating whether the operation is still executing.</summary>
		public bool IsRunning { get; set; } = true;

		/// <summary>
		/// Describes the document paths whose operation is executing for error messages.
		/// </summary>
		/// <returns>A suffix naming the affected paths, or <see cref="string.Empty"/> for a global operation.</returns>
		public string DescribePaths() => (FirstFilePath, SecondFilePath) switch
		{
			({ } firstFilePath, { } secondFilePath) when !LanguageServerPaths.AreLocalPathsEqual(firstFilePath, secondFilePath)
				=> $" for '{firstFilePath}' and '{secondFilePath}'",
			({ } firstFilePath, _) => $" for '{firstFilePath}'",
			_ => string.Empty
		};
	}

	/// <summary>
	/// Runs an operation while publishing the active-operation context so nested scheduler calls are rejected.
	/// </summary>
	/// <typeparam name="TResult">The operation result type.</typeparam>
	/// <param name="context">The active-operation context to publish.</param>
	/// <param name="operation">The operation to run.</param>
	/// <returns>A task that completes with the operation result.</returns>
	private async Task<TResult> RunWithActiveContextAsync<TResult>(ActiveDocumentContext context, Func<Task<TResult>> operation)
	{
		ActiveDocumentContext? previousContext = _activeDocumentContext.Value;
		_activeDocumentContext.Value = context;

		try
		{
			return await operation().ConfigureAwait(false);
		}
		finally
		{
			context.IsRunning = false;
			_activeDocumentContext.Value = previousContext;
		}
	}

	/// <summary>
	/// Rejects an enqueue or wait issued from inside a queued scheduler operation.
	/// </summary>
	/// <param name="memberName">The calling member name, used in the exception message.</param>
	/// <exception cref="InvalidOperationException">
	/// The caller executes inside a queued scheduler operation, so awaiting new scheduler work could deadlock a chain.
	/// </exception>
	private void EnsureNotInsideSchedulerOperation(string memberName)
	{
		ActiveDocumentContext? context = _activeDocumentContext.Value;

		if (context is null || !context.IsRunning)
			return;

		throw new InvalidOperationException(
			$"'{memberName}' was called from inside a queued scheduler operation{context.DescribePaths()}. "
			+ "Queued delegates must not enqueue or wait for scheduler work; await the operation from the caller and queue follow-up work afterwards, "
			+ "or inline the nested work into the operation delegate instead.");
	}

	/// <summary>
	/// Runs a queued operation after the previously scheduled operations have settled and reports the result to the
	/// caller's completion source.
	/// </summary>
	/// <typeparam name="TResult">The operation result type.</typeparam>
	/// <param name="enqueueGate">Completes when the enqueuing call released the scheduler lock; the node starts only afterwards.</param>
	/// <param name="previousOperations">The operations that must settle before the delegate may run.</param>
	/// <param name="activeContext">The active-operation context published while the delegate executes.</param>
	/// <param name="operation">The queued operation to execute.</param>
	/// <param name="completionSource">The completion source exposed to the caller.</param>
	/// <param name="cancellationToken">Cancels the queued operation.</param>
	/// <param name="onCompleted">An optional action that runs after the queued operation settled.</param>
	/// <returns>A task representing the scheduled queue node.</returns>
	private async Task RunCompletionNodeAsync<TResult>(
		Task enqueueGate,
		Task[] previousOperations,
		ActiveDocumentContext activeContext,
		Func<CancellationToken, Task<TResult>> operation,
		TaskCompletionSource<TResult> completionSource,
		CancellationToken cancellationToken,
		Action? onCompleted = null)
	{
		try
		{
			// The enqueuing call completes the gate only after it released the scheduler lock, so the delegate never
			// starts while that lock is held.
			await enqueueGate.ConfigureAwait(false);

			await WaitForQueuedOperationsAsync(previousOperations).ConfigureAwait(false);

			cancellationToken.ThrowIfCancellationRequested();

			TResult result = await RunWithActiveContextAsync(activeContext, () => operation(cancellationToken)).ConfigureAwait(false);
			completionSource.TrySetResult(result);
		}
		// Cancellation is reported as canceled when it targets the caller's token or when the caller's token
		// observed cancellation, even if a delegate linked a token of its own; other faults stay faults.
		catch (OperationCanceledException exception) when (exception.CancellationToken == cancellationToken || cancellationToken.IsCancellationRequested)
		{
			completionSource.TrySetCanceled(cancellationToken);
		}
		catch (Exception exception)
		{
			completionSource.TrySetException(exception);
		}
		finally
		{
			onCompleted?.Invoke();
		}
	}

	/// <summary>
	/// Gets the queue for the supplied normalized document path, creating it on first use.
	/// The caller must hold <see cref="_syncRoot"/>.
	/// </summary>
	/// <param name="normalizedFilePath">The normalized document path.</param>
	/// <returns>The queue that owns the document path.</returns>
	private DocumentQueue GetOrCreateDocumentQueueUnderLock(string normalizedFilePath)
	{
		if (!_documentQueues.TryGetValue(normalizedFilePath, out DocumentQueue? queue))
		{
			queue = new DocumentQueue();
			_documentQueues[normalizedFilePath] = queue;
		}

		return queue;
	}

	/// <summary>
	/// Removes a document queue once it has fully drained.
	/// The caller must hold <see cref="_syncRoot"/>.
	/// </summary>
	/// <param name="normalizedFilePath">The normalized document path.</param>
	/// <param name="queue">The queue to remove once it is idle.</param>
	private void PruneDocumentQueueUnderLock(string normalizedFilePath, DocumentQueue queue)
	{
		if (queue.IsIdle
			&& _documentQueues.TryGetValue(normalizedFilePath, out DocumentQueue? currentQueue)
			&& ReferenceEquals(currentQueue, queue))
		{
			_documentQueues.Remove(normalizedFilePath);
		}
	}

	/// <summary>
	/// Captures the operation chain tails of one or two document paths.
	/// The caller must hold <see cref="_syncRoot"/>.
	/// </summary>
	/// <param name="firstFilePath">The first normalized document path.</param>
	/// <param name="secondFilePath">The second normalized document path.</param>
	/// <returns>The distinct chain tails that were current at snapshot time.</returns>
	private Task[] CaptureChainTailsUnderLock(string firstFilePath, string secondFilePath)
	{
		var queuedOperations = new List<Task>(2);

		AddChainTailUnderLock(queuedOperations, firstFilePath);

		if (!LanguageServerPaths.AreLocalPathsEqual(firstFilePath, secondFilePath))
			AddChainTailUnderLock(queuedOperations, secondFilePath);

		return [.. queuedOperations];
	}

	/// <summary>
	/// Adds the chain tail of one document path to a snapshot.
	/// The caller must hold <see cref="_syncRoot"/>.
	/// </summary>
	/// <param name="queuedOperations">The captured queued operations.</param>
	/// <param name="normalizedFilePath">The normalized document path.</param>
	private void AddChainTailUnderLock(List<Task> queuedOperations, string normalizedFilePath)
	{
		if (_documentQueues.TryGetValue(normalizedFilePath, out DocumentQueue? queue))
			AddQueuedOperation(queuedOperations, queue._chainTail);
	}

	/// <summary>
	/// Clears the chain tail once the completed node is still the current tail and removes the queue once it drained.
	/// </summary>
	/// <param name="normalizedFilePath">The normalized document path whose queue should be completed.</param>
	/// <param name="queue">The queue that owns the document path.</param>
	/// <param name="generation">The chain generation captured when the node was enqueued.</param>
	private void CompleteChainNode(string normalizedFilePath, DocumentQueue queue, long generation)
	{
		lock (_syncRoot)
		{
			if (queue._chainGeneration == generation)
				queue._chainTail = Task.CompletedTask;

			PruneDocumentQueueUnderLock(normalizedFilePath, queue);
		}
	}

	/// <summary>
	/// Adds one queued operation to a snapshot when it is not the completed-task sentinel and has not already been captured.
	/// </summary>
	/// <param name="queuedOperations">The captured queued operations.</param>
	/// <param name="queuedOperation">The queued operation to capture.</param>
	private static void AddQueuedOperation(List<Task> queuedOperations, Task queuedOperation)
	{
		if (ReferenceEquals(queuedOperation, Task.CompletedTask))
			return;

		for (int i = 0; i < queuedOperations.Count; i++)
		{
			if (ReferenceEquals(queuedOperations[i], queuedOperation))
				return;
		}

		queuedOperations.Add(queuedOperation);
	}

	/// <summary>
	/// Tracks the serialized operation chain of one document path and the active latest-only update registration.
	/// </summary>
	private sealed class DocumentQueue
	{
		/// <summary>The tail of the serialized operation chain.</summary>
		internal Task _chainTail = Task.CompletedTask;

		/// <summary>The generation of the current chain tail, bumped on every enqueue.</summary>
		internal long _chainGeneration;

		/// <summary>The registration of the newest latest-only update, if any.</summary>
		internal QueuedUpdateRegistration? _updateSlot;

		/// <summary>The registration of the latest-only update that is currently running, if any.</summary>
		internal QueuedUpdateRegistration? _runningRegistration;

		/// <summary>
		/// Gets a value indicating whether the queue holds no queued, running, or registered work.
		/// </summary>
		public bool IsIdle => ReferenceEquals(_chainTail, Task.CompletedTask) && _updateSlot is null && _runningRegistration is null;
	}
}

namespace Nickelony.LanguageServer.Client;

public sealed partial class DocumentOperationScheduler
{
	/// <summary>
	/// Enqueues an operation behind the current chain for the specified document path.
	/// </summary>
	/// <typeparam name="TResult">The operation result type.</typeparam>
	/// <param name="filePath">The document path whose queue should receive the operation.</param>
	/// <param name="operation">The operation to enqueue.</param>
	/// <param name="cancellationToken">Cancels the queued operation.</param>
	/// <returns>A task that completes with the queued operation result.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="operation"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="filePath"/> is empty or whitespace-only, or the path is invalid on the current platform.</exception>
	/// <exception cref="InvalidOperationException">
	/// The caller executes inside a queued scheduler operation and must queue the operation after awaiting it instead.
	/// </exception>
	public Task<TResult> EnqueuePerDocumentAsync<TResult>(string filePath, Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
		ArgumentNullException.ThrowIfNull(operation);

		string normalizedFilePath = LanguageServerPaths.NormalizeLocalPath(filePath);
		EnsureNotInsideSchedulerOperation(nameof(EnqueuePerDocumentAsync));
		var completionSource = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		var enqueueGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		try
		{
			lock (_syncRoot)
			{
				DocumentQueue queue = GetOrCreateDocumentQueueUnderLock(normalizedFilePath);
				long generation = ++queue._chainGeneration;

				queue._chainTail = RunCompletionNodeAsync(
					enqueueGate.Task,
					[queue._chainTail],
					new ActiveDocumentContext(normalizedFilePath, secondFilePath: null),
					operation,
					completionSource,
					cancellationToken,
					onCompleted: () => CompleteChainNode(normalizedFilePath, queue, generation));
			}
		}
		finally
		{
			enqueueGate.TrySetResult(true);
		}

		return completionSource.Task;
	}

	/// <summary>
	/// Enqueues an exclusive operation for one or two document paths so later work on those paths cannot run until the
	/// exclusive operation has finished.
	/// </summary>
	/// <typeparam name="TResult">The operation result type.</typeparam>
	/// <param name="firstFilePath">The first affected document path.</param>
	/// <param name="secondFilePath">The second affected document path.</param>
	/// <param name="operation">The exclusive operation to enqueue.</param>
	/// <param name="cancellationToken">Cancels the queued operation.</param>
	/// <returns>A task that completes with the queued operation result.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="firstFilePath"/>, <paramref name="secondFilePath"/>, or <paramref name="operation"/> is
	/// <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="firstFilePath"/> or <paramref name="secondFilePath"/> is empty or whitespace-only, or a path is invalid on the current platform.</exception>
	/// <exception cref="InvalidOperationException">
	/// The caller executes inside a queued scheduler operation and must queue the exclusive operation after awaiting it instead.
	/// </exception>
	public Task<TResult> EnqueueExclusivePerDocumentAsync<TResult>(
		string firstFilePath,
		string secondFilePath,
		Func<CancellationToken, Task<TResult>> operation,
		CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(firstFilePath);
		ArgumentException.ThrowIfNullOrWhiteSpace(secondFilePath);
		ArgumentNullException.ThrowIfNull(operation);

		string normalizedFirstFilePath = LanguageServerPaths.NormalizeLocalPath(firstFilePath);
		string normalizedSecondFilePath = LanguageServerPaths.NormalizeLocalPath(secondFilePath);
		bool samePath = LanguageServerPaths.AreLocalPathsEqual(normalizedFirstFilePath, normalizedSecondFilePath);

		EnsureNotInsideSchedulerOperation(nameof(EnqueueExclusivePerDocumentAsync));

		var completionSource = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		var enqueueGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		try
		{
			lock (_syncRoot)
			{
				Task previousGlobalOperation = _queuedGlobalOperation;
				Task[] queuedOperations = CaptureChainTailsUnderLock(normalizedFirstFilePath, normalizedSecondFilePath);

				DocumentQueue firstQueue = GetOrCreateDocumentQueueUnderLock(normalizedFirstFilePath);
				DocumentQueue? secondQueue = samePath ? null : GetOrCreateDocumentQueueUnderLock(normalizedSecondFilePath);

				var previousOperations = new Task[queuedOperations.Length + 1];
				previousOperations[0] = previousGlobalOperation;
				queuedOperations.CopyTo(previousOperations, 1);

				long firstGeneration = ++firstQueue._chainGeneration;
				long secondGeneration = 0;

				Task exclusiveOperation = RunCompletionNodeAsync(
					enqueueGate.Task,
					previousOperations,
					new ActiveDocumentContext(normalizedFirstFilePath, secondQueue is null ? null : normalizedSecondFilePath),
					operation,
					completionSource,
					cancellationToken,
					onCompleted: () =>
					{
						CompleteChainNode(normalizedFirstFilePath, firstQueue, firstGeneration);

						if (secondQueue is not null)
							CompleteChainNode(normalizedSecondFilePath, secondQueue, secondGeneration);
					});

				// The exclusive operation becomes the chain tail of both affected paths, so work queued after it observes it
				// as its predecessor; it also joins the global chain so later global work cannot overtake it.
				firstQueue._chainTail = exclusiveOperation;

				if (secondQueue is not null)
				{
					secondGeneration = ++secondQueue._chainGeneration;
					secondQueue._chainTail = exclusiveOperation;
				}

				_queuedGlobalOperation = exclusiveOperation;
			}
		}
		finally
		{
			enqueueGate.TrySetResult(true);
		}

		return completionSource.Task;
	}

	/// <summary>
	/// Waits for the queued operations of one or two document paths that were queued at the time of the call to finish.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Intended for hosts that need a quiescence point for one or two documents (for example before closing or
	/// renaming them); the client itself does not call this method. Work queued after this call is not awaited.
	/// </para>
	/// <para>
	/// Do not call this method from inside any operation queued with this scheduler; the scheduler rejects that
	/// pattern with an <see cref="InvalidOperationException"/> because awaiting scheduler work from within a queued
	/// operation can deadlock a chain.
	/// </para>
	/// </remarks>
	/// <param name="firstFilePath">The first document path to await.</param>
	/// <param name="secondFilePath">The second document path to await.</param>
	/// <returns>A task that completes when the queued operations have finished.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="firstFilePath"/> or <paramref name="secondFilePath"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="firstFilePath"/> or <paramref name="secondFilePath"/> is empty or whitespace-only, or a path is invalid on the current platform.</exception>
	/// <exception cref="InvalidOperationException">
	/// The caller executes inside a queued scheduler operation, so waiting for scheduler work would deadlock a chain.
	/// </exception>
	public async Task WaitForPerDocumentOperationsAsync(string firstFilePath, string secondFilePath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(firstFilePath);
		ArgumentException.ThrowIfNullOrWhiteSpace(secondFilePath);

		string normalizedFirstFilePath = LanguageServerPaths.NormalizeLocalPath(firstFilePath);
		string normalizedSecondFilePath = LanguageServerPaths.NormalizeLocalPath(secondFilePath);

		EnsureNotInsideSchedulerOperation(nameof(WaitForPerDocumentOperationsAsync));

		Task[] queuedOperations;

		lock (_syncRoot)
			queuedOperations = CaptureChainTailsUnderLock(normalizedFirstFilePath, normalizedSecondFilePath);

		for (int i = 0; i < queuedOperations.Length; i++)
			await WaitForQueuedOperationAsync(queuedOperations[i]).ConfigureAwait(false);
	}
}

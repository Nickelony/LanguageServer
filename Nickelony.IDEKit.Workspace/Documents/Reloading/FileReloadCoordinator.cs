using Nickelony.IDEKit.Core.Pathing;

namespace Nickelony.IDEKit.Workspace.Documents.Reloading;

/// <summary>
/// Queues external file reloads and drives the reload, conflict, and failure flow for tracked
/// workspace documents using a host-neutral prompt result.
/// </summary>
/// <typeparam name="TPromptResult">The host-neutral reload prompt result type.</typeparam>
/// <remarks>
/// This coordinator does not normalize paths or provide synchronization. Callers should serialize
/// access to an instance. A call to <see cref="ProcessQueuedFilesAsync"/> that observes that another
/// pass is already running returns without processing.
/// A pass dequeues the entries that were queued when it started. A path queued while the pass runs
/// (for example from a watcher or host callback, including the path currently being processed) stays
/// queued and is processed by the next pass.
/// </remarks>
public sealed class FileReloadCoordinator<TPromptResult>
{
	private readonly Queue<string> _pendingFileReloads = new();
	private readonly HashSet<string> _queuedPaths;

	/// <summary>
	/// Initializes a new instance of the <see cref="FileReloadCoordinator{TPromptResult}"/> class.
	/// </summary>
	/// <param name="pathComparison">
	/// The comparison used to detect duplicate queued paths. Supply
	/// <see cref="IWorkspaceDocumentReader.PathComparison"/> from the store that processes the reloads;
	/// the value defaults to <see cref="LocalPathComparisonPolicy.ForCurrentPlatform"/>.
	/// </param>
	public FileReloadCoordinator(LocalPathComparisonPolicy? pathComparison = null)
	{
		LocalPathComparisonPolicy comparison = pathComparison ?? LocalPathComparisonPolicy.ForCurrentPlatform;

		_queuedPaths = new HashSet<string>(comparison.Comparer);
	}

	/// <summary>
	/// Gets a value indicating whether a reload pass is currently running on this coordinator.
	/// </summary>
	/// <value><see langword="true"/> for the duration of a <see cref="ProcessQueuedFilesAsync"/> pass, including the queue bookkeeping before and after the callbacks.</value>
	public bool IsRunning { get; private set; }

	/// <summary>
	/// Queues a file for reload. Blank paths and duplicates under the configured path comparison are ignored.
	/// </summary>
	/// <remarks>
	/// This member is not thread-safe; serialize coordinator access as described on the class, for
	/// example by queueing watcher events through a single synchronization point.
	/// </remarks>
	/// <param name="filePath">The file path to reload.</param>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	public void QueueFile(string filePath)
	{
		ArgumentNullException.ThrowIfNull(filePath);

		if (string.IsNullOrWhiteSpace(filePath) || !_queuedPaths.Add(filePath))
			return;

		_pendingFileReloads.Enqueue(filePath);
	}

	/// <summary>
	/// Processes the files queued at the start of the pass, prompting the host only for conflicts.
	/// </summary>
	/// <param name="callbacks">The callback bundle for the pass.</param>
	/// <param name="cancellationToken">
	/// Cancels the pass before the next file is processed and is passed to every callback.
	/// </param>
	/// <remarks>
	/// Reloaded and unchanged results are not reported as failures. The prompt callback receives the
	/// conflict result - including the current snapshot and identity - so a host prompt can present the
	/// document state without re-reading it; it is invoked only when a resolver is supplied, because a
	/// decision without a resolver cannot be applied. A conflict is considered resolved
	/// only when the resolver returns <see cref="WorkspaceDocumentConflictResolutionStatus.ResolvedWithDisk"/>
	/// or <see cref="WorkspaceDocumentConflictResolutionStatus.ResolvedWithLogical"/>; any other
	/// resolution status (including <see langword="null"/>) is reported through
	/// <see cref="FileReloadCallbacks{TPromptResult}.ReportReloadFailure"/> as the original conflict
	/// result, and the resolution result is not surfaced separately. When no resolver is supplied, the
	/// conflict is reported without prompting.
	/// </remarks>
	/// <returns>A task that completes when the pass has finished processing the queued files.</returns>
	/// <exception cref="ArgumentNullException">
	/// <see cref="FileReloadCallbacks{TPromptResult}.PromptReload"/> or
	/// <see cref="FileReloadCallbacks{TPromptResult}.ReloadDocument"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="OperationCanceledException">The pass was canceled between files.</exception>
	public async Task ProcessQueuedFilesAsync(
		FileReloadCallbacks<TPromptResult> callbacks,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(callbacks.PromptReload);
		ArgumentNullException.ThrowIfNull(callbacks.ReloadDocument);

		if (IsRunning)
			return;

		IsRunning = true;

		try
		{
			// The pass takes the entries queued at its start and releases their duplicate markers
			// before processing, so a path queued while the pass runs - including the path currently
			// being processed, for example from a watcher callback fired by the reload itself - is
			// accepted and stays queued for the next pass instead of being dropped as a duplicate.
			string[] pendingSnapshot = _pendingFileReloads.ToArray();
			foreach (string filePath in pendingSnapshot)
				_queuedPaths.Remove(filePath);

			try
			{
				foreach (string filePath in pendingSnapshot)
				{
					cancellationToken.ThrowIfCancellationRequested();
					await ProcessFileAsync(filePath, callbacks, cancellationToken).ConfigureAwait(false);

					// The processed entry is the queue front; removing it as the pass advances keeps the
					// queue holding only work that still needs processing, including a path that was
					// re-queued while it was being processed (its duplicate marker was released for this
					// pass).
					_pendingFileReloads.Dequeue();
				}
			}
			finally
			{
				// Rebuilding the queue and the duplicate markers in one pass restores their invariant - a
				// path is marked exactly while it is queued - and keeps one entry per path: a path that
				// was re-queued while its own processing threw would otherwise appear twice and be
				// processed twice by the next pass.
				string[] remainingPaths = _pendingFileReloads.ToArray();
				_pendingFileReloads.Clear();
				_queuedPaths.Clear();
				foreach (string filePath in remainingPaths)
				{
					if (_queuedPaths.Add(filePath))
						_pendingFileReloads.Enqueue(filePath);
				}
			}
		}
		finally
		{
			IsRunning = false;
		}
	}

	private static async Task ProcessFileAsync(
		string filePath,
		FileReloadCallbacks<TPromptResult> callbacks,
		CancellationToken cancellationToken)
	{
		WorkspaceDocumentReloadResult result = await callbacks
			.ReloadDocument(filePath, cancellationToken)
			.ConfigureAwait(false);
		if (result.Status is WorkspaceDocumentReloadStatus.Reloaded or WorkspaceDocumentReloadStatus.Unchanged)
			return;

		// The prompt exists to feed a resolution decision, so it is skipped when no resolver consumes
		// the answer: prompting for a decision that is then discarded would confuse the user.
		if (result.Status == WorkspaceDocumentReloadStatus.ExternalFileConflict
			&& callbacks.ResolveConflict is { } resolveConflict)
		{
			TPromptResult choice = await callbacks
				.PromptReload(result, cancellationToken)
				.ConfigureAwait(false);
			WorkspaceDocumentConflictResolutionResult? resolution = await resolveConflict(
				result,
				choice,
				cancellationToken).ConfigureAwait(false);
			if (resolution?.Status is WorkspaceDocumentConflictResolutionStatus.ResolvedWithDisk
				or WorkspaceDocumentConflictResolutionStatus.ResolvedWithLogical)
				return;
		}

		if (callbacks.ReportReloadFailure is { } reportReloadFailure)
			await reportReloadFailure(result, cancellationToken).ConfigureAwait(false);
	}
}

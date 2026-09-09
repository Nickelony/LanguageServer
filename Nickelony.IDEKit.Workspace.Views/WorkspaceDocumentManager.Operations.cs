using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views;

public sealed partial class WorkspaceDocumentManager
{
	/// <inheritdoc />
	public Task<WorkspaceDocumentManagerMutationResult> DiscardAsync(WorkspaceDocumentDiscardRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);

		// Discard is a host-intent operation: the host already decided that the logical changes are
		// abandoned, so attached view state does not block it. Every attached view is asked to
		// acknowledge the restored snapshot, which clears its pending state; an acknowledgment failure
		// is retained as unsynchronized view state and reported on the result.
		return RunOperationAsync(async () =>
		{
			WorkspaceDocumentMutationResult result = _store.Discard(request);
			if (result.Snapshot is null
				|| result.Status is not (WorkspaceDocumentMutationStatus.Changed or WorkspaceDocumentMutationStatus.NoChange))
				return new WorkspaceDocumentManagerMutationResult(result, WorkspaceDocumentViewSynchronization.Synchronized, result.Snapshot);

			List<string> failedViewIds = await AcknowledgeDocumentViewsAsync(result).ConfigureAwait(false);
			return new WorkspaceDocumentManagerMutationResult(result, ComposeSynchronization(failedViewIds), result.Snapshot);
		});
	}

	/// <inheritdoc />
	public Task<WorkspaceDocumentManagerMutationResult> ReplaceAsync(WorkspaceDocumentReplaceRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		return ReplaceCoreAsync(request, sourceView: null);
	}

	/// <inheritdoc />
	public Task<WorkspaceDocumentManagerRenameResult> RenameAsync(
		WorkspaceDocumentRenameRequest request,
		CancellationToken cancellationToken = default)
	{
		// Validated before dispatch so a null request throws synchronously instead of surfacing as
		// a faulted task, matching the manager's synchronous members.
		ArgumentNullException.ThrowIfNull(request);

		// Attached view state does not block a rename: the move cannot lose view edits, and the
		// identity change is acknowledged afterwards. Only an acknowledgment failure is reported.
		return RunOperationAsync(async () =>
		{
			WorkspaceDocumentRenameResult result = await _store
				.RenameAsync(request, cancellationToken)
				.ConfigureAwait(false);
			WorkspaceDocumentSnapshot? resultSnapshot = result.Snapshot;
			if (result.Status != WorkspaceDocumentRenameStatus.Renamed || resultSnapshot is null)
				return new WorkspaceDocumentManagerRenameResult(result, WorkspaceDocumentViewSynchronization.Synchronized, resultSnapshot);

			return new WorkspaceDocumentManagerRenameResult(
				result,
				await SynchronizeViewIdentitiesAsync(request.Identity.DocumentId, request.Identity.DocumentKey, resultSnapshot).ConfigureAwait(false),
				resultSnapshot);
		});
	}

	/// <inheritdoc />
	public Task<WorkspaceDocumentManagerSaveAsResult> SaveAsAsync(
		WorkspaceDocumentSaveAsRequest request,
		CancellationToken cancellationToken = default)
	{
		// Validated before dispatch so a null request throws synchronously instead of surfacing as
		// a faulted task, matching the manager's synchronous members.
		ArgumentNullException.ThrowIfNull(request);

		// Attached view state does not block a save-as: the write cannot lose view edits, and the
		// identity change is acknowledged afterwards. Only an acknowledgment failure is reported.
		return RunOperationAsync(async () =>
		{
			WorkspaceDocumentSaveAsResult result = await _store
				.SaveAsAsync(request, cancellationToken)
				.ConfigureAwait(false);
			WorkspaceDocumentSnapshot? resultSnapshot = result.Snapshot;
			if (result.Status != WorkspaceDocumentSaveAsStatus.SavedAs || resultSnapshot is null)
				return new WorkspaceDocumentManagerSaveAsResult(result, WorkspaceDocumentViewSynchronization.Synchronized, resultSnapshot);

			return new WorkspaceDocumentManagerSaveAsResult(
				result,
				await SynchronizeViewIdentitiesAsync(request.Identity.DocumentId, request.Identity.DocumentKey, resultSnapshot).ConfigureAwait(false),
				resultSnapshot);
		});
	}

	/// <inheritdoc />
	public Task<WorkspaceDocumentManagerDeleteResult> DeleteAsync(
		WorkspaceDocumentDeleteRequest request,
		CancellationToken cancellationToken = default)
	{
		// Validated before dispatch so a null request throws synchronously instead of surfacing as
		// a faulted task, matching the manager's synchronous members.
		ArgumentNullException.ThrowIfNull(request);

		return RunOperationAsync(async () =>
		{
			// A view with pending edits or a conflict blocks the delete: the delete destroys the
			// document whose unsaved state the view still holds.
			IReadOnlyList<string>? blockingViewIds = await GetBlockingViewsAsync(request.Identity.DocumentId).ConfigureAwait(false);
			if (blockingViewIds is not null)
				return new WorkspaceDocumentManagerDeleteResult(
					null,
					Blocked(blockingViewIds),
					GetCurrentSnapshot(request.Identity.DocumentId));

			IWorkspaceDocumentView[] views = GetPeers(request.Identity.DocumentId, sourceView: null);
			List<IWorkspaceDocumentDeleteGuardView> enteredGuards = [];
			bool guardsReleased = false;

			// Entered guards are latched on live views, so the finally block releases whatever is
			// still held when an earlier exit skipped the explicit release.
			try
			{
				List<string> failedGuardIds = await EnterDeleteGuardsOnHostAsync(views, enteredGuards).ConfigureAwait(false);
				if (failedGuardIds.Count > 0)
					return new WorkspaceDocumentManagerDeleteResult(
						null,
						Blocked(failedGuardIds),
						GetCurrentSnapshot(request.Identity.DocumentId));

				WorkspaceDocumentDeleteResult result = await _store
					.DeleteAsync(request, cancellationToken)
					.ConfigureAwait(false);
				if (result.Status != WorkspaceDocumentDeleteStatus.Deleted)
				{
					await ReleaseDeleteGuardsOnHostAsync(enteredGuards).ConfigureAwait(false);
					guardsReleased = true;
					return new WorkspaceDocumentManagerDeleteResult(result, WorkspaceDocumentViewSynchronization.Synchronized, result.Snapshot);
				}

				List<string> failedViewIds = [];
				await _dispatchViewAction(() =>
				{
					// Guards are released before the views close: a closed or unregistered view that fails
					// the release would otherwise be reported as unsynchronized although it is already gone.
					// Guard-release failures are only reported through the result's view ids.
					ReleaseDeleteGuards(enteredGuards, failedViewIds, recordViewFailure: false);

					foreach (IWorkspaceDocumentView view in views)
						CloseDeletedView(view, failedViewIds);

					CloseLateAttachedViews(request.Identity.DocumentId, request.Identity.DocumentKey, failedViewIds);
				}).ConfigureAwait(false);
				guardsReleased = true;

				return new WorkspaceDocumentManagerDeleteResult(
					result,
					ComposeSynchronization(failedViewIds),
					result.Snapshot);
			}
			finally
			{
				if (!guardsReleased && enteredGuards.Count > 0)
					await TryReleaseDeleteGuardsOnHostAsync(enteredGuards).ConfigureAwait(false);
			}
		});
	}

	/// <inheritdoc />
	public Task<WorkspaceDocumentManagerDirectoryRenameResult> RenameDirectoryAsync(
		WorkspaceDocumentDirectoryRenameRequest request,
		CancellationToken cancellationToken = default)
	{
		// Validated before dispatch so a null request throws synchronously instead of surfacing as
		// a faulted task, matching the manager's synchronous members.
		ArgumentNullException.ThrowIfNull(request);

		// Attached view state does not block a directory rename for the same reason it does not block
		// a file rename: the move cannot lose view edits, and the identity changes are acknowledged
		// afterwards.
		return RunOperationAsync(async () =>
		{
			WorkspaceDocumentSnapshot[] sourceSnapshots = _store
				.GetSnapshotsUnderDirectory(request.SourceDirectoryPath)
				.ToArray();
			List<ViewBinding> bindings = GetViewBindings(sourceSnapshots);

			List<IWorkspaceDocumentDeleteGuardView> enteredGuards = [];
			bool guardsReleased = false;

			// Entered guards are latched on live views, so the finally block releases whatever is
			// still held when an earlier exit skipped the explicit release.
			try
			{
				List<string> failedGuardIds = await EnterDeleteGuardsOnHostAsync(
					bindings.Select(binding => binding.View),
					enteredGuards)
					.ConfigureAwait(false);
				if (failedGuardIds.Count > 0)
					return new WorkspaceDocumentManagerDirectoryRenameResult(null, Blocked(failedGuardIds), sourceSnapshots);

				WorkspaceDocumentDirectoryRenameResult result = await _store
					.RenameDirectoryAsync(request, cancellationToken)
					.ConfigureAwait(false);
				if (result.Status != WorkspaceDocumentDirectoryRenameStatus.Renamed)
				{
					await ReleaseDeleteGuardsOnHostAsync(enteredGuards).ConfigureAwait(false);
					guardsReleased = true;
					return new WorkspaceDocumentManagerDirectoryRenameResult(result, WorkspaceDocumentViewSynchronization.Synchronized, result.Snapshots);
				}

				List<string> failedViewIds = [];
				await _dispatchViewAction(() =>
				{
					// The result snapshots are indexed once; a per-binding linear search would make the
					// tail quadratic in the number of moved documents.
					Dictionary<WorkspaceDocumentKey, WorkspaceDocumentSnapshot> snapshotsByKey = [];
					foreach (WorkspaceDocumentSnapshot candidate in result.Snapshots)
						snapshotsByKey[candidate.DocumentKey] = candidate;

					HashSet<IWorkspaceDocumentView> capturedViews = new(ReferenceEqualityComparer.Instance);
					foreach (ViewBinding binding in bindings)
					{
						capturedViews.Add(binding.View);
						if (!snapshotsByKey.TryGetValue(binding.Snapshot.DocumentKey, out WorkspaceDocumentSnapshot? snapshot))
						{
							// The result did not carry the view's snapshot, so its binding is stale: record it
							// as unsynchronized in addition to reporting it, matching the acknowledgment-failure
							// branch below.
							RecordViewFailure(binding.View);
							failedViewIds.Add(ReadViewId(binding.View));
							continue;
						}

						WorkspaceDocumentViewIdentityResult acknowledgment = AcknowledgeIdentity(
							binding.View,
							new WorkspaceDocumentIdentityChange(
								binding.Snapshot.DocumentKey,
								binding.Snapshot.DocumentId,
								snapshot));
						if (acknowledgment.Status == WorkspaceDocumentViewIdentityStatus.Updated)
							UpdateViewIdentity(binding.View, binding.Snapshot.DocumentId, snapshot);
						else
						{
							RecordViewFailure(binding.View);
							failedViewIds.Add(ReadViewId(binding.View));
						}
					}

					// A view that attached while the store call was in flight is not in the captured
					// binding list. It is matched to its moved document by the document key it reports and
					// acknowledged the same way, so it does not stay bound to the vacated id.
					foreach (IWorkspaceDocumentView view in GetRegisteredViews())
					{
						if (capturedViews.Contains(view)
							|| !TryGetViewDocumentKey(view, out WorkspaceDocumentKey? viewDocumentKey)
							|| !snapshotsByKey.TryGetValue(viewDocumentKey.Value, out WorkspaceDocumentSnapshot? lateSnapshot))
							continue;

						string? lateDocumentId = GetRegisteredDocumentId(view);
						if (lateDocumentId is null)
							continue;

						WorkspaceDocumentViewIdentityResult lateAcknowledgment = AcknowledgeIdentity(
							view,
							new WorkspaceDocumentIdentityChange(viewDocumentKey.Value, lateDocumentId, lateSnapshot));
						if (lateAcknowledgment.Status == WorkspaceDocumentViewIdentityStatus.Updated)
							UpdateViewIdentity(view, lateDocumentId, lateSnapshot);
						else
						{
							RecordViewFailure(view);
							failedViewIds.Add(ReadViewId(view));
						}
					}

					ReleaseDeleteGuards(enteredGuards);
				}).ConfigureAwait(false);
				guardsReleased = true;

				return new WorkspaceDocumentManagerDirectoryRenameResult(
					result,
					ComposeSynchronization(failedViewIds),
					result.Snapshots);
			}
			finally
			{
				if (!guardsReleased && enteredGuards.Count > 0)
					await TryReleaseDeleteGuardsOnHostAsync(enteredGuards).ConfigureAwait(false);
			}
		});
	}

	/// <inheritdoc />
	public Task<WorkspaceDocumentManagerDirectoryDeleteResult> DeleteDirectoryAsync(
		WorkspaceDocumentDirectoryDeleteRequest request,
		CancellationToken cancellationToken = default)
	{
		// Validated before dispatch so a null request throws synchronously instead of surfacing as
		// a faulted task, matching the manager's synchronous members.
		ArgumentNullException.ThrowIfNull(request);

		return RunOperationAsync(async () =>
		{
			WorkspaceDocumentSnapshot[] sourceSnapshots = _store
				.GetSnapshotsUnderDirectory(request.DirectoryPath)
				.ToArray();
			List<ViewBinding> bindings = GetViewBindings(sourceSnapshots);

			// A view with pending edits or a conflict blocks the delete: the delete destroys the
			// documents whose unsaved state the view still holds.
			IReadOnlyList<string>? blockingViewIds = await GetBlockingViewsAsync(bindings.Select(binding => binding.View)).ConfigureAwait(false);
			if (blockingViewIds is not null)
				return new WorkspaceDocumentManagerDirectoryDeleteResult(null, Blocked(blockingViewIds), sourceSnapshots);

			List<IWorkspaceDocumentDeleteGuardView> enteredGuards = [];
			bool guardsReleased = false;

			// Entered guards are latched on live views, so the finally block releases whatever is
			// still held when an earlier exit skipped the explicit release.
			try
			{
				List<string> failedGuardIds = await EnterDeleteGuardsOnHostAsync(
					bindings.Select(binding => binding.View),
					enteredGuards)
					.ConfigureAwait(false);
				if (failedGuardIds.Count > 0)
					return new WorkspaceDocumentManagerDirectoryDeleteResult(null, Blocked(failedGuardIds), sourceSnapshots);

				WorkspaceDocumentDirectoryDeleteResult result = await _store
					.DeleteDirectoryAsync(request, cancellationToken)
					.ConfigureAwait(false);
				if (result.Status != WorkspaceDocumentDirectoryDeleteStatus.Deleted)
				{
					await ReleaseDeleteGuardsOnHostAsync(enteredGuards).ConfigureAwait(false);
					guardsReleased = true;
					return new WorkspaceDocumentManagerDirectoryDeleteResult(result, WorkspaceDocumentViewSynchronization.Synchronized, result.Snapshots);
				}

				List<string> failedViewIds = [];
				await _dispatchViewAction(() =>
				{
					// Guards are released before the views close: a closed or unregistered view that fails
					// the release would otherwise be reported as unsynchronized although it is already gone.
					// The views are already detached from the manager, so guard-release failures are only
					// reported through the result's view ids.
					ReleaseDeleteGuards(enteredGuards, failedViewIds, recordViewFailure: false);

					foreach (ViewBinding binding in bindings)
						CloseDeletedView(binding.View, failedViewIds);

					// The result snapshots cover every removed descendant, including descendants that
					// attached after this operation enumerated them; views reopened at the same paths
					// carry new keys and are left alone.
					foreach (WorkspaceDocumentSnapshot removedSnapshot in result.Snapshots)
						CloseLateAttachedViews(removedSnapshot.DocumentId, removedSnapshot.DocumentKey, failedViewIds);
				}).ConfigureAwait(false);
				guardsReleased = true;

				return new WorkspaceDocumentManagerDirectoryDeleteResult(
					result,
					ComposeSynchronization(failedViewIds),
					result.Snapshots);
			}
			finally
			{
				if (!guardsReleased && enteredGuards.Count > 0)
					await TryReleaseDeleteGuardsOnHostAsync(enteredGuards).ConfigureAwait(false);
			}
		});
	}

	/// <inheritdoc />
	public Task<WorkspaceDocumentManagerCommitResult> CommitAsync(
		WorkspaceDocumentCommitRequest request,
		CancellationToken cancellationToken = default)
	{
		// Validated before dispatch so a null request throws synchronously instead of surfacing as
		// a faulted task, matching the manager's synchronous members.
		ArgumentNullException.ThrowIfNull(request);

		return RunOperationAsync(async () =>
		{
			// A view whose only outstanding state is a prior synchronization failure does not block the
			// commit: the commit is retried so the write can proceed (a clean document makes it a no-op)
			// and the post-commit refresh retries the failed synchronization. Pending edits, conflicts,
			// and a state probe that throws still block the commit.
			IReadOnlyList<string>? blockingViewIds = await GetBlockingViewsAsync(request.Identity.DocumentId, includeUnsynchronizedState: false).ConfigureAwait(false);
			if (blockingViewIds is not null)
				return new WorkspaceDocumentManagerCommitResult(
					null,
					Blocked(blockingViewIds),
					GetCurrentSnapshot(request.Identity.DocumentId));

			WorkspaceDocumentCommitResult result = await _store
				.CommitAsync(request, cancellationToken)
				.ConfigureAwait(false);
			if (result.Status != WorkspaceDocumentCommitStatus.Committed)
				return new WorkspaceDocumentManagerCommitResult(result, WorkspaceDocumentViewSynchronization.Synchronized, result.Snapshot);

			IReadOnlyList<string> unsynchronizedViewIds = result.Snapshot is null
				? []
				: await RefreshViewsAndCollectUnsynchronizedAsync(request.Identity.DocumentId, result.Snapshot).ConfigureAwait(false);

			return new WorkspaceDocumentManagerCommitResult(
				result,
				ComposeSynchronization(unsynchronizedViewIds),
				result.Snapshot);
		});
	}

	/// <inheritdoc />
	public Task<WorkspaceDocumentManagerReloadResult> ReloadAsync(
		WorkspaceDocumentReloadRequest request,
		CancellationToken cancellationToken = default)
	{
		// Validated before dispatch so a null request throws synchronously instead of surfacing as
		// a faulted task, matching the manager's synchronous members.
		ArgumentNullException.ThrowIfNull(request);

		return RunOperationAsync(async () =>
		{
			// A view with pending edits or a conflict would lose that state when the document content is
			// replaced from disk, so it blocks the reload instead of being refreshed over.
			IReadOnlyList<string>? blockingViewIds = await GetBlockingViewsAsync(request.Identity.DocumentId).ConfigureAwait(false);
			if (blockingViewIds is not null)
				return new WorkspaceDocumentManagerReloadResult(
					null,
					Blocked(blockingViewIds),
					GetCurrentSnapshot(request.Identity.DocumentId));

			WorkspaceDocumentReloadResult result = await _store
				.ReloadAsync(request, cancellationToken)
				.ConfigureAwait(false);

			if (result.Status == WorkspaceDocumentReloadStatus.Reloaded && result.Snapshot is not null)
			{
				IReadOnlyList<string> unsynchronizedViewIds = await RefreshViewsAndCollectUnsynchronizedAsync(request.Identity.DocumentId, result.Snapshot).ConfigureAwait(false);
				if (unsynchronizedViewIds.Count > 0)
					return new WorkspaceDocumentManagerReloadResult(result, Unsynchronized(unsynchronizedViewIds), result.Snapshot);
			}

			return new WorkspaceDocumentManagerReloadResult(result, WorkspaceDocumentViewSynchronization.Synchronized, result.Snapshot);
		});
	}

	/// <inheritdoc />
	public Task<WorkspaceDocumentManagerConflictResolutionResult> ResolveExternalConflictAsync(
		WorkspaceDocumentConflictResolutionRequest request,
		CancellationToken cancellationToken = default)
	{
		// Validated before dispatch so a null request throws synchronously instead of surfacing as
		// a faulted task, matching the manager's synchronous members.
		ArgumentNullException.ThrowIfNull(request);

		return RunOperationAsync(async () =>
		{
			IReadOnlyList<string>? blockingViewIds = await GetBlockingViewsAsync(request.Identity.DocumentId).ConfigureAwait(false);
			if (blockingViewIds is not null)
				return new WorkspaceDocumentManagerConflictResolutionResult(
					null,
					Blocked(blockingViewIds),
					GetCurrentSnapshot(request.Identity.DocumentId));

			WorkspaceDocumentConflictResolutionResult result = await _store
				.ResolveExternalConflictAsync(request, cancellationToken)
				.ConfigureAwait(false);

			if ((result.Status == WorkspaceDocumentConflictResolutionStatus.ResolvedWithDisk
				|| result.Status == WorkspaceDocumentConflictResolutionStatus.ResolvedWithLogical)
				&& result.Snapshot is not null)
			{
				IReadOnlyList<string> unsynchronizedViewIds = await RefreshViewsAndCollectUnsynchronizedAsync(request.Identity.DocumentId, result.Snapshot).ConfigureAwait(false);
				if (unsynchronizedViewIds.Count > 0)
					return new WorkspaceDocumentManagerConflictResolutionResult(result, Unsynchronized(unsynchronizedViewIds), result.Snapshot);
			}

			return new WorkspaceDocumentManagerConflictResolutionResult(result, WorkspaceDocumentViewSynchronization.Synchronized, result.Snapshot);
		});
	}

	private Task<WorkspaceDocumentManagerMutationResult> ReplaceCoreAsync(
		WorkspaceDocumentReplaceRequest request,
		IWorkspaceDocumentView? sourceView)
		=> RunOperationAsync(async () =>
		{
			if (sourceView is not null && !IsRegistered(sourceView))
				throw new InvalidOperationException("An unregistered view published a document replacement.");

			// The store mutation runs on the caller's context, matching the other manager store
			// calls; the single dispatch action below carries the view acknowledgment and peer refreshes
			// so no view access runs outside the dispatch delegate.
			WorkspaceDocumentMutationResult result = _store.Replace(request);

			List<string> failedViewIds = [];
			await _dispatchViewAction(() =>
			{
				if (sourceView is not null)
				{
					WorkspaceDocumentViewRefreshResult acknowledgment = Acknowledge(sourceView, result);
					RecordViewResult(sourceView, acknowledgment, result.Snapshot);
					if (acknowledgment.Status != WorkspaceDocumentViewRefreshStatus.Refreshed)
						failedViewIds.Add(ReadViewId(sourceView));
				}

				if (result.Status != WorkspaceDocumentMutationStatus.Changed || result.Snapshot is null)
					return;

				foreach (IWorkspaceDocumentView view in GetPeers(result.Snapshot.DocumentId, sourceView))
				{
					// A peer whose synchronization failed earlier is retried even when its recorded version
					// is current: a successful refresh clears the unsynchronized state, matching the commit,
					// reload, and conflict-resolution paths.
					if (!IsViewOlder(view, result.Snapshot.Version) && !IsViewMarkedUnsynchronized(view))
						continue;

					WorkspaceDocumentViewRefreshResult refresh = Refresh(view, result.Snapshot);
					RecordViewResult(view, refresh, result.Snapshot);
					if (refresh.Status != WorkspaceDocumentViewRefreshStatus.Refreshed)
						failedViewIds.Add(ReadViewId(view));
				}
			}).ConfigureAwait(false);

			return new WorkspaceDocumentManagerMutationResult(
				result,
				ComposeSynchronization(failedViewIds),
				result.Snapshot);
		});

	private void OnApplyRequested(
		object? sender,
		WorkspaceDocumentViewApplyRequestedEventArgs eventArgs)
	{
		if (sender is not IWorkspaceDocumentView sourceView)
			return;

		_ = HandleApplyRequestedAsync(sourceView, eventArgs);
	}

	// An event-driven apply has no result channel back to the publishing view: the failure is recorded
	// as unsynchronized view state, which blocks later disk operations until a successful refresh
	// clears it. Hosts that need the mutation result call ReplaceAsync directly. The replacement
	// registers a manager operation, so a stop still waits for the fire-and-forget call.
	private async Task HandleApplyRequestedAsync(
		IWorkspaceDocumentView sourceView,
		WorkspaceDocumentViewApplyRequestedEventArgs eventArgs)
	{
		try
		{
			if (!IsRegistered(sourceView))
				return;

			await ReplaceCoreAsync(eventArgs.Request, sourceView).ConfigureAwait(false);
		}
		catch (Exception)
		{
			RecordViewFailure(sourceView);
		}
	}

	private WorkspaceDocumentSnapshot? GetCurrentSnapshot(string documentId)
		=> _store.TryGetSnapshot(documentId, out WorkspaceDocumentSnapshot? snapshot) ? snapshot : null;
}

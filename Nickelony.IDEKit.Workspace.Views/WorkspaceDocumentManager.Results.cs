using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views;

public sealed partial class WorkspaceDocumentManager
{
	private static WorkspaceDocumentViewRefreshResult Acknowledge(
		IWorkspaceDocumentView view,
		WorkspaceDocumentMutationResult result)
	{
		try
		{
			return view.AcknowledgeApply(result);
		}
		catch (Exception exception)
		{
			return new WorkspaceDocumentViewRefreshResult(
				WorkspaceDocumentViewRefreshStatus.UpdateFailed,
				new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.ViewAcknowledgeFailed, exception.Message, exception));
		}
	}

	private static WorkspaceDocumentViewRefreshResult Refresh(
		IWorkspaceDocumentView view,
		WorkspaceDocumentSnapshot snapshot)
	{
		try
		{
			return view.Refresh(snapshot);
		}
		catch (Exception exception)
		{
			return new WorkspaceDocumentViewRefreshResult(
				WorkspaceDocumentViewRefreshStatus.UpdateFailed,
				new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.ViewRefreshFailed, exception.Message, exception));
		}
	}

	// A view blocks a store operation when it is marked unsynchronized, still has pending edits or a
	// conflict, or throws while reporting that state. A commit ignores the unsynchronized state so a
	// view whose only outstanding problem is a prior synchronization failure can be retried. The
	// check must run through the dispatch delegate. The returned ids are not normalized; the callers
	// normalize them when they compose a result.
	private string[] GetBlockingViewIds(IEnumerable<IWorkspaceDocumentView> views, bool includeUnsynchronizedState)
	{
		List<string> blockingIds = [];
		foreach (IWorkspaceDocumentView view in views)
		{
			bool blocks = false;
			if (includeUnsynchronizedState)
			{
				lock (_stateLock)
					blocks = _unsynchronizedViews.Contains(view);
			}

			try
			{
				blocks |= view.HasPendingEdits || view.HasConflict;
			}
			catch
			{
				RecordViewFailure(view);
				blocks = true;
			}

			if (blocks)
				blockingIds.Add(ReadViewId(view));
		}

		return [.. blockingIds];
	}

	// Runs through the dispatch delegate: returns the ids of the views that block a store
	// operation, or null when none block.
	private Task<IReadOnlyList<string>?> GetBlockingViewsAsync(
		string documentId,
		bool includeUnsynchronizedState = true)
		=> GetBlockingViewsAsync(GetPeers(documentId, sourceView: null), includeUnsynchronizedState);

	private async Task<IReadOnlyList<string>?> GetBlockingViewsAsync(
		IEnumerable<IWorkspaceDocumentView> views,
		bool includeUnsynchronizedState = true)
	{
		IReadOnlyList<string>? blockingViewIds = null;
		await _dispatchViewAction(
			() => blockingViewIds = GetBlockingViewIds(views, includeUnsynchronizedState))
			.ConfigureAwait(false);
		return blockingViewIds is { Count: > 0 } ? blockingViewIds : null;
	}

	private void RefreshDocumentViews(WorkspaceDocumentSnapshot snapshot)
	{
		foreach (IWorkspaceDocumentView view in GetPeers(snapshot.DocumentId, sourceView: null))
		{
			// A view whose synchronization failed earlier is retried even when its recorded version is
			// current: a successful refresh clears the unsynchronized state.
			if (!IsViewOlder(view, snapshot.Version) && !IsViewMarkedUnsynchronized(view))
				continue;

			WorkspaceDocumentViewRefreshResult refresh = Refresh(view, snapshot);
			RecordViewResult(view, refresh, snapshot);
		}
	}

	// Refreshes the attached views from the snapshot through the dispatch delegate and returns the
	// ids that remained unsynchronized; an empty list means every attached view synchronized.
	private async Task<IReadOnlyList<string>> RefreshViewsAndCollectUnsynchronizedAsync(
		string documentId,
		WorkspaceDocumentSnapshot snapshot)
	{
		IReadOnlyList<string>? unsynchronizedViewIds = null;
		await _dispatchViewAction(() =>
		{
			RefreshDocumentViews(snapshot);
			unsynchronizedViewIds = GetBlockingViewIds(
				GetPeers(documentId, sourceView: null),
				includeUnsynchronizedState: true);
		}).ConfigureAwait(false);

		return unsynchronizedViewIds ?? [];
	}

	// Asks every peer view attached to the document to acknowledge the mutation through the dispatch
	// delegate and returns the ids of the views that did not refresh. A failed acknowledgment is also
	// recorded as unsynchronized view state.
	private async Task<List<string>> AcknowledgeDocumentViewsAsync(WorkspaceDocumentMutationResult result)
	{
		List<string> failedViewIds = [];
		if (result.Snapshot is null)
			return failedViewIds;

		await _dispatchViewAction(() =>
		{
			foreach (IWorkspaceDocumentView view in GetPeers(result.RequestedIdentity.DocumentId, sourceView: null))
			{
				WorkspaceDocumentViewRefreshResult refresh = Acknowledge(view, result);
				RecordViewResult(view, refresh, result.Snapshot);
				if (refresh.Status != WorkspaceDocumentViewRefreshStatus.Refreshed)
					failedViewIds.Add(ReadViewId(view));
			}
		}).ConfigureAwait(false);

		return failedViewIds;
	}

	// Asks every peer view attached to the document to acknowledge an identity change and returns
	// the composed synchronization outcome for the operation result.
	private async Task<WorkspaceDocumentViewSynchronization> SynchronizeViewIdentitiesAsync(
		string documentId,
		WorkspaceDocumentKey expectedDocumentKey,
		WorkspaceDocumentSnapshot snapshot)
		=> ComposeSynchronization(
			await AcknowledgeIdentityChangesAsync(documentId, expectedDocumentKey, snapshot).ConfigureAwait(false));

	// Asks every peer view attached to the document to acknowledge the identity change through the
	// dispatch delegate and updates the recorded binding for each view that acknowledged. Returns
	// the ids of the views that failed to acknowledge.
	private async Task<List<string>> AcknowledgeIdentityChangesAsync(
		string documentId,
		WorkspaceDocumentKey expectedDocumentKey,
		WorkspaceDocumentSnapshot snapshot)
	{
		List<string> failedViewIds = [];
		await _dispatchViewAction(() =>
		{
			foreach (IWorkspaceDocumentView view in GetPeers(documentId, sourceView: null))
			{
				WorkspaceDocumentViewIdentityResult acknowledgment = AcknowledgeIdentity(
					view,
					new WorkspaceDocumentIdentityChange(expectedDocumentKey, documentId, snapshot));
				if (acknowledgment.Status == WorkspaceDocumentViewIdentityStatus.Updated)
					UpdateViewIdentity(view, documentId, snapshot);
				else
				{
					RecordViewFailure(view);
					failedViewIds.Add(ReadViewId(view));
				}
			}
		}).ConfigureAwait(false);

		return failedViewIds;
	}

	// Composes the view-synchronization outcome for a completed operation from the ids of the views
	// that failed to synchronize.
	private static WorkspaceDocumentViewSynchronization ComposeSynchronization(IReadOnlyList<string> failedViewIds)
		=> failedViewIds.Count == 0
			? WorkspaceDocumentViewSynchronization.Synchronized
			: Unsynchronized(failedViewIds);

	// View-id lists in results are normalized: de-duplicated with an ordinal comparison and sorted
	// ordinally, so the reported order does not depend on registration or enumeration order.
	private static string[] NormalizeViewIds(IEnumerable<string> viewIds)
		=> [.. viewIds.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal)];

	private static WorkspaceDocumentViewSynchronization Blocked(IEnumerable<string> viewIds)
		=> new(WorkspaceDocumentViewSynchronizationStatus.Blocked, NormalizeViewIds(viewIds));

	private static WorkspaceDocumentViewSynchronization Unsynchronized(IEnumerable<string> viewIds)
		=> new(WorkspaceDocumentViewSynchronizationStatus.Unsynchronized, NormalizeViewIds(viewIds));
}

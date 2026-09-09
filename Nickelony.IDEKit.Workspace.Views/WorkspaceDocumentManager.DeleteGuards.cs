using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views;

public sealed partial class WorkspaceDocumentManager
{
	private void ReleaseDeleteGuards(
		IEnumerable<IWorkspaceDocumentDeleteGuardView> views,
		List<string>? failedViewIds = null,
		bool recordViewFailure = true)
	{
		foreach (IWorkspaceDocumentDeleteGuardView view in views)
		{
			try
			{
				WorkspaceDocumentViewDeleteGuardResult guard = view.ReleaseDeleteGuard();
				if (guard.Status == WorkspaceDocumentViewDeleteGuardStatus.Failed)
					RecordGuardReleaseFailure(view, failedViewIds, recordViewFailure);
			}
			catch
			{
				RecordGuardReleaseFailure(view, failedViewIds, recordViewFailure);
			}
		}
	}

	private void RecordGuardReleaseFailure(
		IWorkspaceDocumentView view,
		List<string>? failedViewIds,
		bool recordViewFailure)
	{
		if (recordViewFailure)
			RecordViewFailure(view);

		failedViewIds?.Add(ReadViewId(view));
	}

	// Closes and unregisters a view whose document was removed. Runs inside a host action; a view
	// whose close or unregistration fails is still detached as far as possible and reported in
	// failedViewIds. Unregistration happens first so a view that raises ApplyRequested from Close
	// cannot re-enter a replacement through a handler that is about to be removed.
	private void CloseDeletedView(IWorkspaceDocumentView view, List<string> failedViewIds)
	{
		try
		{
			UnregisterOpenView(view);
		}
		catch
		{
			failedViewIds.Add(ReadViewId(view));
		}

		try
		{
			view.Close();
		}
		catch
		{
			failedViewIds.Add(ReadViewId(view));
		}
	}

	// Best-effort guard release for exits that skip the explicit release path. The entered guards were
	// latched on live views, so leaving them set would block every later synchronization; a failure
	// here cannot be reported through a result the caller never receives, so it is recorded as
	// unsynchronized view state instead.
	private async Task TryReleaseDeleteGuardsOnHostAsync(List<IWorkspaceDocumentDeleteGuardView> views)
	{
		try
		{
			await _dispatchViewAction(() => ReleaseDeleteGuards(views)).ConfigureAwait(false);
		}
		catch
		{
			foreach (IWorkspaceDocumentView view in views)
				RecordViewFailure(view);
		}
	}

	// Enters the delete guard on every view through the dispatch delegate and returns the ids of the
	// views that rejected or threw. The entered guards are latched on live views and the caller owns
	// them: every exit path that skips the explicit release must release them from a finally block,
	// or the views stay guarded for every later synchronization. When any view fails, the guards that
	// were already entered are rolled back by EnterDeleteGuards itself.
	private async Task<List<string>> EnterDeleteGuardsOnHostAsync(
		IEnumerable<IWorkspaceDocumentView> views,
		List<IWorkspaceDocumentDeleteGuardView> enteredGuards)
	{
		List<string> failedGuardIds = [];
		await _dispatchViewAction(
			() => EnterDeleteGuards(views, enteredGuards, failedGuardIds))
			.ConfigureAwait(false);
		return failedGuardIds;
	}

	// Releases the entered guards through the dispatch delegate after a failed or abandoned
	// operation. Guard failures are recorded as unsynchronized view state by ReleaseDeleteGuards.
	private Task ReleaseDeleteGuardsOnHostAsync(List<IWorkspaceDocumentDeleteGuardView> enteredGuards)
		=> _dispatchViewAction(() => ReleaseDeleteGuards(enteredGuards));

	// Closes views that attached while a store operation was in flight and are still bound to the
	// removed instance. A view bound to a document reopened at the same path carries a new document
	// key and is left alone.
	private void CloseLateAttachedViews(
		string documentId,
		WorkspaceDocumentKey deletedDocumentKey,
		List<string> failedViewIds)
	{
		foreach (IWorkspaceDocumentView view in GetPeers(documentId, sourceView: null))
		{
			if (TargetsDeletedInstance(view, deletedDocumentKey))
				CloseDeletedView(view, failedViewIds);
		}
	}

	// A view that cannot report its identity is treated as bound to the removed instance; a view
	// bound to a document reopened at the same path reports the new key and is left alone.
	private static bool TargetsDeletedInstance(IWorkspaceDocumentView view, WorkspaceDocumentKey deletedDocumentKey)
	{
		try
		{
			return view.DocumentKey is not { } documentKey || documentKey == deletedDocumentKey;
		}
		catch
		{
			return true;
		}
	}

	// Enters the delete guard on each view through the dispatch delegate and rolls back the guards
	// that were already entered when any view rejects the guard. Populates enteredGuards with the views that entered
	// successfully and failedGuardIds with the ids that rejected or threw. The rollback uses
	// ReleaseDeleteGuards so a throwing view records an unsynchronized failure instead of faulting
	// the whole operation.
	private void EnterDeleteGuards(
		IEnumerable<IWorkspaceDocumentView> views,
		List<IWorkspaceDocumentDeleteGuardView> enteredGuards,
		List<string> failedGuardIds)
	{
		foreach (IWorkspaceDocumentView view in views)
		{
			// A view that does not implement the guard capability is skipped: it cannot hold state that
			// a delete or directory move would lose, so there is nothing to guard.
			if (view is not IWorkspaceDocumentDeleteGuardView guardView)
				continue;

			try
			{
				WorkspaceDocumentViewDeleteGuardResult result = guardView.ApplyDeleteGuard();
				if (result.Status == WorkspaceDocumentViewDeleteGuardStatus.Applied)
					enteredGuards.Add(guardView);
				else
					failedGuardIds.Add(ReadViewId(view));
			}
			catch
			{
				failedGuardIds.Add(ReadViewId(view));
			}
		}

		if (failedGuardIds.Count > 0)
		{
			ReleaseDeleteGuards(enteredGuards);
			enteredGuards.Clear();
		}
	}
}

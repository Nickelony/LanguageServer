using Nickelony.IDEKit.Workspace.Documents;
using System.Diagnostics.CodeAnalysis;

namespace Nickelony.IDEKit.Workspace.Views;

public sealed partial class WorkspaceDocumentManager
{
	private static WorkspaceDocumentViewIdentityResult AcknowledgeIdentity(
		IWorkspaceDocumentView view,
		WorkspaceDocumentIdentityChange change)
	{
		try
		{
			return view.AcknowledgeIdentity(change);
		}
		catch (Exception exception)
		{
			return new WorkspaceDocumentViewIdentityResult(
				WorkspaceDocumentViewIdentityStatus.Failed,
				new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.ViewIdentityUpdateFailed, exception.Message, exception));
		}
	}

	private void UpdateViewIdentity(
		IWorkspaceDocumentView view,
		string oldDocumentId,
		WorkspaceDocumentSnapshot snapshot)
	{
		lock (_stateLock)
		{
			if (!_views.ContainsKey(view))
				return;

			_views[view] = snapshot.DocumentId;
			_viewVersions[view] = snapshot.Version;
			if (_viewsByDocument.TryGetValue(oldDocumentId, out List<IWorkspaceDocumentView>? oldViews))
			{
				oldViews.Remove(view);
				if (oldViews.Count == 0)
					_viewsByDocument.Remove(oldDocumentId);
			}

			if (!_viewsByDocument.TryGetValue(snapshot.DocumentId, out List<IWorkspaceDocumentView>? newViews))
			{
				newViews = [];
				_viewsByDocument.Add(snapshot.DocumentId, newViews);
			}

			newViews.Add(view);
		}
	}

	// Reads a view id for diagnostics without letting a throwing or host-affine accessor escape a
	// teardown loop: a view that cannot report its id is reported with a fixed marker, and the
	// remaining views are still released.
	private const string UnidentifiedViewId = "<unidentified view>";

	private static string ReadViewId(IWorkspaceDocumentView view)
	{
		try
		{
			string viewId = view.ViewId;
			return string.IsNullOrEmpty(viewId) ? UnidentifiedViewId : viewId;
		}
		catch
		{
			return UnidentifiedViewId;
		}
	}

	/// <inheritdoc />
	public void UnregisterOpenView(IWorkspaceDocumentView view)
	{
		ArgumentNullException.ThrowIfNull(view);
		bool wasRegistered;

		lock (_stateLock)
			wasRegistered = RemoveViewRegistrationLocked(view);

		if (wasRegistered)
			view.ApplyRequested -= OnApplyRequested;
	}

	// Removes every registration entry for a view under the state lock and reports whether the view
	// was registered. The event subscription is not touched here: event accessors are host code and
	// must not run while the state lock is held.
	private bool RemoveViewRegistrationLocked(IWorkspaceDocumentView view)
	{
		if (!_views.TryGetValue(view, out string? documentId))
			return false;

		_views.Remove(view);
		if (_viewIds.Remove(view, out string? viewId))
			_viewsByViewId.Remove(viewId);

		_viewVersions.Remove(view);
		_unsynchronizedViews.Remove(view);

		if (_viewsByDocument.TryGetValue(documentId, out List<IWorkspaceDocumentView>? documentViews))
		{
			documentViews.Remove(view);
			if (documentViews.Count == 0)
				_viewsByDocument.Remove(documentId);
		}

		return true;
	}

	// Determines whether the view may be attached. The view-id read and the state probe both run
	// through the dispatch delegate; duplicate detection uses the view ids captured at registration,
	// so no view member is read while the state lock is held. The state lock is never held across an
	// await: taking it inside dispatched code is safe only because no member awaits a dispatcher
	// action while holding it.
	private async Task<ViewValidation> ValidateViewAsync(IWorkspaceDocumentView view)
	{
		ViewValidation validation = ViewValidation.Valid;
		await _dispatchViewAction(() =>
		{
			string viewId;
			try
			{
				viewId = view.ViewId;
			}
			catch
			{
				validation = ViewValidation.UnavailableForAttachment;
				return;
			}

			if (string.IsNullOrEmpty(viewId))
			{
				validation = ViewValidation.UnavailableForAttachment;
				return;
			}

			lock (_stateLock)
			{
				if (_views.ContainsKey(view))
				{
					validation = ViewValidation.AlreadyRegistered;
					return;
				}

				if (_viewsByViewId.ContainsKey(viewId))
				{
					validation = ViewValidation.DuplicateViewId;
					return;
				}
			}

			try
			{
				if (view.DocumentKey is not null
					|| view.HasPendingEdits
					|| view.HasConflict)
				{
					validation = ViewValidation.UnavailableForAttachment;
				}
			}
			catch
			{
				validation = ViewValidation.UnavailableForAttachment;
			}
		}).ConfigureAwait(false);

		return validation;
	}

	// Snapshots the registered views under the state lock; view members are not read here.
	private IWorkspaceDocumentView[] GetRegisteredViews()
	{
		lock (_stateLock)
			return [.. _views.Keys];
	}

	// Reads the normalized document id a registered view is currently bound to, or null when the view
	// is no longer registered.
	private string? GetRegisteredDocumentId(IWorkspaceDocumentView view)
	{
		lock (_stateLock)
			return _views.TryGetValue(view, out string? documentId) ? documentId : null;
	}

	// A view that cannot report its document key is not matched to a late identity change: the
	// captured bindings remain its only coordination point.
	private static bool TryGetViewDocumentKey(
		IWorkspaceDocumentView view,
		[NotNullWhen(true)] out WorkspaceDocumentKey? documentKey)
	{
		try
		{
			documentKey = view.DocumentKey;
			return documentKey is not null;
		}
		catch
		{
			documentKey = null;
			return false;
		}
	}

	private List<ViewBinding> GetViewBindings(IEnumerable<WorkspaceDocumentSnapshot> snapshots)
	{
		List<ViewBinding> bindings = [];
		HashSet<IWorkspaceDocumentView> seen = new(ReferenceEqualityComparer.Instance);
		foreach (WorkspaceDocumentSnapshot snapshot in snapshots)
		{
			foreach (IWorkspaceDocumentView view in GetPeers(snapshot.DocumentId, sourceView: null))
			{
				if (seen.Add(view))
					bindings.Add(new ViewBinding(view, snapshot));
			}
		}

		return bindings;
	}

	private bool IsViewMarkedUnsynchronized(IWorkspaceDocumentView view)
	{
		lock (_stateLock)
			return _unsynchronizedViews.Contains(view);
	}

	// Resolves the current snapshot of the document a registered view is attached to, or null when
	// the view is no longer registered or its document is no longer tracked. The binding is read
	// under the state lock; the snapshot is resolved through the document authority.
	private WorkspaceDocumentSnapshot? GetRegisteredViewSnapshot(IWorkspaceDocumentView view)
	{
		string documentId;
		lock (_stateLock)
		{
			if (!_views.TryGetValue(view, out string? registeredDocumentId))
				return null;

			documentId = registeredDocumentId;
		}

		return GetCurrentSnapshot(documentId);
	}

	private IWorkspaceDocumentView[] GetPeers(
		string documentId,
		IWorkspaceDocumentView? sourceView)
	{
		lock (_stateLock)
		{
			if (!_viewsByDocument.TryGetValue(documentId, out List<IWorkspaceDocumentView>? documentViews))
				return [];

			if (sourceView is null)
				return documentViews.ToArray();

			IWorkspaceDocumentView[] peers = new IWorkspaceDocumentView[documentViews.Count];
			int count = 0;
			foreach (IWorkspaceDocumentView view in documentViews)
			{
				if (!ReferenceEquals(view, sourceView))
					peers[count++] = view;
			}

			if (count != peers.Length)
				Array.Resize(ref peers, count);

			return peers;
		}
	}

	private bool IsRegistered(IWorkspaceDocumentView view)
	{
		lock (_stateLock)
			return !_stopping && _views.ContainsKey(view);
	}

	private void RecordViewResult(
		IWorkspaceDocumentView view,
		WorkspaceDocumentViewRefreshResult result,
		WorkspaceDocumentSnapshot? snapshot)
	{
		lock (_stateLock)
		{
			// An unregistered view is no longer coordinated; recording state for it would leak until the
			// manager stops.
			if (!_views.ContainsKey(view))
				return;

			if (result.Status == WorkspaceDocumentViewRefreshStatus.Refreshed)
			{
				_unsynchronizedViews.Remove(view);

				// The recorded version only advances: two interleaved refreshes can deliver an older
				// snapshot after a newer one, and moving the version backwards would make a later
				// IsViewOlder check skip a needed refresh.
				if (snapshot is not null && _viewVersions.TryGetValue(view, out long currentVersion))
					_viewVersions[view] = Math.Max(currentVersion, snapshot.Version);
			}
			else
			{
				_unsynchronizedViews.Add(view);
			}
		}
	}

	private bool IsViewOlder(IWorkspaceDocumentView view, long version)
	{
		lock (_stateLock)
			return _viewVersions.TryGetValue(view, out long viewVersion)
				&& viewVersion < version;
	}

	private void RecordViewFailure(IWorkspaceDocumentView view)
	{
		lock (_stateLock)
		{
			// Unregistered views are no longer coordinated; tracking them would retain them until stop.
			if (_views.ContainsKey(view))
				_unsynchronizedViews.Add(view);
		}
	}
}

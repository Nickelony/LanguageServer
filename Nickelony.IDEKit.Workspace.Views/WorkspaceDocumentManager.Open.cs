using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views;

public sealed partial class WorkspaceDocumentManager
{
	/// <inheritdoc />
	public async Task<WorkspaceDocumentManagerOpenResult> OpenAsync(
		string? filePath,
		WorkspaceDocumentOpenOptions options,
		CancellationToken cancellationToken = default)
	{
		WorkspaceDocumentOpenResult openResult = await RunOperationAsync(
			() => _store.OpenAsync(filePath, options, cancellationToken)).ConfigureAwait(false);

		return openResult.Status switch
		{
			WorkspaceDocumentOpenStatus.Opened => new(WorkspaceDocumentManagerOpenStatus.Opened, openResult.Snapshot),
			WorkspaceDocumentOpenStatus.AlreadyOpen => new(WorkspaceDocumentManagerOpenStatus.AlreadyOpen, openResult.Snapshot),
			_ => new(MapOpenStatus(openResult.Status), null, openResult.Failure)
		};
	}

	/// <inheritdoc />
	public Task<WorkspaceDocumentManagerOpenResult> OpenWithViewAsync(
		string? filePath,
		WorkspaceDocumentOpenOptions options,
		IWorkspaceDocumentView view,
		CancellationToken cancellationToken = default)
	{
		// Validated before dispatch so a null view throws synchronously instead of surfacing as a
		// faulted task, matching the manager's other argument validation.
		ArgumentNullException.ThrowIfNull(view);

		return RunOperationAsync(async () =>
		{
			try
			{
				ViewValidation validation = await ValidateViewAsync(view).ConfigureAwait(false);
				if (validation == ViewValidation.AlreadyRegistered)
					return new WorkspaceDocumentManagerOpenResult(
						WorkspaceDocumentManagerOpenStatus.AlreadyOpen,
						GetRegisteredViewSnapshot(view));
				if (validation == ViewValidation.DuplicateViewId)
					return new WorkspaceDocumentManagerOpenResult(
						WorkspaceDocumentManagerOpenStatus.ViewInUse,
						null);
				if (validation == ViewValidation.UnavailableForAttachment)
					return new WorkspaceDocumentManagerOpenResult(
						WorkspaceDocumentManagerOpenStatus.ViewUnavailable,
						null);

				WorkspaceDocumentOpenResult openResult = await _store
					.OpenAsync(filePath, options, cancellationToken)
					.ConfigureAwait(false);
				return await OpenLoadedViewAsync(view, openResult).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				return new WorkspaceDocumentManagerOpenResult(
					WorkspaceDocumentManagerOpenStatus.Canceled,
					null);
			}
			catch (ObjectDisposedException)
			{
				// A stop that starts while the document is being attached is reported as disposal, matching
				// the documented contract for operations that run while the manager stops.
				throw;
			}
			catch (Exception exception)
			{
				return new WorkspaceDocumentManagerOpenResult(
					WorkspaceDocumentManagerOpenStatus.OpenFailed,
					null,
					new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.OpenFailed, exception.Message, exception));
			}
		});
	}

	private async Task<WorkspaceDocumentManagerOpenResult> OpenLoadedViewAsync(
		IWorkspaceDocumentView view,
		WorkspaceDocumentOpenResult openResult)
	{
		if (openResult.Status != WorkspaceDocumentOpenStatus.Opened
			&& openResult.Status != WorkspaceDocumentOpenStatus.AlreadyOpen)
		{
			return new WorkspaceDocumentManagerOpenResult(
				MapOpenStatus(openResult.Status),
				null,
				openResult.Failure);
		}

		WorkspaceDocumentSnapshot snapshot = openResult.Snapshot
			?? throw new InvalidOperationException("A successful document open must include a snapshot.");
		bool registered = false;

		try
		{
			WorkspaceDocumentViewOpenResult? attachResult = null;
			string? viewId = null;
			await _dispatchViewAction(() =>
			{
				attachResult = view.Open(snapshot);
				viewId = view.ViewId;
			}).ConfigureAwait(false);

			WorkspaceDocumentViewOpenResult result = attachResult
				?? throw new InvalidOperationException("The view did not return an attach result.");
			if (result.Status == WorkspaceDocumentViewOpenStatus.AlreadyOpen)
			{
				// The view reports that it is attached elsewhere; it was not attached to this document.
				return new WorkspaceDocumentManagerOpenResult(
					WorkspaceDocumentManagerOpenStatus.ViewInUse,
					snapshot,
					result.Failure);
			}

			if (result.Status != WorkspaceDocumentViewOpenStatus.Opened)
			{
				await CloseAfterFailedOpenAsync(view).ConfigureAwait(false);
				return new WorkspaceDocumentManagerOpenResult(
					WorkspaceDocumentManagerOpenStatus.ViewRejected,
					snapshot,
					result.Failure ?? new WorkspaceOperationFailure(
						WorkspaceOperationFailureCodes.OpenFailed,
						"The view did not accept the workspace document attachment."));
			}

			if (string.IsNullOrEmpty(viewId))
			{
				// A view without an id cannot be indexed for duplicate detection or reported in results.
				await CloseAfterFailedOpenAsync(view).ConfigureAwait(false);
				return new WorkspaceDocumentManagerOpenResult(
					WorkspaceDocumentManagerOpenStatus.ViewRejected,
					snapshot,
					new WorkspaceOperationFailure(
						WorkspaceOperationFailureCodes.OpenFailed,
						"The view did not report a view id."));
			}

			bool alreadyRegistered;
			bool closeDuplicateAttachment;
			lock (_stateLock)
			{
				ThrowIfStoppingUnderLock();

				bool sameInstance = _views.ContainsKey(view);
				alreadyRegistered = sameInstance || _viewsByViewId.ContainsKey(viewId);

				// A concurrent second open of the same instance must not close the registration the
				// first call just made; only a different instance duplicating an existing ViewId is
				// detached as a failed attach.
				closeDuplicateAttachment = alreadyRegistered && !sameInstance;
				if (!alreadyRegistered)
				{
					_views.Add(view, snapshot.DocumentId);
					_viewIds.Add(view, viewId);
					_viewsByViewId.Add(viewId, view);
					_viewVersions.Add(view, snapshot.Version);
					if (!_viewsByDocument.TryGetValue(snapshot.DocumentId, out List<IWorkspaceDocumentView>? documentViews))
					{
						documentViews = [];
						_viewsByDocument.Add(snapshot.DocumentId, documentViews);
					}

					documentViews.Add(view);
					registered = true;
				}
			}

			if (alreadyRegistered)
			{
				if (closeDuplicateAttachment)
				{
					await CloseAfterFailedOpenAsync(view).ConfigureAwait(false);
					return new WorkspaceDocumentManagerOpenResult(
						WorkspaceDocumentManagerOpenStatus.ViewInUse,
						snapshot);
				}

				// The view is already registered: the result reports the snapshot of the document the
				// view is attached to (or null when that document is no longer tracked), matching the
				// short-circuit path in OpenWithViewAsync.
				return new WorkspaceDocumentManagerOpenResult(
					WorkspaceDocumentManagerOpenStatus.AlreadyOpen,
					GetRegisteredViewSnapshot(view));
			}

			// The event subscription is the one view member the manager touches directly, and the
			// accessor is host code: subscribing outside the state lock keeps a blocking or marshaling
			// accessor from stalling every other manager operation. A raise that happens between the
			// registration above and the subscription is dropped; the same raise would have been ignored
			// while the view was unregistered, so the registration contract is unchanged.
			try
			{
				view.ApplyRequested += OnApplyRequested;
			}
			catch
			{
				// A view without a working event channel cannot be coordinated: detach the registration
				// and report the attach as failed instead of tracking a view that never publishes.
				lock (_stateLock)
					RemoveViewRegistrationLocked(view);

				await CloseAfterFailedOpenAsync(view).ConfigureAwait(false);
				throw;
			}

			return new WorkspaceDocumentManagerOpenResult(
				WorkspaceDocumentManagerOpenStatus.Opened,
				snapshot);
		}
		catch
		{
			if (!registered)
				await CloseAfterFailedOpenAsync(view).ConfigureAwait(false);

			throw;
		}
	}

	// Every named store status is mapped explicitly; an unmapped value asserts instead of silently
	// degrading into OpenFailed. The store statuses are a closed set defined by this library, so a
	// value outside it means the store implementation (or a version mismatch) violates the contract;
	// mapping it to OpenFailed would hide that. The test suite fails on any future store status that
	// is not mapped here.
	private static WorkspaceDocumentManagerOpenStatus MapOpenStatus(WorkspaceDocumentOpenStatus status)
		=> status switch
		{
			WorkspaceDocumentOpenStatus.Opened => WorkspaceDocumentManagerOpenStatus.Opened,
			WorkspaceDocumentOpenStatus.AlreadyOpen => WorkspaceDocumentManagerOpenStatus.AlreadyOpen,
			WorkspaceDocumentOpenStatus.InvalidPath => WorkspaceDocumentManagerOpenStatus.InvalidPath,
			WorkspaceDocumentOpenStatus.NotFound => WorkspaceDocumentManagerOpenStatus.NotFound,
			WorkspaceDocumentOpenStatus.IsDirectory => WorkspaceDocumentManagerOpenStatus.IsDirectory,
			WorkspaceDocumentOpenStatus.LoadFailed => WorkspaceDocumentManagerOpenStatus.LoadFailed,
			WorkspaceDocumentOpenStatus.Canceled => WorkspaceDocumentManagerOpenStatus.Canceled,
			_ => throw new ArgumentOutOfRangeException(nameof(status), status, "The store returned an unmapped open status.")
		};

	private async Task CloseAfterFailedOpenAsync(IWorkspaceDocumentView view)
	{
		try
		{
			await _dispatchViewAction(view.Close).ConfigureAwait(false);
		}
		catch
		{ }
	}
}

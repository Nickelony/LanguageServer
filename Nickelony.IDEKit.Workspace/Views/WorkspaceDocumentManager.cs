using Nickelony.IDEKit.Workspace.Documents;
using static Nickelony.IDEKit.Workspace.Views.WorkspaceDocumentResultFactory;

namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>
/// Coordinates view attachment and publication around workspace document authority.
/// </summary>
/// <remarks>
/// The manager serializes its operation lifetime and dispatches all view access through the host
/// callback supplied to the constructor. It tracks views by normalized document id and treats a
/// failed refresh or identity update as an unsynchronized view until a later successful update or
/// unregister.
/// </remarks>
public sealed class WorkspaceDocumentManager : IWorkspaceDocumentManager
{
	private readonly object _stateLock = new();
	private readonly IWorkspaceDocumentStore _store;
	private readonly Action<Action> _invokeOnHost;
	private readonly Dictionary<IWorkspaceDocumentView, string> _views = [];
	private readonly Dictionary<IWorkspaceDocumentView, long> _viewVersions = [];
	private readonly Dictionary<string, List<IWorkspaceDocumentView>> _viewsByDocument;
	private readonly HashSet<IWorkspaceDocumentView> _unsynchronizedViews = [];
	private readonly List<Task> _activeOperations = [];
	private Task? _stopTask;
	private bool _stopping;

	/// <summary>
	/// Initializes a new instance of the <see cref="WorkspaceDocumentManager"/> class.
	/// </summary>
	public WorkspaceDocumentManager(
		IWorkspaceDocumentStore store,
		Action<Action> invokeOnHost)
	{
		_store = store ?? throw new ArgumentNullException(nameof(store));
		_invokeOnHost = invokeOnHost ?? throw new ArgumentNullException(nameof(invokeOnHost));
		_viewsByDocument = new Dictionary<string, List<IWorkspaceDocumentView>>(
			OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
	}

	/// <inheritdoc />
	public Task<WorkspaceDocumentOpenResult> OpenAsync(
		string? filePath,
		WorkspaceDocumentOpenOptions options,
		CancellationToken cancellationToken = default)
		=> RunOperationAsync(() => _store.OpenAsync(filePath, options, cancellationToken));

	/// <inheritdoc />
	public IReadOnlyList<WorkspaceDocumentSnapshot> GetSnapshotsUnderDirectory(string directoryPath)
		=> RunOperation(() => _store.GetSnapshotsUnderDirectory(directoryPath));

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
				new WorkspaceOperationFailure("ViewIdentityUpdateFailed", exception.Message, exception));
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

	private void ReleaseDeleteGuards(IEnumerable<IWorkspaceDocumentView> projections)
	{
		foreach (IWorkspaceDocumentView view in projections)
		{
			try
			{
				WorkspaceDocumentViewDeleteGuardResult guard = view.SetDeleteGuard(false);
				if (guard.Status == WorkspaceDocumentViewDeleteGuardStatus.Failed)
					RecordViewFailure(view);
			}
			catch
			{
				RecordViewFailure(view);
			}
		}
	}

	/// <inheritdoc />
	public Task<WorkspaceDocumentManagerOpenResult> OpenWithViewAsync(
		string? filePath,
		WorkspaceDocumentOpenOptions options,
		IWorkspaceDocumentView view,
		CancellationToken cancellationToken = default)
		=> RunOperationAsync(async () =>
		{
			ArgumentNullException.ThrowIfNull(view);

			try
			{
				ViewValidation validation = ValidateView(view);
				if (validation == ViewValidation.AlreadyOpen)
					return new WorkspaceDocumentManagerOpenResult(
						WorkspaceDocumentManagerOpenStatus.AlreadyOpen,
						null);
				if (validation == ViewValidation.InConflictState)
					return new WorkspaceDocumentManagerOpenResult(
						WorkspaceDocumentManagerOpenStatus.ViewInConflictState,
						null);

				WorkspaceDocumentOpenResult openResult = await _store
					.OpenAsync(filePath, options, cancellationToken)
					.ConfigureAwait(false);
				return OpenLoadedView(view, openResult);
			}
			catch (OperationCanceledException)
			{
				return new WorkspaceDocumentManagerOpenResult(
					WorkspaceDocumentManagerOpenStatus.Cancelled,
					null);
			}
			catch (Exception exception)
			{
				return new WorkspaceDocumentManagerOpenResult(
					WorkspaceDocumentManagerOpenStatus.OpenFailed,
					null,
					new WorkspaceOperationFailure("OpenFailed", exception.Message, exception));
			}
		});

	/// <inheritdoc />
	public WorkspaceDocumentMutationResult Discard(WorkspaceDocumentDiscardRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		return RunOperation(() =>
		{
			WorkspaceDocumentMutationResult result = _store.Discard(request);
			if (result.Snapshot is not null
				&& result.Status is WorkspaceDocumentMutationStatus.Replaced or WorkspaceDocumentMutationStatus.NoChange)
				_invokeOnHost(() => AcknowledgeDocumentViews(result));

			return result;
		});
	}

	/// <inheritdoc />
	public WorkspaceDocumentMutationResult Replace(WorkspaceDocumentReplaceRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		return ReplaceCore(request, sourceView: null);
	}

	/// <inheritdoc />
	public WorkspaceDocumentManagerOpenResult OpenWithView(
		string? filePath,
		WorkspaceDocumentOpenOptions options,
		IWorkspaceDocumentView view,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(view);
		return RunOperation(() =>
		{
			try
			{
				ViewValidation validation = ValidateView(view);
				if (validation == ViewValidation.AlreadyOpen)
					return new WorkspaceDocumentManagerOpenResult(
						WorkspaceDocumentManagerOpenStatus.AlreadyOpen,
						null);
				if (validation == ViewValidation.InConflictState)
					return new WorkspaceDocumentManagerOpenResult(
						WorkspaceDocumentManagerOpenStatus.ViewInConflictState,
						null);

				// Deliberate sync-over-async: this synchronous member exists for host
				// callers that cannot await. The store pipeline awaits with
				// ConfigureAwait(false), so blocking here does not capture the caller's
				// synchronization context; keep that invariant (see the interface remarks).
				WorkspaceDocumentOpenResult openResult = _store
					.OpenAsync(filePath, options, cancellationToken)
					.GetAwaiter()
					.GetResult();

				return OpenLoadedView(view, openResult);
			}
			catch (OperationCanceledException)
			{
				return new WorkspaceDocumentManagerOpenResult(
					WorkspaceDocumentManagerOpenStatus.Cancelled,
					null);
			}
			catch (Exception exception)
			{
				return new WorkspaceDocumentManagerOpenResult(
					WorkspaceDocumentManagerOpenStatus.OpenFailed,
					null,
					new WorkspaceOperationFailure("OpenFailed", exception.Message, exception));
			}
		});
	}

	/// <inheritdoc />
	public Task<WorkspaceDocumentRenameResult> RenameAsync(
		WorkspaceDocumentRenameRequest request,
		CancellationToken cancellationToken = default)
		=> RunOperationAsync(async () =>
		{
			ArgumentNullException.ThrowIfNull(request);

			IReadOnlyList<string>? blockingViewIds = GetBlockingViews(request.DocumentId);
			if (blockingViewIds is not null)
			{
				_store.TryGetSnapshot(request.DocumentId, out WorkspaceDocumentSnapshot? snapshot);
				return CreateRenameResult(
					request,
					WorkspaceDocumentRenameStatus.ViewNotSynchronized,
					snapshot,
					failedViewIds: blockingViewIds);
			}

			WorkspaceDocumentRenameResult result = await _store
				.RenameAsync(request, cancellationToken)
				.ConfigureAwait(false);
			WorkspaceDocumentSnapshot? resultSnapshot = result.Snapshot;
			if (result.Status != WorkspaceDocumentRenameStatus.Renamed || resultSnapshot is null)
				return result;

			List<string> failedViewIds = SynchronizeViewIdentities(request.DocumentId, request.ExpectedDocumentKey, resultSnapshot);
			return failedViewIds.Count == 0
				? result
				: result with
				{
					Status = WorkspaceDocumentRenameStatus.ViewUpdateFailed,
					FailedViewIds = failedViewIds
				};
		});

	/// <inheritdoc />
	public Task<WorkspaceDocumentSaveAsResult> SaveAsAsync(
		WorkspaceDocumentSaveAsRequest request,
		CancellationToken cancellationToken = default)
		=> RunOperationAsync(async () =>
		{
			ArgumentNullException.ThrowIfNull(request);

			IReadOnlyList<string>? blockingViewIds = GetBlockingViews(request.DocumentId);
			if (blockingViewIds is not null)
			{
				_store.TryGetSnapshot(request.DocumentId, out WorkspaceDocumentSnapshot? snapshot);
				return CreateSaveAsResult(
					request,
					WorkspaceDocumentSaveAsStatus.ViewNotSynchronized,
					snapshot,
					failedViewIds: blockingViewIds);
			}

			WorkspaceDocumentSaveAsResult result = await _store
				.SaveAsAsync(request, cancellationToken)
				.ConfigureAwait(false);
			WorkspaceDocumentSnapshot? resultSnapshot = result.Snapshot;
			if (result.Status != WorkspaceDocumentSaveAsStatus.SavedAs || resultSnapshot is null)
				return result;

			List<string> failedViewIds = SynchronizeViewIdentities(request.DocumentId, request.ExpectedDocumentKey, resultSnapshot);
			return failedViewIds.Count == 0
				? result
				: result with
				{
					Status = WorkspaceDocumentSaveAsStatus.ViewUpdateFailed,
					FailedViewIds = failedViewIds
				};
		});

	/// <inheritdoc />
	public Task<WorkspaceDocumentDeleteResult> DeleteAsync(
		WorkspaceDocumentDeleteRequest request,
		CancellationToken cancellationToken = default)
		=> RunOperationAsync(async () =>
		{
			ArgumentNullException.ThrowIfNull(request);

			IReadOnlyList<string>? blockingViewIds = GetBlockingViews(request.DocumentId);
			if (blockingViewIds is not null)
			{
				_store.TryGetSnapshot(request.DocumentId, out WorkspaceDocumentSnapshot? snapshot);
				return CreateDeleteResult(
					request,
					WorkspaceDocumentDeleteStatus.ViewNotSynchronized,
					snapshot,
					failedViewIds: blockingViewIds);
			}

			IWorkspaceDocumentView[] projections = [];
			List<IWorkspaceDocumentView> enteredGuards = [];
			List<string> failedGuardIds = [];
			_invokeOnHost(() =>
			{
				projections = GetPeers(request.DocumentId, sourceView: null);
				EnterViewDeleteGuards(projections, enteredGuards, failedGuardIds);
			});
			if (failedGuardIds.Count > 0)
			{
				_store.TryGetSnapshot(request.DocumentId, out WorkspaceDocumentSnapshot? snapshot);
				return CreateDeleteResult(
					request,
					WorkspaceDocumentDeleteStatus.ViewUpdateFailed,
					snapshot,
					failedViewIds: failedGuardIds);
			}

			WorkspaceDocumentDeleteResult result = await _store
				.DeleteAsync(request, cancellationToken)
				.ConfigureAwait(false);
			if (result.Status != WorkspaceDocumentDeleteStatus.Deleted)
			{
				_invokeOnHost(() => ReleaseDeleteGuards(enteredGuards));
				return result;
			}

			List<string> failedViewIds = [];
			_invokeOnHost(() =>
			{
				foreach (IWorkspaceDocumentView view in projections)
				{
					try
					{
						view.Close();
					}
					catch
					{
						failedViewIds.Add(view.ViewId);
					}

					try
					{
						WorkspaceDocumentViewDeleteGuardResult guard = view.SetDeleteGuard(false);
						if (guard.Status == WorkspaceDocumentViewDeleteGuardStatus.Failed)
							failedViewIds.Add(view.ViewId);
					}
					catch
					{
						failedViewIds.Add(view.ViewId);
					}

					UnregisterOpenView(view);
				}
			});

			return failedViewIds.Count == 0
				? result
				: result with
				{
					Status = WorkspaceDocumentDeleteStatus.ViewUpdateFailed,
					FailedViewIds = failedViewIds
				};
		});

	/// <inheritdoc />
	public Task<WorkspaceDocumentDirectoryRenameResult> RenameDirectoryAsync(
		WorkspaceDocumentDirectoryRenameRequest request,
		CancellationToken cancellationToken = default)
		=> RunOperationAsync(async () =>
		{
			ArgumentNullException.ThrowIfNull(request);

			WorkspaceDocumentSnapshot[] sourceSnapshots = _store
				.GetSnapshotsUnderDirectory(request.SourceDirectoryPath)
				.ToArray();
			List<ViewBinding> bindings = GetViewBindings(sourceSnapshots);
			string[] blockingViewIds = GetBlockingViewIds(bindings);
			if (blockingViewIds.Length > 0)
				return CreateDirectoryRenameResult(
					request,
					WorkspaceDocumentDirectoryRenameStatus.ViewUpdateFailed,
					sourceSnapshots,
					blockingViewIds);

			List<IWorkspaceDocumentView> enteredGuards = [];
			List<string> failedGuardIds = [];
			_invokeOnHost(() => EnterDeleteGuards(bindings, enteredGuards, failedGuardIds));
			if (failedGuardIds.Count > 0)
			{
				_invokeOnHost(() => ReleaseDeleteGuards(enteredGuards));
				return CreateDirectoryRenameResult(
					request,
					WorkspaceDocumentDirectoryRenameStatus.ViewUpdateFailed,
					sourceSnapshots,
					failedGuardIds);
			}

			WorkspaceDocumentDirectoryRenameResult result = await _store
				.RenameDirectoryAsync(
					request with
					{
						Documents = sourceSnapshots
							.Select(snapshot => new WorkspaceDocumentBatchEntry(
								snapshot.DocumentKey,
								snapshot.DocumentId,
								snapshot.Version,
								snapshot.OnDiskStamp))
							.ToArray()
					},
					cancellationToken)
				.ConfigureAwait(false);
			if (result.Status != WorkspaceDocumentDirectoryRenameStatus.Renamed)
			{
				_invokeOnHost(() => ReleaseDeleteGuards(enteredGuards));
				return result;
			}

			List<string> failedViewIds = [];
			_invokeOnHost(() =>
			{
				foreach (ViewBinding binding in bindings)
				{
					WorkspaceDocumentSnapshot? snapshot = result.Snapshots
						.FirstOrDefault(candidate => candidate.DocumentKey == binding.Snapshot.DocumentKey);
					if (snapshot is null)
					{
						failedViewIds.Add(binding.View.ViewId);
						continue;
					}

					WorkspaceDocumentViewIdentityResult acknowledgement = AcknowledgeIdentity(
						binding.View,
						new WorkspaceDocumentIdentityChange(
							binding.Snapshot.DocumentKey,
							binding.Snapshot.DocumentId,
							snapshot));
					if (acknowledgement.Status == WorkspaceDocumentViewIdentityStatus.Updated)
						UpdateViewIdentity(binding.View, binding.Snapshot.DocumentId, snapshot);
					else
					{
						RecordViewFailure(binding.View);
						failedViewIds.Add(binding.View.ViewId);
					}
				}

				ReleaseDeleteGuards(enteredGuards);
			});

			return failedViewIds.Count == 0
				? result
				: result with
				{
					Status = WorkspaceDocumentDirectoryRenameStatus.ViewUpdateFailed,
					FailedViewIds = failedViewIds
				};
		});

	/// <inheritdoc />
	public Task<WorkspaceDocumentDirectoryDeleteResult> DeleteDirectoryAsync(
		WorkspaceDocumentDirectoryDeleteRequest request,
		CancellationToken cancellationToken = default)
		=> RunOperationAsync(async () =>
		{
			ArgumentNullException.ThrowIfNull(request);

			WorkspaceDocumentSnapshot[] sourceSnapshots = _store
				.GetSnapshotsUnderDirectory(request.DirectoryPath)
				.ToArray();
			List<ViewBinding> bindings = GetViewBindings(sourceSnapshots);
			string[] blockingViewIds = GetBlockingViewIds(bindings);
			if (blockingViewIds.Length > 0)
				return CreateDirectoryDeleteResult(
					request,
					WorkspaceDocumentDirectoryDeleteStatus.ViewNotSynchronized,
					sourceSnapshots,
					blockingViewIds);

			List<IWorkspaceDocumentView> enteredGuards = [];
			List<string> failedGuardIds = [];
			_invokeOnHost(() => EnterDeleteGuards(bindings, enteredGuards, failedGuardIds));
			if (failedGuardIds.Count > 0)
			{
				_invokeOnHost(() => ReleaseDeleteGuards(enteredGuards));
				return CreateDirectoryDeleteResult(
					request,
					WorkspaceDocumentDirectoryDeleteStatus.ViewUpdateFailed,
					sourceSnapshots,
					failedGuardIds);
			}

			WorkspaceDocumentDirectoryDeleteResult result = await _store
				.DeleteDirectoryAsync(
					request with
					{
						Documents = sourceSnapshots
							.Select(snapshot => new WorkspaceDocumentBatchEntry(
								snapshot.DocumentKey,
								snapshot.DocumentId,
								snapshot.Version,
								snapshot.OnDiskStamp))
							.ToArray()
					},
					cancellationToken)
				.ConfigureAwait(false);
			if (result.Status != WorkspaceDocumentDirectoryDeleteStatus.Deleted)
			{
				_invokeOnHost(() => ReleaseDeleteGuards(enteredGuards));
				return result;
			}

			List<string> failedViewIds = [];
			_invokeOnHost(() =>
			{
				foreach (ViewBinding binding in bindings)
				{
					try
					{
						binding.View.Close();
					}
					catch
					{
						failedViewIds.Add(binding.View.ViewId);
					}

					if (!_views.ContainsKey(binding.View))
						continue;

					UnregisterOpenView(binding.View);
				}

				ReleaseDeleteGuards(enteredGuards);
			});

			return failedViewIds.Count == 0
				? result
				: result with
				{
					Status = WorkspaceDocumentDirectoryDeleteStatus.ViewUpdateFailed,
					FailedViewIds = failedViewIds
				};
		});

	/// <inheritdoc />
	public Task<WorkspaceDocumentCommitResult> CommitAsync(
		WorkspaceDocumentCommitRequest request,
		CancellationToken cancellationToken = default)
		=> RunOperationAsync(async () =>
		{
			ArgumentNullException.ThrowIfNull(request);

			IReadOnlyList<string>? blockingViewIds = GetBlockingViews(request.DocumentId);
			if (blockingViewIds is not null)
			{
				_store.TryGetSnapshot(request.DocumentId, out WorkspaceDocumentSnapshot? snapshot);
				return new WorkspaceDocumentCommitResult(
					WorkspaceDocumentCommitStatus.ViewNotSynchronized,
					request.ExpectedDocumentKey,
					request.DocumentId,
					request.ExpectedVersion,
					snapshot,
					BlockingViewIds: blockingViewIds);
			}

			WorkspaceDocumentCommitResult result = await _store
				.CommitAsync(request, cancellationToken)
				.ConfigureAwait(false);
			if (result.Status != WorkspaceDocumentCommitStatus.Committed)
				return result;

			string[]? unsynchronizedViewIds = null;
			_invokeOnHost(() =>
			{
				if (result.Snapshot is not null)
					RefreshDocumentViews(result.Snapshot);

				unsynchronizedViewIds = GetBlockingViewIds(request.DocumentId);
			});
			if (unsynchronizedViewIds is null || unsynchronizedViewIds.Length == 0)
				return result;

			return result with
			{
				Status = WorkspaceDocumentCommitStatus.CommittedWithUnsynchronizedView,
				BlockingViewIds = unsynchronizedViewIds
			};
		});

	/// <inheritdoc />
	public Task<WorkspaceDocumentReloadResult> ReloadAsync(
		WorkspaceDocumentReloadRequest request,
		CancellationToken cancellationToken = default)
		=> RunOperationAsync(async () =>
		{
			ArgumentNullException.ThrowIfNull(request);

			WorkspaceDocumentReloadResult result = await _store
				.ReloadAsync(request, cancellationToken)
				.ConfigureAwait(false);

			if (result.Status == WorkspaceDocumentReloadStatus.Reloaded && result.Snapshot is not null)
				_invokeOnHost(() => RefreshDocumentViews(result.Snapshot));

			return result;
		});

	/// <inheritdoc />
	public Task<WorkspaceDocumentConflictResolutionResult> ResolveExternalConflictAsync(
		WorkspaceDocumentConflictResolutionRequest request,
		CancellationToken cancellationToken = default)
		=> RunOperationAsync(async () =>
		{
			ArgumentNullException.ThrowIfNull(request);

			IReadOnlyList<string>? blockingViewIds = GetBlockingViews(request.DocumentId);
			if (blockingViewIds is not null)
			{
				_store.TryGetSnapshot(request.DocumentId, out WorkspaceDocumentSnapshot? snapshot);
				return new WorkspaceDocumentConflictResolutionResult(
					WorkspaceDocumentConflictResolutionStatus.ViewNotSynchronized,
					request.ExpectedDocumentKey,
					request.DocumentId,
					request.ExpectedVersion,
					request.Choice,
					snapshot,
					BlockingViewIds: blockingViewIds);
			}

			WorkspaceDocumentConflictResolutionResult result = await _store
				.ResolveExternalConflictAsync(request, cancellationToken)
				.ConfigureAwait(false);

			if ((result.Status == WorkspaceDocumentConflictResolutionStatus.ResolvedWithDisk
				|| result.Status == WorkspaceDocumentConflictResolutionStatus.ResolvedWithLogical)
				&& result.Snapshot is not null)
			{
				string[]? unsynchronizedViewIds = null;
				_invokeOnHost(() =>
				{
					RefreshDocumentViews(result.Snapshot);
					unsynchronizedViewIds = GetBlockingViewIds(request.DocumentId);
				});

				if (unsynchronizedViewIds is not null && unsynchronizedViewIds.Length > 0)
					return result with
					{
						Status = WorkspaceDocumentConflictResolutionStatus.ResolvedWithUnsynchronizedView,
						BlockingViewIds = unsynchronizedViewIds
					};
			}

			return result;
		});

	/// <inheritdoc />
	public void UnregisterOpenView(IWorkspaceDocumentView view)
	{
		ArgumentNullException.ThrowIfNull(view);
		bool wasRegistered;

		lock (_stateLock)
		{
			if (!_views.TryGetValue(view, out string? documentId))
				return;

			wasRegistered = true;
			_views.Remove(view);
			_viewVersions.Remove(view);
			_unsynchronizedViews.Remove(view);

			if (_viewsByDocument.TryGetValue(documentId, out List<IWorkspaceDocumentView>? documentViews))
			{
				documentViews.Remove(view);
				if (documentViews.Count == 0)
					_viewsByDocument.Remove(documentId);
			}
		}

		if (wasRegistered)
			view.ApplyRequested -= OnApplyRequested;
	}

	/// <inheritdoc />
	public Task StopAsync()
	{
		TaskCompletionSource<object?> completion;
		Task[] activeOperations;

		lock (_stateLock)
		{
			if (_stopTask is not null)
				return _stopTask;

			_stopping = true;
			completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
			_stopTask = completion.Task;
			activeOperations = _activeOperations.ToArray();
		}

		_ = StopCoreAsync(activeOperations, completion);
		return completion.Task;
	}

	/// <inheritdoc />
	public ValueTask DisposeAsync()
		=> new(StopAsync());

	private WorkspaceDocumentManagerOpenResult OpenLoadedView(
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
			_invokeOnHost(() => attachResult = view.Open(snapshot));

			WorkspaceDocumentViewOpenResult result = attachResult
				?? throw new InvalidOperationException("The view did not return an attach result.");
			if (result.Status == WorkspaceDocumentViewOpenStatus.AlreadyOpen)
				return new WorkspaceDocumentManagerOpenResult(
					WorkspaceDocumentManagerOpenStatus.AlreadyOpen,
					snapshot,
					result.Failure);
			if (result.Status != WorkspaceDocumentViewOpenStatus.Opened)
			{
				CloseAfterFailedOpen(view);
				return new WorkspaceDocumentManagerOpenResult(
					WorkspaceDocumentManagerOpenStatus.OpenFailed,
					snapshot,
					result.Failure ?? new WorkspaceOperationFailure(
						"OpenFailed",
						"The view could not attach to the workspace document."));
			}

			bool alreadyRegistered;
			lock (_stateLock)
			{
				ThrowIfStoppingUnderLock();

				alreadyRegistered = _views.ContainsKey(view)
					|| _views.Keys.Any(existing =>
						string.Equals(existing.ViewId, view.ViewId, StringComparison.Ordinal));
				if (!alreadyRegistered)
				{
					view.ApplyRequested += OnApplyRequested;
					_views.Add(view, snapshot.DocumentId);
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
				CloseAfterFailedOpen(view);
				return new WorkspaceDocumentManagerOpenResult(
					WorkspaceDocumentManagerOpenStatus.AlreadyOpen,
					snapshot);
			}

			return new WorkspaceDocumentManagerOpenResult(
				WorkspaceDocumentManagerOpenStatus.Opened,
				snapshot);
		}
		catch
		{
			if (!registered)
				CloseAfterFailedOpen(view);

			throw;
		}
	}

	private WorkspaceDocumentMutationResult ReplaceCore(
		WorkspaceDocumentReplaceRequest request,
		IWorkspaceDocumentView? sourceView)
		=> RunOperation(() =>
		{
			WorkspaceDocumentMutationResult? mutationResult = null;
			_invokeOnHost(() =>
			{
				if (sourceView is not null && !IsRegistered(sourceView))
					return;

				mutationResult = _store.TryReplace(request);
				WorkspaceDocumentMutationResult result = mutationResult;

				if (sourceView is not null)
				{
					WorkspaceDocumentViewRefreshResult acknowledgement = Acknowledge(sourceView, result);
					RecordViewResult(sourceView, acknowledgement, result.Snapshot);
				}

				if (result.Status != WorkspaceDocumentMutationStatus.Replaced || result.Snapshot is null)
					return;

				foreach (IWorkspaceDocumentView view in GetPeers(result.Snapshot.DocumentId, sourceView))
				{
					if (!IsViewOlder(view, result.Snapshot.Version))
						continue;

					WorkspaceDocumentViewRefreshResult refresh = Refresh(view, result.Snapshot);
					RecordViewResult(view, refresh, result.Snapshot);
				}
			});

			return mutationResult
				?? throw new InvalidOperationException("An unregistered view published a document replacement.");
		});

	private void OnApplyRequested(
		object? sender,
		WorkspaceDocumentViewApplyRequestedEventArgs eventArgs)
	{
		if (sender is not IWorkspaceDocumentView sourceView)
			return;

		try
		{
			if (!IsRegistered(sourceView))
				return;

			ReplaceCore(eventArgs.Request, sourceView);
		}
		catch (Exception)
		{
			RecordViewFailure(sourceView);
		}
	}

	private ViewValidation ValidateView(IWorkspaceDocumentView view)
	{
		lock (_stateLock)
		{
			if (_views.ContainsKey(view)
				|| _views.Keys.Any(existing =>
					string.Equals(existing.ViewId, view.ViewId, StringComparison.Ordinal)))
				return ViewValidation.AlreadyOpen;
		}

		ViewValidation validation = ViewValidation.Valid;
		_invokeOnHost(() =>
		{
			try
			{
				if (view.DocumentKey is not null
					|| view.HasPendingEdits
					|| view.HasConflict)
				{
					validation = ViewValidation.InConflictState;
				}
			}
			catch
			{
				validation = ViewValidation.InConflictState;
			}
		});

		return validation;
	}

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
				new WorkspaceOperationFailure("ViewAcknowledgeFailed", exception.Message, exception));
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
				new WorkspaceOperationFailure("ViewRefreshFailed", exception.Message, exception));
		}
	}

	private string[] GetBlockingViewIds(string documentId)
	{
		List<string> blockingIds = [];
		foreach (IWorkspaceDocumentView view in GetPeers(documentId, sourceView: null))
		{
			bool blocks;
			lock (_stateLock)
				blocks = _unsynchronizedViews.Contains(view);
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
				blockingIds.Add(view.ViewId);
		}

		return blockingIds
			.Distinct(StringComparer.Ordinal)
			.OrderBy(id => id, StringComparer.Ordinal)
			.ToArray();
	}

	// Returns the ids of peer views that block a store operation because they
	// still have unsynchronized edits or conflicts, or null when none block. The
	// check runs on the host.
	private IReadOnlyList<string>? GetBlockingViews(string documentId)
	{
		IReadOnlyList<string>? blockingViewIds = null;
		_invokeOnHost(() => blockingViewIds = GetBlockingViewIds(documentId));
		return blockingViewIds is { Count: > 0 } ? blockingViewIds : null;
	}

	private List<ViewBinding> GetViewBindings(IEnumerable<WorkspaceDocumentSnapshot> snapshots)
	{
		List<ViewBinding> bindings = [];
		HashSet<IWorkspaceDocumentView> seen = [];
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

	private static string[] GetBlockingViewIds(IEnumerable<ViewBinding> bindings)
	{
		List<string> blockingIds = [];
		foreach (ViewBinding binding in bindings)
		{
			try
			{
				if (binding.View.HasPendingEdits || binding.View.HasConflict)
					blockingIds.Add(binding.View.ViewId);
			}
			catch
			{
				blockingIds.Add(binding.View.ViewId);
			}
		}

		return blockingIds
			.Distinct(StringComparer.Ordinal)
			.OrderBy(id => id, StringComparer.Ordinal)
			.ToArray();
	}

	private static void EnterDeleteGuards(
		IEnumerable<ViewBinding> bindings,
		List<IWorkspaceDocumentView> enteredGuards,
		List<string> failedGuardIds)
	{
		foreach (ViewBinding binding in bindings)
		{
			try
			{
				WorkspaceDocumentViewDeleteGuardResult result = binding.View.SetDeleteGuard(true);
				if (result.Status == WorkspaceDocumentViewDeleteGuardStatus.Applied)
					enteredGuards.Add(binding.View);
				else
					failedGuardIds.Add(binding.View.ViewId);
			}
			catch
			{
				failedGuardIds.Add(binding.View.ViewId);
			}
		}
	}

	// Enters the delete guard on each view and rolls back the guards that
	// were already entered when any view rejects the guard, on the host.
	// Populates enteredGuards with the views that entered successfully and
	// failedGuardIds with the ids that rejected or threw.
	private static void EnterViewDeleteGuards(
		IEnumerable<IWorkspaceDocumentView> projections,
		List<IWorkspaceDocumentView> enteredGuards,
		List<string> failedGuardIds)
	{
		foreach (IWorkspaceDocumentView view in projections)
		{
			try
			{
				WorkspaceDocumentViewDeleteGuardResult result = view.SetDeleteGuard(true);
				if (result.Status == WorkspaceDocumentViewDeleteGuardStatus.Applied)
					enteredGuards.Add(view);
				else
					failedGuardIds.Add(view.ViewId);
			}
			catch
			{
				failedGuardIds.Add(view.ViewId);
			}
		}

		if (failedGuardIds.Count > 0)
		{
			foreach (IWorkspaceDocumentView view in enteredGuards)
				view.SetDeleteGuard(false);

			enteredGuards.Clear();
		}
	}

	private void RefreshDocumentViews(WorkspaceDocumentSnapshot snapshot)
	{
		foreach (IWorkspaceDocumentView view in GetPeers(snapshot.DocumentId, sourceView: null))
		{
			if (!IsViewOlder(view, snapshot.Version))
				continue;

			WorkspaceDocumentViewRefreshResult refresh = Refresh(view, snapshot);
			RecordViewResult(view, refresh, snapshot);
		}
	}

	private void AcknowledgeDocumentViews(WorkspaceDocumentMutationResult result)
	{
		if (result.Snapshot is null)
			return;

		foreach (IWorkspaceDocumentView view in GetPeers(result.RequestedDocumentId, sourceView: null))
		{
			WorkspaceDocumentViewRefreshResult refresh = Acknowledge(view, result);
			RecordViewResult(view, refresh, result.Snapshot);
		}
	}

	// Asks every peer view attached to the document to acknowledge the identity
	// change and re-indexes it under the new identity, on the host. Returns the ids
	// of the views that failed to acknowledge.
	private List<string> SynchronizeViewIdentities(
		string documentId,
		WorkspaceDocumentKey expectedDocumentKey,
		WorkspaceDocumentSnapshot snapshot)
	{
		List<string> failedViewIds = [];
		_invokeOnHost(() =>
		{
			foreach (IWorkspaceDocumentView view in GetPeers(documentId, sourceView: null))
			{
				WorkspaceDocumentViewIdentityResult acknowledgement = AcknowledgeIdentity(
					view,
					new WorkspaceDocumentIdentityChange(expectedDocumentKey, documentId, snapshot));
				if (acknowledgement.Status == WorkspaceDocumentViewIdentityStatus.Updated)
					UpdateViewIdentity(view, documentId, snapshot);
				else
				{
					RecordViewFailure(view);
					failedViewIds.Add(view.ViewId);
				}
			}
		});

		return failedViewIds;
	}

	private IWorkspaceDocumentView[] GetPeers(
		string documentId,
		IWorkspaceDocumentView? sourceView)
	{
		lock (_stateLock)
		{
			if (!_viewsByDocument.TryGetValue(documentId, out List<IWorkspaceDocumentView>? projections))
				return [];

			return projections
				.Where(view => !ReferenceEquals(view, sourceView))
				.ToArray();
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
			if (result.Status == WorkspaceDocumentViewRefreshStatus.Refreshed)
			{
				_unsynchronizedViews.Remove(view);
				if (snapshot is not null && _viewVersions.ContainsKey(view))
					_viewVersions[view] = snapshot.Version;
			}
			else
				_unsynchronizedViews.Add(view);
		}
	}

	private bool IsViewOlder(IWorkspaceDocumentView view, long version)
	{
		lock (_stateLock)
			return _viewVersions.TryGetValue(view, out long projectionVersion)
				&& projectionVersion < version;
	}

	private void RecordViewFailure(IWorkspaceDocumentView view)
	{
		lock (_stateLock)
			_unsynchronizedViews.Add(view);
	}

	// Runs an asynchronous operation while it is registered as an active manager
	// operation so StopAsync waits for it to finish before detaching views.
	private async Task<T> RunOperationAsync<T>(Func<Task<T>> body)
	{
		TaskCompletionSource<object?> operation = EnterOperation();

		try
		{
			return await body().ConfigureAwait(false);
		}
		finally
		{
			CompleteOperation(operation);
		}
	}

	// Runs a synchronous operation while it is registered as an active manager
	// operation so StopAsync waits for it to finish before detaching views.
	private T RunOperation<T>(Func<T> body)
	{
		TaskCompletionSource<object?> operation = EnterOperation();

		try
		{
			return body();
		}
		finally
		{
			CompleteOperation(operation);
		}
	}

	private TaskCompletionSource<object?> EnterOperation()
	{
		lock (_stateLock)
		{
			ThrowIfStoppingUnderLock();

			TaskCompletionSource<object?> operation = new(TaskCreationOptions.RunContinuationsAsynchronously);
			_activeOperations.Add(operation.Task);
			return operation;
		}
	}

	private void CompleteOperation(TaskCompletionSource<object?> operation)
	{
		lock (_stateLock)
		{
			_activeOperations.Remove(operation.Task);
			operation.TrySetResult(null);
		}
	}

	private async Task StopCoreAsync(
		Task[] activeOperations,
		TaskCompletionSource<object?> completion)
	{
		try
		{
			if (activeOperations.Length > 0)
				await Task.WhenAll(activeOperations).ConfigureAwait(false);

			IWorkspaceDocumentView[] projections;
			lock (_stateLock)
			{
				projections = _views.Keys.ToArray();

				_views.Clear();
				_viewVersions.Clear();
				_viewsByDocument.Clear();
				_unsynchronizedViews.Clear();
			}

			foreach (IWorkspaceDocumentView view in projections)
				view.ApplyRequested -= OnApplyRequested;

			_invokeOnHost(() =>
			{
				foreach (IWorkspaceDocumentView view in projections)
				{
					try
					{
						view.Close();
					}
					catch
					{ }
				}
			});

			completion.TrySetResult(null);
		}
		catch (Exception exception)
		{
			completion.TrySetException(exception);
		}
	}

	private void CloseAfterFailedOpen(IWorkspaceDocumentView view)
	{
		try
		{
			_invokeOnHost(view.Close);
		}
		catch
		{ }
	}

	private void ThrowIfStoppingUnderLock()
	{
		ObjectDisposedException.ThrowIf(_stopping, nameof(WorkspaceDocumentManager));
	}
}

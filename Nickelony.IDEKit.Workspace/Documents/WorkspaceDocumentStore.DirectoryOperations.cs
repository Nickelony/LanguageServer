using Nickelony.IDEKit.Workspace.Documents.FileSystem;
using static Nickelony.IDEKit.Workspace.Documents.WorkspaceDocumentPath;
using static Nickelony.IDEKit.Workspace.Documents.WorkspaceDocumentResultFactory;

namespace Nickelony.IDEKit.Workspace.Documents;

public sealed partial class WorkspaceDocumentStore
{
	/// <inheritdoc />
	public async Task<WorkspaceDocumentDirectoryRenameResult> RenameDirectoryAsync(
		WorkspaceDocumentDirectoryRenameRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ThrowIfDisposed();

		// Only unnormalizable paths are invalid here. A destination spelling that normalizes to the
		// source directory is a no-op and reports NoChange; the comparison happens on the normalized
		// ids so equivalent spellings (a trailing separator or a "." segment) are covered as well. A
		// case-only difference is a real rename and is handed to the file system, which completes it
		// on a case-insensitive target through an intermediate rename.
		if (!TryNormalizePath(request.SourceDirectoryPath, out string sourceDirectoryId)
			|| !TryNormalizePath(request.DestinationDirectoryPath, out string destinationDirectoryId))
			return CreateDirectoryRenameResult(request, WorkspaceDocumentDirectoryRenameStatus.InvalidPath, []);

		string sourcePrefix = GetDirectoryPrefix(sourceDirectoryId);
		List<LogicalDocument> documents = [];
		List<SemaphoreSlim> acquiredGates = [];
		List<DestinationReservation> reservations = [];
		List<string> destinationIds = [];
		List<FileStamp> expectedStamps = [];
		OperationRegistration operation;

		lock (_stateLock)
		{
			ThrowIfDisposedUnderLock();

			// The move applies to the whole directory on disk, so every tracked descendant is part of
			// the operation: each one is validated against the stamp the store currently tracks, and a
			// descendant whose file changed since the store last observed it stops the move. The
			// descendants are collected in id order so failure and snapshot order stay deterministic.
			List<LogicalDocument> trackedDescendants = CollectTrackedDescendants(sourcePrefix);

			// A destination that normalizes to the source directory already is where the caller wants it.
			// The result mirrors the file rename NoChange outcome and carries the current snapshots.
			if (string.Equals(sourceDirectoryId, destinationDirectoryId, StringComparison.Ordinal))
				return CreateDirectoryRenameResult(request, WorkspaceDocumentDirectoryRenameStatus.NoChange, CreateSnapshots(trackedDescendants));

			foreach (LogicalDocument trackedDocument in trackedDescendants)
			{
				documents.Add(trackedDocument);
				expectedStamps.Add(trackedDocument.OnDiskStamp);
			}

			HashSet<LogicalDocument> movingDocuments = [.. documents];
			foreach (LogicalDocument document in documents)
			{
				string destinationId = RebasePath(document.DocumentId, sourceDirectoryId, destinationDirectoryId, _pathComparison.Comparison);
				destinationIds.Add(destinationId);
				if (_documents.TryGetValue(destinationId, out LogicalDocument? destinationDocument)
					&& !movingDocuments.Contains(destinationDocument))
					return CreateDirectoryRenameResult(request, WorkspaceDocumentDirectoryRenameStatus.DestinationInUse, CreateSnapshots(documents));

				if (_destinationReservations.ContainsKey(destinationId))
					return CreateDirectoryRenameResult(request, WorkspaceDocumentDirectoryRenameStatus.DestinationBusy, CreateSnapshots(documents));

				// An open that already holds a reservation for a destination descendant would add its
				// document while the directory move runs; the reservations created below exclude later opens.
				if (_openReservations.ContainsKey(destinationId))
					return CreateDirectoryRenameResult(request, WorkspaceDocumentDirectoryRenameStatus.DestinationBusy, CreateSnapshots(documents));
			}

			if (!TryAcquireGates(documents, acquiredGates))
			{
				ReleaseGates(acquiredGates);
				return CreateDirectoryRenameResult(request, WorkspaceDocumentDirectoryRenameStatus.OperationInProgress, CreateSnapshots(documents));
			}

			foreach (string destinationId in destinationIds.Distinct(_pathComparison.Comparer))
			{
				DestinationReservation reservation = new(destinationId);
				_destinationReservations.Add(destinationId, reservation);
				reservations.Add(reservation);
			}

			operation = new OperationRegistration(acquiredGates);
			_activeOperations.Add(operation);
		}

		try
		{
			using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
				cancellationToken,
				_lifetimeCancellation.Token);
			linkedCancellation.Token.ThrowIfCancellationRequested();

			(string DocumentId, FileStamp Stamp)? changedDescendant = await FindChangedDescendantStampAsync(
				documents,
				expectedStamps,
				linkedCancellation.Token).ConfigureAwait(false);
			if (changedDescendant is { } conflict)
				return CreateDirectoryRenameResult(
					request,
					WorkspaceDocumentDirectoryRenameStatus.ExternalFileConflict,
					CreateSnapshotsUnderLock(documents),
					new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.ExternalFileConflict, $"The tracked descendant '{conflict.DocumentId}' changed before the directory rename."));

			// The normalized directory ids are handed to the file system, so a case-sensitive file
			// system is not at the mercy of the caller's display spelling; the file operations pass the
			// source id and the caller's destination spelling for the move itself, while this batch move
			// passes both normalized directory ids. The result and the rebased snapshots still echo the
			// caller-supplied paths.
			WorkspaceFileMoveResult move = await _fileSystem
				.MoveDirectoryAsync(sourceDirectoryId, destinationDirectoryId, linkedCancellation.Token)
				.ConfigureAwait(false);
			if (move.Status != WorkspaceFileMoveStatus.Moved)
				return CreateDirectoryRenameResult(request, MapDirectoryRenameStatus(move.Status), CreateSnapshotsUnderLock(documents), move.Failure);

			lock (_stateLock)
			{
				// A descendant that was opened while the file-system call was in flight is not part of the
				// captured batch. Its file moved with the directory, so it is rebased together with the
				// tracked descendants instead of being left behind under the vacated source path. An open that
				// registers after this pass is equivalent to an open issued after the rename: the source path
				// no longer exists, so the open creates a new logical document for the vacated path.
				HashSet<LogicalDocument> capturedDocuments = [.. documents];
				foreach (LogicalDocument trackedDocument in _documents.Values)
				{
					if (trackedDocument.DocumentId.StartsWith(sourcePrefix, _pathComparison.Comparison)
						&& capturedDocuments.Add(trackedDocument))
					{
						documents.Add(trackedDocument);
						destinationIds.Add(RebasePath(
							trackedDocument.DocumentId,
							sourceDirectoryId,
							destinationDirectoryId,
							_pathComparison.Comparison));
					}
				}

				// The rebased set is prepared before the tracking dictionary is touched, so a destination
				// occupied by a document that was opened while the move ran cannot fail the update midway
				// and leave tracking partially rebased. The moved instance wins that collision: it carries
				// the identity the caller observed before the operation, while the late instance was opened
				// from the same file at its destination path.
				Dictionary<string, LogicalDocument> rebasedDocuments = new(_pathComparison.Comparer);
				foreach (LogicalDocument trackedDocument in _documents.Values)
					rebasedDocuments[trackedDocument.DocumentId] = trackedDocument;
				foreach (LogicalDocument document in documents)
					rebasedDocuments.Remove(document.DocumentId);

				for (int index = 0; index < documents.Count; index++)
				{
					LogicalDocument document = documents[index];
					string relativePath = GetRelativePathUnderDirectory(
						document.DocumentId,
						sourceDirectoryId,
						_pathComparison.Comparison);
					document.DocumentId = destinationIds[index];
					document.DisplayPath = Path.Combine(request.DestinationDirectoryPath, relativePath);
					document.Version++;
					rebasedDocuments[document.DocumentId] = document;
				}

				_documents.Clear();
				foreach (KeyValuePair<string, LogicalDocument> entry in rebasedDocuments)
					_documents.Add(entry.Key, entry.Value);

				return CreateDirectoryRenameResult(
					request,
					WorkspaceDocumentDirectoryRenameStatus.Renamed,
					CreateSnapshots(documents));
			}
		}
		catch (OperationCanceledException)
		{
			return CreateDirectoryRenameResult(request, WorkspaceDocumentDirectoryRenameStatus.Canceled, CreateSnapshotsUnderLock(documents));
		}
		catch (Exception exception)
		{
			return CreateDirectoryRenameResult(
				request,
				WorkspaceDocumentDirectoryRenameStatus.MoveFailed,
				CreateSnapshotsUnderLock(documents),
				new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.MoveFailed, exception.Message, exception));
		}
		finally
		{
			foreach (DestinationReservation reservation in reservations)
				CompleteDestinationReservation(reservation);

			CompleteOperation(operation);
		}
	}

	/// <inheritdoc />
	public async Task<WorkspaceDocumentDirectoryDeleteResult> DeleteDirectoryAsync(
		WorkspaceDocumentDirectoryDeleteRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ThrowIfDisposed();

		if (!TryNormalizePath(request.DirectoryPath, out string directoryId))
			return CreateDirectoryDeleteResult(request, WorkspaceDocumentDirectoryDeleteStatus.InvalidPath, []);

		string directoryPrefix = GetDirectoryPrefix(directoryId);
		List<LogicalDocument> documents = [];
		List<SemaphoreSlim> acquiredGates = [];
		List<FileStamp> expectedStamps = [];
		OperationRegistration operation;

		lock (_stateLock)
		{
			ThrowIfDisposedUnderLock();

			// The recursive delete applies to the whole directory on disk, so every tracked descendant
			// is part of the operation: each one is validated against the stamp the store currently
			// tracks, and a descendant whose file changed since the store last observed it stops the
			// delete. The descendants are collected in id order so failure and snapshot order stay
			// deterministic.
			List<LogicalDocument> trackedDescendants = CollectTrackedDescendants(directoryPrefix);

			foreach (LogicalDocument trackedDocument in trackedDescendants)
			{
				documents.Add(trackedDocument);
				expectedStamps.Add(trackedDocument.OnDiskStamp);
			}

			if (!TryAcquireGates(documents, acquiredGates))
			{
				ReleaseGates(acquiredGates);
				return CreateDirectoryDeleteResult(request, WorkspaceDocumentDirectoryDeleteStatus.OperationInProgress, CreateSnapshots(documents));
			}

			// The delete-active flag is published only after every gate is acquired. Setting it per
			// acquired gate would let the early return above skip the finally that resets it and leave
			// earlier documents permanently rejecting Replace with OperationInProgress. Both loops
			// run under the state lock, so Replace cannot observe a partially flagged batch.
			foreach (LogicalDocument document in documents)
			{
				document.DeleteOperationActive = true;
				document.DeleteCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			}

			operation = new OperationRegistration(acquiredGates);
			_activeOperations.Add(operation);
		}

		try
		{
			using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
				cancellationToken,
				_lifetimeCancellation.Token);
			linkedCancellation.Token.ThrowIfCancellationRequested();

			(string DocumentId, FileStamp Stamp)? changedDescendant = await FindChangedDescendantStampAsync(
				documents,
				expectedStamps,
				linkedCancellation.Token).ConfigureAwait(false);
			if (changedDescendant is { } conflict)
				return CreateDirectoryDeleteResult(
					request,
					WorkspaceDocumentDirectoryDeleteStatus.ExternalFileConflict,
					CreateSnapshotsUnderLock(documents),
					new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.ExternalFileConflict, $"The tracked descendant '{conflict.DocumentId}' changed before the directory deletion."));

			// The normalized directory id is handed to the file system, matching the file delete path.
			WorkspaceFileDeleteResult deletion = await _fileSystem
				.DeleteDirectoryAsync(directoryId, linkedCancellation.Token)
				.ConfigureAwait(false);
			if (deletion.Status != WorkspaceFileDeleteStatus.Deleted)
				return CreateDirectoryDeleteResult(
					request,
					MapDirectoryDeleteStatus(deletion.Status),
					CreateSnapshotsUnderLock(documents),
					deletion.Failure);

			lock (_stateLock)
			{
				// A descendant that was opened while the file-system call was in flight is not part of the
				// captured batch; this final pass removes it from tracking together with the descendants. An
				// open that completes after this pass is equivalent to an open issued after the delete: the
				// path no longer exists, so the open creates a new logical document for it.
				HashSet<LogicalDocument> capturedDocuments = [.. documents];
				foreach (LogicalDocument trackedDocument in _documents.Values)
				{
					if (trackedDocument.DocumentId.StartsWith(directoryPrefix, _pathComparison.Comparison)
						&& capturedDocuments.Add(trackedDocument))
					{
						documents.Add(trackedDocument);
					}
				}

				foreach (LogicalDocument document in documents)
					_documents.Remove(document.DocumentId);

				return CreateDirectoryDeleteResult(
					request,
					WorkspaceDocumentDirectoryDeleteStatus.Deleted,
					CreateSnapshots(documents));
			}
		}
		catch (OperationCanceledException)
		{
			return CreateDirectoryDeleteResult(request, WorkspaceDocumentDirectoryDeleteStatus.Canceled, CreateSnapshotsUnderLock(documents));
		}
		catch (Exception exception)
		{
			return CreateDirectoryDeleteResult(
				request,
				WorkspaceDocumentDirectoryDeleteStatus.DeleteFailed,
				CreateSnapshotsUnderLock(documents),
				new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.DeleteFailed, exception.Message, exception));
		}
		finally
		{
			lock (_stateLock)
			{
				foreach (LogicalDocument document in documents)
				{
					document.DeleteOperationActive = false;
					document.DeleteCompletion?.TrySetResult();
					document.DeleteCompletion = null;
				}
			}

			CompleteOperation(operation);
		}
	}

	// Collects the tracked descendants of a directory prefix in deterministic id order. Runs under
	// _stateLock; both directory operations validate and rebase the same set shape.
	private List<LogicalDocument> CollectTrackedDescendants(string directoryPrefix)
	{
		List<LogicalDocument> trackedDescendants = [];
		foreach (LogicalDocument trackedDocument in _documents.Values)
		{
			if (trackedDocument.DocumentId.StartsWith(directoryPrefix, _pathComparison.Comparison))
				trackedDescendants.Add(trackedDocument);
		}

		trackedDescendants.Sort((left, right) => _pathComparison.Comparer.Compare(left.DocumentId, right.DocumentId));
		return trackedDescendants;
	}

	// Acquires every document's disk gate without waiting. On failure the caller releases the gates
	// recorded so far; both directory operations share the acquire-and-roll-back shape.
	private static bool TryAcquireGates(List<LogicalDocument> documents, List<SemaphoreSlim> acquiredGates)
	{
		foreach (LogicalDocument document in documents)
		{
			if (!document.DiskOperationGate.Wait(0, CancellationToken.None))
				return false;

			acquiredGates.Add(document.DiskOperationGate);
		}

		return true;
	}

	// Re-captures every descendant stamp and returns the first descendant whose observed stamp no
	// longer matches the stamp the store tracks, together with its document id; null when every
	// descendant is unchanged.
	private async Task<(string DocumentId, FileStamp Stamp)?> FindChangedDescendantStampAsync(
		List<LogicalDocument> documents,
		List<FileStamp> expectedStamps,
		CancellationToken cancellationToken)
	{
		for (int index = 0; index < documents.Count; index++)
		{
			FileStamp observedStamp = await _fileSystem
				.CaptureStampAsync(documents[index].DocumentId, cancellationToken)
				.ConfigureAwait(false);
			if (observedStamp != expectedStamps[index])
				return (documents[index].DocumentId, observedStamp);
		}

		return null;
	}
}

using Nickelony.IDEKit.Workspace.Documents.FileSystem;
using System.Text;
using static Nickelony.IDEKit.Workspace.Documents.WorkspaceDocumentPath;
using static Nickelony.IDEKit.Workspace.Documents.WorkspaceDocumentResultFactory;

namespace Nickelony.IDEKit.Workspace.Documents;

public sealed partial class WorkspaceDocumentStore
{
	/// <inheritdoc />
	public async Task<WorkspaceDocumentRenameResult> RenameAsync(
		WorkspaceDocumentRenameRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ThrowIfDisposed();

		// The request identity is validated before the destination path so the documented
		// ArgumentException holds even when the destination is also unnormalizable; TryBeginOperation
		// repeats the check for the shared operation preamble.
		ArgumentException.ThrowIfNullOrWhiteSpace(request.Identity.DocumentId);

		if (!TryNormalizePath(request.DestinationPath, out string destinationId))
			return CreateRenameResult(request, WorkspaceDocumentRenameStatus.InvalidPath, null);

		WorkspaceDocumentRenameStatus? destinationFailure = null;
		DestinationReservation? reservation = null;
		OperationBegin begin = TryBeginOperation(
			request.Identity,
			captureSnapshot: false,
			preGateCheck: document =>
			{
				// The destination normalizes to the document's current id: an exactly repeated spelling is
				// a no-op, while a differently-cased spelling keeps the display-path update path below.
				if (string.Equals(document.DocumentId, destinationId, StringComparison.Ordinal))
				{
					destinationFailure = WorkspaceDocumentRenameStatus.NoChange;
					return false;
				}

				if (_documents.TryGetValue(destinationId, out LogicalDocument? destinationDocument)
					&& !ReferenceEquals(destinationDocument, document))
				{
					destinationFailure = WorkspaceDocumentRenameStatus.DestinationInUse;
					return false;
				}

				if (_destinationReservations.ContainsKey(destinationId))
				{
					destinationFailure = WorkspaceDocumentRenameStatus.DestinationBusy;
					return false;
				}

				// An open that already holds a reservation for the destination would add its document
				// while this move runs; the destination reservation created below excludes later opens.
				if (_openReservations.ContainsKey(destinationId))
				{
					destinationFailure = WorkspaceDocumentRenameStatus.DestinationBusy;
					return false;
				}

				return true;
			},
			postGateSetup: _ =>
			{
				reservation = new DestinationReservation(destinationId);
				_destinationReservations.Add(destinationId, reservation);
			});

		if (begin is not { Succeeded: true, Document: { } document, Operation: { } operation })
		{
			if (destinationFailure is not null)
				return CreateRenameResult(request, destinationFailure.Value, begin.FailureSnapshot);

			return begin.Failure switch
			{
				OperationBeginFailure.DocumentNotFound => CreateRenameResult(request, WorkspaceDocumentRenameStatus.DocumentNotFound, null),
				OperationBeginFailure.StaleDocumentInstance => CreateRenameResult(request, WorkspaceDocumentRenameStatus.StaleDocumentInstance, begin.FailureSnapshot),
				OperationBeginFailure.StaleDocument => CreateRenameResult(request, WorkspaceDocumentRenameStatus.StaleDocument, begin.FailureSnapshot),
				_ => CreateRenameResult(request, WorkspaceDocumentRenameStatus.OperationInProgress, begin.FailureSnapshot),
			};
		}

		try
		{
			using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
				cancellationToken,
				_lifetimeCancellation.Token);
			linkedCancellation.Token.ThrowIfCancellationRequested();

			WorkspaceFileMoveResult move = await _fileSystem
				.MoveAsync(
					document.DocumentId,
					request.DestinationPath,
					request.ExpectedOnDiskStamp,
					linkedCancellation.Token)
				.ConfigureAwait(false);

			if (move.Status != WorkspaceFileMoveStatus.Moved)
				return CreateRenameResult(request, MapRenameStatus(move.Status), CreateSnapshotUnderLock(document), move.ObservedOnDiskStamp, move.Failure);

			lock (_stateLock)
			{
				// The destination reservation, created under this lock before the move, excluded tracked
				// documents, destination reservations, and in-flight open reservations for the destination.
				// A later open waits for the reservation instead of loading, so destinationId stays
				// unoccupied for this operation and the identity-path update cannot orphan the document by
				// failing the dictionary add.
				_documents.Remove(document.DocumentId);
				document.DocumentId = destinationId;
				document.DisplayPath = request.DestinationPath;
				document.OnDiskStamp = move.ObservedOnDiskStamp ?? request.ExpectedOnDiskStamp;
				document.Version++;
				_documents.Add(destinationId, document);

				return CreateRenameResult(
					request,
					WorkspaceDocumentRenameStatus.Renamed,
					CreateSnapshot(document),
					document.OnDiskStamp);
			}
		}
		catch (OperationCanceledException)
		{
			return CreateRenameResult(request, WorkspaceDocumentRenameStatus.Canceled, CreateSnapshotUnderLock(document));
		}
		catch (Exception exception)
		{
			return CreateRenameResult(
				request,
				WorkspaceDocumentRenameStatus.MoveFailed,
				CreateSnapshotUnderLock(document),
				failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.MoveFailed, exception.Message, exception));
		}
		finally
		{
			if (reservation is not null)
				CompleteDestinationReservation(reservation);

			CompleteOperation(operation);
		}
	}

	/// <inheritdoc />
	public async Task<WorkspaceDocumentSaveAsResult> SaveAsAsync(
		WorkspaceDocumentSaveAsRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ThrowIfDisposed();

		// See RenameAsync: the identity is validated before the destination path so the documented
		// ArgumentException holds for every input combination.
		ArgumentException.ThrowIfNullOrWhiteSpace(request.Identity.DocumentId);

		if (!TryNormalizePath(request.DestinationPath, out string destinationId))
			return CreateSaveAsResult(request, WorkspaceDocumentSaveAsStatus.InvalidPath, null);

		WorkspaceDocumentSaveAsStatus? destinationFailure = null;
		DestinationReservation? reservation = null;
		bool savesInPlace = false;
		OperationBegin begin = TryBeginOperation(
			request.Identity,
			captureSnapshot: true,
			preGateCheck: document =>
			{
				if (_documents.TryGetValue(destinationId, out LogicalDocument? destinationDocument))
				{
					// A destination that resolves to the tracked document itself (including a case variant
					// under a case-insensitive policy) is a save in place: the source file is the destination,
					// so there is no second path and no destination collision.
					if (ReferenceEquals(destinationDocument, document))
					{
						savesInPlace = true;
						return true;
					}

					destinationFailure = WorkspaceDocumentSaveAsStatus.DestinationInUse;
					return false;
				}

				if (_destinationReservations.ContainsKey(destinationId))
				{
					destinationFailure = WorkspaceDocumentSaveAsStatus.DestinationBusy;
					return false;
				}

				// An open that already holds a reservation for the destination would add its document
				// while this operation runs; the destination reservation created below excludes later opens.
				if (_openReservations.ContainsKey(destinationId))
				{
					destinationFailure = WorkspaceDocumentSaveAsStatus.DestinationBusy;
					return false;
				}

				return true;
			},
			postGateSetup: _ =>
			{
				// A save in place writes to the tracked file itself, so no destination identity is reserved
				// and no later open waits behind this operation.
				if (savesInPlace)
					return;

				reservation = new DestinationReservation(destinationId);
				_destinationReservations.Add(destinationId, reservation);
			});

		if (begin is not { Succeeded: true, Document: { } document, CapturedSnapshot: { } capturedSnapshot, Operation: { } operation })
		{
			if (destinationFailure is not null)
				return CreateSaveAsResult(request, destinationFailure.Value, begin.FailureSnapshot);

			return begin.Failure switch
			{
				OperationBeginFailure.DocumentNotFound => CreateSaveAsResult(request, WorkspaceDocumentSaveAsStatus.DocumentNotFound, null),
				OperationBeginFailure.StaleDocumentInstance => CreateSaveAsResult(request, WorkspaceDocumentSaveAsStatus.StaleDocumentInstance, begin.FailureSnapshot),
				OperationBeginFailure.StaleDocument => CreateSaveAsResult(request, WorkspaceDocumentSaveAsStatus.StaleDocument, begin.FailureSnapshot),
				_ => CreateSaveAsResult(request, WorkspaceDocumentSaveAsStatus.OperationInProgress, begin.FailureSnapshot),
			};
		}

		try
		{
			using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
				cancellationToken,
				_lifetimeCancellation.Token);
			linkedCancellation.Token.ThrowIfCancellationRequested();

			FileStamp sourceStamp = await _fileSystem
				.CaptureStampAsync(document.DocumentId, linkedCancellation.Token)
				.ConfigureAwait(false);
			if (sourceStamp != request.ExpectedOnDiskStamp)
				return CreateSaveAsResult(
					request,
					WorkspaceDocumentSaveAsStatus.ExternalFileConflict,
					CreateSnapshotUnderLock(document),
					sourceStamp,
					new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.ExternalFileConflict, "The source file changed before the save-as."));

			// A save in place replaces the source file itself, so the validated source stamp is the
			// replacement's expectation and there is no separate destination to probe. A save to a second
			// path requires the destination to be absent.
			FileStamp replacementExpectation = sourceStamp;
			if (!savesInPlace)
			{
				FileStamp destinationStamp = await _fileSystem
					.CaptureStampAsync(destinationId, linkedCancellation.Token)
					.ConfigureAwait(false);
				if (destinationStamp.Exists)
					return CreateSaveAsResult(request, WorkspaceDocumentSaveAsStatus.DestinationExists, CreateSnapshotUnderLock(document), destinationStamp);

				replacementExpectation = FileStamp.Missing;
			}

			var (replacement, resolvedStamp) = await WriteReplacementAsync(
				destinationId,
				capturedSnapshot.Content,
				capturedSnapshot.FileFormat,
				replacementExpectation,
				linkedCancellation.Token).ConfigureAwait(false);

			if (replacement.Status != WorkspaceFileReplacementStatus.Replaced)
			{
				// A conflict from the conditional replacement is a destination conflict for a save to a
				// second path; a save in place re-validated the source file itself, so the same outcome
				// means the tracked file changed again.
				WorkspaceDocumentSaveAsStatus failureStatus = replacement.Status switch
				{
					WorkspaceFileReplacementStatus.ExternalFileConflict => savesInPlace
						? WorkspaceDocumentSaveAsStatus.ExternalFileConflict
						: WorkspaceDocumentSaveAsStatus.DestinationExists,
					WorkspaceFileReplacementStatus.DestinationExists => WorkspaceDocumentSaveAsStatus.DestinationExists,
					WorkspaceFileReplacementStatus.Canceled => WorkspaceDocumentSaveAsStatus.Canceled,
					WorkspaceFileReplacementStatus.ReplacementStateUnknown => WorkspaceDocumentSaveAsStatus.ReplacementStateUnknown,
					_ => WorkspaceDocumentSaveAsStatus.WriteFailed
				};

				return CreateSaveAsResult(
					request,
					failureStatus,
					CreateSnapshotUnderLock(document),
					replacement.ObservedOnDiskStamp,
					replacement.Failure);
			}

			if (resolvedStamp is null)
			{
				return CreateSaveAsResult(
					request,
					WorkspaceDocumentSaveAsStatus.ReplacementStateUnknown,
					CreateSnapshotUnderLock(document),
					failure: CreateUnknownReplacementStampFailure());
			}

			lock (_stateLock)
			{
				// For a save to a second path, the destination reservation and the pre-gate destination checks
				// (tracked documents, destination reservations, and in-flight open reservations) prove that
				// destinationId is unoccupied for the whole operation, so the identity-path update cannot
				// orphan the document by failing the dictionary add. A save in place reuses the tracked id,
				// which the document already occupies.
				_documents.Remove(document.DocumentId);
				document.DocumentId = destinationId;
				document.DisplayPath = request.DestinationPath;
				document.OnDiskStamp = resolvedStamp.Value;
				document.PersistedContent = capturedSnapshot.Content;
				document.PersistedFileFormat = capturedSnapshot.FileFormat;
				document.Version++;

				// Follow the commit-path convention: the persisted version identifies the captured content
				// that was written and can lag the retargeted document version under a concurrent edit.
				document.PersistedVersion = capturedSnapshot.Version;
				_documents.Add(destinationId, document);
				return CreateSaveAsResult(
					request,
					WorkspaceDocumentSaveAsStatus.SavedAs,
					CreateSnapshot(document),
					document.OnDiskStamp);
			}
		}
		catch (OperationCanceledException)
		{
			return CreateSaveAsResult(request, WorkspaceDocumentSaveAsStatus.Canceled, CreateSnapshotUnderLock(document));
		}
		catch (Exception exception)
		{
			return CreateSaveAsResult(
				request,
				WorkspaceDocumentSaveAsStatus.WriteFailed,
				CreateSnapshotUnderLock(document),
				failure: new WorkspaceOperationFailure(CreateWriteFailureCode(exception), exception.Message, exception));
		}
		finally
		{
			if (reservation is not null)
				CompleteDestinationReservation(reservation);

			CompleteOperation(operation);
		}
	}

	/// <inheritdoc />
	public async Task<WorkspaceDocumentDeleteResult> DeleteAsync(
		WorkspaceDocumentDeleteRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ThrowIfDisposed();

		OperationBegin begin = TryBeginOperation(
			request.Identity,
			captureSnapshot: true,
			postGateSetup: document =>
			{
				document.DeleteOperationActive = true;
				document.DeleteCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			});

		if (begin is not { Succeeded: true, Document: { } document, CapturedSnapshot: { } capturedSnapshot, Operation: { } operation })
		{
			return begin.Failure switch
			{
				OperationBeginFailure.DocumentNotFound => CreateDeleteResult(request, WorkspaceDocumentDeleteStatus.DocumentNotFound, null),
				OperationBeginFailure.StaleDocumentInstance => CreateDeleteResult(request, WorkspaceDocumentDeleteStatus.StaleDocumentInstance, begin.FailureSnapshot),
				OperationBeginFailure.StaleDocument => CreateDeleteResult(request, WorkspaceDocumentDeleteStatus.StaleDocument, begin.FailureSnapshot),
				_ => CreateDeleteResult(request, WorkspaceDocumentDeleteStatus.OperationInProgress, begin.FailureSnapshot),
			};
		}

		try
		{
			using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
				cancellationToken,
				_lifetimeCancellation.Token);
			linkedCancellation.Token.ThrowIfCancellationRequested();

			WorkspaceFileDeleteResult deletion = await _fileSystem
				.DeleteAsync(document.DocumentId, request.ExpectedOnDiskStamp, linkedCancellation.Token)
				.ConfigureAwait(false);

			if (deletion.Status != WorkspaceFileDeleteStatus.Deleted)
				return CreateDeleteResult(
					request,
					deletion.Status == WorkspaceFileDeleteStatus.ExternalFileConflict
						? WorkspaceDocumentDeleteStatus.ExternalFileConflict
						: deletion.Status == WorkspaceFileDeleteStatus.Canceled
							? WorkspaceDocumentDeleteStatus.Canceled
							: WorkspaceDocumentDeleteStatus.DeleteFailed,
					CreateSnapshotUnderLock(document),
					deletion.ObservedOnDiskStamp,
					deletion.Failure);

			lock (_stateLock)
			{
				_documents.Remove(document.DocumentId);
				return CreateDeleteResult(request, WorkspaceDocumentDeleteStatus.Deleted, capturedSnapshot, deletion.ObservedOnDiskStamp);
			}
		}
		catch (OperationCanceledException)
		{
			return CreateDeleteResult(request, WorkspaceDocumentDeleteStatus.Canceled, CreateSnapshotUnderLock(document));
		}
		catch (Exception exception)
		{
			return CreateDeleteResult(
				request,
				WorkspaceDocumentDeleteStatus.DeleteFailed,
				CreateSnapshotUnderLock(document),
				failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.DeleteFailed, exception.Message, exception));
		}
		finally
		{
			lock (_stateLock)
			{
				document.DeleteOperationActive = false;
				document.DeleteCompletion?.TrySetResult();
				document.DeleteCompletion = null;
			}

			CompleteOperation(operation);
		}
	}

	// The temporary file must be created in the same directory as the file it replaces so the
	// replacement never crosses volumes, which File.Replace and File.Move do not support. A file id
	// that is itself a volume root has no parent directory, and the root is its own directory; the
	// process current directory must not be used because it is unrelated ambient state.
	private static string GetSameVolumeDirectory(string fileId)
		=> Path.GetDirectoryName(fileId)
			?? Path.GetPathRoot(fileId)
			?? throw new InvalidOperationException($"The document id '{fileId}' has no path root.");

	// Writes content through a same-volume temporary file and the conditional replacement, deleting
	// the temporary file in every case. The replacement result is returned together with its resolved
	// stamp: a completed replacement whose stamp could not be established reports a null stamp, and
	// the callers map that to ReplacementStateUnknown instead of installing a false baseline.
	private async Task<(WorkspaceFileReplacementResult Replacement, FileStamp? ResolvedStamp)> WriteReplacementAsync(
		string documentId,
		string content,
		TextFileFormat fileFormat,
		FileStamp expectedStamp,
		CancellationToken cancellationToken)
	{
		// The temporary file is written in the target file's directory derived from the normalized
		// document id rather than the caller-supplied display spelling, so the replacement stays on
		// the same volume regardless of how the host spelled the path.
		byte[] bytes = WorkspaceTextCodec.Encode(content, fileFormat);
		WorkspaceTemporaryFile temporaryFile = await _fileSystem
			.WriteTemporaryAsync(
				GetSameVolumeDirectory(documentId),
				bytes,
				cancellationToken)
			.ConfigureAwait(false);

		WorkspaceFileReplacementResult replacement;
		try
		{
			replacement = await _fileSystem
				.ReplaceFileAsync(temporaryFile, documentId, expectedStamp, cancellationToken)
				.ConfigureAwait(false);
		}
		finally
		{
			await TryDeleteTemporaryAsync(temporaryFile).ConfigureAwait(false);
		}

		FileStamp? resolvedStamp = replacement.Status == WorkspaceFileReplacementStatus.Replaced
			? await ResolveReplacementStampAsync(replacement, documentId, cancellationToken).ConfigureAwait(false)
			: null;

		return (replacement, resolvedStamp);
	}

	// The replacement completed but its final state could not be established: callers report
	// ReplacementStateUnknown rather than installing a false baseline.
	private static WorkspaceOperationFailure CreateUnknownReplacementStampFailure()
		=> new(
			WorkspaceOperationFailureCodes.ReplacementStateUnknown,
			"The replacement completed, but the resulting file stamp was neither reported nor captured.");

	// A file-system implementation may complete a replacement without reporting the resulting stamp
	// (IWorkspaceFileSystem.ReplaceFileAsync documents the observed stamp as optional). Recording the
	// pre-write stamp or a missing stamp in that case would be wrong: the first makes the next write
	// report a spurious conflict against the store's own replacement, and the second claims a file
	// that now exists does not. Re-capture the stamp instead; when even that fails, the caller treats
	// the replacement as state-unknown rather than installing a false baseline.
	private async Task<FileStamp?> ResolveReplacementStampAsync(
		WorkspaceFileReplacementResult replacement,
		string path,
		CancellationToken cancellationToken)
	{
		if (replacement.ObservedOnDiskStamp.HasValue)
			return replacement.ObservedOnDiskStamp;

		try
		{
			return await _fileSystem.CaptureStampAsync(path, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception)
		{
			// The replacement itself already completed, so a failed or canceled re-capture means the
			// stamp is unknown - not that the write was canceled.
			return null;
		}
	}

	// The destination already holds the replacement when this runs, so a failed cleanup must not
	// mask the replacement outcome. A leftover temporary file is silently ignored: the result
	// describes the replacement only, not the cleanup.
	private async Task TryDeleteTemporaryAsync(WorkspaceTemporaryFile temporaryFile)
	{
		try
		{
			await _fileSystem.DeleteTemporaryAsync(temporaryFile).ConfigureAwait(false);
		}
		catch (Exception)
		{
			// Cleanup is best effort and must not mask the result of the replacement.
		}
	}

	// Encoding failures are a property of the content and the selected format, not a write problem:
	// the decode paths classify the equivalent condition as InvalidEncoding, so writes do too instead
	// of reporting an unfixable encoding mismatch as a generic write failure.
	private static string CreateWriteFailureCode(Exception exception)
		=> exception is EncoderFallbackException
			? WorkspaceOperationFailureCodes.InvalidEncoding
			: WorkspaceOperationFailureCodes.WriteFailed;
}

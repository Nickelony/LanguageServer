using Nickelony.IDEKit.Workspace.Documents.FileSystem;
using System.Text;
using static Nickelony.IDEKit.Workspace.Documents.WorkspaceDocumentResultFactory;

namespace Nickelony.IDEKit.Workspace.Documents;

public sealed partial class WorkspaceDocumentStore
{
	/// <inheritdoc />
	public WorkspaceDocumentMutationResult Replace(WorkspaceDocumentReplaceRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		ThrowIfDisposed();
		ArgumentException.ThrowIfNullOrWhiteSpace(request.Identity.DocumentId);
		ArgumentNullException.ThrowIfNull(request.Content);

		// The shared codec validation rejects an undefined encoding and the Windows-1252 and
		// byte-order mark combination here so an unencodable format never reaches the tracked document,
		// where every later commit would fail.
		WorkspaceTextCodec.EnsureEncodable(request.FileFormat, nameof(request));

		lock (_stateLock)
		{
			ThrowIfDisposedUnderLock();

			if (!_documents.TryGetValue(request.Identity.DocumentId, out LogicalDocument? document))
				return CreateMutationResult(request, WorkspaceDocumentMutationStatus.DocumentNotFound, null);

			if (document.DocumentKey != request.Identity.DocumentKey)
				return CreateMutationResult(
					request,
					WorkspaceDocumentMutationStatus.StaleDocumentInstance,
					CreateSnapshot(document));

			if (document.Version != request.Identity.Version)
				return CreateMutationResult(
					request,
					WorkspaceDocumentMutationStatus.StaleDocument,
					CreateSnapshot(document));

			if (document.DeleteOperationActive)
				return CreateMutationResult(
					request,
					WorkspaceDocumentMutationStatus.OperationInProgress,
					CreateSnapshot(document));

			if (string.Equals(document.Content, request.Content, StringComparison.Ordinal)
				&& document.FileFormat == request.FileFormat)
			{
				return CreateMutationResult(
					request,
					WorkspaceDocumentMutationStatus.NoChange,
					CreateSnapshot(document));
			}

			document.Content = request.Content;
			document.FileFormat = request.FileFormat;
			document.Version++;

			return CreateMutationResult(
				request,
				WorkspaceDocumentMutationStatus.Changed,
				CreateSnapshot(document));
		}
	}

	/// <inheritdoc />
	public WorkspaceDocumentMutationResult Discard(WorkspaceDocumentDiscardRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		ThrowIfDisposed();
		ArgumentException.ThrowIfNullOrWhiteSpace(request.Identity.DocumentId);

		lock (_stateLock)
		{
			ThrowIfDisposedUnderLock();

			if (!_documents.TryGetValue(request.Identity.DocumentId, out LogicalDocument? document))
				return CreateMutationResult(request, WorkspaceDocumentMutationStatus.DocumentNotFound, null);

			if (document.DocumentKey != request.Identity.DocumentKey)
				return CreateMutationResult(
					request,
					WorkspaceDocumentMutationStatus.StaleDocumentInstance,
					CreateSnapshot(document));

			if (document.Version != request.Identity.Version)
				return CreateMutationResult(
					request,
					WorkspaceDocumentMutationStatus.StaleDocument,
					CreateSnapshot(document));

			// Gate transitions happen under the state lock, so the gate's count is an exact probe and
			// the gate does not need to be acquired and released to observe it.
			if (document.DiskOperationGate.CurrentCount == 0)
				return CreateMutationResult(
					request,
					WorkspaceDocumentMutationStatus.OperationInProgress,
					CreateSnapshot(document));

			if (!document.IsDirty)
				return CreateMutationResult(
					request,
					WorkspaceDocumentMutationStatus.NoChange,
					CreateSnapshot(document));

			document.Content = document.PersistedContent;
			document.FileFormat = document.PersistedFileFormat;
			document.Version++;
			document.PersistedVersion = document.Version;

			return CreateMutationResult(
				request,
				WorkspaceDocumentMutationStatus.Changed,
				CreateSnapshot(document));
		}
	}

	/// <inheritdoc />
	public Task<WorkspaceDocumentCommitResult> CommitAsync(
		WorkspaceDocumentCommitRequest request,
		CancellationToken cancellationToken = default)
		=> CommitCoreAsync(request, forceWrite: false, cancellationToken);

	private async Task<WorkspaceDocumentCommitResult> CommitCoreAsync(
		WorkspaceDocumentCommitRequest request,
		bool forceWrite,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		ThrowIfDisposed();

		OperationBegin begin = TryBeginOperation(
			request.Identity,
			captureSnapshot: true);

		if (begin is not { Succeeded: true, Document: { } document, CapturedSnapshot: { } capturedSnapshot, Operation: { } operation })
		{
			return begin.Failure switch
			{
				OperationBeginFailure.DocumentNotFound => CreateCommitResult(request, WorkspaceDocumentCommitStatus.DocumentNotFound, null),
				OperationBeginFailure.StaleDocumentInstance => CreateCommitResult(request, WorkspaceDocumentCommitStatus.StaleDocumentInstance, begin.FailureSnapshot),
				OperationBeginFailure.StaleDocument => CreateCommitResult(request, WorkspaceDocumentCommitStatus.StaleDocument, begin.FailureSnapshot),
				_ => CreateCommitResult(request, WorkspaceDocumentCommitStatus.OperationInProgress, begin.FailureSnapshot),
			};
		}

		try
		{
			using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
				cancellationToken,
				_lifetimeCancellation.Token);
			linkedCancellation.Token.ThrowIfCancellationRequested();

			// The expected on-disk stamp is the caller's precondition; the conditional replacement
			// validates it against its own capture of the destination, so an accepted commit that writes
			// reads and hashes the file once. A clean tracked document whose file exists has nothing to
			// write: the commit is a no-op without re-reading the file, so the caller's expectation is
			// validated against the tracked stamp instead - a comparison that catches an expectation
			// which disagrees with the state the store last observed. A later write that does occur
			// still validates the expectation against a fresh capture before replacing.
			if (!forceWrite && !capturedSnapshot.IsDirty && capturedSnapshot.ExistsOnDisk)
			{
				if (request.ExpectedOnDiskStamp != capturedSnapshot.OnDiskStamp)
				{
					return CreateCommitResult(
						request,
						WorkspaceDocumentCommitStatus.ExternalFileConflict,
						capturedSnapshot,
						capturedSnapshot.OnDiskStamp);
				}

				lock (_stateLock)
					return CreateCommitResult(
						request,
						WorkspaceDocumentCommitStatus.Committed,
						CreateSnapshot(document));
			}

			var (replacement, resolvedStamp) = await WriteReplacementAsync(
				capturedSnapshot.DocumentId,
				capturedSnapshot.Content,
				capturedSnapshot.FileFormat,
				request.ExpectedOnDiskStamp,
				linkedCancellation.Token).ConfigureAwait(false);

			if (replacement.Status == WorkspaceFileReplacementStatus.Replaced)
			{
				if (resolvedStamp is null)
				{
					return CreateCurrentCommitResult(
						request,
						document,
						WorkspaceDocumentCommitStatus.ReplacementStateUnknown,
						null,
						CreateUnknownReplacementStampFailure());
				}

				return InstallCommittedBaseline(request, document, capturedSnapshot, resolvedStamp.Value);
			}

			return replacement.Status switch
			{
				WorkspaceFileReplacementStatus.ExternalFileConflict => UpdateObservedConflict(
					request,
					document,
					replacement.ObservedOnDiskStamp),
				WorkspaceFileReplacementStatus.Canceled => CreateCommitResult(
					request,
					WorkspaceDocumentCommitStatus.Canceled,
					CreateSnapshotUnderLock(document)),
				WorkspaceFileReplacementStatus.DestinationExists => CreateCurrentCommitResult(
					request,
					document,
					WorkspaceDocumentCommitStatus.WriteFailed,
					replacement.ObservedOnDiskStamp,
					replacement.Failure),
				WorkspaceFileReplacementStatus.ReplacementStateUnknown => CreateCurrentCommitResult(
					request,
					document,
					WorkspaceDocumentCommitStatus.ReplacementStateUnknown,
					replacement.ObservedOnDiskStamp,
					replacement.Failure),
				_ => CreateCurrentCommitResult(
					request,
					document,
					WorkspaceDocumentCommitStatus.WriteFailed,
					replacement.ObservedOnDiskStamp,
					replacement.Failure)
			};
		}
		catch (OperationCanceledException)
		{
			lock (_stateLock)
				return CreateCommitResult(
					request,
					WorkspaceDocumentCommitStatus.Canceled,
					CreateSnapshot(document));
		}
		catch (Exception exception)
		{
			lock (_stateLock)
				return CreateCommitResult(
					request,
					WorkspaceDocumentCommitStatus.WriteFailed,
					CreateSnapshot(document),
					failure: new WorkspaceOperationFailure(CreateWriteFailureCode(exception), exception.Message, exception));
		}
		finally
		{
			CompleteOperation(operation);
		}
	}

	/// <inheritdoc />
	public async Task<WorkspaceDocumentReloadResult> ReloadAsync(
		WorkspaceDocumentReloadRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ThrowIfDisposed();

		// The dirty branch resolves the reload from file stamps alone, so it bypasses the
		// per-document disk gate instead of waiting behind an in-flight write.
		OperationBegin begin = TryBeginOperation(
			request.Identity,
			captureSnapshot: true,
			skipGateWhen: static (_, snapshot) => snapshot?.IsDirty == true);

		if (begin is not { Succeeded: true, Document: { } document, CapturedSnapshot: { } capturedSnapshot })
		{
			return begin.Failure switch
			{
				OperationBeginFailure.DocumentNotFound => CreateReloadResult(request, WorkspaceDocumentReloadStatus.DocumentNotFound, null),
				OperationBeginFailure.StaleDocumentInstance => CreateReloadResult(request, WorkspaceDocumentReloadStatus.StaleDocumentInstance, begin.FailureSnapshot),
				OperationBeginFailure.StaleDocument => CreateReloadResult(request, WorkspaceDocumentReloadStatus.StaleDocument, begin.FailureSnapshot),
				_ => CreateReloadResult(request, WorkspaceDocumentReloadStatus.OperationInProgress, begin.FailureSnapshot),
			};
		}

		OperationRegistration? operation = begin.Operation;

		if (capturedSnapshot.IsDirty)
		{
			// The dirty branch is not registered as an active operation, so disposal can complete
			// between the operation preamble and this point. Linking against the lifetime source of a
			// disposed store throws ObjectDisposedException; the documented outcome for an in-flight
			// dirty reload is cancellation, so an already-completed disposal reports that instead.
			using CancellationTokenSource? linkedCancellation = TryCreateLifetimeLinkedCancellation(cancellationToken);
			if (linkedCancellation is null)
				return CreateReloadResult(
					request,
					WorkspaceDocumentReloadStatus.Canceled,
					capturedSnapshot);

			try
			{
				FileStamp observedStamp = await _fileSystem
					.CaptureStampAsync(capturedSnapshot.DocumentId, linkedCancellation.Token)
					.ConfigureAwait(false);

				lock (_stateLock)
				{
					// Dirty reloads bypass the per-document disk gate and are not registered as active operations,
					// so disposal can complete while the stamp capture is in flight. Re-check disposal
					// before mutating the (possibly detached) document or returning its snapshot.
					if (_disposed)
						return CreateReloadResult(
							request,
							WorkspaceDocumentReloadStatus.Canceled,
							CreateSnapshot(document),
							observedStamp);

					// Deletion or replacement of the tracked instance can also complete while the stamp capture
					// is in flight because the dirty branch holds no gate. The captured instance is no longer
					// authoritative when the id maps to a different instance or to nothing at all.
					if (!_documents.TryGetValue(request.Identity.DocumentId, out LogicalDocument? trackedDocument))
					{
						// The instance can also be retargeted by a rename or save-as while the capture is in
						// flight: the id no longer resolves, but the instance key still does. Report the stale
						// instance with its current snapshot instead of a not-found that the caller cannot
						// distinguish from a deleted document.
						trackedDocument = FindDocumentByKey(request.Identity.DocumentKey);
						if (trackedDocument is null)
						{
							return CreateReloadResult(
								request,
								WorkspaceDocumentReloadStatus.DocumentNotFound,
								null,
								observedStamp);
						}

						return CreateReloadResult(
							request,
							WorkspaceDocumentReloadStatus.StaleDocumentInstance,
							CreateSnapshot(trackedDocument),
							observedStamp);
					}

					if (!ReferenceEquals(trackedDocument, document))
						return CreateReloadResult(
							request,
							WorkspaceDocumentReloadStatus.StaleDocumentInstance,
							CreateSnapshot(trackedDocument),
							observedStamp);

					if (document.Version != request.Identity.Version)
						return CreateReloadResult(
							request,
							WorkspaceDocumentReloadStatus.StaleDocument,
							CreateSnapshot(document),
							observedStamp);

					// Nothing changed externally since the store last observed the file: a dirty document has
					// nothing to reload, and a spurious watcher event must not raise a conflict prompt.
					if (observedStamp == document.OnDiskStamp)
						return CreateReloadResult(
							request,
							WorkspaceDocumentReloadStatus.Unchanged,
							CreateSnapshot(document),
							observedStamp);

					// The observed stamp is not installed on the document: a commit (including its failure
					// paths) can install a newer stamp without advancing the version, and this branch holds
					// no gate, so writing the observation here could replace a newer record with a stale one.
					// Callers resolve the conflict with the stamp carried by this result.
					return CreateReloadResult(
						request,
						WorkspaceDocumentReloadStatus.ExternalFileConflict,
						CreateSnapshot(document),
						observedStamp);
				}
			}
			catch (OperationCanceledException)
			{
				return CreateReloadResult(
					request,
					WorkspaceDocumentReloadStatus.Canceled,
					capturedSnapshot);
			}
			catch (Exception exception)
			{
				return CreateReloadResult(
					request,
					WorkspaceDocumentReloadStatus.ReadFailed,
					capturedSnapshot,
					failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.ReadFailed, exception.Message, exception));
			}
		}

		try
		{
			using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
				cancellationToken,
				_lifetimeCancellation.Token);
			linkedCancellation.Token.ThrowIfCancellationRequested();

			WorkspaceFileReadResult file = await _fileSystem
				.ReadAsync(capturedSnapshot.DocumentId, linkedCancellation.Token)
				.ConfigureAwait(false);

			if (file.IsDirectory)
			{
				// A tracked file that was replaced by a directory is not a reloadable document: adopting it
				// as empty content would produce a document whose writes and deletes can never succeed.
				lock (_stateLock)
				{
					return CreateReloadResult(
						request,
						WorkspaceDocumentReloadStatus.ReadFailed,
						CreateSnapshot(document),
						failure: new WorkspaceOperationFailure(
							WorkspaceOperationFailureCodes.IsDirectory,
							"The path exists as a directory, not a file."));
				}
			}

			string content = file.Content;
			// A missing file has no on-disk format to adopt: the document keeps its current format.
			TextFileFormat fileFormat = file.OnDiskStamp.Exists ? file.FileFormat : document.FileFormat;
			if (file.RawBytes.HasValue)
				content = WorkspaceTextCodec.Decode(file.RawBytes.Value.Span, document.NoBomEncoding, out fileFormat);

			lock (_stateLock)
			{
				if (document.Version != request.Identity.Version)
					return CreateReloadResult(
						request,
						WorkspaceDocumentReloadStatus.StaleDocument,
						CreateSnapshot(document),
						file.OnDiskStamp);

				// The unchanged outcome is defined by the tracked stamp, not by the caller's expectation:
				// the tracked logical content corresponds to the stamp the store recorded, so a file whose
				// stamp matches it needs no adoption. An expectation that matches the disk while the tracked
				// stamp does not means the file changed after the store last observed it, and the new content
				// is adopted below.
				if (file.OnDiskStamp == document.OnDiskStamp)
				{
					return CreateReloadResult(
						request,
						WorkspaceDocumentReloadStatus.Unchanged,
						CreateSnapshot(document),
						file.OnDiskStamp);
				}

				document.Content = content;
				document.PersistedContent = content;
				document.FileFormat = fileFormat;
				document.PersistedFileFormat = fileFormat;
				document.Version++;
				document.PersistedVersion = document.Version;
				document.OnDiskStamp = file.OnDiskStamp;

				return CreateReloadResult(
					request,
					WorkspaceDocumentReloadStatus.Reloaded,
					CreateSnapshot(document),
					file.OnDiskStamp);
			}
		}
		catch (OperationCanceledException)
		{
			lock (_stateLock)
				return CreateReloadResult(
					request,
					WorkspaceDocumentReloadStatus.Canceled,
					CreateSnapshot(document));
		}
		catch (DecoderFallbackException exception)
		{
			// A decode failure is an encoding problem, not a read problem: the code mirrors the open and
			// conflict-resolution paths so hosts branch on InvalidEncoding for the same root cause.
			lock (_stateLock)
				return CreateReloadResult(
					request,
					WorkspaceDocumentReloadStatus.ReadFailed,
					CreateSnapshot(document),
					failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.InvalidEncoding, exception.Message, exception));
		}
		catch (Exception exception)
		{
			lock (_stateLock)
				return CreateReloadResult(
					request,
					WorkspaceDocumentReloadStatus.ReadFailed,
					CreateSnapshot(document),
					failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.ReadFailed, exception.Message, exception));
		}
		finally
		{
			if (operation is not null)
				CompleteOperation(operation);
		}
	}

	/// <inheritdoc />
	public async Task<WorkspaceDocumentConflictResolutionResult> ResolveExternalConflictAsync(
		WorkspaceDocumentConflictResolutionRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ThrowIfDisposed();

		// An undefined choice must not silently take the UseDisk branch, which would discard the
		// caller's unpublishable logical content without an explicit decision.
		if (request.Choice is not (WorkspaceDocumentConflictResolutionChoice.UseLogical
			or WorkspaceDocumentConflictResolutionChoice.UseDisk))
		{
			throw new ArgumentOutOfRangeException(
				nameof(request),
				request.Choice,
				"The conflict resolution choice is not a defined value.");
		}

		if (request.Choice == WorkspaceDocumentConflictResolutionChoice.UseLogical)
		{
			WorkspaceDocumentCommitResult commitResult = await CommitCoreAsync(
				new WorkspaceDocumentCommitRequest(
					request.Identity,
					request.ObservedOnDiskStamp),
				forceWrite: true,
				cancellationToken).ConfigureAwait(false);

			return CreateConflictResolutionFromCommit(request, commitResult);
		}

		OperationBegin begin = TryBeginOperation(
			request.Identity,
			captureSnapshot: true);

		if (begin is not { Succeeded: true, Document: { } document, CapturedSnapshot: { } capturedSnapshot, Operation: { } operation })
		{
			return begin.Failure switch
			{
				OperationBeginFailure.DocumentNotFound => CreateConflictResolutionResult(request, WorkspaceDocumentConflictResolutionStatus.DocumentNotFound, null),
				OperationBeginFailure.StaleDocumentInstance => CreateConflictResolutionResult(request, WorkspaceDocumentConflictResolutionStatus.StaleDocumentInstance, begin.FailureSnapshot),
				OperationBeginFailure.StaleDocument => CreateConflictResolutionResult(request, WorkspaceDocumentConflictResolutionStatus.StaleDocument, begin.FailureSnapshot),
				_ => CreateConflictResolutionResult(request, WorkspaceDocumentConflictResolutionStatus.OperationInProgress, begin.FailureSnapshot),
			};
		}

		try
		{
			using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
				cancellationToken,
				_lifetimeCancellation.Token);
			linkedCancellation.Token.ThrowIfCancellationRequested();

			// The stamp carried by the read describes exactly the bytes that are about to be adopted,
			// so it is the only evidence the adoption needs. Re-capturing the stamp would double the
			// I/O for a large file without closing the race: the file can still change before the
			// adoption below takes the state lock.
			WorkspaceFileReadResult file = await _fileSystem
				.ReadAsync(capturedSnapshot.DocumentId, linkedCancellation.Token)
				.ConfigureAwait(false);

			if (file.IsDirectory)
			{
				// A directory is not disk content to adopt: the resolution fails instead of producing a
				// document whose writes and deletes can never succeed.
				lock (_stateLock)
				{
					return CreateConflictResolutionResult(
						request,
						WorkspaceDocumentConflictResolutionStatus.ReadFailed,
						CreateSnapshot(document),
						failure: new WorkspaceOperationFailure(
							WorkspaceOperationFailureCodes.IsDirectory,
							"The path exists as a directory, not a file."));
				}
			}

			if (file.OnDiskStamp != request.ObservedOnDiskStamp)
				return CreateConflictResolutionResult(
					request,
					WorkspaceDocumentConflictResolutionStatus.ExternalFileConflict,
					CreateSnapshotUnderLock(document),
					file.OnDiskStamp,
					new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.ExternalFileConflict, "The disk file changed again after the conflict was observed."));

			string content = file.Content;
			// A missing file has no on-disk format to adopt: the document keeps its current format.
			TextFileFormat fileFormat = file.OnDiskStamp.Exists ? file.FileFormat : document.FileFormat;
			if (file.RawBytes.HasValue)
				content = WorkspaceTextCodec.Decode(file.RawBytes.Value.Span, document.NoBomEncoding, out fileFormat);

			lock (_stateLock)
			{
				if (document.Version != request.Identity.Version)
					return CreateConflictResolutionResult(
						request,
						WorkspaceDocumentConflictResolutionStatus.StaleDocument,
						CreateSnapshot(document),
						file.OnDiskStamp);

				document.Content = content;
				document.PersistedContent = content;
				document.FileFormat = fileFormat;
				document.PersistedFileFormat = fileFormat;
				document.Version++;
				document.PersistedVersion = document.Version;
				document.OnDiskStamp = file.OnDiskStamp;

				return CreateConflictResolutionResult(
					request,
					WorkspaceDocumentConflictResolutionStatus.ResolvedWithDisk,
					CreateSnapshot(document),
					file.OnDiskStamp);
			}
		}
		catch (OperationCanceledException)
		{
			lock (_stateLock)
				return CreateConflictResolutionResult(
					request,
					WorkspaceDocumentConflictResolutionStatus.Canceled,
					CreateSnapshot(document));
		}
		catch (DecoderFallbackException exception)
		{
			lock (_stateLock)
				return CreateConflictResolutionResult(
					request,
					WorkspaceDocumentConflictResolutionStatus.ReadFailed,
					CreateSnapshot(document),
					failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.InvalidEncoding, exception.Message, exception));
		}
		catch (Exception exception)
		{
			lock (_stateLock)
				return CreateConflictResolutionResult(
					request,
					WorkspaceDocumentConflictResolutionStatus.ReadFailed,
					CreateSnapshot(document),
					failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.ReadFailed, exception.Message, exception));
		}
		finally
		{
			CompleteOperation(operation);
		}
	}

	private WorkspaceDocumentCommitResult InstallCommittedBaseline(
		WorkspaceDocumentCommitRequest request,
		LogicalDocument document,
		WorkspaceDocumentSnapshot capturedSnapshot,
		FileStamp onDiskStamp)
	{
		lock (_stateLock)
		{
			document.PersistedContent = capturedSnapshot.Content;
			document.PersistedFileFormat = capturedSnapshot.FileFormat;
			document.PersistedVersion = capturedSnapshot.Version;
			document.OnDiskStamp = onDiskStamp;

			return CreateCommitResult(
				request,
				WorkspaceDocumentCommitStatus.Committed,
				CreateSnapshot(document),
				onDiskStamp);
		}
	}

	private WorkspaceDocumentCommitResult UpdateObservedConflict(
		WorkspaceDocumentCommitRequest request,
		LogicalDocument document,
		FileStamp? observedOnDiskStamp)
	{
		lock (_stateLock)
		{
			if (observedOnDiskStamp.HasValue)
				document.OnDiskStamp = observedOnDiskStamp.Value;

			return CreateCommitResult(
				request,
				WorkspaceDocumentCommitStatus.ExternalFileConflict,
				CreateSnapshot(document),
				observedOnDiskStamp);
		}
	}

	private WorkspaceDocumentCommitResult CreateCurrentCommitResult(
		WorkspaceDocumentCommitRequest request,
		LogicalDocument document,
		WorkspaceDocumentCommitStatus status,
		FileStamp? observedOnDiskStamp,
		WorkspaceOperationFailure? failure)
	{
		lock (_stateLock)
		{
			if (observedOnDiskStamp.HasValue)
				document.OnDiskStamp = observedOnDiskStamp.Value;

			return CreateCommitResult(request, status, CreateSnapshot(document), observedOnDiskStamp, failure);
		}
	}
}

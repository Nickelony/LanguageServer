using Nickelony.IDEKit.Workspace.Documents.FileSystem;
using System.Diagnostics;
using System.Text;
using static Nickelony.IDEKit.Workspace.Documents.WorkspaceDocumentPath;
using static Nickelony.IDEKit.Workspace.Documents.WorkspaceDocumentResultFactory;

namespace Nickelony.IDEKit.Workspace.Documents;

public sealed partial class WorkspaceDocumentStore
{
	/// <inheritdoc />
	public async Task<WorkspaceDocumentOpenResult> OpenAsync(
		string? filePath,
		WorkspaceDocumentOpenOptions options,
		CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();

		// The format values are validated up front so an unencodable format cannot be stored on a
		// document and then surface only when the document is first committed.
		WorkspaceTextCodec.EnsureDefinedEncoding(options.NoBomEncoding, nameof(options));
		WorkspaceTextCodec.EnsureEncodable(options.NewFileFormat, nameof(options));

		if (filePath is null || string.IsNullOrWhiteSpace(filePath))
			return new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenStatus.InvalidPath, null);

		if (!TryNormalizePath(filePath, out string documentId))
			return new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenStatus.InvalidPath, null);

		while (true)
		{
			DestinationReservation? destinationReservation = null;
			OpenReservation? existingReservation = null;
			Task? pendingDelete = null;
			bool isLoader = false;

			lock (_stateLock)
			{
				ThrowIfDisposedUnderLock();

				if (_documents.TryGetValue(documentId, out LogicalDocument? document))
				{
					// A delete holds the document gate, so operations that need that gate report
					// OperationInProgress while it is in flight. Reporting the document as already open would
					// hand the caller an instance that the in-flight delete is about to remove, so wait for the
					// delete to resolve and re-evaluate the path instead.
					if (document.DeleteOperationActive)
						pendingDelete = document.DeleteCompletion?.Task ?? Task.CompletedTask;
					else
						return new WorkspaceDocumentOpenResult(
							WorkspaceDocumentOpenStatus.AlreadyOpen,
							CreateSnapshot(document));
				}
				else if (_destinationReservations.TryGetValue(documentId, out destinationReservation))
				{
					// The path is reserved as the destination of an in-flight move or save-as: wait for that
					// operation to resolve and re-evaluate the path.
				}
				else if (_openReservations.TryGetValue(documentId, out existingReservation))
				{
					// Another open owns the load for this path: wait for its result instead of loading
					// the same file twice.
				}
				else
				{
					existingReservation = new OpenReservation();
					_openReservations.Add(documentId, existingReservation);
					isLoader = true;
				}
			}

			if (pendingDelete is not null)
			{
				try
				{
					await pendingDelete.WaitAsync(cancellationToken).ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
					return new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenStatus.Canceled, null);
				}

				continue;
			}

			if (destinationReservation is not null)
			{
				try
				{
					await destinationReservation.Completion.Task
						.WaitAsync(cancellationToken)
						.ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
					return new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenStatus.Canceled, null);
				}

				continue;
			}

			// Every path that reaches this point holds a reservation: either one that was already
			// registered for the path or the one created above for this call.
			OpenReservation reservation = existingReservation
				?? throw new UnreachableException("An open reservation was expected for this path.");

			if (!isLoader)
			{
				// Another open owns the load for this path: wait for its result and re-evaluate the path in
				// this loop. The wait is iterative rather than recursive, so many competing callers cannot
				// build a call stack; a loader that failed or was canceled makes this call a fresh attempt.
				WorkspaceDocumentOpenResult ownerResult;
				try
				{
					ownerResult = await reservation.Completion.Task
						.WaitAsync(cancellationToken)
						.ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
					return new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenStatus.Canceled, null);
				}

				if (ownerResult.Status == WorkspaceDocumentOpenStatus.Opened)
					return ownerResult with { Status = WorkspaceDocumentOpenStatus.AlreadyOpen };

				continue;
			}

			return await LoadReservedDocumentAsync(
				documentId,
				filePath,
				options,
				reservation,
				cancellationToken).ConfigureAwait(false);
		}
	}

	/// <inheritdoc />
	public bool TryGetSnapshot(string? filePath, out WorkspaceDocumentSnapshot? snapshot)
	{
		// Disposal is checked before path validity so every member follows the documented
		// ObjectDisposedException contract for a disposed store, regardless of the supplied path.
		ThrowIfDisposed();

		if (filePath is null || string.IsNullOrWhiteSpace(filePath))
		{
			snapshot = null;
			return false;
		}

		if (!TryNormalizePath(filePath, out string documentId))
		{
			snapshot = null;
			return false;
		}

		lock (_stateLock)
		{
			ThrowIfDisposedUnderLock();

			if (_documents.TryGetValue(documentId, out LogicalDocument? document))
			{
				snapshot = CreateSnapshot(document);
				return true;
			}
		}

		snapshot = null;
		return false;
	}

	/// <inheritdoc />
	public IReadOnlyList<WorkspaceDocumentSnapshot> GetSnapshotsUnderDirectory(string? directoryPath)
	{
		ThrowIfDisposed();

		// Null and blank directory paths follow the same convention as the other path-accepting
		// members: they are treated as invalid input and return no snapshots instead of throwing.
		if (string.IsNullOrWhiteSpace(directoryPath))
			return [];

		if (!TryNormalizePath(directoryPath, out string normalizedDirectoryPath))
			return [];

		string directoryPrefix = GetDirectoryPrefix(normalizedDirectoryPath);
		StringComparison comparison = _pathComparison.Comparison;

		lock (_stateLock)
		{
			ThrowIfDisposedUnderLock();

			// One pass over the tracked documents; the result is sorted by id without the
			// intermediate LINQ buffers of a filter-order-project chain.
			List<WorkspaceDocumentSnapshot> snapshots = [];
			foreach (LogicalDocument document in _documents.Values)
			{
				if (document.DocumentId.StartsWith(directoryPrefix, comparison))
					snapshots.Add(CreateSnapshot(document));
			}

			snapshots.Sort((left, right) => _pathComparison.Comparer.Compare(left.DocumentId, right.DocumentId));
			return snapshots;
		}
	}

	private async Task<WorkspaceDocumentOpenResult> LoadReservedDocumentAsync(
		string documentId,
		string filePath,
		WorkspaceDocumentOpenOptions options,
		OpenReservation reservation,
		CancellationToken cancellationToken)
	{
		WorkspaceDocumentOpenResult result;

		using (CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
			cancellationToken,
			_lifetimeCancellation.Token))
		{
			try
			{
				linkedCancellation.Token.ThrowIfCancellationRequested();

				WorkspaceFileReadResult file = await _fileSystem
					.ReadAsync(documentId, linkedCancellation.Token)
					.ConfigureAwait(false);

				linkedCancellation.Token.ThrowIfCancellationRequested();

				if (file.IsDirectory)
				{
					// A directory is not a document: tracking it would produce a document whose writes and
					// deletes can never succeed. The path is rejected with a dedicated outcome instead of
					// being tracked as a new empty document for a missing file.
					result = new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenStatus.IsDirectory, null);
					CompleteReservation(documentId, reservation, result);
					return result;
				}

				if (!file.OnDiskStamp.Exists && !options.CreateIfMissing)
				{
					// The caller requires an existing file; opening the path as new content would
					// silently turn a missing file into a tracked document.
					result = new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenStatus.NotFound, null);
					CompleteReservation(documentId, reservation, result);
					return result;
				}

				string content = file.Content;
				TextFileFormat fileFormat = file.OnDiskStamp.Exists
					? file.FileFormat
					: options.NewFileFormat;
				if (file.RawBytes.HasValue)
					content = WorkspaceTextCodec.Decode(file.RawBytes.Value.Span, options.NoBomEncoding, out fileFormat);

				lock (_stateLock)
				{
					if (_disposed)
					{
						result = new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenStatus.Canceled, null);
					}
					else
					{
						LogicalDocument document = new(
							new WorkspaceDocumentKey(Guid.NewGuid()),
							documentId,
							filePath,
							content,
							fileFormat,
							file.OnDiskStamp,
							options.NoBomEncoding);

						// The snapshot is created before the document becomes tracked: an exception while
						// snapshotting must not leave a tracked document behind a reported load failure.
						WorkspaceDocumentSnapshot snapshot = CreateSnapshot(document);

						_documents.Add(documentId, document);
						result = new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenStatus.Opened, snapshot);
					}

					CompleteReservationUnderLock(documentId, reservation, result);
				}
			}
			catch (OperationCanceledException)
			{
				result = new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenStatus.Canceled, null);
				CompleteReservation(documentId, reservation, result);
			}
			catch (Exception exception)
			{
				result = new WorkspaceDocumentOpenResult(
					WorkspaceDocumentOpenStatus.LoadFailed,
					null,
					new WorkspaceOperationFailure(
						exception is DecoderFallbackException ? WorkspaceOperationFailureCodes.InvalidEncoding : WorkspaceOperationFailureCodes.LoadFailed,
						exception.Message,
						exception));
				CompleteReservation(documentId, reservation, result);
			}
		}

		return result;
	}

	private void CompleteReservation(
		string documentId,
		OpenReservation reservation,
		WorkspaceDocumentOpenResult result)
	{
		lock (_stateLock)
		{
			CompleteReservationUnderLock(documentId, reservation, result);
		}
	}

	private void CompleteReservationUnderLock(
		string documentId,
		OpenReservation reservation,
		WorkspaceDocumentOpenResult result)
	{
		if (_openReservations.TryGetValue(documentId, out OpenReservation? current)
			&& ReferenceEquals(current, reservation))
		{
			_openReservations.Remove(documentId);
		}

		reservation.Completion.TrySetResult(result);
	}
}

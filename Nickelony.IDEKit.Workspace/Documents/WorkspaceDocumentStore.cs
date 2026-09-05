using System.Diagnostics.CodeAnalysis;
using System.Text;
using static Nickelony.IDEKit.Workspace.Documents.WorkspaceDocumentPath;
using static Nickelony.IDEKit.Workspace.Documents.WorkspaceDocumentResultFactory;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Owns logical workspace document content, persistence state, and filesystem operations.
/// </summary>
/// <remarks>
/// Document IDs are normalized full paths and are compared using the current platform's path
/// comparison rules. Public operations are safe to call concurrently; document mutations are
/// serialized under the store state lock and disk operations use per-document gates. Dispose waits
/// for tracked open reservations and registered disk operations before releasing document resources.
/// </remarks>
public sealed class WorkspaceDocumentStore : IWorkspaceDocumentStore
{
	private readonly object _stateLock = new();
	private readonly IWorkspaceFileSystem _fileSystem;
	private readonly Dictionary<string, LogicalDocument> _documents;
	private readonly Dictionary<string, OpenReservation> _openReservations;
	private readonly Dictionary<string, DestinationReservation> _destinationReservations;
	private readonly HashSet<OperationRegistration> _activeOperations = [];
	private readonly CancellationTokenSource _lifetimeCancellation = new();
	private Task? _disposeTask;
	private bool _disposed;

	/// <summary>
	/// Initializes a new instance of the <see cref="WorkspaceDocumentStore"/> class.
	/// </summary>
	/// <param name="fileSystem">The file-system implementation used for reads, writes, moves, and deletes.</param>
	public WorkspaceDocumentStore(IWorkspaceFileSystem fileSystem)
	{
		ArgumentNullException.ThrowIfNull(fileSystem);

		_fileSystem = fileSystem;
		_documents = new Dictionary<string, LogicalDocument>(GetPathComparer());
		_openReservations = new Dictionary<string, OpenReservation>(GetPathComparer());
		_destinationReservations = new Dictionary<string, DestinationReservation>(GetPathComparer());
	}

	/// <inheritdoc />
	public async Task<WorkspaceDocumentOpenResult> OpenAsync(
		string? filePath,
		WorkspaceDocumentOpenOptions options,
		CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();

		if (filePath is null || string.IsNullOrWhiteSpace(filePath))
			return new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenStatus.InvalidPath, null);

		if (!TryNormalizePath(filePath, out string documentId))
			return new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenStatus.InvalidPath, null);

		while (true)
		{
			DestinationReservation? destinationReservation = null;
			OpenReservation? reservation;
			bool isLoader;

			lock (_stateLock)
			{
				ThrowIfDisposedUnderLock();

				if (_documents.TryGetValue(documentId, out LogicalDocument? document))
					return new WorkspaceDocumentOpenResult(
						WorkspaceDocumentOpenStatus.AlreadyOpen,
						CreateSnapshot(document));

				if (_destinationReservations.TryGetValue(documentId, out destinationReservation))
				{
					reservation = null;
					isLoader = false;
				}
				else if (_openReservations.TryGetValue(documentId, out OpenReservation? existingReservation))
				{
					reservation = existingReservation;
					isLoader = false;
				}
				else
				{
					reservation = new OpenReservation();
					_openReservations.Add(documentId, reservation);
					isLoader = true;
				}
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
					return new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenStatus.Cancelled, null);
				}

				continue;
			}

			if (!isLoader)
				return await WaitForExistingOpenAsync(
					reservation ?? throw new InvalidOperationException("An existing open reservation was not found."),
					filePath,
					options,
					cancellationToken).ConfigureAwait(false);

			return await LoadReservedDocumentAsync(
				documentId,
				filePath,
				options,
				reservation ?? throw new InvalidOperationException("An open reservation was not created."),
				cancellationToken).ConfigureAwait(false);
		}
	}

	/// <inheritdoc />
	public bool TryGetSnapshot(string? filePath, out WorkspaceDocumentSnapshot? snapshot)
	{
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
	public IReadOnlyList<WorkspaceDocumentSnapshot> GetSnapshotsUnderDirectory(string directoryPath)
	{
		ArgumentNullException.ThrowIfNull(directoryPath);

		if (!TryNormalizePath(directoryPath, out string normalizedDirectoryPath))
			return [];

		string directoryPrefix = normalizedDirectoryPath.EndsWith(Path.DirectorySeparatorChar)
			? normalizedDirectoryPath
			: normalizedDirectoryPath + Path.DirectorySeparatorChar;
		StringComparison comparison = OperatingSystem.IsWindows()
			? StringComparison.OrdinalIgnoreCase
			: StringComparison.Ordinal;

		lock (_stateLock)
		{
			ThrowIfDisposedUnderLock();
			return _documents.Values
				.Where(document => document.DocumentId.StartsWith(directoryPrefix, comparison))
				.OrderBy(document => document.DocumentId, GetPathComparer())
				.Select(CreateSnapshot)
				.ToArray();
		}
	}

	/// <inheritdoc />
	public WorkspaceDocumentMutationResult TryReplace(WorkspaceDocumentReplaceRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		ThrowIfDisposed();

		lock (_stateLock)
		{
			ThrowIfDisposedUnderLock();

			if (!_documents.TryGetValue(request.DocumentId, out LogicalDocument? document))
				return CreateMutationResult(request, WorkspaceDocumentMutationStatus.DocumentNotFound, null);

			if (document.DocumentKey != request.ExpectedDocumentKey)
				return CreateMutationResult(
					request,
					WorkspaceDocumentMutationStatus.StaleDocumentInstance,
					CreateSnapshot(document));

			if (document.Version != request.ExpectedVersion)
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
				WorkspaceDocumentMutationStatus.Replaced,
				CreateSnapshot(document));
		}
	}

	/// <inheritdoc />
	public WorkspaceDocumentMutationResult Discard(WorkspaceDocumentDiscardRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		ThrowIfDisposed();

		lock (_stateLock)
		{
			ThrowIfDisposedUnderLock();

			if (!_documents.TryGetValue(request.DocumentId, out LogicalDocument? document))
				return CreateMutationResult(request, WorkspaceDocumentMutationStatus.DocumentNotFound, null);

			if (document.DocumentKey != request.ExpectedDocumentKey)
				return CreateMutationResult(
					request,
					WorkspaceDocumentMutationStatus.StaleDocumentInstance,
					CreateSnapshot(document));

			if (document.Version != request.ExpectedVersion)
				return CreateMutationResult(
					request,
					WorkspaceDocumentMutationStatus.StaleDocument,
					CreateSnapshot(document));

			if (!document.DiskOperationGate.Wait(0, CancellationToken.None))
				return CreateMutationResult(
					request,
					WorkspaceDocumentMutationStatus.OperationInProgress,
					CreateSnapshot(document));

			document.DiskOperationGate.Release();

			if (!IsDirty(document))
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
				WorkspaceDocumentMutationStatus.Replaced,
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
			request.DocumentId,
			request.ExpectedDocumentKey,
			request.ExpectedVersion,
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

			FileStamp actualStamp = await _fileSystem
				.CaptureStampAsync(capturedSnapshot.DocumentId, linkedCancellation.Token)
				.ConfigureAwait(false);

			if (!forceWrite && actualStamp != request.ExpectedOnDiskStamp)
			{
				lock (_stateLock)
				{
					document.OnDiskStamp = actualStamp;
					return CreateCommitResult(
						request,
						WorkspaceDocumentCommitStatus.ExternalFileConflict,
						CreateSnapshot(document),
						actualStamp);
				}
			}

			if (!forceWrite && !capturedSnapshot.IsDirty && capturedSnapshot.ExistsOnDisk)
			{
				lock (_stateLock)
					return CreateCommitResult(
						request,
						WorkspaceDocumentCommitStatus.Committed,
						CreateSnapshot(document));
			}

			byte[] content = WorkspaceFileCodec.Encode(capturedSnapshot.Content, capturedSnapshot.FileFormat);
			WorkspaceTemporaryFile temporaryFile = await _fileSystem
				.WriteTemporaryAsync(
					Path.GetDirectoryName(capturedSnapshot.DisplayPath) ?? Directory.GetCurrentDirectory(),
					content,
					linkedCancellation.Token)
				.ConfigureAwait(false);

			WorkspaceFileReplacementResult replacement;
			FileStamp replacementExpectedStamp = forceWrite ? actualStamp : request.ExpectedOnDiskStamp;
			try
			{
				replacement = await _fileSystem
					.ReplaceAsync(
						temporaryFile,
						capturedSnapshot.DocumentId,
						replacementExpectedStamp,
						linkedCancellation.Token)
					.ConfigureAwait(false);
			}
			finally
			{
				await _fileSystem.DeleteTemporaryAsync(temporaryFile).ConfigureAwait(false);
			}

			return replacement.Status switch
			{
				WorkspaceFileReplacementStatus.Replaced => InstallCommittedBaseline(
					request,
					document,
					capturedSnapshot,
					replacement.ObservedOnDiskStamp ?? actualStamp),
				WorkspaceFileReplacementStatus.ExternalFileConflict => UpdateObservedConflict(
					request,
					document,
					replacement.ObservedOnDiskStamp),
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
					WorkspaceDocumentCommitStatus.Cancelled,
					CreateSnapshot(document));
		}
		catch (Exception exception)
		{
			lock (_stateLock)
				return CreateCommitResult(
					request,
					WorkspaceDocumentCommitStatus.WriteFailed,
					CreateSnapshot(document),
					failure: new WorkspaceOperationFailure("WriteFailed", exception.Message, exception));
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

		OperationBegin begin = TryBeginOperation(
			request.DocumentId,
			request.ExpectedDocumentKey,
			request.ExpectedVersion,
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
			using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
				cancellationToken,
				_lifetimeCancellation.Token);
			try
			{
				FileStamp observedStamp = await _fileSystem
					.CaptureStampAsync(capturedSnapshot.DocumentId, linkedCancellation.Token)
					.ConfigureAwait(false);

				lock (_stateLock)
				{
					if (document.DocumentKey != request.ExpectedDocumentKey)
						return CreateReloadResult(
							request,
							WorkspaceDocumentReloadStatus.StaleDocumentInstance,
							CreateSnapshot(document),
							observedStamp);

					if (document.Version != request.ExpectedVersion)
						return CreateReloadResult(
							request,
							WorkspaceDocumentReloadStatus.StaleDocument,
							CreateSnapshot(document),
							observedStamp);

					document.OnDiskStamp = observedStamp;
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
					WorkspaceDocumentReloadStatus.Cancelled,
					capturedSnapshot);
			}
			catch (Exception exception)
			{
				return CreateReloadResult(
					request,
					WorkspaceDocumentReloadStatus.ReadFailed,
					capturedSnapshot,
					failure: new WorkspaceOperationFailure("ReadFailed", exception.Message, exception));
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

			string content = file.Content;
			TextFileFormat fileFormat = file.FileFormat;
			if (file.RawBytes.HasValue)
				content = WorkspaceFileCodec.Decode(file.RawBytes.Value.Span, document.NoBomEncoding, out fileFormat);

			lock (_stateLock)
			{
				if (document.Version != request.ExpectedVersion)
					return CreateReloadResult(
						request,
						WorkspaceDocumentReloadStatus.StaleDocument,
						CreateSnapshot(document),
						file.OnDiskStamp);

				if (file.OnDiskStamp == request.ExpectedOnDiskStamp)
				{
					document.OnDiskStamp = file.OnDiskStamp;
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
					WorkspaceDocumentReloadStatus.Cancelled,
					CreateSnapshot(document));
		}
		catch (Exception exception)
		{
			lock (_stateLock)
				return CreateReloadResult(
					request,
					WorkspaceDocumentReloadStatus.ReadFailed,
					CreateSnapshot(document),
					failure: new WorkspaceOperationFailure("ReadFailed", exception.Message, exception));
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

		if (request.Choice == WorkspaceDocumentConflictResolutionChoice.UseLogical)
		{
			WorkspaceDocumentCommitResult commitResult = await CommitCoreAsync(
				new WorkspaceDocumentCommitRequest(
					request.ExpectedDocumentKey,
					request.DocumentId,
					request.ExpectedVersion,
					request.ObservedOnDiskStamp),
				forceWrite: true,
				cancellationToken).ConfigureAwait(false);

			return CreateConflictResolutionFromCommit(request, commitResult);
		}

		OperationBegin begin = TryBeginOperation(
			request.DocumentId,
			request.ExpectedDocumentKey,
			request.ExpectedVersion,
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

			WorkspaceFileReadResult file = await _fileSystem
				.ReadAsync(capturedSnapshot.DocumentId, linkedCancellation.Token)
				.ConfigureAwait(false);
			FileStamp currentStamp = await _fileSystem
				.CaptureStampAsync(capturedSnapshot.DocumentId, linkedCancellation.Token)
				.ConfigureAwait(false);
			if (currentStamp != file.OnDiskStamp)
				return CreateConflictResolutionResult(
					request,
					WorkspaceDocumentConflictResolutionStatus.ExternalFileConflict,
					CreateSnapshot(document),
					currentStamp,
					new WorkspaceOperationFailure("ExternalFileConflict", "The disk file changed during conflict resolution."));

			string content = file.Content;
			TextFileFormat fileFormat = file.FileFormat;
			if (file.RawBytes.HasValue)
				content = WorkspaceFileCodec.Decode(file.RawBytes.Value.Span, document.NoBomEncoding, out fileFormat);

			lock (_stateLock)
			{
				if (document.Version != request.ExpectedVersion)
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
					WorkspaceDocumentConflictResolutionStatus.Cancelled,
					CreateSnapshot(document));
		}
		catch (DecoderFallbackException exception)
		{
			lock (_stateLock)
				return CreateConflictResolutionResult(
					request,
					WorkspaceDocumentConflictResolutionStatus.ReadFailed,
					CreateSnapshot(document),
					failure: new WorkspaceOperationFailure("InvalidEncoding", exception.Message, exception));
		}
		catch (Exception exception)
		{
			lock (_stateLock)
				return CreateConflictResolutionResult(
					request,
					WorkspaceDocumentConflictResolutionStatus.ReadFailed,
					CreateSnapshot(document),
					failure: new WorkspaceOperationFailure("ReadFailed", exception.Message, exception));
		}
		finally
		{
			CompleteOperation(operation);
		}
	}

	/// <inheritdoc />
	public async Task<WorkspaceDocumentRenameResult> RenameAsync(
		WorkspaceDocumentRenameRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ThrowIfDisposed();

		if (!TryNormalizePath(request.DestinationPath, out string destinationId))
			return CreateRenameResult(request, WorkspaceDocumentRenameStatus.InvalidPath, null);

		WorkspaceDocumentRenameStatus? destinationFailure = null;
		DestinationReservation? reservation = null;
		OperationBegin begin = TryBeginOperation(
			request.DocumentId,
			request.ExpectedDocumentKey,
			request.ExpectedVersion,
			captureSnapshot: false,
			preGateCheck: document =>
			{
				if (string.Equals(document.DocumentId, destinationId, StringComparison.Ordinal)
					&& string.Equals(document.DisplayPath, request.DestinationPath, StringComparison.Ordinal))
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
			WorkspaceFileMoveResult move = await _fileSystem
				.MoveAsync(
					document.DocumentId,
					request.DestinationPath,
					request.ExpectedOnDiskStamp,
					linkedCancellation.Token)
				.ConfigureAwait(false);

			if (move.Status != WorkspaceFileMoveStatus.Moved)
				return CreateRenameResult(request, MapRenameStatus(move.Status), CreateSnapshot(document), move.ObservedOnDiskStamp, move.Failure);

			lock (_stateLock)
			{
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
			return CreateRenameResult(request, WorkspaceDocumentRenameStatus.Cancelled, CreateSnapshot(document));
		}
		catch (Exception exception)
		{
			return CreateRenameResult(
				request,
				WorkspaceDocumentRenameStatus.MoveFailed,
				CreateSnapshot(document),
				failure: new WorkspaceOperationFailure("MoveFailed", exception.Message, exception));
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

		if (!TryNormalizePath(request.DestinationPath, out string destinationId))
			return CreateSaveAsResult(request, WorkspaceDocumentSaveAsStatus.InvalidPath, null);

		WorkspaceDocumentSaveAsStatus? destinationFailure = null;
		DestinationReservation? reservation = null;
		OperationBegin begin = TryBeginOperation(
			request.DocumentId,
			request.ExpectedDocumentKey,
			request.ExpectedVersion,
			captureSnapshot: true,
			preGateCheck: _ =>
			{
				if (_documents.ContainsKey(destinationId))
				{
					destinationFailure = WorkspaceDocumentSaveAsStatus.DestinationInUse;
					return false;
				}

				if (_destinationReservations.ContainsKey(destinationId))
				{
					destinationFailure = WorkspaceDocumentSaveAsStatus.DestinationBusy;
					return false;
				}

				return true;
			},
			postGateSetup: _ =>
			{
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
			FileStamp sourceStamp = await _fileSystem
				.CaptureStampAsync(document.DocumentId, linkedCancellation.Token)
				.ConfigureAwait(false);
			if (sourceStamp != request.ExpectedOnDiskStamp)
				return CreateSaveAsResult(
					request,
					WorkspaceDocumentSaveAsStatus.ExternalFileConflict,
					CreateSnapshot(document),
					sourceStamp,
					new WorkspaceOperationFailure("ExternalFileConflict", "The source file changed before Save As."));

			FileStamp destinationStamp = await _fileSystem
				.CaptureStampAsync(destinationId, linkedCancellation.Token)
				.ConfigureAwait(false);
			if (destinationStamp.Exists)
				return CreateSaveAsResult(request, WorkspaceDocumentSaveAsStatus.DestinationExists, CreateSnapshot(document), destinationStamp);

			byte[] content = WorkspaceFileCodec.Encode(capturedSnapshot.Content, capturedSnapshot.FileFormat);
			WorkspaceTemporaryFile temporaryFile = await _fileSystem
				.WriteTemporaryAsync(
					Path.GetDirectoryName(destinationId) ?? Directory.GetCurrentDirectory(),
					content,
					linkedCancellation.Token)
				.ConfigureAwait(false);
			WorkspaceFileReplacementResult replacement;
			try
			{
				replacement = await _fileSystem
					.ReplaceAsync(temporaryFile, destinationId, FileStamp.Missing, linkedCancellation.Token)
					.ConfigureAwait(false);
			}
			finally
			{
				await _fileSystem.DeleteTemporaryAsync(temporaryFile).ConfigureAwait(false);
			}

			if (replacement.Status != WorkspaceFileReplacementStatus.Replaced)
				return CreateSaveAsResult(
					request,
					replacement.Status == WorkspaceFileReplacementStatus.ExternalFileConflict
						? WorkspaceDocumentSaveAsStatus.DestinationExists
						: replacement.Status == WorkspaceFileReplacementStatus.ReplacementStateUnknown
							? WorkspaceDocumentSaveAsStatus.ReplacementStateUnknown
							: WorkspaceDocumentSaveAsStatus.WriteFailed,
					CreateSnapshot(document),
					replacement.ObservedOnDiskStamp,
					replacement.Failure);

			lock (_stateLock)
			{
				_documents.Remove(document.DocumentId);
				document.DocumentId = destinationId;
				document.DisplayPath = request.DestinationPath;
				document.OnDiskStamp = replacement.ObservedOnDiskStamp ?? FileStamp.Missing;
				document.PersistedContent = capturedSnapshot.Content;
				document.PersistedFileFormat = capturedSnapshot.FileFormat;
				document.Version++;
				document.PersistedVersion = document.Version;
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
			return CreateSaveAsResult(request, WorkspaceDocumentSaveAsStatus.Cancelled, CreateSnapshot(document));
		}
		catch (Exception exception)
		{
			return CreateSaveAsResult(
				request,
				WorkspaceDocumentSaveAsStatus.WriteFailed,
				CreateSnapshot(document),
				failure: new WorkspaceOperationFailure("WriteFailed", exception.Message, exception));
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
			request.DocumentId,
			request.ExpectedDocumentKey,
			request.ExpectedVersion,
			captureSnapshot: true,
			postGateSetup: document => document.DeleteOperationActive = true);

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
			WorkspaceFileDeleteResult deletion = await _fileSystem
				.DeleteAsync(document.DocumentId, request.ExpectedOnDiskStamp, linkedCancellation.Token, request.UseRecycleBin)
				.ConfigureAwait(false);

			if (deletion.Status != WorkspaceFileDeleteStatus.Deleted)
				return CreateDeleteResult(
					request,
					deletion.Status == WorkspaceFileDeleteStatus.ExternalFileConflict
						? WorkspaceDocumentDeleteStatus.ExternalFileConflict
						: deletion.Status == WorkspaceFileDeleteStatus.Cancelled
							? WorkspaceDocumentDeleteStatus.Cancelled
							: WorkspaceDocumentDeleteStatus.DeleteFailed,
					CreateSnapshot(document),
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
			return CreateDeleteResult(request, WorkspaceDocumentDeleteStatus.Cancelled, CreateSnapshot(document));
		}
		catch (Exception exception)
		{
			return CreateDeleteResult(
				request,
				WorkspaceDocumentDeleteStatus.DeleteFailed,
				CreateSnapshot(document),
				failure: new WorkspaceOperationFailure("DeleteFailed", exception.Message, exception));
		}
		finally
		{
			lock (_stateLock)
				document.DeleteOperationActive = false;

			CompleteOperation(operation);
		}
	}

	/// <inheritdoc />
	public async Task<WorkspaceDocumentDirectoryRenameResult> RenameDirectoryAsync(
		WorkspaceDocumentDirectoryRenameRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ThrowIfDisposed();

		if (!TryNormalizePath(request.SourceDirectoryPath, out string sourceDirectoryId)
			|| !TryNormalizePath(request.DestinationDirectoryPath, out string destinationDirectoryId)
			|| string.Equals(sourceDirectoryId, destinationDirectoryId, StringComparison.Ordinal)
			&& string.Equals(request.SourceDirectoryPath, request.DestinationDirectoryPath, StringComparison.Ordinal))
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
			foreach (WorkspaceDocumentBatchEntry entry in request.Documents.OrderBy(entry => entry.DocumentId, GetPathComparer()))
			{
				if (!TryValidateBatchEntry(entry, sourcePrefix, out LogicalDocument? document, out BatchEntryFailure failure))
					return CreateDirectoryRenameResult(request, MapRenameBatchEntryFailure(failure), CreateSnapshots(documents));

				documents.Add(document);
				expectedStamps.Add(entry.ExpectedOnDiskStamp);
			}

			foreach (LogicalDocument document in documents)
			{
				string destinationId = RebasePath(document.DocumentId, sourceDirectoryId, destinationDirectoryId);
				destinationIds.Add(destinationId);
				if (_documents.TryGetValue(destinationId, out LogicalDocument? destinationDocument)
					&& !documents.Contains(destinationDocument))
					return CreateDirectoryRenameResult(request, WorkspaceDocumentDirectoryRenameStatus.DestinationInUse, CreateSnapshots(documents));

				if (_destinationReservations.ContainsKey(destinationId))
					return CreateDirectoryRenameResult(request, WorkspaceDocumentDirectoryRenameStatus.DestinationBusy, CreateSnapshots(documents));
			}

			foreach (LogicalDocument document in documents)
			{
				if (!document.DiskOperationGate.Wait(0, CancellationToken.None))
				{
					ReleaseGates(acquiredGates);
					return CreateDirectoryRenameResult(request, WorkspaceDocumentDirectoryRenameStatus.OperationInProgress, CreateSnapshots(documents));
				}

				acquiredGates.Add(document.DiskOperationGate);
			}

			foreach (string destinationId in destinationIds.Distinct(GetPathComparer()))
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
			for (int index = 0; index < documents.Count; index++)
			{
				FileStamp observedStamp = await _fileSystem
					.CaptureStampAsync(documents[index].DocumentId, linkedCancellation.Token)
					.ConfigureAwait(false);
				if (observedStamp != expectedStamps[index])
					return CreateDirectoryRenameResult(
						request,
						WorkspaceDocumentDirectoryRenameStatus.ExternalFileConflict,
						CreateSnapshots(documents),
						new WorkspaceOperationFailure("ExternalFileConflict", "A retained descendant changed before directory rename."));
			}

			WorkspaceFileMoveResult move = await _fileSystem
				.MoveDirectoryAsync(request.SourceDirectoryPath, request.DestinationDirectoryPath, linkedCancellation.Token)
				.ConfigureAwait(false);
			if (move.Status != WorkspaceFileMoveStatus.Moved)
				return CreateDirectoryRenameResult(request, MapDirectoryRenameStatus(move.Status), CreateSnapshots(documents), move.Failure);

			lock (_stateLock)
			{
				foreach (LogicalDocument document in documents)
					_documents.Remove(document.DocumentId);

				for (int index = 0; index < documents.Count; index++)
				{
					LogicalDocument document = documents[index];
					string relativePath = Path.GetRelativePath(sourceDirectoryId, document.DocumentId);
					document.DocumentId = destinationIds[index];
					document.DisplayPath = Path.Combine(request.DestinationDirectoryPath, relativePath);
					document.Version++;
					_documents.Add(document.DocumentId, document);
				}

				return CreateDirectoryRenameResult(
					request,
					WorkspaceDocumentDirectoryRenameStatus.Renamed,
					CreateSnapshots(documents));
			}
		}
		catch (OperationCanceledException)
		{
			return CreateDirectoryRenameResult(request, WorkspaceDocumentDirectoryRenameStatus.Cancelled, CreateSnapshots(documents));
		}
		catch (Exception exception)
		{
			return CreateDirectoryRenameResult(
				request,
				WorkspaceDocumentDirectoryRenameStatus.MoveFailed,
				CreateSnapshots(documents),
				new WorkspaceOperationFailure("MoveFailed", exception.Message, exception));
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
			foreach (WorkspaceDocumentBatchEntry entry in request.Documents.OrderBy(entry => entry.DocumentId, GetPathComparer()))
			{
				if (!TryValidateBatchEntry(entry, directoryPrefix, out LogicalDocument? document, out BatchEntryFailure failure))
					return CreateDirectoryDeleteResult(request, MapDeleteBatchEntryFailure(failure), CreateSnapshots(documents));

				documents.Add(document);
				expectedStamps.Add(entry.ExpectedOnDiskStamp);
			}

			foreach (LogicalDocument document in documents)
			{
				if (!document.DiskOperationGate.Wait(0, CancellationToken.None))
				{
					ReleaseGates(acquiredGates);
					return CreateDirectoryDeleteResult(request, WorkspaceDocumentDirectoryDeleteStatus.OperationInProgress, CreateSnapshots(documents));
				}

				acquiredGates.Add(document.DiskOperationGate);
				document.DeleteOperationActive = true;
			}

			operation = new OperationRegistration(acquiredGates);
			_activeOperations.Add(operation);
		}

		try
		{
			using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
				cancellationToken,
				_lifetimeCancellation.Token);
			for (int index = 0; index < documents.Count; index++)
			{
				FileStamp observedStamp = await _fileSystem
					.CaptureStampAsync(documents[index].DocumentId, linkedCancellation.Token)
					.ConfigureAwait(false);
				if (observedStamp != expectedStamps[index])
					return CreateDirectoryDeleteResult(
						request,
						WorkspaceDocumentDirectoryDeleteStatus.ExternalFileConflict,
						CreateSnapshots(documents),
						new WorkspaceOperationFailure("ExternalFileConflict", "A retained descendant changed before directory deletion."));
			}

			WorkspaceFileDeleteResult deletion = await _fileSystem
				.DeleteDirectoryAsync(request.DirectoryPath, linkedCancellation.Token, request.UseRecycleBin)
				.ConfigureAwait(false);
			if (deletion.Status != WorkspaceFileDeleteStatus.Deleted)
				return CreateDirectoryDeleteResult(
					request,
					MapDirectoryDeleteStatus(deletion.Status),
					CreateSnapshots(documents),
					deletion.Failure);

			lock (_stateLock)
			{
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
			return CreateDirectoryDeleteResult(request, WorkspaceDocumentDirectoryDeleteStatus.Cancelled, CreateSnapshots(documents));
		}
		catch (Exception exception)
		{
			return CreateDirectoryDeleteResult(
				request,
				WorkspaceDocumentDirectoryDeleteStatus.DeleteFailed,
				CreateSnapshots(documents),
				new WorkspaceOperationFailure("DeleteFailed", exception.Message, exception));
		}
		finally
		{
			lock (_stateLock)
			{
				foreach (LogicalDocument document in documents)
					document.DeleteOperationActive = false;
			}

			CompleteOperation(operation);
		}
	}

	/// <inheritdoc />
	public ValueTask DisposeAsync()
	{
		lock (_stateLock)
		{
			if (_disposeTask is not null)
				return new ValueTask(_disposeTask);

			_disposed = true;
			_lifetimeCancellation.Cancel();

			Task[] reservations = new Task[_openReservations.Count];
			int index = 0;
			foreach (OpenReservation reservation in _openReservations.Values)
				reservations[index++] = reservation.Completion.Task;

			Task[] operations = new Task[_activeOperations.Count];
			index = 0;
			foreach (OperationRegistration operation in _activeOperations)
				operations[index++] = operation.Completion.Task;

			_disposeTask = DisposeCoreAsync(reservations, operations);
			return new ValueTask(_disposeTask);
		}
	}

	private async Task<WorkspaceDocumentOpenResult> WaitForExistingOpenAsync(
		OpenReservation reservation,
		string filePath,
		WorkspaceDocumentOpenOptions options,
		CancellationToken cancellationToken)
	{
		while (true)
		{
			try
			{
				WorkspaceDocumentOpenResult result = await reservation.Completion.Task
					.WaitAsync(cancellationToken)
					.ConfigureAwait(false);

				if (result.Status == WorkspaceDocumentOpenStatus.Opened)
					return result with { Status = WorkspaceDocumentOpenStatus.AlreadyOpen };

				return await OpenAsync(filePath, options, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				return new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenStatus.Cancelled, null);
			}
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
				WorkspaceFileReadResult file = await _fileSystem
					.ReadAsync(documentId, linkedCancellation.Token)
					.ConfigureAwait(false);

				linkedCancellation.Token.ThrowIfCancellationRequested();
				string content = file.Content;
				TextFileFormat fileFormat = file.OnDiskStamp.Exists
					? file.FileFormat
					: options.NewFileFormat;
				if (file.RawBytes.HasValue)
					content = WorkspaceFileCodec.Decode(file.RawBytes.Value.Span, options.NoBomEncoding, out fileFormat);

				lock (_stateLock)
				{
					if (_disposed)
					{
						result = new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenStatus.Cancelled, null);
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

						_documents.Add(documentId, document);
						result = new WorkspaceDocumentOpenResult(
							WorkspaceDocumentOpenStatus.Opened,
							CreateSnapshot(document));
					}

					CompleteReservationUnderLock(documentId, reservation, result);
				}
			}
			catch (OperationCanceledException)
			{
				result = new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenStatus.Cancelled, null);
				CompleteReservation(documentId, reservation, result);
			}
			catch (Exception exception)
			{
				result = new WorkspaceDocumentOpenResult(
					WorkspaceDocumentOpenStatus.LoadFailed,
					null,
					new WorkspaceOperationFailure(
						exception is DecoderFallbackException ? "InvalidEncoding" : "LoadFailed",
						exception.Message,
						exception));
				CompleteReservation(documentId, reservation, result);
			}
		}

		return result;
	}

	private async Task DisposeCoreAsync(Task[] reservations, Task[] operations)
	{
		if (reservations.Length > 0)
			await Task.WhenAll(reservations).ConfigureAwait(false);
		if (operations.Length > 0)
			await Task.WhenAll(operations).ConfigureAwait(false);

		lock (_stateLock)
		{
			foreach (LogicalDocument document in _documents.Values)
				document.DiskOperationGate.Dispose();

			_documents.Clear();
			_openReservations.Clear();
		}

		_lifetimeCancellation.Dispose();
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

	private static void ReleaseGates(IEnumerable<SemaphoreSlim> gates)
	{
		foreach (SemaphoreSlim gate in gates.Reverse())
			gate.Release();
	}

	private enum OperationBeginFailure
	{
		DocumentNotFound,
		StaleDocumentInstance,
		StaleDocument,
		OperationInProgress,
		PreGateCheckFailed,
	}

	// Outcome of a TryBeginOperation attempt. On success the document and, when
	// requested, captured snapshot are populated; a gated operation also has an
	// operation registration. On failure the failure
	// kind and, when available, a snapshot of the state that failed validation are populated;
	// the failure snapshot is captured under _stateLock so it matches the state that was validated.
	private readonly struct OperationBegin
	{
		public LogicalDocument? Document { get; init; }
		public WorkspaceDocumentSnapshot? CapturedSnapshot { get; init; }
		public WorkspaceDocumentSnapshot? FailureSnapshot { get; init; }
		public OperationRegistration? Operation { get; init; }
		public OperationBeginFailure? Failure { get; init; }

		public bool Succeeded => Failure is null;
	}

	// Performs the shared operation preamble under _stateLock: validates the
	// disposed state and the request's document presence, key, and version
	// expectations; runs the optional pre-gate check; captures a snapshot;
	// acquires the document's disk-operation gate; runs the optional post-gate
	// setup; and registers the operation. When skipGateWhen returns true (used by
	// reload for dirty documents) the operation proceeds without holding the gate
	// or registering an operation. Returns an OperationBegin describing either the
	// begun operation or the failure.
	private OperationBegin TryBeginOperation(
		string documentId,
		WorkspaceDocumentKey expectedDocumentKey,
		long expectedVersion,
		bool captureSnapshot,
		Func<LogicalDocument, bool>? preGateCheck = null,
		Func<LogicalDocument, WorkspaceDocumentSnapshot?, bool>? skipGateWhen = null,
		Action<LogicalDocument>? postGateSetup = null)
	{
		lock (_stateLock)
		{
			ThrowIfDisposedUnderLock();

			if (!_documents.TryGetValue(documentId, out LogicalDocument? document))
				return new OperationBegin { Failure = OperationBeginFailure.DocumentNotFound };

			if (document.DocumentKey != expectedDocumentKey)
				return new OperationBegin
				{
					Document = document,
					FailureSnapshot = CreateSnapshot(document),
					Failure = OperationBeginFailure.StaleDocumentInstance
				};

			if (document.Version != expectedVersion)
				return new OperationBegin
				{
					Document = document,
					FailureSnapshot = CreateSnapshot(document),
					Failure = OperationBeginFailure.StaleDocument
				};

			if (preGateCheck is not null && !preGateCheck(document))
				return new OperationBegin
				{
					Document = document,
					FailureSnapshot = CreateSnapshot(document),
					Failure = OperationBeginFailure.PreGateCheckFailed
				};

			WorkspaceDocumentSnapshot? capturedSnapshot = captureSnapshot ? CreateSnapshot(document) : null;
			if (skipGateWhen is not null && skipGateWhen(document, capturedSnapshot))
				return new OperationBegin
				{
					Document = document,
					CapturedSnapshot = capturedSnapshot
				};

			if (!document.DiskOperationGate.Wait(0, CancellationToken.None))
				return new OperationBegin
				{
					Document = document,
					FailureSnapshot = CreateSnapshot(document),
					Failure = OperationBeginFailure.OperationInProgress
				};

			postGateSetup?.Invoke(document);

			OperationRegistration operation = new(document.DiskOperationGate);
			_activeOperations.Add(operation);

			return new OperationBegin
			{
				Document = document,
				CapturedSnapshot = capturedSnapshot,
				Operation = operation
			};
		}
	}

	private enum BatchEntryFailure
	{
		DocumentNotFound,
		StaleDocumentInstance,
		StaleDocument,
		InvalidPath,
	}

	// Validates a single directory-batch entry against the open documents under
	// the caller's _stateLock: the document must exist, the entry's key and
	// version expectations must hold, and the document must live under the
	// directory prefix.
	private bool TryValidateBatchEntry(
		WorkspaceDocumentBatchEntry entry,
		string directoryPrefix,
		[NotNullWhen(true)] out LogicalDocument? document,
		out BatchEntryFailure failure)
	{
		if (!_documents.TryGetValue(entry.DocumentId, out LogicalDocument? current))
		{
			document = null;
			failure = BatchEntryFailure.DocumentNotFound;
			return false;
		}

		document = current;
		if (document.DocumentKey != entry.ExpectedDocumentKey)
		{
			failure = BatchEntryFailure.StaleDocumentInstance;
			return false;
		}

		if (document.Version != entry.ExpectedVersion)
		{
			failure = BatchEntryFailure.StaleDocument;
			return false;
		}

		if (!document.DocumentId.StartsWith(directoryPrefix, GetPathComparison()))
		{
			failure = BatchEntryFailure.InvalidPath;
			return false;
		}

		failure = default;
		return true;
	}

	private static WorkspaceDocumentDirectoryRenameStatus MapRenameBatchEntryFailure(BatchEntryFailure failure)
		=> failure switch
		{
			BatchEntryFailure.DocumentNotFound => WorkspaceDocumentDirectoryRenameStatus.DocumentNotFound,
			BatchEntryFailure.StaleDocumentInstance => WorkspaceDocumentDirectoryRenameStatus.StaleDocumentInstance,
			BatchEntryFailure.StaleDocument => WorkspaceDocumentDirectoryRenameStatus.StaleDocument,
			_ => WorkspaceDocumentDirectoryRenameStatus.InvalidPath,
		};

	private static WorkspaceDocumentDirectoryDeleteStatus MapDeleteBatchEntryFailure(BatchEntryFailure failure)
		=> failure switch
		{
			BatchEntryFailure.DocumentNotFound => WorkspaceDocumentDirectoryDeleteStatus.DocumentNotFound,
			BatchEntryFailure.StaleDocumentInstance => WorkspaceDocumentDirectoryDeleteStatus.StaleDocumentInstance,
			BatchEntryFailure.StaleDocument => WorkspaceDocumentDirectoryDeleteStatus.StaleDocument,
			_ => WorkspaceDocumentDirectoryDeleteStatus.InvalidPath,
		};

	private void ThrowIfDisposed()
	{
		lock (_stateLock)
			ThrowIfDisposedUnderLock();
	}

	private void ThrowIfDisposedUnderLock()
	{
		ObjectDisposedException.ThrowIf(_disposed, nameof(WorkspaceDocumentStore));
	}

	private void CompleteOperation(OperationRegistration operation)
	{
		lock (_stateLock)
		{
			_activeOperations.Remove(operation);
			operation.Complete();
		}
	}

	private void CompleteDestinationReservation(DestinationReservation reservation)
	{
		lock (_stateLock)
		{
			if (_destinationReservations.TryGetValue(reservation.DocumentId, out DestinationReservation? current)
				&& ReferenceEquals(current, reservation))
			{
				_destinationReservations.Remove(reservation.DocumentId);
				reservation.Completion.TrySetResult(null);
			}
		}
	}
}

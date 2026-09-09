using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Constructs workspace document operation results and document snapshots.
/// </summary>
internal static class WorkspaceDocumentResultFactory
{
	public static WorkspaceDocumentSnapshot CreateSnapshot(LogicalDocument document)
	{
		return new WorkspaceDocumentSnapshot(
			document.DocumentKey,
			document.DocumentId,
			document.DisplayPath,
			document.Version,
			document.PersistedVersion,
			document.IsDirty,
			new DeferredTextSnapshot(document.Content, document.DisplayPath),
			document.FileFormat,
			document.OnDiskStamp);
	}

	public static IReadOnlyList<WorkspaceDocumentSnapshot> CreateSnapshots(IEnumerable<LogicalDocument> documents)
		=> documents.Select(CreateSnapshot).ToArray();

	public static WorkspaceDocumentCommitResult CreateCommitResult(
		WorkspaceDocumentCommitRequest request,
		WorkspaceDocumentCommitStatus status,
		WorkspaceDocumentSnapshot? snapshot,
		FileStamp? observedOnDiskStamp = null,
		WorkspaceOperationFailure? failure = null)
	{
		return new WorkspaceDocumentCommitResult(
			status,
			request.Identity,
			snapshot,
			observedOnDiskStamp,
			failure);
	}

	public static WorkspaceDocumentReloadResult CreateReloadResult(
		WorkspaceDocumentReloadRequest request,
		WorkspaceDocumentReloadStatus status,
		WorkspaceDocumentSnapshot? snapshot,
		FileStamp? observedOnDiskStamp = null,
		WorkspaceOperationFailure? failure = null)
	{
		return new WorkspaceDocumentReloadResult(
			status,
			request.Identity,
			snapshot,
			observedOnDiskStamp,
			failure);
	}

	public static WorkspaceDocumentConflictResolutionResult CreateConflictResolutionResult(
		WorkspaceDocumentConflictResolutionRequest request,
		WorkspaceDocumentConflictResolutionStatus status,
		WorkspaceDocumentSnapshot? snapshot,
		FileStamp? observedOnDiskStamp = null,
		WorkspaceOperationFailure? failure = null)
	{
		return new WorkspaceDocumentConflictResolutionResult(
			status,
			request.Identity,
			snapshot,
			observedOnDiskStamp,
			failure,
			request.Choice);
	}

	public static WorkspaceDocumentConflictResolutionResult CreateConflictResolutionFromCommit(
		WorkspaceDocumentConflictResolutionRequest request,
		WorkspaceDocumentCommitResult commitResult)
	{
		WorkspaceDocumentConflictResolutionStatus status = commitResult.Status switch
		{
			WorkspaceDocumentCommitStatus.Committed => WorkspaceDocumentConflictResolutionStatus.ResolvedWithLogical,
			WorkspaceDocumentCommitStatus.StaleDocument => WorkspaceDocumentConflictResolutionStatus.StaleDocument,
			WorkspaceDocumentCommitStatus.StaleDocumentInstance => WorkspaceDocumentConflictResolutionStatus.StaleDocumentInstance,
			WorkspaceDocumentCommitStatus.DocumentNotFound => WorkspaceDocumentConflictResolutionStatus.DocumentNotFound,
			WorkspaceDocumentCommitStatus.OperationInProgress => WorkspaceDocumentConflictResolutionStatus.OperationInProgress,
			WorkspaceDocumentCommitStatus.ExternalFileConflict => WorkspaceDocumentConflictResolutionStatus.ExternalFileConflict,
			WorkspaceDocumentCommitStatus.WriteFailed => WorkspaceDocumentConflictResolutionStatus.WriteFailed,
			WorkspaceDocumentCommitStatus.ReplacementStateUnknown => WorkspaceDocumentConflictResolutionStatus.ReplacementStateUnknown,
			WorkspaceDocumentCommitStatus.Canceled => WorkspaceDocumentConflictResolutionStatus.Canceled,
			_ => WorkspaceDocumentConflictResolutionStatus.WriteFailed
		};

		return new WorkspaceDocumentConflictResolutionResult(
			status,
			commitResult.RequestedIdentity,
			commitResult.Snapshot,
			commitResult.ObservedOnDiskStamp,
			commitResult.Failure,
			request.Choice);
	}

	public static WorkspaceDocumentMutationResult CreateMutationResult(
		WorkspaceDocumentReplaceRequest request,
		WorkspaceDocumentMutationStatus status,
		WorkspaceDocumentSnapshot? snapshot)
	{
		return CreateMutationResultCore(
			request.Identity,
			status,
			snapshot);
	}

	public static WorkspaceDocumentRenameResult CreateRenameResult(
		WorkspaceDocumentRenameRequest request,
		WorkspaceDocumentRenameStatus status,
		WorkspaceDocumentSnapshot? snapshot,
		FileStamp? observedOnDiskStamp = null,
		WorkspaceOperationFailure? failure = null)
	{
		return new WorkspaceDocumentRenameResult(
			status,
			request.Identity,
			snapshot,
			observedOnDiskStamp,
			failure);
	}

	public static WorkspaceDocumentSaveAsResult CreateSaveAsResult(
		WorkspaceDocumentSaveAsRequest request,
		WorkspaceDocumentSaveAsStatus status,
		WorkspaceDocumentSnapshot? snapshot,
		FileStamp? observedOnDiskStamp = null,
		WorkspaceOperationFailure? failure = null)
	{
		return new WorkspaceDocumentSaveAsResult(
			status,
			request.Identity,
			snapshot,
			observedOnDiskStamp,
			failure);
	}

	public static WorkspaceDocumentDeleteResult CreateDeleteResult(
		WorkspaceDocumentDeleteRequest request,
		WorkspaceDocumentDeleteStatus status,
		WorkspaceDocumentSnapshot? snapshot,
		FileStamp? observedOnDiskStamp = null,
		WorkspaceOperationFailure? failure = null)
	{
		return new WorkspaceDocumentDeleteResult(
			status,
			request.Identity,
			snapshot,
			observedOnDiskStamp,
			failure);
	}

	public static WorkspaceDocumentDirectoryRenameResult CreateDirectoryRenameResult(
		WorkspaceDocumentDirectoryRenameRequest request,
		WorkspaceDocumentDirectoryRenameStatus status,
		IReadOnlyList<WorkspaceDocumentSnapshot> snapshots,
		WorkspaceOperationFailure? failure = null)
	{
		return new WorkspaceDocumentDirectoryRenameResult(
			status,
			request.SourceDirectoryPath,
			request.DestinationDirectoryPath,
			snapshots,
			failure);
	}

	public static WorkspaceDocumentDirectoryDeleteResult CreateDirectoryDeleteResult(
		WorkspaceDocumentDirectoryDeleteRequest request,
		WorkspaceDocumentDirectoryDeleteStatus status,
		IReadOnlyList<WorkspaceDocumentSnapshot> snapshots,
		WorkspaceOperationFailure? failure = null)
	{
		return new WorkspaceDocumentDirectoryDeleteResult(
			status,
			request.DirectoryPath,
			snapshots,
			failure);
	}

	public static WorkspaceDocumentMutationResult CreateMutationResult(
		WorkspaceDocumentDiscardRequest request,
		WorkspaceDocumentMutationStatus status,
		WorkspaceDocumentSnapshot? snapshot)
	{
		return CreateMutationResultCore(
			request.Identity,
			status,
			snapshot);
	}

	private static WorkspaceDocumentMutationResult CreateMutationResultCore(
		WorkspaceDocumentRequestIdentity identity,
		WorkspaceDocumentMutationStatus status,
		WorkspaceDocumentSnapshot? snapshot)
	{
		return new WorkspaceDocumentMutationResult(
			status,
			identity,
			snapshot);
	}

	public static WorkspaceDocumentRenameStatus MapRenameStatus(WorkspaceFileMoveStatus status)
		=> status switch
		{
			WorkspaceFileMoveStatus.DestinationExists => WorkspaceDocumentRenameStatus.DestinationExists,
			WorkspaceFileMoveStatus.ExternalFileConflict => WorkspaceDocumentRenameStatus.ExternalFileConflict,
			WorkspaceFileMoveStatus.Canceled => WorkspaceDocumentRenameStatus.Canceled,
			WorkspaceFileMoveStatus.MoveStateUnknown => WorkspaceDocumentRenameStatus.MoveStateUnknown,
			_ => WorkspaceDocumentRenameStatus.MoveFailed
		};

	public static WorkspaceDocumentDirectoryRenameStatus MapDirectoryRenameStatus(WorkspaceFileMoveStatus status)
		=> status switch
		{
			WorkspaceFileMoveStatus.DestinationExists => WorkspaceDocumentDirectoryRenameStatus.DestinationExists,
			WorkspaceFileMoveStatus.ExternalFileConflict => WorkspaceDocumentDirectoryRenameStatus.ExternalFileConflict,
			WorkspaceFileMoveStatus.Canceled => WorkspaceDocumentDirectoryRenameStatus.Canceled,
			WorkspaceFileMoveStatus.MoveStateUnknown => WorkspaceDocumentDirectoryRenameStatus.MoveStateUnknown,
			_ => WorkspaceDocumentDirectoryRenameStatus.MoveFailed
		};

	public static WorkspaceDocumentDirectoryDeleteStatus MapDirectoryDeleteStatus(WorkspaceFileDeleteStatus status)
		=> status switch
		{
			WorkspaceFileDeleteStatus.ExternalFileConflict => WorkspaceDocumentDirectoryDeleteStatus.ExternalFileConflict,
			WorkspaceFileDeleteStatus.Canceled => WorkspaceDocumentDirectoryDeleteStatus.Canceled,
			_ => WorkspaceDocumentDirectoryDeleteStatus.DeleteFailed
		};
}

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Requests replacement of the logical content and file format of a document.
/// </summary>
/// <remarks>The request does not write to disk; persistence is performed by <see cref="IWorkspaceDocumentStore.CommitAsync(WorkspaceDocumentCommitRequest, CancellationToken)"/>.</remarks>
public sealed record WorkspaceDocumentReplaceRequest(
	WorkspaceDocumentKey ExpectedDocumentKey,
	string DocumentId,
	long ExpectedVersion,
	string Content,
	TextFileFormat FileFormat);

/// <summary>
/// Requests that a document discard its unsaved logical changes.
/// </summary>
/// <remarks>The document is restored to its last persisted content and format if it is dirty.</remarks>
public sealed record WorkspaceDocumentDiscardRequest(
	WorkspaceDocumentKey ExpectedDocumentKey,
	string DocumentId,
	long ExpectedVersion);

/// <summary>
/// Requests that a document move to a new path.
/// </summary>
/// <remarks>The expected source stamp protects the move from an external source-file change.</remarks>
public sealed record WorkspaceDocumentRenameRequest(
	WorkspaceDocumentKey ExpectedDocumentKey,
	string DocumentId,
	long ExpectedVersion,
	FileStamp ExpectedOnDiskStamp,
	string DestinationPath);

/// <summary>
/// Describes the outcome of renaming a document.
/// </summary>
public enum WorkspaceDocumentRenameStatus
{
	/// <summary>The document was renamed.</summary>
	Renamed,

	/// <summary>The requested rename made no change.</summary>
	NoChange,

	/// <summary>The path is invalid.</summary>
	InvalidPath,

	/// <summary>The document version was stale.</summary>
	StaleDocument,

	/// <summary>The document identity was stale.</summary>
	StaleDocumentInstance,

	/// <summary>The document was not found.</summary>
	DocumentNotFound,

	/// <summary>Another operation is in progress.</summary>
	OperationInProgress,

	/// <summary>An attached view has pending edits, a conflict, or a prior synchronization failure.</summary>
	ViewNotSynchronized,

	/// <summary>The source file stamp did not match the expected stamp.</summary>
	ExternalFileConflict,

	/// <summary>The destination already exists.</summary>
	DestinationExists,

	/// <summary>The destination is in use.</summary>
	DestinationInUse,

	/// <summary>The destination is busy.</summary>
	DestinationBusy,

	/// <summary>The move failed.</summary>
	MoveFailed,

	/// <summary>View synchronization failed after the document moved.</summary>
	ViewUpdateFailed,

	/// <summary>The operation was cancelled.</summary>
	Cancelled
}

/// <summary>
/// Contains the outcome of renaming a document.
/// </summary>
/// <remarks>
/// The requested identity fields are echoed in the result. <see cref="Snapshot"/> contains the
/// current document state when it is still tracked; <see cref="FailedViewIds"/> identifies views
/// that could not be synchronized when the operation reached the view manager.
/// </remarks>
public sealed record WorkspaceDocumentRenameResult(
	WorkspaceDocumentRenameStatus Status,
	WorkspaceDocumentKey RequestedDocumentKey,
	string RequestedDocumentId,
	long RequestedVersion,
	WorkspaceDocumentSnapshot? Snapshot,
	FileStamp? ObservedOnDiskStamp = null,
	WorkspaceOperationFailure? Failure = null,
	IReadOnlyList<string>? FailedViewIds = null);

/// <summary>
/// Requests that a document be persisted at a second path.
/// </summary>
/// <remarks>The source file is retained; the tracked document is retargeted to the destination on success.</remarks>
public sealed record WorkspaceDocumentSaveAsRequest(
	WorkspaceDocumentKey ExpectedDocumentKey,
	string DocumentId,
	long ExpectedVersion,
	FileStamp ExpectedOnDiskStamp,
	string DestinationPath);

/// <summary>
/// Describes the outcome of saving a document at a second path.
/// </summary>
public enum WorkspaceDocumentSaveAsStatus
{
	/// <summary>The document was saved at the new path.</summary>
	SavedAs,

	/// <summary>The path is invalid.</summary>
	InvalidPath,

	/// <summary>The document version was stale.</summary>
	StaleDocument,

	/// <summary>The document identity was stale.</summary>
	StaleDocumentInstance,

	/// <summary>The document was not found.</summary>
	DocumentNotFound,

	/// <summary>Another operation is in progress.</summary>
	OperationInProgress,

	/// <summary>An attached view has pending edits, a conflict, or a prior synchronization failure.</summary>
	ViewNotSynchronized,

	/// <summary>The destination already exists.</summary>
	DestinationExists,

	/// <summary>The destination is in use.</summary>
	DestinationInUse,

	/// <summary>The destination is busy.</summary>
	DestinationBusy,

	/// <summary>The source file stamp did not match the expected stamp.</summary>
	ExternalFileConflict,

	/// <summary>The write failed.</summary>
	WriteFailed,

	/// <summary>The replacement state is unknown.</summary>
	ReplacementStateUnknown,

	/// <summary>View synchronization failed after the document was saved at the destination.</summary>
	ViewUpdateFailed,

	/// <summary>The operation was cancelled.</summary>
	Cancelled
}

/// <summary>
/// Contains the outcome of saving a document at a second path.
/// </summary>
/// <remarks>
/// A successful result points to the destination path and preserves the document key. The source
/// file is not removed. <see cref="FailedViewIds"/> is populated only when view synchronization fails.
/// </remarks>
public sealed record WorkspaceDocumentSaveAsResult(
	WorkspaceDocumentSaveAsStatus Status,
	WorkspaceDocumentKey RequestedDocumentKey,
	string RequestedDocumentId,
	long RequestedVersion,
	WorkspaceDocumentSnapshot? Snapshot,
	FileStamp? ObservedOnDiskStamp = null,
	WorkspaceOperationFailure? Failure = null,
	IReadOnlyList<string>? FailedViewIds = null);

/// <summary>
/// Requests deletion of a workspace document.
/// </summary>
/// <param name="ExpectedDocumentKey">The document incarnation expected by the caller.</param>
/// <param name="DocumentId">The normalized document id expected by the caller.</param>
/// <param name="ExpectedVersion">The document version captured by the caller.</param>
/// <param name="ExpectedOnDiskStamp">The on-disk stamp that must still match before deletion.</param>
/// <param name="UseRecycleBin"><see langword="true"/> to request recycle-bin deletion where supported; <see langword="false"/> to delete permanently.</param>
public sealed record WorkspaceDocumentDeleteRequest(
	WorkspaceDocumentKey ExpectedDocumentKey,
	string DocumentId,
	long ExpectedVersion,
	FileStamp ExpectedOnDiskStamp,
	bool UseRecycleBin = false);

/// <summary>
/// Describes the outcome of deleting a document.
/// </summary>
public enum WorkspaceDocumentDeleteStatus
{
	/// <summary>The document was deleted.</summary>
	Deleted,

	/// <summary>The path is invalid.</summary>
	InvalidPath,

	/// <summary>The document version was stale.</summary>
	StaleDocument,

	/// <summary>The document identity was stale.</summary>
	StaleDocumentInstance,

	/// <summary>The document was not found.</summary>
	DocumentNotFound,

	/// <summary>Another operation is in progress.</summary>
	OperationInProgress,

	/// <summary>An attached view has pending edits, a conflict, or a prior synchronization failure.</summary>
	ViewNotSynchronized,

	/// <summary>The file stamp did not match the expected stamp.</summary>
	ExternalFileConflict,

	/// <summary>The delete failed.</summary>
	DeleteFailed,

	/// <summary>View synchronization failed after the document was deleted.</summary>
	ViewUpdateFailed,

	/// <summary>The operation was cancelled.</summary>
	Cancelled
}

/// <summary>
/// Contains the outcome of deleting a document.
/// </summary>
/// <remarks>
/// The snapshot in a successful result describes the document immediately before it was removed
/// from tracking. <see cref="FailedViewIds"/> is populated only when the view manager could not
/// close or release all attached views.
/// </remarks>
public sealed record WorkspaceDocumentDeleteResult(
	WorkspaceDocumentDeleteStatus Status,
	WorkspaceDocumentKey RequestedDocumentKey,
	string RequestedDocumentId,
	long RequestedVersion,
	WorkspaceDocumentSnapshot? Snapshot,
	FileStamp? ObservedOnDiskStamp = null,
	WorkspaceOperationFailure? Failure = null,
	IReadOnlyList<string>? FailedViewIds = null);

/// <summary>
/// Describes the outcome of a synchronous logical document mutation.
/// </summary>
/// <remarks>No status in this enumeration writes to disk.</remarks>
public enum WorkspaceDocumentMutationStatus
{
	/// <summary>The content was replaced.</summary>
	Replaced,

	/// <summary>The request made no logical change.</summary>
	NoChange,

	/// <summary>The document version was stale.</summary>
	StaleDocument,

	/// <summary>The document identity was stale.</summary>
	StaleDocumentInstance,

	/// <summary>The document was not found.</summary>
	DocumentNotFound,

	/// <summary>Another operation is in progress.</summary>
	OperationInProgress
}

/// <summary>
/// Contains the outcome of a synchronous logical document mutation.
/// </summary>
/// <remarks>
/// <see cref="Snapshot"/> contains the current tracked state for every status except
/// <see cref="WorkspaceDocumentMutationStatus.DocumentNotFound"/>.
/// </remarks>
public sealed record WorkspaceDocumentMutationResult(
	WorkspaceDocumentMutationStatus Status,
	WorkspaceDocumentKey RequestedDocumentKey,
	string RequestedDocumentId,
	long RequestedVersion,
	WorkspaceDocumentSnapshot? Snapshot);

/// <summary>
/// Describes the outcome of opening or loading a workspace document.
/// </summary>
/// <remarks><see cref="AlreadyOpen"/> means the existing tracked snapshot was returned without another load.</remarks>
public enum WorkspaceDocumentOpenStatus
{
	/// <summary>The document was opened.</summary>
	Opened,

	/// <summary>The document was already open.</summary>
	AlreadyOpen,

	/// <summary>The path is invalid.</summary>
	InvalidPath,

	/// <summary>The document could not be loaded.</summary>
	LoadFailed,

	/// <summary>The operation was cancelled.</summary>
	Cancelled
}

/// <summary>
/// Contains the outcome of opening or loading a workspace document.
/// </summary>
/// <remarks>
/// A successful result includes a snapshot. A load failure includes a typed
/// <see cref="WorkspaceOperationFailure"/> and no snapshot; cancellation and invalid paths also
/// return no snapshot.
/// </remarks>
public sealed record WorkspaceDocumentOpenResult(
	WorkspaceDocumentOpenStatus Status,
	WorkspaceDocumentSnapshot? Snapshot,
	WorkspaceOperationFailure? Failure = null);

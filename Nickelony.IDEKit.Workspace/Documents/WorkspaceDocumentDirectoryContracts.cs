namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Supplies the expected state of one document in a batch file operation.
/// </summary>
/// <param name="ExpectedDocumentKey">The document incarnation expected by the caller.</param>
/// <param name="DocumentId">The normalized document id expected by the caller.</param>
/// <param name="ExpectedVersion">The document version captured by the caller.</param>
/// <param name="ExpectedOnDiskStamp">The file stamp captured by the caller.</param>
public sealed record WorkspaceDocumentBatchEntry(
	WorkspaceDocumentKey ExpectedDocumentKey,
	string DocumentId,
	long ExpectedVersion,
	FileStamp ExpectedOnDiskStamp);

/// <summary>
/// Requests a directory move and the document identities it must update.
/// </summary>
/// <remarks>
/// <see cref="Documents"/> is an optimistic batch: every entry is validated by document key, version,
/// location, and expected on-disk stamp before the directory is moved. Only the listed tracked
/// documents are rekeyed in the store; the directory move itself applies to the complete directory
/// on disk.
/// </remarks>
public sealed record WorkspaceDocumentDirectoryRenameRequest(
	string SourceDirectoryPath,
	string DestinationDirectoryPath,
	IReadOnlyList<WorkspaceDocumentBatchEntry> Documents);

/// <summary>
/// Describes the outcome of renaming a directory and its tracked documents.
/// </summary>
public enum WorkspaceDocumentDirectoryRenameStatus
{
	/// <summary>The directory was renamed and the listed tracked documents were rekeyed.</summary>
	Renamed,

	/// <summary>The path is invalid.</summary>
	InvalidPath,

	/// <summary>A document version was stale.</summary>
	StaleDocument,

	/// <summary>A document identity was stale.</summary>
	StaleDocumentInstance,

	/// <summary>A document was not found.</summary>
	DocumentNotFound,

	/// <summary>Another operation is in progress.</summary>
	OperationInProgress,

	/// <summary>A listed document's file stamp changed before the directory move.</summary>
	ExternalFileConflict,

	/// <summary>The destination already exists.</summary>
	DestinationExists,

	/// <summary>A tracked document already uses a destination identity.</summary>
	DestinationInUse,

	/// <summary>A concurrent move or save operation reserved a destination identity.</summary>
	DestinationBusy,

	/// <summary>The move failed.</summary>
	MoveFailed,

	/// <summary>View synchronization prevented the move or failed after the directory moved.</summary>
	ViewUpdateFailed,

	/// <summary>The operation was cancelled.</summary>
	Cancelled
}

/// <summary>
/// Contains the outcome of renaming a directory and its tracked documents.
/// </summary>
/// <remarks>
/// <c>Snapshots</c> contains the current snapshots of the listed documents when available.
/// A result produced by the view manager may also contain <c>FailedViewIds</c> when view
/// synchronization prevented the operation or failed after the directory operation completed.
/// </remarks>
public sealed record WorkspaceDocumentDirectoryRenameResult(
	WorkspaceDocumentDirectoryRenameStatus Status,
	string SourceDirectoryPath,
	string DestinationDirectoryPath,
	IReadOnlyList<WorkspaceDocumentSnapshot> Snapshots,
	WorkspaceOperationFailure? Failure = null,
	IReadOnlyList<string>? FailedViewIds = null);

/// <summary>
/// Requests deletion of a directory and the documents it contains.
/// </summary>
/// <param name="DirectoryPath">The directory path to delete.</param>
/// <param name="Documents">The tracked documents whose keys, versions, paths, and on-disk stamps must still match before deletion.</param>
/// <param name="UseRecycleBin"><see langword="true"/> to request recycle-bin deletion where supported; <see langword="false"/> to delete permanently.</param>
/// <remarks>
/// The directory operation itself can remove unlisted files as part of the recursive delete.
/// </remarks>
public sealed record WorkspaceDocumentDirectoryDeleteRequest(
	string DirectoryPath,
	IReadOnlyList<WorkspaceDocumentBatchEntry> Documents,
	bool UseRecycleBin = false);

/// <summary>
/// Describes the outcome of deleting a directory and its tracked documents.
/// </summary>
public enum WorkspaceDocumentDirectoryDeleteStatus
{
	/// <summary>The directory was recursively deleted and the listed tracked documents were removed.</summary>
	Deleted,

	/// <summary>The path is invalid.</summary>
	InvalidPath,

	/// <summary>A document version was stale.</summary>
	StaleDocument,

	/// <summary>A document identity was stale.</summary>
	StaleDocumentInstance,

	/// <summary>A document was not found.</summary>
	DocumentNotFound,

	/// <summary>Another operation is in progress.</summary>
	OperationInProgress,

	/// <summary>A listed document's file stamp changed before directory deletion.</summary>
	ExternalFileConflict,

	/// <summary>The delete failed.</summary>
	DeleteFailed,

	/// <summary>An attached view was not synchronized and blocked deletion.</summary>
	ViewNotSynchronized,

	/// <summary>View synchronization prevented deletion or one or more attached views could not be closed after deletion.</summary>
	ViewUpdateFailed,

	/// <summary>The operation was cancelled.</summary>
	Cancelled
}

/// <summary>
/// Contains the outcome of deleting a directory and its tracked documents.
/// </summary>
/// <remarks>
/// <c>Snapshots</c> contains snapshots of the listed documents captured before successful
/// removal from tracking. <c>FailedViewIds</c> identifies views that prevented deletion or could
/// not be closed after deletion.
/// </remarks>
public sealed record WorkspaceDocumentDirectoryDeleteResult(
	WorkspaceDocumentDirectoryDeleteStatus Status,
	string DirectoryPath,
	IReadOnlyList<WorkspaceDocumentSnapshot> Snapshots,
	WorkspaceOperationFailure? Failure = null,
	IReadOnlyList<string>? FailedViewIds = null);

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Owns logical workspace documents and their persisted filesystem state.
/// </summary>
/// <remarks>
/// Requests use a normalized <c>DocumentId</c>, a <see cref="WorkspaceDocumentKey"/>, and an
/// expected <c>Version</c> to provide optimistic concurrency checks. Operation results report
/// expected-state failures instead of silently applying a request to a different document state.
/// </remarks>
public interface IWorkspaceDocumentStore : IAsyncDisposable
{
	/// <summary>Opens a document and loads its content from disk, or creates an empty logical document for a missing path.</summary>
	/// <remarks>
	/// The path is normalized for document identity while the supplied path is retained as
	/// <see cref="WorkspaceDocumentSnapshot.DisplayPath"/>. Opening an already tracked path returns
	/// <see cref="WorkspaceDocumentOpenStatus.AlreadyOpen"/> without reading it again. A missing path
	/// uses <see cref="WorkspaceDocumentOpenOptions.NewFileFormat"/>.
	/// </remarks>
	Task<WorkspaceDocumentOpenResult> OpenAsync(
		string? filePath,
		WorkspaceDocumentOpenOptions options,
		CancellationToken cancellationToken = default);

	/// <summary>Gets tracked document snapshots for documents below a directory.</summary>
	/// <remarks>The result is ordered by normalized document id and is empty for an invalid directory path.</remarks>
	IReadOnlyList<WorkspaceDocumentSnapshot> GetSnapshotsUnderDirectory(string directoryPath);

	/// <summary>Attempts to get a tracked document snapshot by path.</summary>
	/// <returns><see langword="true"/> and the current snapshot when the path is tracked; otherwise <see langword="false"/> and <see langword="null"/>.</returns>
	bool TryGetSnapshot(string? filePath, out WorkspaceDocumentSnapshot? snapshot);

	/// <summary>Replaces the logical content and format of a tracked document without writing to disk.</summary>
	/// <remarks>The request is accepted only when its document key and version match the current snapshot and no delete operation is active.</remarks>
	WorkspaceDocumentMutationResult TryReplace(
		WorkspaceDocumentReplaceRequest request);

	/// <summary>Conditionally commits a tracked document's captured logical content and format to disk.</summary>
	/// <remarks>
	/// The expected on-disk stamp is checked before replacement. A later logical edit made while the
	/// commit is in progress remains in the returned snapshot as dirty content, while the captured
	/// version becomes the persisted baseline. A replacement whose final state cannot be determined
	/// is reported as <see cref="WorkspaceDocumentCommitStatus.ReplacementStateUnknown"/>.
	/// </remarks>
	Task<WorkspaceDocumentCommitResult> CommitAsync(
		WorkspaceDocumentCommitRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>Reloads a tracked document from disk when its logical content is clean.</summary>
	/// <remarks>
	/// A dirty document is not overwritten: the method captures the current disk stamp and returns
	/// <see cref="WorkspaceDocumentReloadStatus.ExternalFileConflict"/>. For a clean document, the
	/// expected disk stamp determines whether the result is <see cref="WorkspaceDocumentReloadStatus.Unchanged"/>
	/// or <see cref="WorkspaceDocumentReloadStatus.Reloaded"/>; the request's version must still match
	/// before the snapshot is updated.
	/// </remarks>
	Task<WorkspaceDocumentReloadResult> ReloadAsync(
		WorkspaceDocumentReloadRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>Resolves an external change conflict by choosing disk content or logical content.</summary>
	/// <remarks>
	/// <see cref="WorkspaceDocumentConflictResolutionChoice.UseLogical"/> writes the logical snapshot
	/// after capturing the current disk stamp. <see cref="WorkspaceDocumentConflictResolutionChoice.UseDisk"/>
	/// reads and adopts the current disk content. The supplied observed stamp identifies the conflict
	/// being resolved; it is not reused as the write precondition.
	/// </remarks>
	Task<WorkspaceDocumentConflictResolutionResult> ResolveExternalConflictAsync(
		WorkspaceDocumentConflictResolutionRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>Discards unsaved logical content and format changes by restoring the persisted baseline.</summary>
	/// <remarks>This is a synchronous in-memory mutation; it does not write to disk.</remarks>
	WorkspaceDocumentMutationResult Discard(
		WorkspaceDocumentDiscardRequest request);

	/// <summary>Moves a tracked document to a new path and updates its document identity path.</summary>
	/// <remarks>The logical document key is preserved; attached-view synchronization is the manager's responsibility.</remarks>
	Task<WorkspaceDocumentRenameResult> RenameAsync(
		WorkspaceDocumentRenameRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>Writes a tracked document at a second path and retargets the tracked document to that path.</summary>
	/// <remarks>The original file is not deleted. The logical document key is preserved.</remarks>
	Task<WorkspaceDocumentSaveAsResult> SaveAsAsync(
		WorkspaceDocumentSaveAsRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>Deletes a tracked document's file and removes the document from the store after a successful delete.</summary>
	Task<WorkspaceDocumentDeleteResult> DeleteAsync(
		WorkspaceDocumentDeleteRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>Moves a directory and updates the paths of the listed tracked documents below it.</summary>
	/// <remarks>The listed documents are validated by key, version, path, and expected disk stamp before the directory move.</remarks>
	Task<WorkspaceDocumentDirectoryRenameResult> RenameDirectoryAsync(
		WorkspaceDocumentDirectoryRenameRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>Recursively deletes a directory and removes the listed tracked documents after success.</summary>
	/// <remarks>The listed documents are validated by key, version, path, and expected disk stamp before deletion.</remarks>
	Task<WorkspaceDocumentDirectoryDeleteResult> DeleteDirectoryAsync(
		WorkspaceDocumentDirectoryDeleteRequest request,
		CancellationToken cancellationToken = default);
}

/// <summary>
/// Requests persistence of the current logical document content.
/// </summary>
/// <param name="ExpectedDocumentKey">The document incarnation expected by the caller.</param>
/// <param name="DocumentId">The normalized document id expected by the caller.</param>
/// <param name="ExpectedVersion">The document version captured by the caller.</param>
/// <param name="ExpectedOnDiskStamp">The on-disk stamp that must still match before writing.</param>
public sealed record WorkspaceDocumentCommitRequest(
	WorkspaceDocumentKey ExpectedDocumentKey,
	string DocumentId,
	long ExpectedVersion,
	FileStamp ExpectedOnDiskStamp);

/// <summary>
/// Describes the outcome of persisting a workspace document.
/// </summary>
public enum WorkspaceDocumentCommitStatus
{
	/// <summary>The document was committed.</summary>
	Committed,

	/// <summary>The document was committed, but one or more attached views remained unsynchronized.</summary>
	CommittedWithUnsynchronizedView,

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

	/// <summary>The current disk stamp differs from the stamp expected by the commit.</summary>
	ExternalFileConflict,

	/// <summary>The write failed.</summary>
	WriteFailed,

	/// <summary>The replacement state is unknown.</summary>
	ReplacementStateUnknown,

	/// <summary>The operation was cancelled.</summary>
	Cancelled
}

/// <summary>
/// Contains the outcome of persisting a workspace document.
/// </summary>
/// <remarks>
/// <see cref="Snapshot"/> is the current tracked state when the document exists, including for
/// most failures. It is <see langword="null"/> when the document was not found. The requested key,
/// id, and version are echoed even when validation fails.
/// </remarks>
public sealed record WorkspaceDocumentCommitResult(
	WorkspaceDocumentCommitStatus Status,
	WorkspaceDocumentKey RequestedDocumentKey,
	string RequestedDocumentId,
	long RequestedVersion,
	WorkspaceDocumentSnapshot? Snapshot,
	FileStamp? ObservedOnDiskStamp = null,
	WorkspaceOperationFailure? Failure = null,
	IReadOnlyList<string>? BlockingViewIds = null);

/// <summary>
/// Requests that a workspace document be reloaded from disk.
/// </summary>
/// <param name="ExpectedDocumentKey">The document incarnation expected by the caller.</param>
/// <param name="DocumentId">The normalized document id expected by the caller.</param>
/// <param name="ExpectedVersion">The document version captured by the caller.</param>
/// <param name="ExpectedOnDiskStamp">The stamp the caller last observed for the file.</param>
public sealed record WorkspaceDocumentReloadRequest(
	WorkspaceDocumentKey ExpectedDocumentKey,
	string DocumentId,
	long ExpectedVersion,
	FileStamp ExpectedOnDiskStamp);

/// <summary>
/// Describes the outcome of reloading a workspace document.
/// </summary>
public enum WorkspaceDocumentReloadStatus
{
	/// <summary>The document was reloaded.</summary>
	Reloaded,

	/// <summary>The document was already current.</summary>
	Unchanged,

	/// <summary>The document version was stale.</summary>
	StaleDocument,

	/// <summary>The document identity was stale.</summary>
	StaleDocumentInstance,

	/// <summary>The document was not found.</summary>
	DocumentNotFound,

	/// <summary>Another operation is in progress.</summary>
	OperationInProgress,

	/// <summary>The logical document is dirty and was not overwritten by the reload.</summary>
	ExternalFileConflict,

	/// <summary>The read failed.</summary>
	ReadFailed,

	/// <summary>The operation was cancelled.</summary>
	Cancelled
}

/// <summary>
/// Contains the outcome of reloading a workspace document.
/// </summary>
/// <remarks>
/// <see cref="ObservedOnDiskStamp"/> reports the stamp obtained during the operation when one was
/// available. <see cref="Snapshot"/> is the current tracked state for a tracked document and is
/// <see langword="null"/> only when the document was not found.
/// </remarks>
public sealed record WorkspaceDocumentReloadResult(
	WorkspaceDocumentReloadStatus Status,
	WorkspaceDocumentKey RequestedDocumentKey,
	string RequestedDocumentId,
	long RequestedVersion,
	WorkspaceDocumentSnapshot? Snapshot,
	FileStamp? ObservedOnDiskStamp = null,
	WorkspaceOperationFailure? Failure = null);

/// <summary>
/// Chooses the source of truth when logical content conflicts with disk content.
/// </summary>
public enum WorkspaceDocumentConflictResolutionChoice
{
	/// <summary>Use the content currently on disk.</summary>
	UseDisk,

	/// <summary>Keep the logical document content and write it to disk.</summary>
	UseLogical
}

/// <summary>
/// Requests resolution of an external workspace document conflict.
/// </summary>
/// <param name="ExpectedDocumentKey">The document incarnation expected by the caller.</param>
/// <param name="DocumentId">The normalized document id expected by the caller.</param>
/// <param name="ExpectedVersion">The document version captured by the caller.</param>
/// <param name="ObservedOnDiskStamp">The disk stamp that caused the conflict and must be revalidated.</param>
/// <param name="Choice">The source of truth to use when resolving the conflict.</param>
public sealed record WorkspaceDocumentConflictResolutionRequest(
	WorkspaceDocumentKey ExpectedDocumentKey,
	string DocumentId,
	long ExpectedVersion,
	FileStamp ObservedOnDiskStamp,
	WorkspaceDocumentConflictResolutionChoice Choice);

/// <summary>
/// Describes the outcome of resolving an external workspace document conflict.
/// </summary>
public enum WorkspaceDocumentConflictResolutionStatus
{
	/// <summary>The conflict was resolved with disk content.</summary>
	ResolvedWithDisk,

	/// <summary>The conflict was resolved with logical content.</summary>
	ResolvedWithLogical,

	/// <summary>The conflict was resolved, but one or more attached views remained unsynchronized.</summary>
	ResolvedWithUnsynchronizedView,

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

	/// <summary>The disk state changed again while the conflict was being resolved.</summary>
	ExternalFileConflict,

	/// <summary>The read failed.</summary>
	ReadFailed,

	/// <summary>The write failed.</summary>
	WriteFailed,

	/// <summary>The replacement state is unknown.</summary>
	ReplacementStateUnknown,

	/// <summary>The operation was cancelled.</summary>
	Cancelled
}

/// <summary>
/// Contains the outcome of resolving an external workspace document conflict.
/// </summary>
/// <remarks>
/// <see cref="Choice"/> echoes the requested source of truth. For <see cref="WorkspaceDocumentConflictResolutionStatus.ResolvedWithLogical"/>,
/// the logical snapshot was committed; for <see cref="WorkspaceDocumentConflictResolutionStatus.ResolvedWithDisk"/>,
/// it was replaced with freshly read disk content. A later logical edit may leave the returned
/// snapshot dirty even when the resolution itself succeeded.
/// </remarks>
public sealed record WorkspaceDocumentConflictResolutionResult(
	WorkspaceDocumentConflictResolutionStatus Status,
	WorkspaceDocumentKey RequestedDocumentKey,
	string RequestedDocumentId,
	long RequestedVersion,
	WorkspaceDocumentConflictResolutionChoice Choice,
	WorkspaceDocumentSnapshot? Snapshot,
	FileStamp? ObservedOnDiskStamp = null,
	WorkspaceOperationFailure? Failure = null,
	IReadOnlyList<string>? BlockingViewIds = null);

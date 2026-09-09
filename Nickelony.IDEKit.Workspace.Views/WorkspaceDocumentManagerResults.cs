using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>
/// Contains the outcome of a manager logical-mutation operation.
/// </summary>
/// <remarks>
/// The mutation itself never blocks on attached views, so <see cref="StoreResult"/> is always
/// populated. A mutation that completed while a view could not acknowledge or refresh reports its
/// store status together with <see cref="WorkspaceDocumentViewSynchronizationStatus.Unsynchronized"/>.
/// </remarks>
/// <param name="StoreResult">The document-authority outcome.</param>
/// <param name="Views">The view-synchronization outcome.</param>
/// <param name="Snapshot">The current document snapshot when the document is still tracked; otherwise, <see langword="null"/>.</param>
public sealed record WorkspaceDocumentManagerMutationResult(
	WorkspaceDocumentMutationResult StoreResult,
	WorkspaceDocumentViewSynchronization Views,
	WorkspaceDocumentSnapshot? Snapshot)
{
	/// <summary>
	/// Gets the document-authority status.
	/// </summary>
	public WorkspaceDocumentMutationStatus Status => StoreResult.Status;
}

/// <summary>
/// Contains the outcome of a manager commit operation.
/// </summary>
/// <remarks>
/// <see cref="StoreResult"/> is <see langword="null"/> when attached views blocked the commit before
/// the store was reached. A commit that succeeded while a view stayed unsynchronized reports
/// <see cref="WorkspaceDocumentCommitStatus.Committed"/> together with
/// <see cref="WorkspaceDocumentViewSynchronizationStatus.Unsynchronized"/>.
/// </remarks>
/// <param name="StoreResult">The document-authority outcome; <see langword="null"/> when attached views blocked the operation.</param>
/// <param name="Views">The view-synchronization outcome.</param>
/// <param name="Snapshot">The current document snapshot when the document is still tracked; otherwise, <see langword="null"/>.</param>
public sealed record WorkspaceDocumentManagerCommitResult(
	WorkspaceDocumentCommitResult? StoreResult,
	WorkspaceDocumentViewSynchronization Views,
	WorkspaceDocumentSnapshot? Snapshot)
{
	/// <summary>
	/// Gets the document-authority status, or <see langword="null"/> when attached views blocked the
	/// operation before the store was reached.
	/// </summary>
	public WorkspaceDocumentCommitStatus? Status => StoreResult?.Status;
}

/// <summary>
/// Contains the outcome of a manager rename operation.
/// </summary>
/// <remarks>
/// <see cref="StoreResult"/> is always populated: the rename never blocks on attached views. A rename
/// that completed while a view could not accept the new identity
/// reports <see cref="WorkspaceDocumentRenameStatus.Renamed"/> together with
/// <see cref="WorkspaceDocumentViewSynchronizationStatus.Unsynchronized"/>.
/// </remarks>
/// <param name="StoreResult">The document-authority outcome.</param>
/// <param name="Views">The view-synchronization outcome.</param>
/// <param name="Snapshot">The current document snapshot when the document is still tracked; otherwise, <see langword="null"/>. The renamed identity is only in this snapshot; the wrapped store result echoes the identity supplied with the request.</param>
public sealed record WorkspaceDocumentManagerRenameResult(
	WorkspaceDocumentRenameResult StoreResult,
	WorkspaceDocumentViewSynchronization Views,
	WorkspaceDocumentSnapshot? Snapshot)
{
	/// <summary>
	/// Gets the document-authority status.
	/// </summary>
	public WorkspaceDocumentRenameStatus Status => StoreResult.Status;
}

/// <summary>
/// Contains the outcome of a manager save-as operation.
/// </summary>
/// <remarks>
/// <see cref="StoreResult"/> is always populated: the save never blocks on attached views. A save
/// that completed while a view could not accept the new identity
/// reports <see cref="WorkspaceDocumentSaveAsStatus.SavedAs"/> together with
/// <see cref="WorkspaceDocumentViewSynchronizationStatus.Unsynchronized"/>.
/// </remarks>
/// <param name="StoreResult">The document-authority outcome.</param>
/// <param name="Views">The view-synchronization outcome.</param>
/// <param name="Snapshot">The current document snapshot when the document is still tracked; otherwise, <see langword="null"/>. The retargeted identity is only in this snapshot; the wrapped store result echoes the identity supplied with the request.</param>
public sealed record WorkspaceDocumentManagerSaveAsResult(
	WorkspaceDocumentSaveAsResult StoreResult,
	WorkspaceDocumentViewSynchronization Views,
	WorkspaceDocumentSnapshot? Snapshot)
{
	/// <summary>
	/// Gets the document-authority status.
	/// </summary>
	public WorkspaceDocumentSaveAsStatus Status => StoreResult.Status;
}

/// <summary>
/// Contains the outcome of a manager delete operation.
/// </summary>
/// <remarks>
/// <see cref="StoreResult"/> is <see langword="null"/> when attached views blocked the delete before
/// the store was reached. A delete that completed while a view could not be closed reports
/// <see cref="WorkspaceDocumentDeleteStatus.Deleted"/> together with
/// <see cref="WorkspaceDocumentViewSynchronizationStatus.Unsynchronized"/>.
/// </remarks>
/// <param name="StoreResult">The document-authority outcome; <see langword="null"/> when attached views blocked the operation.</param>
/// <param name="Views">The view-synchronization outcome.</param>
/// <param name="Snapshot">The document state immediately before it was removed from tracking; otherwise, <see langword="null"/>.</param>
public sealed record WorkspaceDocumentManagerDeleteResult(
	WorkspaceDocumentDeleteResult? StoreResult,
	WorkspaceDocumentViewSynchronization Views,
	WorkspaceDocumentSnapshot? Snapshot)
{
	/// <summary>
	/// Gets the document-authority status, or <see langword="null"/> when attached views blocked the
	/// operation before the store was reached.
	/// </summary>
	public WorkspaceDocumentDeleteStatus? Status => StoreResult?.Status;
}

/// <summary>
/// Contains the outcome of a manager conflict-resolution operation.
/// </summary>
/// <remarks>
/// <see cref="StoreResult"/> is <see langword="null"/> when attached views blocked the resolution
/// before the store was reached. A resolution that completed while a view could not refresh reports
/// <see cref="WorkspaceDocumentConflictResolutionStatus.ResolvedWithDisk"/> or
/// <see cref="WorkspaceDocumentConflictResolutionStatus.ResolvedWithLogical"/> together with
/// <see cref="WorkspaceDocumentViewSynchronizationStatus.Unsynchronized"/>.
/// </remarks>
/// <param name="StoreResult">The document-authority outcome; <see langword="null"/> when attached views blocked the operation.</param>
/// <param name="Views">The view-synchronization outcome.</param>
/// <param name="Snapshot">The current document snapshot when the document is still tracked; otherwise, <see langword="null"/>.</param>
public sealed record WorkspaceDocumentManagerConflictResolutionResult(
	WorkspaceDocumentConflictResolutionResult? StoreResult,
	WorkspaceDocumentViewSynchronization Views,
	WorkspaceDocumentSnapshot? Snapshot)
{
	/// <summary>
	/// Gets the document-authority status, or <see langword="null"/> when attached views blocked the
	/// operation before the store was reached.
	/// </summary>
	public WorkspaceDocumentConflictResolutionStatus? Status => StoreResult?.Status;
}

/// <summary>
/// Contains the outcome of a manager reload operation.
/// </summary>
/// <remarks>
/// <see cref="StoreResult"/> is <see langword="null"/> when attached views blocked the reload before
/// the store was reached. A reload that replaced the document content while a view could not refresh
/// reports <see cref="WorkspaceDocumentReloadStatus.Reloaded"/> together with
/// <see cref="WorkspaceDocumentViewSynchronizationStatus.Unsynchronized"/>.
/// </remarks>
/// <param name="StoreResult">The document-authority outcome; <see langword="null"/> when attached views blocked the operation.</param>
/// <param name="Views">The view-synchronization outcome.</param>
/// <param name="Snapshot">The current document snapshot when the document is still tracked; otherwise, <see langword="null"/>.</param>
public sealed record WorkspaceDocumentManagerReloadResult(
	WorkspaceDocumentReloadResult? StoreResult,
	WorkspaceDocumentViewSynchronization Views,
	WorkspaceDocumentSnapshot? Snapshot)
{
	/// <summary>
	/// Gets the document-authority status, or <see langword="null"/> when attached views blocked the
	/// operation before the store was reached.
	/// </summary>
	public WorkspaceDocumentReloadStatus? Status => StoreResult?.Status;
}

/// <summary>
/// Contains the outcome of a manager directory-rename operation.
/// </summary>
/// <remarks>
/// <see cref="StoreResult"/> is <see langword="null"/> when attached views blocked the move before
/// the store was reached; <see cref="Snapshots"/> then carries the source snapshots of the listed
/// documents. A move that completed while a view could not accept its new identity reports
/// <see cref="WorkspaceDocumentDirectoryRenameStatus.Renamed"/> together with
/// <see cref="WorkspaceDocumentViewSynchronizationStatus.Unsynchronized"/>.
/// </remarks>
/// <param name="StoreResult">The document-authority outcome; <see langword="null"/> when attached views blocked the operation.</param>
/// <param name="Views">The view-synchronization outcome.</param>
/// <param name="Snapshots">The current snapshots of the listed documents when available; otherwise, the source snapshots.</param>
public sealed record WorkspaceDocumentManagerDirectoryRenameResult(
	WorkspaceDocumentDirectoryRenameResult? StoreResult,
	WorkspaceDocumentViewSynchronization Views,
	IReadOnlyList<WorkspaceDocumentSnapshot> Snapshots)
{
	/// <summary>
	/// Gets the document-authority status, or <see langword="null"/> when attached views blocked the
	/// operation before the store was reached.
	/// </summary>
	public WorkspaceDocumentDirectoryRenameStatus? Status => StoreResult?.Status;
}

/// <summary>
/// Contains the outcome of a manager directory-delete operation.
/// </summary>
/// <remarks>
/// <see cref="StoreResult"/> is <see langword="null"/> when attached views blocked the delete before
/// the store was reached; <see cref="Snapshots"/> then carries the source snapshots of the listed
/// documents. A delete that completed while a view could not be closed reports
/// <see cref="WorkspaceDocumentDirectoryDeleteStatus.Deleted"/> together with
/// <see cref="WorkspaceDocumentViewSynchronizationStatus.Unsynchronized"/>.
/// </remarks>
/// <param name="StoreResult">The document-authority outcome; <see langword="null"/> when attached views blocked the operation.</param>
/// <param name="Views">The view-synchronization outcome.</param>
/// <param name="Snapshots">The snapshots of the listed documents; the source snapshots when attached views blocked the operation.</param>
public sealed record WorkspaceDocumentManagerDirectoryDeleteResult(
	WorkspaceDocumentDirectoryDeleteResult? StoreResult,
	WorkspaceDocumentViewSynchronization Views,
	IReadOnlyList<WorkspaceDocumentSnapshot> Snapshots)
{
	/// <summary>
	/// Gets the document-authority status, or <see langword="null"/> when attached views blocked the
	/// operation before the store was reached.
	/// </summary>
	public WorkspaceDocumentDirectoryDeleteStatus? Status => StoreResult?.Status;
}

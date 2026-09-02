using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>
/// Coordinates workspace document authority with registered views.
/// </summary>
/// <remarks>
/// The manager keeps view bindings and document mutations coordinated, but it does not own the
/// editor or UI thread. View access is dispatched through the host callback supplied to the
/// implementation, and view synchronization failures are represented in operation results or by
/// subsequent blocking state.
/// </remarks>
public interface IWorkspaceDocumentManager : IAsyncDisposable
{
	/// <summary>
	/// Opens a document through the workspace authority without attaching a view.
	/// </summary>
	/// <remarks>This operation has the same path, reservation, and load statuses as the document store.</remarks>
	Task<WorkspaceDocumentOpenResult> OpenAsync(
		string? filePath,
		WorkspaceDocumentOpenOptions options,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets tracked document snapshots below a directory.
	/// </summary>
	/// <remarks>The result contains document authority state and does not include view bindings.</remarks>
	IReadOnlyList<WorkspaceDocumentSnapshot> GetSnapshotsUnderDirectory(string directoryPath);

	/// <summary>
	/// Opens a document and attaches a view to it.
	/// </summary>
	/// <remarks>
	/// The view is opened through the host callback. An already registered view or a view that already
	/// has a document, pending edits, or a conflict is not reused for a new attachment.
	/// </remarks>
	Task<WorkspaceDocumentManagerOpenResult> OpenWithViewAsync(
		string? filePath,
		WorkspaceDocumentOpenOptions options,
		IWorkspaceDocumentView view,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Resolves an external change conflict for a tracked document and refreshes attached views when possible.
	/// </summary>
	/// <remarks>A view that cannot refresh changes a successful resolution to <see cref="WorkspaceDocumentConflictResolutionStatus.ResolvedWithUnsynchronizedView"/>.</remarks>
	Task<WorkspaceDocumentConflictResolutionResult> ResolveExternalConflictAsync(
		WorkspaceDocumentConflictResolutionRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Synchronously opens a document and attaches a view.
	/// </summary>
	/// <remarks>
	/// This member blocks the calling thread while the underlying asynchronous store
	/// operation runs. It exists for host callers that cannot await. Do not call it
	/// from a thread with a captured synchronization context that the caller must pump
	/// (for example the UI thread), because blocking can deadlock. Prefer
	/// <see cref="OpenWithViewAsync(string?, WorkspaceDocumentOpenOptions, IWorkspaceDocumentView, CancellationToken)"/>.
	/// </remarks>
	WorkspaceDocumentManagerOpenResult OpenWithView(
		string? filePath,
		WorkspaceDocumentOpenOptions options,
		IWorkspaceDocumentView view,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Applies a replacement to a workspace document without writing to disk.
	/// </summary>
	/// <remarks>The manager performs the mutation and asks older peer views to refresh through the host callback.</remarks>
	WorkspaceDocumentMutationResult Replace(WorkspaceDocumentReplaceRequest request);

	/// <summary>
	/// Discards unsaved changes and refreshes attached views.
	/// </summary>
	/// <remarks>View refresh failures are retained as unsynchronized state and can block later disk operations.</remarks>
	WorkspaceDocumentMutationResult Discard(WorkspaceDocumentDiscardRequest request);

	/// <summary>
	/// Renames a tracked document and updates attached views.
	/// </summary>
	/// <remarks>The document operation is performed first; a view update failure is reported as <see cref="WorkspaceDocumentRenameStatus.ViewUpdateFailed"/>.</remarks>
	Task<WorkspaceDocumentRenameResult> RenameAsync(
		WorkspaceDocumentRenameRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Saves a tracked document at a second path and updates attached views.
	/// </summary>
	/// <remarks>The source file is retained. A view update failure is reported after the document has been retargeted.</remarks>
	Task<WorkspaceDocumentSaveAsResult> SaveAsAsync(
		WorkspaceDocumentSaveAsRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Deletes a tracked document and updates attached views.
	/// </summary>
	/// <remarks>Attached views must accept a delete guard before deletion; successful deletion then closes and unregisters them.</remarks>
	Task<WorkspaceDocumentDeleteResult> DeleteAsync(
		WorkspaceDocumentDeleteRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Renames a directory and updates the identities of tracked documents below it.
	/// </summary>
	/// <remarks>Attached views are guarded before the move and acknowledge their new identities afterward.</remarks>
	Task<WorkspaceDocumentDirectoryRenameResult> RenameDirectoryAsync(
		WorkspaceDocumentDirectoryRenameRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Deletes a directory and its tracked documents.
	/// </summary>
	/// <remarks>Attached views are guarded before recursive deletion and closed afterward.</remarks>
	Task<WorkspaceDocumentDirectoryDeleteResult> DeleteDirectoryAsync(
		WorkspaceDocumentDirectoryDeleteRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Commits a tracked document to disk and refreshes attached views.
	/// </summary>
	/// <remarks>
	/// Views with pending edits, conflicts, or a prior synchronization failure block the commit. A
	/// commit can still return <see cref="WorkspaceDocumentCommitStatus.CommittedWithUnsynchronizedView"/>
	/// when a view fails to refresh after the write.
	/// </remarks>
	Task<WorkspaceDocumentCommitResult> CommitAsync(
		WorkspaceDocumentCommitRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Reloads a tracked document from disk and refreshes attached views when content changes.
	/// </summary>
	/// <remarks>Dirty documents are handled by the store as conflicts and are not overwritten.</remarks>
	Task<WorkspaceDocumentReloadResult> ReloadAsync(
		WorkspaceDocumentReloadRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Unregisters a view that is no longer open.
	/// </summary>
	/// <remarks>This method removes the binding and event subscription; it does not call <see cref="IWorkspaceDocumentView.Close"/>.</remarks>
	void UnregisterOpenView(IWorkspaceDocumentView view);

	/// <summary>
	/// Stops new operations, waits for active operations to finish, and asks the host to close registered views.
	/// </summary>
	/// <remarks>Repeated calls return the same completion task. <c>DisposeAsync</c> delegates to this method.</remarks>
	Task StopAsync();
}

/// <summary>
/// Describes a workspace document identity change acknowledged by a view.
/// </summary>
/// <remarks>
/// <see cref="OldDocumentId"/> identifies the binding before a rename or directory move;
/// <see cref="Snapshot"/> carries the new path, content, version, and preserved document key.
/// </remarks>
public sealed record WorkspaceDocumentIdentityChange(
	WorkspaceDocumentKey OldDocumentKey,
	string OldDocumentId,
	WorkspaceDocumentSnapshot Snapshot);

/// <summary>
/// Contains the result of opening a workspace document and attaching a view.
/// </summary>
/// <remarks>
/// <see cref="Snapshot"/> is present when the document was loaded even if view attachment later
/// failed. It is <see langword="null"/> when the path was invalid, loading failed, or the view was
/// rejected before a document was opened.
/// </remarks>
public sealed record WorkspaceDocumentManagerOpenResult(
	WorkspaceDocumentManagerOpenStatus Status,
	WorkspaceDocumentSnapshot? Snapshot,
	WorkspaceOperationFailure? Failure = null);

/// <summary>
/// Describes the outcome of a manager open-and-attach operation.
/// </summary>
public enum WorkspaceDocumentManagerOpenStatus
{
	/// <summary>The view was attached to the workspace snapshot.</summary>
	Opened,

	/// <summary>The view was already registered or attached, so no new binding was created.</summary>
	AlreadyOpen,

	/// <summary>The supplied view was already bound, dirty, or conflicted and could not be attached.</summary>
	ViewInConflictState,

	/// <summary>The requested path is invalid.</summary>
	InvalidPath,

	/// <summary>The document could not be loaded.</summary>
	LoadFailed,

	/// <summary>The document loaded, but the view did not accept the attachment.</summary>
	OpenFailed,

	/// <summary>The operation was cancelled.</summary>
	Cancelled
}

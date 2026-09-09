using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>
/// Coordinates workspace document authority with registered views.
/// </summary>
/// <remarks>
/// <para>
/// The manager keeps view bindings and document mutations coordinated, but it does not own the
/// editor or UI thread. View access is dispatched through the delegate supplied to the
/// implementation; the delegate must execute the supplied action and complete its returned task
/// before the manager reads the results the action captured, but it may complete asynchronously.
/// Store mutations run on the caller's context and only view operations are dispatched through the
/// delegate.
/// </para>
/// <para>
/// Operations that coordinate views report the document-authority outcome and the
/// view-synchronization outcome separately: the wrapped store result describes what happened to the
/// document, while <see cref="WorkspaceDocumentViewSynchronization"/> reports whether attached views
/// blocked the operation or stayed unsynchronized afterward. The store contracts themselves never
/// contain view state. The view-driven mutations (<see cref="ReplaceAsync"/> and
/// <see cref="DiscardAsync"/>) report the same composed result family; their store result is always
/// populated because those operations never block on attached views.
/// </para>
/// <para>
/// Blocking is decided per operation. Operations that can lose view state or destroy the document
/// behind it consult attached-view state: delete, delete-directory, reload, and conflict resolution
/// report <see cref="WorkspaceDocumentViewSynchronizationStatus.Blocked"/> while a view has pending
/// edits, a conflict, or an unsynchronized failure. Commit blocks on pending edits and conflicts but
/// deliberately ignores a prior synchronization failure, so the commit can be retried and the
/// post-commit refresh can retry the failed synchronization. Discard is a host-intent operation and
/// overrides attached view state. The operations that only change document identity or path
/// (<see cref="RenameAsync"/>, <see cref="SaveAsAsync"/>, and <see cref="RenameDirectoryAsync"/>) do
/// not block on pending view state: the move cannot lose view edits, and the identity change is
/// acknowledged afterwards, so a view update failure is reported as
/// <see cref="WorkspaceDocumentViewSynchronizationStatus.Unsynchronized"/>. A directory move still
/// reports <see cref="WorkspaceDocumentViewSynchronizationStatus.Blocked"/> when a view cannot enter
/// its delete guard.
/// </para>
/// <para>
/// The manager subscribes and unsubscribes its <see cref="IWorkspaceDocumentView.ApplyRequested"/>
/// handler on the calling thread, so event accessors must be usable from any thread; every other view
/// member invocation is dispatched through the delegate. Event-driven applies have no result
/// channel back to the publishing view: a failure is retained as unsynchronized view state that blocks
/// later disk operations until a successful refresh clears it.
/// </para>
/// </remarks>
public interface IWorkspaceDocumentManager : IAsyncDisposable
{
	/// <summary>
	/// Opens a document through the workspace authority without attaching a view.
	/// </summary>
	/// <remarks>
	/// The result uses the manager result family. No view is attached, so the view-specific statuses
	/// and view-attachment failures never occur; the remaining statuses mirror the store's open
	/// statuses with the document snapshot.
	/// </remarks>
	/// <param name="filePath">The path to open; a null, blank, or unnormalizable path reports <see cref="WorkspaceDocumentManagerOpenStatus.InvalidPath"/>.</param>
	/// <param name="options">The encoding and new-file format defaults for the load.</param>
	/// <param name="cancellationToken">Cancels the load.</param>
	/// <returns>The open outcome with a snapshot for a successful or already-open document.</returns>
	/// <exception cref="ObjectDisposedException">Thrown after the manager has been stopped or disposed.</exception>
	Task<WorkspaceDocumentManagerOpenResult> OpenAsync(
		string? filePath,
		WorkspaceDocumentOpenOptions options,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets the read-side view of the document authority this manager coordinates.
	/// </summary>
	/// <remarks>
	/// Read-only consumers use this accessor instead of a forwarding member per reader operation;
	/// the returned reader is the manager's underlying store.
	/// </remarks>
	IWorkspaceDocumentReader Documents { get; }

	/// <summary>
	/// Opens a document and attaches a view to it.
	/// </summary>
	/// <remarks>
	/// The view is attached through the dispatch delegate. A view that is already registered is
	/// reported as <see cref="WorkspaceDocumentManagerOpenStatus.AlreadyOpen"/> without a second
	/// attachment, and the result carries the current snapshot of the document the view is attached to
	/// (or <see langword="null"/> when that document is no longer tracked). A view that reports its own
	/// <see cref="WorkspaceDocumentViewOpenStatus.AlreadyOpen"/>, and a different view that duplicates
	/// a registered view id, are both reported as
	/// <see cref="WorkspaceDocumentManagerOpenStatus.ViewInUse"/> (the first carries the loaded
	/// snapshot, the second does not). A view that already has a document, pending edits, or a
	/// conflict is reported as <see cref="WorkspaceDocumentManagerOpenStatus.ViewUnavailable"/>.
	/// </remarks>
	/// <param name="filePath">The path to open.</param>
	/// <param name="options">The encoding and new-file format defaults for the load.</param>
	/// <param name="view">The view to attach to the opened document.</param>
	/// <param name="cancellationToken">Cancels the open and attachment.</param>
	/// <returns>The open outcome; a failed attachment carries the loaded snapshot and the attachment failure.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="view"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">Thrown after the manager has been stopped or disposed.</exception>
	Task<WorkspaceDocumentManagerOpenResult> OpenWithViewAsync(
		string? filePath,
		WorkspaceDocumentOpenOptions options,
		IWorkspaceDocumentView view,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Resolves an external change conflict for a tracked document and refreshes attached views when possible.
	/// </summary>
	/// <remarks>A view that cannot refresh reports the resolution together with <see cref="WorkspaceDocumentViewSynchronizationStatus.Unsynchronized"/>.</remarks>
	/// <param name="request">The conflict-resolution request.</param>
	/// <param name="cancellationToken">Cancels the resolution.</param>
	/// <returns>The resolution outcome with the view-synchronization result and the current snapshot.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">Thrown after the manager has been stopped or disposed.</exception>
	Task<WorkspaceDocumentManagerConflictResolutionResult> ResolveExternalConflictAsync(
		WorkspaceDocumentConflictResolutionRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Applies a replacement to a workspace document without writing to disk.
	/// </summary>
	/// <remarks>
	/// The manager performs the mutation and asks attached views to synchronize through the dispatch
	/// delegate: the publishing view acknowledges the mutation, and peer views refresh when their
	/// recorded state is older or their synchronization failed earlier. A view that cannot synchronize
	/// is retained as unsynchronized view state and reported on the result.
	/// </remarks>
	/// <param name="request">The replacement request.</param>
	/// <returns>The mutation outcome with the view-synchronization result and the current snapshot.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">Thrown after the manager has been stopped or disposed.</exception>
	Task<WorkspaceDocumentManagerMutationResult> ReplaceAsync(WorkspaceDocumentReplaceRequest request);

	/// <summary>
	/// Discards unsaved logical changes and asks attached views to acknowledge the restored snapshot.
	/// </summary>
	/// <remarks>
	/// Discard is a host-intent operation: it overrides attached view state, including pending edits
	/// and conflicts, because the host already decided that the changes are abandoned. Every attached
	/// view is asked to acknowledge the restored snapshot, which clears its pending state; a view that
	/// cannot acknowledge is retained as unsynchronized view state and reported on the result.
	/// </remarks>
	/// <param name="request">The discard request.</param>
	/// <returns>The mutation outcome with the view-synchronization result and the restored snapshot.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">Thrown after the manager has been stopped or disposed.</exception>
	Task<WorkspaceDocumentManagerMutationResult> DiscardAsync(WorkspaceDocumentDiscardRequest request);

	/// <summary>
	/// Renames a tracked document and updates attached views.
	/// </summary>
	/// <remarks>
	/// Attached view state does not block the operation: the move cannot lose view edits, and the
	/// identity change is acknowledged afterwards. A view that cannot accept the new identity is
	/// reported through <see cref="WorkspaceDocumentViewSynchronizationStatus.Unsynchronized"/> on the
	/// result.
	/// </remarks>
	/// <param name="request">The rename request.</param>
	/// <param name="cancellationToken">Cancels the move.</param>
	/// <returns>The rename outcome with the view-synchronization result and the current snapshot.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">Thrown after the manager has been stopped or disposed.</exception>
	Task<WorkspaceDocumentManagerRenameResult> RenameAsync(
		WorkspaceDocumentRenameRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Saves a tracked document at a destination path and updates attached views.
	/// </summary>
	/// <remarks>
	/// The source file is retained. Attached view state does not block the operation: the write cannot
	/// lose view edits, and the identity change is acknowledged afterwards. A view that cannot accept
	/// the new identity is reported through
	/// <see cref="WorkspaceDocumentViewSynchronizationStatus.Unsynchronized"/> on the result.
	/// </remarks>
	/// <param name="request">The save-as request.</param>
	/// <param name="cancellationToken">Cancels the write.</param>
	/// <returns>The save-as outcome with the view-synchronization result and the current snapshot.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">Thrown after the manager has been stopped or disposed.</exception>
	Task<WorkspaceDocumentManagerSaveAsResult> SaveAsAsync(
		WorkspaceDocumentSaveAsRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Deletes a tracked document and updates attached views.
	/// </summary>
	/// <remarks>Attached views must accept a delete guard before deletion; successful deletion then closes and unregisters them.</remarks>
	/// <param name="request">The delete request.</param>
	/// <param name="cancellationToken">Cancels the delete.</param>
	/// <returns>The delete outcome with the view-synchronization result and the pre-removal snapshot.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">Thrown after the manager has been stopped or disposed.</exception>
	Task<WorkspaceDocumentManagerDeleteResult> DeleteAsync(
		WorkspaceDocumentDeleteRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Renames a directory and updates the identities of tracked documents below it.
	/// </summary>
	/// <remarks>
	/// Attached views are guarded before the move and acknowledge their new identities afterward; their
	/// state does not block the move, but a view that cannot enter its delete guard does. A view that
	/// attached while the move was in flight is acknowledged as well, matched to its moved document by
	/// its document key.
	/// </remarks>
	/// <param name="request">The directory-rename request.</param>
	/// <param name="cancellationToken">Cancels the move.</param>
	/// <returns>The rename outcome with the view-synchronization result and the current snapshots.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">Thrown after the manager has been stopped or disposed.</exception>
	Task<WorkspaceDocumentManagerDirectoryRenameResult> RenameDirectoryAsync(
		WorkspaceDocumentDirectoryRenameRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Deletes a directory and its tracked documents.
	/// </summary>
	/// <remarks>
	/// Attached views are guarded before recursive deletion and closed afterward.
	/// </remarks>
	/// <param name="request">The directory-delete request.</param>
	/// <param name="cancellationToken">Cancels the delete.</param>
	/// <returns>The delete outcome with the view-synchronization result and the current snapshots.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">Thrown after the manager has been stopped or disposed.</exception>
	Task<WorkspaceDocumentManagerDirectoryDeleteResult> DeleteDirectoryAsync(
		WorkspaceDocumentDirectoryDeleteRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Commits a tracked document to disk and refreshes attached views.
	/// </summary>
	/// <remarks>
	/// Views with pending edits or conflicts block the commit and report
	/// <see cref="WorkspaceDocumentViewSynchronizationStatus.Blocked"/>. A view whose only outstanding
	/// state is a prior synchronization failure does not block the commit: the manager performs the
	/// commit, retries the view synchronization, and reports
	/// <see cref="WorkspaceDocumentViewSynchronizationStatus.Unsynchronized"/> when that retry fails
	/// again.
	/// </remarks>
	/// <param name="request">The commit request.</param>
	/// <param name="cancellationToken">Cancels the commit.</param>
	/// <returns>The commit outcome with the view-synchronization result and the current snapshot.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">Thrown after the manager has been stopped or disposed.</exception>
	Task<WorkspaceDocumentManagerCommitResult> CommitAsync(
		WorkspaceDocumentCommitRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Reloads a tracked document from disk and refreshes attached views when content changes.
	/// </summary>
	/// <remarks>
	/// Blocking view state is reported before the reload: a view with pending edits or a conflict can
	/// lose that state when the document content is replaced from disk, so it blocks the operation.
	/// Dirty documents are handled by the store as conflicts and are not overwritten.
	/// </remarks>
	/// <param name="request">The reload request.</param>
	/// <param name="cancellationToken">Cancels the reload.</param>
	/// <returns>The reload outcome with the view-synchronization result and the current snapshot.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">Thrown after the manager has been stopped or disposed.</exception>
	Task<WorkspaceDocumentManagerReloadResult> ReloadAsync(
		WorkspaceDocumentReloadRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Unregisters a view that is no longer open.
	/// </summary>
	/// <remarks>
	/// This member removes the binding and event subscription; it does not call <see cref="IWorkspaceDocumentView.Close"/>.
	/// The subscription is removed on the calling thread.
	/// </remarks>
	/// <param name="view">The view to unregister; an unregistered view is ignored.</param>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="view"/> is <see langword="null"/>.</exception>
	void UnregisterOpenView(IWorkspaceDocumentView view);

	/// <summary>
	/// Stops new operations, waits for active operations to finish, and closes registered views.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Repeated calls return the same completion task. <c>DisposeAsync</c> delegates to this member.
	/// </para>
	/// <para>
	/// The completion waits for active operations to finish before the dispatch delegate closes the
	/// registered views. Calling this member from inside a view member or dispatch action that belongs
	/// to an active operation therefore deadlocks: the stop waits for that operation, which cannot
	/// finish while its own thread is blocked on the stop.
	/// </para>
	/// </remarks>
	/// <returns>A task that completes when the manager has stopped; repeated calls observe the same completion.</returns>
	Task StopAsync();
}

/// <summary>
/// Describes a workspace document identity change acknowledged by a view.
/// </summary>
/// <remarks>
/// <see cref="OldDocumentId"/> identifies the binding before a rename or directory move;
/// <see cref="Snapshot"/> carries the new path, content, version, and preserved document key.
/// </remarks>
/// <param name="OldDocumentKey">The document instance the view was bound to before the change.</param>
/// <param name="OldDocumentId">The normalized document id the view was bound to before the change.</param>
/// <param name="Snapshot">The snapshot after the change, carrying the new path, content, version, and preserved document key.</param>
public sealed record WorkspaceDocumentIdentityChange(
	WorkspaceDocumentKey OldDocumentKey,
	string OldDocumentId,
	WorkspaceDocumentSnapshot Snapshot);

/// <summary>
/// Contains the result of opening a workspace document and attaching a view.
/// </summary>
/// <remarks>
/// <see cref="Snapshot"/> is present when the document was loaded and the failure is reported as
/// <see cref="WorkspaceDocumentManagerOpenStatus.ViewInUse"/> or
/// <see cref="WorkspaceDocumentManagerOpenStatus.ViewRejected"/>, and it carries the attached
/// document's snapshot when an already-registered view made the attach a no-op. It is
/// <see langword="null"/> when the path was invalid, loading failed, or an unexpected exception
/// (reported as <see cref="WorkspaceDocumentManagerOpenStatus.OpenFailed"/>) interrupted the attach
/// after the document was loaded.
/// </remarks>
/// <param name="Status">The open-and-attach outcome.</param>
/// <param name="Snapshot">The loaded snapshot when the document was loaded; otherwise, <see langword="null"/>.</param>
/// <param name="Failure">Explains a load or attachment failure, when one occurred.</param>
public sealed record WorkspaceDocumentManagerOpenResult(
	WorkspaceDocumentManagerOpenStatus Status,
	WorkspaceDocumentSnapshot? Snapshot,
	WorkspaceOperationFailure? Failure = null);

/// <summary>
/// Describes the outcome of a manager open operation, with or without view attachment.
/// </summary>
public enum WorkspaceDocumentManagerOpenStatus
{
	/// <summary>The document was opened or was already open; when a view was supplied, it was attached.</summary>
	Opened,

	/// <summary>
	/// The document was already open; the call was a no-op.
	/// </summary>
	/// <remarks>
	/// The status is produced when no view was supplied, when the supplied view was already
	/// registered, or when the store reports the document as already open without a view attach. When
	/// a registered view made the attach a no-op, the result carries the current snapshot of the
	/// document the view is attached to, or <see langword="null"/> when that document is no longer
	/// tracked.
	/// </remarks>
	AlreadyOpen,

	/// <summary>
	/// The supplied view has a document, pending edits, a conflict, or could not report its state,
	/// so it cannot be attached to another document.
	/// </summary>
	ViewUnavailable,

	/// <summary>A different registered view already reports the supplied view's id; the supplied view was not attached.</summary>
	ViewInUse,

	/// <summary>The document was loaded but the view did not accept the attachment; <see cref="WorkspaceDocumentManagerOpenResult.Failure"/> carries the detail.</summary>
	ViewRejected,

	/// <summary>The requested path is invalid.</summary>
	InvalidPath,

	/// <summary>
	/// The path does not exist and the open options did not allow creating a new document
	/// (<see cref="WorkspaceDocumentOpenOptions.CreateIfMissing"/> is <see langword="false"/>).
	/// </summary>
	NotFound,

	/// <summary>
	/// The path exists as a directory, not a file. A directory cannot be tracked as a document, so the
	/// open is rejected; nothing was loaded and <see cref="WorkspaceDocumentManagerOpenResult.Snapshot"/>
	/// is <see langword="null"/>.
	/// </summary>
	IsDirectory,

	/// <summary>The document could not be loaded.</summary>
	LoadFailed,

	/// <summary>The open path threw an unexpected exception; <see cref="WorkspaceDocumentManagerOpenResult.Failure"/> carries the detail.</summary>
	OpenFailed,

	/// <summary>The operation was canceled.</summary>
	Canceled
}

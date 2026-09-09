using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>
/// Represents a host view of a workspace document, such as an editor or another document model.
/// </summary>
/// <remarks>
/// <para>
/// The manager invokes members of this interface through the dispatch delegate supplied to its
/// constructor; view access is therefore always dispatched, never performed on whatever thread the
/// manager operation runs on. The one exception is the <see cref="ApplyRequested"/> subscription:
/// the manager adds and removes the handler on the calling thread, so the event accessors must be
/// usable from any thread.
/// </para>
/// <para>
/// Lifecycle: the manager attaches the view with <see cref="Open"/> and reports the failure when the
/// view rejects the snapshot. While attached, the view receives <see cref="Refresh"/> calls for
/// content or version updates it has not seen, <see cref="AcknowledgeIdentity"/> calls before it has
/// to adopt a renamed or moved identity, and <see cref="AcknowledgeApply"/> calls when a mutation
/// published by the view, or a manager-initiated discard, becomes the document state. Delete
/// operations and directory moves first ask each attached view that implements
/// <see cref="IWorkspaceDocumentDeleteGuardView"/> to enter its delete guard and always release it
/// afterwards. When the document is deleted
/// or the manager stops, the manager closes the view and unregisters it; a view the host closes on
/// its own must be unregistered through
/// <see cref="IWorkspaceDocumentManager.UnregisterOpenView"/>.
/// </para>
/// <para>
/// A detached view has a <see langword="null"/> <see cref="DocumentKey"/>. A view with
/// <see cref="HasPendingEdits"/> or <see cref="HasConflict"/> blocks the operations that could lose
/// that state (delete, commit, reload, and conflict resolution); operations that only change the
/// document identity (rename, save-as, and directory rename) do not block on view state, and discard
/// is a host-intent operation that overrides it.
/// </para>
/// </remarks>
public interface IWorkspaceDocumentView
{
	/// <summary>
	/// Occurs when the view requests that edited content be published to the workspace document.
	/// </summary>
	/// <remarks>The event request should identify the view's current document key, id, and version.</remarks>
	event EventHandler<WorkspaceDocumentViewApplyRequestedEventArgs> ApplyRequested;

	/// <summary>Gets the stable identifier used to report and distinguish this view.</summary>
	string ViewId { get; }

	/// <summary>Gets the normalized document id currently attached to the view.</summary>
	/// <value>The attached document id, or <see langword="null"/> when the view is detached.</value>
	/// <remarks>The manager does not read this member; it is for host bookkeeping such as locating the views of a document.</remarks>
	string? DocumentId { get; }

	/// <summary>Gets the current logical document identity, or <see langword="null"/> when detached.</summary>
	WorkspaceDocumentKey? DocumentKey { get; }

	/// <summary>Gets a value indicating whether the view has edits not published to the workspace document.</summary>
	bool HasPendingEdits { get; }

	/// <summary>Gets a value indicating whether the view cannot currently be synchronized without conflict resolution.</summary>
	bool HasConflict { get; }

	/// <summary>Attaches the view to a document snapshot and makes that snapshot its current content.</summary>
	/// <param name="snapshot">The workspace document snapshot to attach.</param>
	/// <returns>An open status; return <see cref="WorkspaceDocumentViewOpenStatus.AlreadyOpen"/> when the view is already attached.</returns>
	WorkspaceDocumentViewOpenResult Open(WorkspaceDocumentSnapshot snapshot);

	/// <summary>Refreshes the view from a document snapshot.</summary>
	/// <remarks>The view may return <see cref="WorkspaceDocumentViewRefreshStatus.MarkedStale"/> when it cannot apply the snapshot immediately.</remarks>
	/// <param name="snapshot">The workspace document snapshot to refresh from.</param>
	/// <returns>The refresh outcome.</returns>
	WorkspaceDocumentViewRefreshResult Refresh(WorkspaceDocumentSnapshot snapshot);

	/// <summary>Applies a document identity change, such as a rename or directory move, to the view.</summary>
	/// <remarks>The view should update its document id and content from <see cref="WorkspaceDocumentIdentityChange.Snapshot"/> before reporting success.</remarks>
	/// <param name="change">The identity change to apply.</param>
	/// <returns>The identity update outcome.</returns>
	WorkspaceDocumentViewIdentityResult AcknowledgeIdentity(WorkspaceDocumentIdentityChange change);

	/// <summary>Acknowledges the result of applying a logical document mutation to the view.</summary>
	/// <remarks>Used when a mutation published by the view, or a manager-initiated discard, becomes the document state. A non-refreshed acknowledgment leaves the view unsynchronized.</remarks>
	/// <param name="result">The mutation result the view should adopt.</param>
	/// <returns>The acknowledgment outcome.</returns>
	WorkspaceDocumentViewRefreshResult AcknowledgeApply(WorkspaceDocumentMutationResult result);

	/// <summary>Closes the view and detaches it from its current document.</summary>
	/// <remarks>Closing a view does not unregister it from the manager; the owner must call <see cref="IWorkspaceDocumentManager.UnregisterOpenView"/>.</remarks>
	void Close();
}

/// <summary>
/// Represents a workspace view that can hold a delete guard for an in-flight delete or directory move.
/// </summary>
/// <remarks>
/// The manager enters the guard on every attached view that implements this interface before a file
/// delete, a directory move, or a directory delete, and releases it afterwards. A view that
/// implements only <see cref="IWorkspaceDocumentView"/> is skipped: it cannot hold state that one of
/// those operations would lose, so it has nothing to guard.
/// </remarks>
public interface IWorkspaceDocumentDeleteGuardView : IWorkspaceDocumentView
{
	/// <summary>Applies the view's delete guard for an in-flight file delete or directory move or deletion.</summary>
	/// <returns>The guard outcome; a view that cannot enter the guard reports <see cref="WorkspaceDocumentViewDeleteGuardStatus.Failed"/>.</returns>
	WorkspaceDocumentViewDeleteGuardResult ApplyDeleteGuard();

	/// <summary>Releases the view's delete guard after the delete or directory operation completed or was abandoned.</summary>
	/// <remarks>Releasing a guard that is not applied is a no-op.</remarks>
	/// <returns>The guard outcome.</returns>
	WorkspaceDocumentViewDeleteGuardResult ReleaseDeleteGuard();
}

/// <summary>
/// Represents a workspace view whose full text can be read and replaced through the workspace edit
/// pipeline.
/// </summary>
/// <remarks>
/// The manager coordinates documents on <see cref="IWorkspaceDocumentView"/> alone; this interface
/// additionally promises the Core text-edit target contract (a full
/// <see cref="ITextEditTarget.Text"/> and offset-based
/// <see cref="ITextEditTarget.Apply(PreparedTextEdits)"/>), which hosts use to prepare whole-content
/// replacements for the document. A view that cannot expose its full text, such as an outline, a
/// diff, or a read-only preview, implements only the base interface.
/// </remarks>
public interface IWorkspaceDocumentEditView : IWorkspaceDocumentView, ITextEditTarget
{
}

/// <summary>
/// Provides the replacement requested by a view.
/// </summary>
/// <remarks>The manager applies <see cref="Request"/> as a logical mutation; committing it to disk is a separate operation.</remarks>
public sealed class WorkspaceDocumentViewApplyRequestedEventArgs : EventArgs
{
	/// <summary>
	/// Initializes a new instance of the <see cref="WorkspaceDocumentViewApplyRequestedEventArgs"/> class.
	/// </summary>
	/// <param name="request">The replacement to apply.</param>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	public WorkspaceDocumentViewApplyRequestedEventArgs(WorkspaceDocumentReplaceRequest request)
	{
		Request = request ?? throw new ArgumentNullException(nameof(request));
	}

	/// <summary>
	/// Gets the replacement requested by the view.
	/// </summary>
	public WorkspaceDocumentReplaceRequest Request { get; }
}

/// <summary>
/// Contains the outcome of applying or releasing a view delete guard.
/// </summary>
/// <remarks><see cref="Failure"/> is populated when the view could not change its guard state.</remarks>
/// <param name="Status">The guard outcome.</param>
/// <param name="Failure">Explains a failed guard operation, when one occurred.</param>
public sealed record WorkspaceDocumentViewDeleteGuardResult(
	WorkspaceDocumentViewDeleteGuardStatus Status,
	WorkspaceOperationFailure? Failure = null);

/// <summary>
/// Describes the outcome of a view delete-guard operation.
/// </summary>
public enum WorkspaceDocumentViewDeleteGuardStatus
{
	/// <summary>The guard was applied or released.</summary>
	Applied,

	/// <summary>The view could not change its guard state.</summary>
	Failed
}

/// <summary>
/// Contains the outcome of updating a view's document identity.
/// </summary>
/// <remarks>A failed result causes the manager to retain the view's prior binding and mark it unsynchronized.</remarks>
/// <param name="Status">The identity update outcome.</param>
/// <param name="Failure">Explains a failed identity update, when one occurred.</param>
public sealed record WorkspaceDocumentViewIdentityResult(
	WorkspaceDocumentViewIdentityStatus Status,
	WorkspaceOperationFailure? Failure = null);

/// <summary>
/// Describes the outcome of updating a view's document identity.
/// </summary>
public enum WorkspaceDocumentViewIdentityStatus
{
	/// <summary>The identity was updated.</summary>
	Updated,

	/// <summary>The view rejected the identity change or could not apply it.</summary>
	Failed
}

/// <summary>
/// Contains the outcome of attaching a view to a workspace document.
/// </summary>
/// <remarks><see cref="Failure"/> explains an unavailable or failed attach when the view can provide that detail.</remarks>
/// <param name="Status">The attach outcome.</param>
/// <param name="Failure">Explains an unavailable or failed attach, when the view can provide that detail.</param>
public sealed record WorkspaceDocumentViewOpenResult(
	WorkspaceDocumentViewOpenStatus Status,
	WorkspaceOperationFailure? Failure = null);

/// <summary>
/// Describes the outcome of attaching a view.
/// </summary>
public enum WorkspaceDocumentViewOpenStatus
{
	/// <summary>The view was attached.</summary>
	Opened,

	/// <summary>The view was already attached.</summary>
	AlreadyOpen,

	/// <summary>The view could not be attached.</summary>
	Unavailable
}

/// <summary>
/// Contains the outcome of refreshing a view from workspace content.
/// </summary>
/// <remarks>
/// Any status other than <see cref="WorkspaceDocumentViewRefreshStatus.Refreshed"/> requires the
/// manager to treat the view as unsynchronized until it is refreshed successfully.
/// </remarks>
/// <param name="Status">The refresh outcome.</param>
/// <param name="Failure">Explains a failed refresh, when one occurred.</param>
public sealed record WorkspaceDocumentViewRefreshResult(
	WorkspaceDocumentViewRefreshStatus Status,
	WorkspaceOperationFailure? Failure = null);

/// <summary>
/// Describes the outcome of refreshing a view.
/// </summary>
public enum WorkspaceDocumentViewRefreshStatus
{
	/// <summary>The view was refreshed.</summary>
	Refreshed,

	/// <summary>The view was marked stale.</summary>
	MarkedStale,

	/// <summary>The view could not apply the workspace content.</summary>
	UpdateFailed
}

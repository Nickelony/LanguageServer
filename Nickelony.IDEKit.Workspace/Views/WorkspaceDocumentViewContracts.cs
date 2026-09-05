using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>
/// Represents a view of a workspace document into another editor or model.
/// </summary>
/// <remarks>
/// The manager uses its host-invocation callback for view operations that require host affinity. A
/// detached view has a <see langword="null"/> <see cref="DocumentKey"/>; <see cref="HasPendingEdits"/>
/// and <see cref="HasConflict"/> can block document persistence, moves, and deletes.
/// </remarks>
public interface IWorkspaceDocumentView : ITextEditTarget
{
	/// <summary>
	/// Occurs when the view requests that edited content be published to the workspace document.
	/// </summary>
	/// <remarks>The event request should identify the view's current document key, id, and version.</remarks>
	event EventHandler<WorkspaceDocumentViewApplyRequestedEventArgs> ApplyRequested;

	/// <summary>Gets the stable identifier used to report and distinguish this view.</summary>
	string ViewId { get; }

	/// <summary>Gets the normalized document id currently attached to the view.</summary>
	/// <value>An empty string or other host-defined value when the view is detached.</value>
	string DocumentId { get; }

	/// <summary>Gets the current logical document identity, or <see langword="null"/> when detached.</summary>
	WorkspaceDocumentKey? DocumentKey { get; }

	/// <summary>Gets a value indicating whether the view has edits not published to the workspace document.</summary>
	bool HasPendingEdits { get; }

	/// <summary>Gets a value indicating whether the view cannot currently be synchronized without conflict resolution.</summary>
	bool HasConflict { get; }

	/// <summary>Attaches the view to a document snapshot and makes that snapshot its current content.</summary>
	/// <returns>An open status; return <see cref="WorkspaceDocumentViewOpenStatus.AlreadyOpen"/> when the view is already attached.</returns>
	WorkspaceDocumentViewOpenResult Open(WorkspaceDocumentSnapshot snapshot);

	/// <summary>Refreshes the view from a document snapshot.</summary>
	/// <remarks>The view may return <see cref="WorkspaceDocumentViewRefreshStatus.MarkedStale"/> when it cannot apply the snapshot immediately.</remarks>
	WorkspaceDocumentViewRefreshResult Refresh(WorkspaceDocumentSnapshot snapshot);

	/// <summary>Discards unpublished edits and refreshes the view from a document snapshot.</summary>
	WorkspaceDocumentViewRefreshResult DiscardPendingEdits(WorkspaceDocumentSnapshot snapshot);

	/// <summary>Applies a document identity change, such as a rename or directory move, to the view.</summary>
	/// <remarks>The view should update its document id and content from <see cref="WorkspaceDocumentIdentityChange.Snapshot"/> before reporting success.</remarks>
	WorkspaceDocumentViewIdentityResult AcknowledgeIdentity(WorkspaceDocumentIdentityChange change);

	/// <summary>Sets or releases the view's delete guard.</summary>
	/// <param name="active"><see langword="true"/> to block conflicting view activity during a directory move or deletion of a file or directory; <see langword="false"/> to release the guard.</param>
	WorkspaceDocumentViewDeleteGuardResult SetDeleteGuard(bool active);

	/// <summary>Acknowledges the result of applying a logical document mutation to the view.</summary>
	/// <remarks>Used for content published by the view and manager-initiated mutations such as discarding edits. A non-refreshed acknowledgement leaves the view unsynchronized.</remarks>
	WorkspaceDocumentViewRefreshResult AcknowledgeApply(WorkspaceDocumentMutationResult result);

	/// <summary>Closes the view and detaches it from its current document.</summary>
	/// <remarks>Closing a view does not unregister it from the manager; the owner must call <see cref="IWorkspaceDocumentManager.UnregisterOpenView"/>.</remarks>
	void Close();
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
	/// <param name="request">The replacement requested by the view.</param>
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

	/// <summary>The guard operation failed.</summary>
	Failed
}

/// <summary>
/// Contains the outcome of updating a view's document identity.
/// </summary>
/// <remarks>A failed result causes the manager to retain the view's prior binding and mark it unsynchronized.</remarks>
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

	/// <summary>The identity update failed.</summary>
	Failed
}

/// <summary>
/// Contains the outcome of attaching a view to a workspace document.
/// </summary>
/// <remarks><see cref="Failure"/> explains an unavailable or failed attach when the view can provide that detail.</remarks>
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
/// <see cref="WorkspaceDocumentViewRefreshStatus.MarkedStale"/> is distinct from
/// <see cref="WorkspaceDocumentViewRefreshStatus.UpdateFailed"/> but both require the manager to
/// treat the view as unsynchronized until it is refreshed successfully.
/// </remarks>
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

	/// <summary>The view update failed.</summary>
	UpdateFailed
}

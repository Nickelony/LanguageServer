namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>
/// Describes how attached views synchronized around a manager operation.
/// </summary>
public enum WorkspaceDocumentViewSynchronizationStatus
{
	/// <summary>No attached view needed synchronization, or every view operation succeeded.</summary>
	Synchronized,

	/// <summary>One or more attached views prevented the operation before the document store was reached.</summary>
	Blocked,

	/// <summary>The document operation completed, but one or more attached views remained unsynchronized afterward.</summary>
	Unsynchronized
}

/// <summary>
/// Contains the view-synchronization outcome of a manager operation.
/// </summary>
/// <remarks>
/// A manager operation reports the document-authority outcome and the view-synchronization outcome
/// separately: the store result describes what happened to the document, while this value describes
/// whether attached views blocked the operation or stayed unsynchronized after it. <see cref="ViewIds"/>
/// is de-duplicated with an ordinal comparison and sorted ordinally, so the reported order does not
/// depend on registration or enumeration order.
/// </remarks>
/// <param name="Status">The synchronization outcome.</param>
/// <param name="ViewIds">
/// The view ids that blocked the operation or remained unsynchronized; empty when the status is
/// <see cref="WorkspaceDocumentViewSynchronizationStatus.Synchronized"/>.
/// </param>
public sealed record WorkspaceDocumentViewSynchronization(
	WorkspaceDocumentViewSynchronizationStatus Status,
	IReadOnlyList<string> ViewIds)
{
	/// <summary>
	/// Gets a synchronization outcome in which every attached view synchronized successfully, including
	/// when no view was attached.
	/// </summary>
	public static WorkspaceDocumentViewSynchronization Synchronized { get; } =
		new(WorkspaceDocumentViewSynchronizationStatus.Synchronized, []);
}

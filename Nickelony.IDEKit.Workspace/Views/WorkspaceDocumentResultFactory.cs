using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>
/// Constructs manager-level document operation results.
/// </summary>
internal static class WorkspaceDocumentResultFactory
{
	public static WorkspaceDocumentRenameResult CreateRenameResult(
		WorkspaceDocumentRenameRequest request,
		WorkspaceDocumentRenameStatus status,
		WorkspaceDocumentSnapshot? snapshot,
		IReadOnlyList<string>? failedViewIds = null)
		=> new(
			status,
			request.ExpectedDocumentKey,
			request.DocumentId,
			request.ExpectedVersion,
			snapshot,
			FailedViewIds: failedViewIds);

	public static WorkspaceDocumentSaveAsResult CreateSaveAsResult(
		WorkspaceDocumentSaveAsRequest request,
		WorkspaceDocumentSaveAsStatus status,
		WorkspaceDocumentSnapshot? snapshot,
		IReadOnlyList<string>? failedViewIds = null)
		=> new(
			status,
			request.ExpectedDocumentKey,
			request.DocumentId,
			request.ExpectedVersion,
			snapshot,
			FailedViewIds: failedViewIds);

	public static WorkspaceDocumentDeleteResult CreateDeleteResult(
		WorkspaceDocumentDeleteRequest request,
		WorkspaceDocumentDeleteStatus status,
		WorkspaceDocumentSnapshot? snapshot,
		IReadOnlyList<string>? failedViewIds = null)
		=> new(
			status,
			request.ExpectedDocumentKey,
			request.DocumentId,
			request.ExpectedVersion,
			snapshot,
			FailedViewIds: failedViewIds);

	public static WorkspaceDocumentDirectoryRenameResult CreateDirectoryRenameResult(
		WorkspaceDocumentDirectoryRenameRequest request,
		WorkspaceDocumentDirectoryRenameStatus status,
		IReadOnlyList<WorkspaceDocumentSnapshot> snapshots,
		IReadOnlyList<string>? failedViewIds = null)
		=> new(
			status,
			request.SourceDirectoryPath,
			request.DestinationDirectoryPath,
			snapshots,
			FailedViewIds: failedViewIds);

	public static WorkspaceDocumentDirectoryDeleteResult CreateDirectoryDeleteResult(
		WorkspaceDocumentDirectoryDeleteRequest request,
		WorkspaceDocumentDirectoryDeleteStatus status,
		IReadOnlyList<WorkspaceDocumentSnapshot> snapshots,
		IReadOnlyList<string>? failedViewIds = null)
		=> new(
			status,
			request.DirectoryPath,
			snapshots,
			FailedViewIds: failedViewIds);

	public static WorkspaceDocumentManagerOpenStatus MapOpenStatus(WorkspaceDocumentOpenStatus status)
		=> status switch
		{
			WorkspaceDocumentOpenStatus.InvalidPath => WorkspaceDocumentManagerOpenStatus.InvalidPath,
			WorkspaceDocumentOpenStatus.LoadFailed => WorkspaceDocumentManagerOpenStatus.LoadFailed,
			WorkspaceDocumentOpenStatus.Cancelled => WorkspaceDocumentManagerOpenStatus.Cancelled,
			_ => throw new ArgumentOutOfRangeException(nameof(status))
		};
}

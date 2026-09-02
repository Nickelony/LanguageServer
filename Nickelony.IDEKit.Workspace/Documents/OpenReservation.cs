namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Coordinates a caller waiting for an in-progress document open.
/// </summary>
internal sealed class OpenReservation
{
	public TaskCompletionSource<WorkspaceDocumentOpenResult> Completion { get; } =
		new(TaskCreationOptions.RunContinuationsAsynchronously);
}

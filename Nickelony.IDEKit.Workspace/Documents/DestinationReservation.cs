namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Coordinates callers waiting for an in-progress file or directory move or save-as destination.
/// </summary>
internal sealed class DestinationReservation(string documentId)
{
	public string DocumentId { get; } = documentId;

	public TaskCompletionSource<object?> Completion { get; } =
		new(TaskCreationOptions.RunContinuationsAsynchronously);
}

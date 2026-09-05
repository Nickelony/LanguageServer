namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Represents a destination reserved by an in-progress file or directory move or Save As operation.
/// </summary>
internal sealed class DestinationReservation(string documentId)
{
	public string DocumentId { get; } = documentId;

	public TaskCompletionSource<object?> Completion { get; } =
		new(TaskCreationOptions.RunContinuationsAsynchronously);
}

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Tracks an active workspace operation and the per-document disk gates it holds.
/// </summary>
/// <remarks>
/// Completion releases the held gates in reverse acquisition order before completing the waiter
/// task, so a waiting operation can observe the released state after completion.
/// </remarks>
internal sealed class OperationRegistration
{
	private readonly IReadOnlyList<SemaphoreSlim> _gates;

	public OperationRegistration(SemaphoreSlim gate)
		: this([gate])
	{ }

	public OperationRegistration(IReadOnlyList<SemaphoreSlim> gates)
	{
		_gates = gates;
	}

	public TaskCompletionSource<object?> Completion { get; } =
		new(TaskCreationOptions.RunContinuationsAsynchronously);

	public void Complete()
	{
		foreach (SemaphoreSlim gate in _gates.Reverse())
			gate.Release();

		Completion.TrySetResult(null);
	}
}

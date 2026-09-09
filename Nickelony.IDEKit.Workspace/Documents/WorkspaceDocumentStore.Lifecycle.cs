namespace Nickelony.IDEKit.Workspace.Documents;

public sealed partial class WorkspaceDocumentStore
{
	/// <summary>
	/// Releases all document resources after tracked open reservations and registered disk operations complete.
	/// </summary>
	/// <remarks>
	/// Disposal is idempotent: the first call marks the store disposed, cancels the lifetime token that
	/// in-flight operations observe, waits for open reservations and registered operations, and disposes
	/// the per-document disk gates. Later calls observe the same disposal task. Public operations throw
	/// <see cref="ObjectDisposedException"/> after disposal; a dirty reload that was already in flight
	/// completes with <see cref="WorkspaceDocumentReloadStatus.Canceled"/>.
	/// </remarks>
	/// <returns>A task that completes when disposal has finished.</returns>
	public ValueTask DisposeAsync()
	{
		lock (_stateLock)
		{
			if (_disposeTask is not null)
				return new ValueTask(_disposeTask);

			_disposed = true;
			_lifetimeCancellation.Cancel();

			Task[] reservations = new Task[_openReservations.Count];
			int index = 0;
			foreach (OpenReservation reservation in _openReservations.Values)
				reservations[index++] = reservation.Completion.Task;

			Task[] operations = new Task[_activeOperations.Count];
			index = 0;
			foreach (OperationRegistration operation in _activeOperations)
				operations[index++] = operation.Completion.Task;

			_disposeTask = DisposeCoreAsync(reservations, operations);
			return new ValueTask(_disposeTask);
		}
	}

	private async Task DisposeCoreAsync(Task[] reservations, Task[] operations)
	{
		if (reservations.Length > 0)
			await Task.WhenAll(reservations).ConfigureAwait(false);
		if (operations.Length > 0)
			await Task.WhenAll(operations).ConfigureAwait(false);

		lock (_stateLock)
		{
			_documents.Clear();
			_openReservations.Clear();
			_destinationReservations.Clear();
		}

		_lifetimeCancellation.Dispose();
	}

	private void ThrowIfDisposed()
	{
		lock (_stateLock)
			ThrowIfDisposedUnderLock();
	}

	// Creates a cancellation source linked to the caller's token and the store lifetime. The dirty
	// reload branch runs without an active-operation registration, so the lifetime source can already
	// be disposed when the branch starts; that case reports null so the caller returns the documented
	// Canceled outcome instead of observing ObjectDisposedException.
	private CancellationTokenSource? TryCreateLifetimeLinkedCancellation(CancellationToken cancellationToken)
		=> TryCreateLifetimeLinkedCancellation(_lifetimeCancellation, cancellationToken);

	internal static CancellationTokenSource? TryCreateLifetimeLinkedCancellation(
		CancellationTokenSource lifetimeCancellation,
		CancellationToken cancellationToken)
	{
		try
		{
			return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeCancellation.Token);
		}
		catch (ObjectDisposedException)
		{
			return null;
		}
	}

	private void ThrowIfDisposedUnderLock()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}
}

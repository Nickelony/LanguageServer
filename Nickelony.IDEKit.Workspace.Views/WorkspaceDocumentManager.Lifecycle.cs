namespace Nickelony.IDEKit.Workspace.Views;

public sealed partial class WorkspaceDocumentManager
{
	/// <inheritdoc />
	public Task StopAsync()
	{
		TaskCompletionSource<object?> completion;
		Task[] activeOperations;

		lock (_stateLock)
		{
			if (_stopTask is not null)
				return _stopTask;

			_stopping = true;
			completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
			_stopTask = completion.Task;
			activeOperations = _activeOperations.ToArray();
		}

		_ = StopCoreAsync(activeOperations, completion);
		return completion.Task;
	}

	/// <summary>
	/// Stops the manager and releases all registered views.
	/// </summary>
	/// <remarks>
	/// Disposal delegates to <see cref="StopAsync"/>: new operations are rejected, active operations are
	/// awaited, and registered views are closed through the dispatch delegate. Calling this member more
	/// than once observes the same stop completion.
	/// </remarks>
	/// <returns>A task that completes when the manager has stopped.</returns>
	public ValueTask DisposeAsync()
		=> new(StopAsync());

	// Runs an asynchronous operation while it is registered as an active manager
	// operation so StopAsync waits for it to finish before detaching views.
	private async Task<T> RunOperationAsync<T>(Func<Task<T>> body)
	{
		TaskCompletionSource<object?> operation = EnterOperation();

		try
		{
			return await body().ConfigureAwait(false);
		}
		finally
		{
			CompleteOperation(operation);
		}
	}

	private TaskCompletionSource<object?> EnterOperation()
	{
		lock (_stateLock)
		{
			ThrowIfStoppingUnderLock();

			TaskCompletionSource<object?> operation = new(TaskCreationOptions.RunContinuationsAsynchronously);
			_activeOperations.Add(operation.Task);
			return operation;
		}
	}

	private void CompleteOperation(TaskCompletionSource<object?> operation)
	{
		lock (_stateLock)
		{
			_activeOperations.Remove(operation.Task);
			operation.TrySetResult(null);
		}
	}

	private async Task StopCoreAsync(
		Task[] activeOperations,
		TaskCompletionSource<object?> completion)
	{
		try
		{
			if (activeOperations.Length > 0)
				await Task.WhenAll(activeOperations).ConfigureAwait(false);

			IWorkspaceDocumentView[] registeredViews;
			lock (_stateLock)
			{
				registeredViews = _views.Keys.ToArray();

				// Every collection that tracks a view is released: leaving any of them populated
				// would retain every view after the teardown and leave the stopped state inconsistent.
				_views.Clear();
				_viewIds.Clear();
				_viewVersions.Clear();
				_viewsByDocument.Clear();
				_viewsByViewId.Clear();
				_unsynchronizedViews.Clear();
			}

			await _dispatchViewAction(() =>
			{
				foreach (IWorkspaceDocumentView view in registeredViews)
				{
					try
					{
						view.ApplyRequested -= OnApplyRequested;
					}
					catch
					{
						// A throwing event accessor must not fault the memoized stop task for later callers;
						// the remaining views are still released.
					}

					try
					{
						view.Close();
					}
					catch
					{ }
				}
			}).ConfigureAwait(false);

			completion.TrySetResult(null);
		}
		catch (Exception exception)
		{
			completion.TrySetException(exception);
		}
	}

	private void ThrowIfStoppingUnderLock()
	{
		ObjectDisposedException.ThrowIf(_stopping, this);
	}
}

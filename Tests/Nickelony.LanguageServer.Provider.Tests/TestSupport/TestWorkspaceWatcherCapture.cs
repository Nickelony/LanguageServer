namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Captures the workspace file watcher the framework creates so framework tests can drive the coordinator's
/// dispatch path directly.
/// </summary>
internal sealed class TestWorkspaceWatcherCapture
{
	private Action<WorkspaceFileWatcher, Exception?>? _onWatcherFailed;

	/// <summary>
	/// Gets the dispatch delegate the framework registered with the created watcher.
	/// </summary>
	public Func<FileChangeBatch, CancellationToken, Task>? Dispatch { get; private set; }

	/// <summary>
	/// Gets the created watcher.
	/// </summary>
	public WorkspaceFileWatcher? Watcher { get; private set; }

	/// <summary>
	/// Gets the number of watchers the factory created.
	/// </summary>
	public int CreatedCount { get; private set; }

	/// <summary>
	/// Gets or sets a value indicating whether the factory throws instead of creating a watcher, simulating a
	/// watcher that cannot be recreated during recovery.
	/// </summary>
	public bool ThrowOnCreate { get; set; }

	/// <summary>
	/// Reports a watcher failure through the callback the framework registered, so tests can drive the
	/// runtime-failure recovery path.
	/// </summary>
	/// <param name="exception">The failure to report, or <see langword="null"/> for none.</param>
	public void Fail(Exception? exception = null)
		=> _onWatcherFailed?.Invoke(Watcher!, exception);

	/// <summary>
	/// Creates and captures a workspace file watcher for the framework's watcher factory seam.
	/// </summary>
	/// <param name="workspaceRootDirectoryPath">The workspace root directory the watcher watches.</param>
	/// <param name="dispatchAsync">The dispatch delegate for coalesced file changes.</param>
	/// <param name="onWatcherFailed">The watcher failure callback.</param>
	/// <returns>The created watcher.</returns>
	public WorkspaceFileWatcher Create(
		string workspaceRootDirectoryPath,
		Func<FileChangeBatch, CancellationToken, Task> dispatchAsync,
		Action<WorkspaceFileWatcher, Exception?> onWatcherFailed)
	{
		CreatedCount++;
		_onWatcherFailed = onWatcherFailed;

		if (ThrowOnCreate)
			throw new InvalidOperationException("Simulated watcher factory failure during recovery.");

		Watcher = new WorkspaceFileWatcher(
			workspaceRootDirectoryPath,
			dispatchAsync,
			[new WorkspaceWatchSpecification("*.test", IncludeSubdirectories: true)],
			onWatcherFailed);

		Dispatch = dispatchAsync;
		return Watcher;
	}
}

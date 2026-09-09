namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Creates the low-level workspace file watcher for one workspace root, used internally between the provider
/// base and the workspace change machinery.
/// </summary>
/// <param name="workspaceRootDirectoryPath">The normalized workspace root directory to watch.</param>
/// <param name="dispatchAsync">The callback that forwards coalesced file changes to the owning coordinator.</param>
/// <param name="onWatcherFailed">The callback that reports a watcher failure to the owning coordinator.</param>
/// <returns>The workspace file watcher for the supplied root.</returns>
internal delegate WorkspaceFileWatcher WorkspaceFileWatcherFactory(
	string workspaceRootDirectoryPath,
	Func<FileChangeBatch, CancellationToken, Task> dispatchAsync,
	Action<WorkspaceFileWatcher, Exception?> onWatcherFailed);

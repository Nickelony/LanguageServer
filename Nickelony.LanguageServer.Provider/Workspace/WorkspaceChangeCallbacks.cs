namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Groups the owner callbacks the workspace change coordinator and its per-root watch scopes depend on,
/// so the constructors that consume them stay readable and positional mistakes between the delegate
/// parameters are impossible.
/// </summary>
/// <param name="ClientAccessor">Returns the active language-server client when available.</param>
/// <param name="IsDisposedAccessor">Returns whether the owner has been disposed.</param>
/// <param name="EnsureStartedAsync">Starts the language server on demand before forwarding file changes.</param>
/// <param name="TryMarkTransportUnhealthy">Attempts to mark one observed transport generation unhealthy after forwarding failures; a failed attempt is the owner's concern.</param>
/// <param name="RaiseWorkspaceWatcherFailed">Reports unrecoverable watcher failures to the owner.</param>
internal sealed record WorkspaceChangeCallbacks(
	Func<ILanguageServerClient?> ClientAccessor,
	Func<bool> IsDisposedAccessor,
	Func<CancellationToken, Task<bool>> EnsureStartedAsync,
	Action<long> TryMarkTransportUnhealthy,
	Action<WorkspaceWatcherFailure> RaiseWorkspaceWatcherFailed);

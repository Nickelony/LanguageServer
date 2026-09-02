namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Describes a workspace-watcher failure that should be surfaced to the UI after automatic recovery is unavailable.
/// </summary>
/// <param name="Message">The user-facing failure message. Editor-buffer IntelliSense may remain available while external workspace forwarding is degraded.</param>
public readonly record struct WorkspaceWatcherFailure(string Message);

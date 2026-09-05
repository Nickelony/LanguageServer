namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Describes a workspace file watcher failure that could not be started or recovered.
/// </summary>
/// <param name="Message">The message to show to the user. IntelliSense for open editor files may remain available while external workspace changes are not forwarded.</param>
public readonly record struct WorkspaceWatcherFailure(string Message);

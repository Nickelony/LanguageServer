namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Adapts the provider's non-generic <see cref="ILogger"/> to the generic logger category that the Client's
/// <see cref="WorkspaceFileWatcher"/> expects, so watcher-internal diagnostics reach the provider's log instead
/// of a no-op logger.
/// </summary>
internal sealed class WorkspaceWatcherLogger : ILogger<WorkspaceFileWatcher>
{
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="WorkspaceWatcherLogger"/> class.
	/// </summary>
	/// <param name="logger">The provider logger the watcher messages are forwarded to.</param>
	internal WorkspaceWatcherLogger(ILogger logger)
		=> _logger = logger;

	/// <inheritdoc/>
	public IDisposable? BeginScope<TState>(TState state)
		where TState : notnull
		=> _logger.BeginScope(state);

	/// <inheritdoc/>
	public bool IsEnabled(LogLevel logLevel)
		=> _logger.IsEnabled(logLevel);

	/// <inheritdoc/>
	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
		=> _logger.Log(logLevel, eventId, state, exception, formatter);
}

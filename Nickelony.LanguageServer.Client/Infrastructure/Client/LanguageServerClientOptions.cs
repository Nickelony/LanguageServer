namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Describes the host-specific payload factories and lifecycle timeouts used to initialize a language-server client.
/// </summary>
/// <remarks>
/// The payload factories may be invoked from background transport threads and should be thread-safe,
/// non-blocking, and cheap to execute.
/// </remarks>
/// <example>
/// <code>
/// var options = new LanguageServerClientOptions(() =&gt; new { maxPreload = 10 })
/// {
///     InitializeTimeout = TimeSpan.FromSeconds(20)
/// };
/// </code>
/// </example>
public sealed class LanguageServerClientOptions
{
	private TimeSpan _initializeTimeout = TimeSpan.FromSeconds(20.0);
	private TimeSpan _shutdownRequestTimeout = TimeSpan.FromSeconds(3.0);
	private TimeSpan _disposeWaitTimeout = TimeSpan.FromSeconds(5.0);

	/// <summary>
	/// Initializes a new instance of the <see cref="LanguageServerClientOptions"/> class.
	/// </summary>
	/// <param name="settingsProvider">Produces the current settings payload for <c>workspace/didChangeConfiguration</c>.</param>
	public LanguageServerClientOptions(Func<object> settingsProvider)
	{
		ArgumentNullException.ThrowIfNull(settingsProvider);
		SettingsProvider = settingsProvider;
	}

	/// <summary>
	/// Gets the settings payload factory for <c>workspace/didChangeConfiguration</c>.
	/// The client invokes this factory lazily and caches the resulting snapshot for <c>workspace/configuration</c>
	/// callbacks. A successfully sent <c>workspace/didChangeConfiguration</c> notification replaces the cached snapshot.
	/// </summary>
	public Func<object> SettingsProvider { get; }

	/// <summary>
	/// Gets or initializes how long startup waits for the server to answer the <c>initialize</c> request.
	/// </summary>
	public TimeSpan InitializeTimeout
	{
		get => _initializeTimeout;
		init => _initializeTimeout = ValidateTimeout(value, nameof(InitializeTimeout));
	}

	/// <summary>
	/// Gets or initializes how long graceful shutdown waits for the server to answer the <c>shutdown</c> request.
	/// </summary>
	public TimeSpan ShutdownRequestTimeout
	{
		get => _shutdownRequestTimeout;
		init => _shutdownRequestTimeout = ValidateTimeout(value, nameof(ShutdownRequestTimeout));
	}

	/// <summary>
	/// Gets or initializes how long disposal waits for background transport work to quiesce before teardown continues.
	/// </summary>
	public TimeSpan DisposeWaitTimeout
	{
		get => _disposeWaitTimeout;
		init => _disposeWaitTimeout = ValidateTimeout(value, nameof(DisposeWaitTimeout));
	}

	/// <summary>
	/// Gets or initializes the client capabilities payload factory for the <c>initialize</c> request.
	/// The argument is the normalized workspace root directory path. This delegate may run on a background transport thread during startup.
	/// </summary>
	public Func<string, object?> ClientCapabilitiesProvider { get; init; } = static _ => new { };

	/// <summary>
	/// Gets or initializes the language-specific initialization options factory for the <c>initialize</c> request.
	/// The argument is the normalized workspace root directory path. This delegate may run on a background transport thread during startup.
	/// </summary>
	public Func<string, object?> InitializationOptionsProvider { get; init; } = static _ => new { };

	private static TimeSpan ValidateTimeout(TimeSpan value, string propertyName)
	{
		if (value == Timeout.InfiniteTimeSpan)
			throw new ArgumentOutOfRangeException(propertyName, value, "Infinite timeouts are not supported for transport lifecycle operations.");

		if (value <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(propertyName, value, "The timeout must be greater than zero.");

		return value;
	}
}

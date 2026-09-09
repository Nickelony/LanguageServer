using System.Collections.ObjectModel;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Describes the payload factories, server process configuration, and lifecycle timeouts used to initialize a language-server client.
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
	private static readonly IReadOnlyDictionary<string, string> s_emptyEnvironmentVariables = new Dictionary<string, string>();

	/// <summary>
	/// The settings provider used when the host passes none: an empty settings payload.
	/// </summary>
	private static readonly Func<object> s_defaultSettingsProvider = static () => new { };

	/// <summary>
	/// The largest timeout that timer-based cancellation accepts (the <see cref="CancellationTokenSource"/> limit).
	/// </summary>
	private static readonly TimeSpan s_maximumTimeout = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

	private TimeSpan _initializeTimeout = TimeSpan.FromSeconds(20.0);
	private IReadOnlyList<string> _serverArguments = [];
	private string? _serverWorkingDirectory;
	private IReadOnlyDictionary<string, string> _environmentVariables = s_emptyEnvironmentVariables;
	private TimeSpan _shutdownRequestTimeout = TimeSpan.FromSeconds(3.0);
	private TimeSpan _disposeWaitTimeout = TimeSpan.FromSeconds(5.0);

	/// <summary>
	/// Initializes a new instance of the <see cref="LanguageServerClientOptions"/> class.
	/// </summary>
	/// <param name="settingsProvider">
	/// Produces the current settings payload for <c>workspace/didChangeConfiguration</c>, or <see langword="null"/>
	/// for an empty settings payload.
	/// </param>
	public LanguageServerClientOptions(Func<object>? settingsProvider = null)
	{
		SettingsProvider = settingsProvider ?? s_defaultSettingsProvider;
	}

	/// <summary>
	/// Gets the settings payload factory for <c>workspace/didChangeConfiguration</c>.
	/// </summary>
	/// <remarks>
	/// The client invokes this factory lazily and caches the resulting snapshot for <c>workspace/configuration</c>
	/// callbacks. The cache is refreshed from the factory before the initialization push and from the outgoing
	/// payload whenever a <c>workspace/didChangeConfiguration</c> notification is sent through this client. Both
	/// channels are served from the same serialized element, so the same member casing reaches the server on either
	/// channel. The factory must return a non-null payload; returning <see langword="null"/> fails the initialization
	/// handshake.
	/// </remarks>
	public Func<object> SettingsProvider { get; }

	/// <summary>
	/// Gets or initializes how long startup waits for the server to answer the <c>initialize</c> request.
	/// Defaults to 20 seconds.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown when the assigned value is not a positive finite duration; infinite timeouts are not supported.
	/// </exception>
	public TimeSpan InitializeTimeout
	{
		get => _initializeTimeout;
		init => _initializeTimeout = ValidateTimeout(value, nameof(InitializeTimeout));
	}

	/// <summary>
	/// Gets or initializes how long graceful shutdown waits for the server to answer the <c>shutdown</c> request.
	/// Defaults to 3 seconds. The same budget bounds the graceful <c>exit</c> notification dispatch.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown when the assigned value is not a positive finite duration; infinite timeouts are not supported.
	/// </exception>
	public TimeSpan ShutdownRequestTimeout
	{
		get => _shutdownRequestTimeout;
		init => _shutdownRequestTimeout = ValidateTimeout(value, nameof(ShutdownRequestTimeout));
	}

	/// <summary>
	/// Gets or initializes how long disposal waits for background transport work to quiesce before teardown continues.
	/// Defaults to 5 seconds; the budget is shared by all teardown stages of a single disposal.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown when the assigned value is not a positive finite duration; infinite timeouts are not supported.
	/// </exception>
	public TimeSpan DisposeWaitTimeout
	{
		get => _disposeWaitTimeout;
		init => _disposeWaitTimeout = ValidateTimeout(value, nameof(DisposeWaitTimeout));
	}

	/// <summary>
	/// Gets or initializes the client capabilities payload factory for the <c>initialize</c> request.
	/// Defaults to a factory that returns an empty object.
	/// </summary>
	/// <remarks>
	/// The argument holds the normalized workspace root directory paths in caller order; the first entry is the
	/// primary root. This delegate may run on a background transport thread during startup. The client rewrites
	/// <c>dynamicRegistration</c> to <see langword="false"/> on every capability object under <c>workspace</c> and
	/// <c>textDocument</c> because it does not service dynamic registration, forces <c>window.workDoneProgress</c>
	/// to <see langword="false"/> when the payload advertises it because it has no client-side progress sink,
	/// pins <c>general.positionEncodings</c> to UTF-16 (the only encoding its coordinate math implements), and a
	/// factory that returns <see langword="null"/> is treated as an empty capabilities object.
	/// </remarks>
	public Func<IReadOnlyList<string>, object?> ClientCapabilitiesProvider { get; init; } = static _ => new { };

	/// <summary>
	/// Gets or initializes the language-specific initialization options factory for the <c>initialize</c> request.
	/// Defaults to a factory that returns an empty object.
	/// </summary>
	/// <remarks>
	/// The argument holds the normalized workspace root directory paths in caller order; the first entry is the
	/// primary root. This delegate may run on a background transport thread during startup.
	/// </remarks>
	public Func<IReadOnlyList<string>, object?> InitializationOptionsProvider { get; init; } = static _ => new { };

	/// <summary>
	/// Gets or initializes whether startup fails when the server does not advertise full or incremental text synchronization.
	/// Defaults to <see langword="true"/>.
	/// </summary>
	/// <remarks>
	/// A host that mirrors documents through the tracked-document store must not silently start against a server that
	/// will not accept its synchronization notifications; set this option to <see langword="false"/> to accept servers
	/// that do not support text synchronization at all.
	/// </remarks>
	public bool RequireTextDocumentSynchronization { get; init; } = true;

	/// <summary>
	/// Gets or initializes the command-line arguments passed to the language-server process.
	/// The list is copied on assignment; <see langword="null"/> means no arguments.
	/// </summary>
	/// <exception cref="ArgumentException"><paramref name="value"/> contains a <see langword="null"/> entry.</exception>
	public IReadOnlyList<string> ServerArguments
	{
		get => _serverArguments;
		init => _serverArguments = value is null ? [] : ValidateServerArguments(value, nameof(ServerArguments));
	}

	/// <summary>
	/// Copies and validates one server-argument list.
	/// </summary>
	/// <param name="value">The argument list to validate.</param>
	/// <param name="propertyName">The property name reported in the validation failure.</param>
	/// <returns>The validated read-only argument list.</returns>
	/// <exception cref="ArgumentException">The list contains a <see langword="null"/> entry.</exception>
	private static ReadOnlyCollection<string> ValidateServerArguments(IReadOnlyList<string> value, string propertyName)
	{
		string[] arguments = [.. value];

		for (int i = 0; i < arguments.Length; i++)
		{
			if (arguments[i] is null)
				throw new ArgumentException("Server arguments must not contain null entries.", propertyName);
		}

		return Array.AsReadOnly(arguments);
	}

	/// <summary>
	/// Gets or initializes the working directory for the language-server process, or <see langword="null"/> to use
	/// the directory containing the server executable.
	/// </summary>
	/// <remarks>
	/// Servers that resolve configuration or support files relative to the workspace root need
	/// <see cref="ServerWorkingDirectory"/> set to that root, because the process default is the executable's own
	/// directory.
	/// </remarks>
	/// <exception cref="ArgumentException">The assigned value is empty or whitespace-only.</exception>
	public string? ServerWorkingDirectory
	{
		get => _serverWorkingDirectory;
		init => _serverWorkingDirectory = value is null ? null : ValidateServerWorkingDirectory(value, nameof(ServerWorkingDirectory));
	}

	/// <summary>
	/// Validates one server working directory value.
	/// </summary>
	/// <param name="value">The directory path to validate.</param>
	/// <param name="propertyName">The property name used in the thrown exception.</param>
	/// <returns>The validated directory path.</returns>
	/// <exception cref="ArgumentException">The value is empty or whitespace-only.</exception>
	private static string ValidateServerWorkingDirectory(string value, string propertyName)
	{
		if (string.IsNullOrWhiteSpace(value))
			throw new ArgumentException("The server working directory must not be empty or whitespace-only.", propertyName);

		return value;
	}

	/// <summary>
	/// Gets or initializes additional environment variables applied to the language-server process.
	/// The variables are added on top of the environment the process would otherwise inherit;
	/// the dictionary is copied on assignment and <see langword="null"/> means none.
	/// </summary>
	/// <remarks>
	/// The copy uses the case sensitivity of environment-variable names on the current platform (case-insensitive on
	/// Windows, case-sensitive elsewhere), so sibling entries that differ only in casing are rejected by the copy
	/// instead of silently overwriting each other in the child process environment.
	/// </remarks>
	/// <exception cref="ArgumentException">
	/// <paramref name="value"/> contains two entries that differ only in casing on a case-insensitive platform.
	/// </exception>
	public IReadOnlyDictionary<string, string> EnvironmentVariables
	{
		get => _environmentVariables;
		init => _environmentVariables = value is null
			? s_emptyEnvironmentVariables
			: new Dictionary<string, string>(value, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
	}

	private static TimeSpan ValidateTimeout(TimeSpan value, string propertyName)
	{
		if (value == Timeout.InfiniteTimeSpan)
			throw new ArgumentOutOfRangeException(propertyName, value, "Infinite timeouts are not supported for transport lifecycle operations.");

		if (value <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(propertyName, value, "The timeout must be greater than zero.");

		// Timer-based cancellation rejects delays above uint.MaxValue - 1 milliseconds; validating the same bound here
		// keeps an oversized value from failing every startup or teardown at the platform limit instead.
		if (value > s_maximumTimeout)
		{
			throw new ArgumentOutOfRangeException(propertyName, value,
				$"The timeout must not exceed {s_maximumTimeout.TotalDays:F1} days, the platform limit for timer-based cancellation.");
		}

		return value;
	}
}

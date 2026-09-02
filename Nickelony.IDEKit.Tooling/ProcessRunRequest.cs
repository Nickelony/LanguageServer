using System.Text;

namespace Nickelony.IDEKit.Tooling;

/// <summary>
/// Immutable description of an external process to launch.
/// </summary>
public sealed record ProcessRunRequest
{
	private static readonly IReadOnlyDictionary<string, string> s_emptyEnvironment = new Dictionary<string, string>();

	/// <summary>
	/// Gets the executable, document, or shell target to launch.
	/// </summary>
	public required string FileName { get; init; }

	/// <summary>
	/// Gets the command-line arguments to pass to the executable, or an empty string when none are required.
	/// </summary>
	public string Arguments { get; init; } = string.Empty;

	/// <summary>
	/// Gets the working directory for the process, or <see langword="null"/> to inherit the caller's directory.
	/// </summary>
	public string? WorkingDirectory { get; init; }

	/// <summary>
	/// Gets the environment variables to add or replace for the process, or an empty collection when none are requested.
	/// </summary>
	/// <remarks>
	/// The launcher copies these entries to the process start information. Variables not listed here retain the
	/// environment inherited from the caller. Environment overrides may not be supported when shell execution is enabled.
	/// </remarks>
	public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; } = s_emptyEnvironment;

	/// <summary>
	/// Gets whether the executable or shell target is launched through the operating system shell.
	/// </summary>
	/// <remarks>
	/// Shell execution cannot be combined with standard-output or standard-error redirection. Environment variable
	/// overrides may not be supported for shell-launched processes.
	/// </remarks>
	public bool UseShellExecute { get; init; }

	/// <summary>
	/// Gets whether standard output is captured into the run result.
	/// </summary>
	public bool RedirectStandardOutput { get; init; }

	/// <summary>
	/// Gets whether standard error is captured into the run result.
	/// </summary>
	public bool RedirectStandardError { get; init; }

	/// <summary>
	/// Gets the encoding used to decode captured standard output, or <see langword="null"/> for the platform default.
	/// </summary>
	public Encoding? StandardOutputEncoding { get; init; }

	/// <summary>
	/// Gets the encoding used to decode captured standard error, or <see langword="null"/> for the platform default.
	/// </summary>
	public Encoding? StandardErrorEncoding { get; init; }

	/// <summary>
	/// Gets the maximum time to wait for the process before termination is attempted, or <see langword="null"/> to wait indefinitely.
	/// </summary>
	public TimeSpan? Timeout { get; init; }
}

namespace Nickelony.IDEKit.Tooling;

/// <summary>
/// Immutable outcome of a process run.
/// </summary>
/// <remarks>
/// When <see cref="Started"/> is <see langword="false"/>, the remaining properties have their default values.
/// When it is <see langword="true"/>, the run has waited for the process to exit or attempted to terminate it.
/// </remarks>
public sealed record ProcessRunResult
{
	/// <summary>
	/// Gets whether the process was successfully started.
	/// </summary>
	public bool Started { get; init; }

	/// <summary>
	/// Gets the process exit code when the process started and exited; otherwise, the default value is <c>0</c>.
	/// </summary>
	public int ExitCode { get; init; }

	/// <summary>
	/// Gets all captured standard output, or <see langword="null"/> when output was not captured.
	/// </summary>
	public string? StandardOutput { get; init; }

	/// <summary>
	/// Gets all captured standard error, or <see langword="null"/> when error output was not captured.
	/// </summary>
	public string? StandardError { get; init; }

	/// <summary>
	/// Gets whether waiting ended because the configured timeout elapsed while cancellation was not observed.
	/// </summary>
	public bool TimedOut { get; init; }

	/// <summary>
	/// Gets whether cancellation was requested when the run outcome was created.
	/// </summary>
	public bool Cancelled { get; init; }
}

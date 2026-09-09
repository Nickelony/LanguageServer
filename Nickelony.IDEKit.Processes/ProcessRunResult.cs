namespace Nickelony.IDEKit.Processes;

/// <summary>
/// Immutable outcome of a process run.
/// </summary>
/// <remarks>
/// When <see cref="Started"/> is <see langword="false"/>, the remaining properties have their default values.
/// When it is <see langword="true"/>, the run has waited for the process to exit or attempted to terminate it.
/// Exit observation is bounded: when a termination attempt cannot be confirmed, <see cref="ExitCode"/> and the
/// captured output are <see langword="null"/> and the process may still be running.
/// </remarks>
public sealed record ProcessRunResult
{
	/// <summary>
	/// Gets whether the launcher returned a process handle.
	/// </summary>
	public bool Started { get; init; }

	/// <summary>
	/// Gets the process exit code when the process started and its exit was observed, or <see langword="null"/>
	/// when the exit could not be observed after termination attempts failed.
	/// </summary>
	public int? ExitCode { get; init; }

	/// <summary>
	/// Gets all captured standard output, or <see langword="null"/> when output was not captured or the
	/// process exit could not be observed.
	/// </summary>
	public string? StandardOutput { get; init; }

	/// <summary>
	/// Gets all captured standard error, or <see langword="null"/> when error output was not captured or the
	/// process exit could not be observed.
	/// </summary>
	public string? StandardError { get; init; }

	/// <summary>
	/// Gets whether waiting ended because the configured timeout elapsed while cancellation was not observed.
	/// </summary>
	public bool TimedOut { get; init; }

	/// <summary>
	/// Gets whether cancellation was requested when the run outcome was created.
	/// </summary>
	/// <remarks>
	/// When cancellation is observed while waiting, the runner attempts to terminate the process and its descendants,
	/// falling back to the process alone if necessary, before creating the result. This property reports the token state;
	/// it does not indicate whether termination succeeded. An unconfirmed termination is reported by
	/// <see cref="ExitCode"/> being <see langword="null"/>.
	/// </remarks>
	public bool Canceled { get; init; }
}

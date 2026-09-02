namespace Nickelony.IDEKit.Tooling;

/// <summary>
/// Launches and drives external processes for compiler, formatter, linter, and generator integration.
/// </summary>
public interface IProcessRunner
{
	/// <summary>
	/// Runs the process described by the request until it exits, reaches its timeout, or is cancelled, then returns the outcome.
	/// </summary>
	/// <param name="request">The process run request.</param>
	/// <param name="cancellationToken">The token checked while waiting. When cancellation is observed, termination of the
	/// process is attempted and the result is marked as cancelled.</param>
	/// <returns>The run outcome; when the shell did not produce a process, <see cref="ProcessRunResult.Started"/> is
	/// <see langword="false"/> and the other members are at their defaults.</returns>
	ProcessRunResult Run(ProcessRunRequest request, CancellationToken cancellationToken = default);

	/// <summary>
	/// Starts the process described by the request and returns a handle the caller drives.
	/// </summary>
	/// <param name="request">The process run request.</param>
	/// <returns>The started process handle. The caller is responsible for disposing the handle.</returns>
	/// <exception cref="InvalidOperationException">The launch mechanism did not produce a process.</exception>
	IProcessHandle Start(ProcessRunRequest request);
}

namespace Nickelony.IDEKit.Tooling;

/// <summary>
/// Launches and drives external processes for compiler, formatter, linter, and generator integration.
/// </summary>
public interface IProcessRunner
{
	/// <summary>
	/// Waits for the process described by the request to exit, for its timeout to elapse, or for cancellation, then returns
	/// the outcome.
	/// </summary>
	/// <param name="request">The process run request.</param>
	/// <param name="cancellationToken">The token checked while waiting. When cancellation is observed, the runner attempts
	/// to terminate the process and its descendants, falling back to the process alone if necessary. The result reports
	/// cancellation.</param>
	/// <returns>The run outcome. If the launcher does not produce a process, <see cref="ProcessRunResult.Started"/> is
	/// <see langword="false"/> and the other members have their default values.</returns>
	/// <remarks>
	/// The process handle is disposed before this method returns.
	/// </remarks>
	ProcessRunResult Run(ProcessRunRequest request, CancellationToken cancellationToken = default);

	/// <summary>
	/// Starts the process described by the request and returns a handle the caller drives.
	/// </summary>
	/// <param name="request">The process run request.</param>
	/// <returns>The started process handle. The caller is responsible for disposing the handle.</returns>
	/// <exception cref="InvalidOperationException">The launch mechanism did not produce a process.</exception>
	IProcessHandle Start(ProcessRunRequest request);
}

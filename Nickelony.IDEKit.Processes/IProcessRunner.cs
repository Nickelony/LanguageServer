namespace Nickelony.IDEKit.Processes;

/// <summary>
/// Launches and drives external processes for compiler, formatter, linter, and generator integration.
/// </summary>
public interface IProcessRunner
{
	/// <summary>
	/// Waits for the process described by the request to exit, for its timeout to elapse, or for cancellation, then returns
	/// the outcome.
	/// </summary>
	/// <remarks>
	/// The process handle is disposed before this method returns. Redirected standard output and error are
	/// drained while the process runs, so a child that writes more than the operating-system pipe buffer
	/// cannot deadlock the wait.
	/// </remarks>
	/// <param name="request">The process run request.</param>
	/// <param name="cancellationToken">The token checked while waiting. When cancellation is observed, the runner attempts
	/// to terminate the process and its descendants, falling back to the process alone if necessary. The result reports
	/// cancellation.</param>
	/// <returns>The run outcome. If the launcher does not produce a process, <see cref="ProcessRunResult.Started"/> is
	/// <see langword="false"/> and the other members have their default values.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="System.ComponentModel.Win32Exception">The process could not be started, such as when the executable does not exist.</exception>
	/// <exception cref="InvalidOperationException">The operating system reported a failure while starting or waiting for the process.</exception>
	ProcessRunResult Run(ProcessRunRequest request, CancellationToken cancellationToken = default);

	/// <summary>
	/// Starts the process described by the request and returns a handle the caller drives.
	/// </summary>
	/// <remarks>The caller is responsible for disposing the handle.</remarks>
	/// <param name="request">The process run request.</param>
	/// <returns>The started process handle.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="System.ComponentModel.Win32Exception">The process could not be started, such as when the executable does not exist.</exception>
	/// <exception cref="InvalidOperationException">The launch mechanism did not produce a process.</exception>
	IProcessHandle Start(ProcessRunRequest request);
}

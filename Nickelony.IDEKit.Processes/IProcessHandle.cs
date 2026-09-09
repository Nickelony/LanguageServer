namespace Nickelony.IDEKit.Processes;

/// <summary>
/// Represents a started external process and provides operations for waiting, requesting termination, and reading output.
/// </summary>
/// <remarks>
/// <para>
/// The caller owns the handle and must dispose it when it is no longer needed. Disposing the handle releases
/// process resources; it does not terminate the process.
/// </para>
/// <para>
/// Implementations drain redirected standard output and error while the process runs, so a process that writes
/// more than the operating-system pipe buffer does not block on its own output.
/// </para>
/// </remarks>
public interface IProcessHandle : IDisposable
{
	/// <summary>
	/// Gets the process exit code after the process has exited.
	/// </summary>
	/// <exception cref="InvalidOperationException">The process has not exited.</exception>
	int ExitCode { get; }

	/// <summary>
	/// Gets all standard output captured from the redirected output stream.
	/// </summary>
	/// <remarks>
	/// Implementations may drain the stream while the process runs; reading the property may block until the
	/// stream closes. The process must have been started with standard-output redirection enabled; otherwise,
	/// accessing this property throws <see cref="InvalidOperationException"/>.
	/// </remarks>
	string StandardOutput { get; }

	/// <summary>
	/// Gets all standard error captured from the redirected error stream.
	/// </summary>
	/// <remarks>
	/// Implementations may drain the stream while the process runs; reading the property may block until the
	/// stream closes. The process must have been started with standard-error redirection enabled; otherwise,
	/// accessing this property throws <see cref="InvalidOperationException"/>.
	/// </remarks>
	string StandardError { get; }

	/// <summary>
	/// Blocks until the process exits.
	/// </summary>
	/// <remarks>
	/// This method does not terminate the process.
	/// </remarks>
	void WaitForExit();

	/// <summary>
	/// Waits up to the specified number of milliseconds and reports whether the process exited in time.
	/// </summary>
	/// <remarks>
	/// This method does not terminate the process.
	/// </remarks>
	/// <param name="timeoutMilliseconds">The maximum wait in milliseconds. Use <c>-1</c> to wait indefinitely.</param>
	/// <returns><see langword="true"/> when the process exited within the timeout; otherwise, <see langword="false"/>.</returns>
	bool WaitForExit(int timeoutMilliseconds);

	/// <summary>
	/// Requests termination of the process without terminating its child processes.
	/// </summary>
	/// <remarks>
	/// This method does not wait for the process to exit.
	/// </remarks>
	void Kill();

	/// <summary>
	/// Requests termination of the process and its descendants.
	/// </summary>
	/// <remarks>
	/// This method does not wait for the process to exit. Terminating descendants may not be supported for every process.
	/// </remarks>
	void KillEntireProcessTree();
}

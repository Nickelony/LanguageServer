namespace Nickelony.IDEKit.Tooling;

/// <summary>
/// Represents a started external process and provides operations for waiting, termination, and output access.
/// </summary>
/// <remarks>
/// The caller owns the handle and must dispose it when it is no longer needed. Disposing the handle releases
/// process resources; it does not terminate the process.
/// </remarks>
public interface IProcessHandle : IDisposable
{
	/// <summary>
	/// Gets the process exit code after the process has exited.
	/// </summary>
	/// <exception cref="InvalidOperationException">The process has not exited.</exception>
	int ExitCode { get; }

	/// <summary>
	/// Gets all standard output by reading the redirected output stream to its end.
	/// </summary>
	/// <remarks>
	/// Reading may block until the stream closes. The process must have been started with standard-output redirection
	/// enabled; otherwise, accessing this property throws <see cref="InvalidOperationException"/>.
	/// </remarks>
	string? StandardOutput { get; }

	/// <summary>
	/// Gets all standard error by reading the redirected error stream to its end.
	/// </summary>
	/// <remarks>
	/// Reading may block until the stream closes. The process must have been started with standard-error redirection
	/// enabled; otherwise, accessing this property throws <see cref="InvalidOperationException"/>.
	/// </remarks>
	string? StandardError { get; }

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
	/// <param name="timeoutMilliseconds">The maximum wait in milliseconds. Use <c>-1</c> to wait indefinitely.</param>
	/// <returns><see langword="true"/> when the process exited within the timeout; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// This method does not terminate the process.
	/// </remarks>
	bool WaitForExit(int timeoutMilliseconds);

	/// <summary>
	/// Requests termination of the process without terminating its child processes.
	/// </summary>
	/// <remarks>
	/// This method does not wait for the process to exit.
	/// </remarks>
	void Kill();

	/// <summary>
	/// Requests termination of the process and its child process tree.
	/// </summary>
	/// <remarks>
	/// This method does not wait for the process to exit. Tree termination may not be supported for every process.
	/// </remarks>
	void KillEntireProcessTree();
}

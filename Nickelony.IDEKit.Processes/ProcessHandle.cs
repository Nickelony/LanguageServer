using System.Diagnostics;

namespace Nickelony.IDEKit.Processes;

/// <summary>
/// Adapts <see cref="Process"/> to <see cref="IProcessHandle"/>.
/// </summary>
/// <remarks>
/// Redirected standard output and error are drained asynchronously from the moment the handle is created, so a
/// process that writes more than the operating-system pipe buffer cannot deadlock while the caller waits for it
/// to exit. The <see cref="StandardOutput"/> and <see cref="StandardError"/> properties return the drained text
/// and may block until the corresponding stream closes.
/// </remarks>
internal sealed class ProcessHandle : IProcessHandle
{
	private readonly Process _process;
	private readonly Task<string>? _standardOutputTask;
	private readonly Task<string>? _standardErrorTask;

	public ProcessHandle(Process process, bool redirectStandardOutput, bool redirectStandardError)
	{
		_process = process;

		if (redirectStandardOutput)
			_standardOutputTask = process.StandardOutput.ReadToEndAsync();

		if (redirectStandardError)
			_standardErrorTask = process.StandardError.ReadToEndAsync();
	}

	public int ExitCode
		=> _process.ExitCode;

	public string StandardOutput
		=> _standardOutputTask is not null
			? _standardOutputTask.GetAwaiter().GetResult()
			: _process.StandardOutput.ReadToEnd();

	public string StandardError
		=> _standardErrorTask is not null
			? _standardErrorTask.GetAwaiter().GetResult()
			: _process.StandardError.ReadToEnd();

	public void WaitForExit()
		=> _process.WaitForExit();

	public bool WaitForExit(int timeoutMilliseconds)
		=> _process.WaitForExit(timeoutMilliseconds);

	public void Kill()
		=> _process.Kill();

	public void KillEntireProcessTree()
		=> _process.Kill(entireProcessTree: true);

	public void Dispose()
	{
		_process.Dispose();

		// The drains can still be running when a caller disposes a live handle; observing their faults
		// keeps the teardown from surfacing as an unobserved task exception.
		ObserveDrainFault(_standardOutputTask);
		ObserveDrainFault(_standardErrorTask);
	}

	private static void ObserveDrainFault(Task<string>? drainTask)
	{
		if (drainTask is { IsCompleted: false })
			_ = drainTask.ContinueWith(static task => _ = task.Exception, TaskScheduler.Default);
	}
}

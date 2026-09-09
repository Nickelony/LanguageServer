using System.ComponentModel;

namespace Nickelony.IDEKit.Processes;

/// <summary>
/// Runs external processes from <see cref="ProcessRunRequest"/> values, handling timeouts,
/// cancellation, and process-tree termination.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
	private const int PollIntervalMilliseconds = 100;

	// Termination is requested but still observed: a failed kill must not turn into an unbounded wait,
	// so the runner waits at most this long for the exit to become observable.
	private const int TerminationGracePeriodMilliseconds = 5000;

	private readonly IProcessLauncher _launcher;

	/// <summary>
	/// Initializes a new instance of the <see cref="ProcessRunner"/> class.
	/// </summary>
	public ProcessRunner()
		=> _launcher = new ProcessLauncher();

	internal ProcessRunner(IProcessLauncher launcher)
		=> _launcher = launcher;

	/// <inheritdoc />
	public IProcessHandle Start(ProcessRunRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);

		return _launcher.Start(request)
			?? throw new InvalidOperationException("The process could not be started.");
	}

	/// <inheritdoc />
	public ProcessRunResult Run(ProcessRunRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		using IProcessHandle? process = _launcher.Start(request);

		if (process is null)
			return new ProcessRunResult { Started = false };

		bool shouldTerminate = WaitForCompletion(process, request.Timeout, cancellationToken);

		bool exitObserved = true;

		if (shouldTerminate)
			exitObserved = TerminateProcessTree(process);

		bool canceled = cancellationToken.IsCancellationRequested;

		// A termination that could not be confirmed leaves the exit state (and the drained output)
		// unobservable, so the result reports the exit code as unknown instead of blocking on it.
		return new ProcessRunResult
		{
			Started = true,
			ExitCode = exitObserved ? process.ExitCode : null,
			StandardOutput = exitObserved && request.RedirectStandardOutput ? process.StandardOutput : null,
			StandardError = exitObserved && request.RedirectStandardError ? process.StandardError : null,
			TimedOut = shouldTerminate && !canceled,
			Canceled = canceled
		};
	}

	private static bool WaitForCompletion(IProcessHandle process, TimeSpan? timeout, CancellationToken cancellationToken)
	{
		if (timeout is null && !cancellationToken.CanBeCanceled)
		{
			process.WaitForExit();
			return false;
		}

		int remainingMilliseconds = timeout is TimeSpan value ? checked((int)value.TotalMilliseconds) : Timeout.Infinite;

		while (remainingMilliseconds != 0 && !cancellationToken.IsCancellationRequested)
		{
			int waitMilliseconds = remainingMilliseconds == Timeout.Infinite
				? PollIntervalMilliseconds
				: Math.Min(PollIntervalMilliseconds, remainingMilliseconds);

			if (process.WaitForExit(waitMilliseconds))
				return false;

			if (remainingMilliseconds != Timeout.Infinite)
				remainingMilliseconds -= waitMilliseconds;
		}

		return true;
	}

	/// <summary>
	/// Requests termination of the process tree, falls back to terminating the process alone, and observes
	/// the exit within the termination grace period.
	/// </summary>
	/// <returns><see langword="true"/> when the process exit could be observed; otherwise, <see langword="false"/>.</returns>
	private static bool TerminateProcessTree(IProcessHandle process)
	{
		try
		{
			process.KillEntireProcessTree();
		}
		catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or Win32Exception)
		{
			// Tree termination can be unsupported or fail when the process is no longer available. Fall back to terminating only the launched process.
			try
			{
				process.Kill();
			}
			catch (Exception fallbackException) when (fallbackException is InvalidOperationException or Win32Exception)
			{
				// The underlying process could not be terminated; the bounded wait below reports the unobserved exit.
			}
		}

		try
		{
			return process.WaitForExit(TerminationGracePeriodMilliseconds);
		}
		catch (InvalidOperationException)
		{
			// The underlying process is no longer available to observe.
			return true;
		}
	}
}

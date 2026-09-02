using System.ComponentModel;

namespace Nickelony.IDEKit.Tooling;

/// <summary>
/// Runs external processes from <see cref="ProcessRunRequest"/> values, handling timeouts,
/// cancellation, and process-tree termination.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
	private const int PollIntervalMilliseconds = 100;

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

		if (shouldTerminate)
			TerminateProcessTree(process);

		bool cancelled = cancellationToken.IsCancellationRequested;

		return new ProcessRunResult
		{
			Started = true,
			ExitCode = process.ExitCode,
			StandardOutput = request.RedirectStandardOutput ? process.StandardOutput : null,
			StandardError = request.RedirectStandardError ? process.StandardError : null,
			TimedOut = shouldTerminate && !cancelled,
			Cancelled = cancelled
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

	private static void TerminateProcessTree(IProcessHandle process)
	{
		try
		{
			process.KillEntireProcessTree();
		}
		catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or Win32Exception)
		{
			// Some process types do not support whole-tree termination. Fall back to terminating only the launched process.
			try
			{
				process.Kill();
			}
			catch (Exception fallbackException) when (fallbackException is InvalidOperationException or Win32Exception)
			{
				// The underlying process could not be terminated; continue to observe its exit state.
			}
		}

		try
		{
			process.WaitForExit();
		}
		catch (InvalidOperationException)
		{
			// The underlying process is no longer available to observe.
		}
	}
}

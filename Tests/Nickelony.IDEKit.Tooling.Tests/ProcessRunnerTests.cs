using System.Text;

namespace Nickelony.IDEKit.Tooling.Tests;

/// <summary>
/// Verifies that <see cref="ProcessRunner"/> waits for normal completion, reports timeouts and
/// cancellation, handles launch and wait failures, preserves run requests, captures requested output,
/// disposes run handles, and returns handles from <see cref="ProcessRunner.Start(ProcessRunRequest)"/>.
/// </summary>
[TestClass]
public class ProcessRunnerTests
{
	[TestMethod]
	public void Run_WithoutTimeoutOrCancellation_BlocksUntilExitAndReturnsResult()
	{
		var handle = new FakeProcessHandle(exitCode: 7);
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle));

		ProcessRunResult result = runner.Run(CreateRequest());

		Assert.IsTrue(result.Started);
		Assert.AreEqual(7, result.ExitCode);
		Assert.IsFalse(result.TimedOut);
		Assert.IsFalse(result.Cancelled);
		Assert.IsTrue(handle.WaitForExitCalled);
		Assert.AreEqual(0, handle.TimedWaitForExitCalls);
		Assert.IsTrue(handle.Disposed);
	}

	[TestMethod]
	public void Run_Timeout_ProcessExitsInTime_ReturnsResultWithoutTerminating()
	{
		var handle = new FakeProcessHandle(onTimedWaitForExit: _ => true);
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle));

		ProcessRunResult result = runner.Run(CreateRequest(timeout: TimeSpan.FromSeconds(1)));

		Assert.IsTrue(result.Started);
		Assert.IsFalse(result.TimedOut);
		Assert.IsFalse(result.Cancelled);
		Assert.AreEqual(1, handle.TimedWaitForExitCalls);
		Assert.AreEqual(0, handle.KillEntireProcessTreeCalls);
		Assert.AreEqual(0, handle.KillCalls);
		Assert.IsTrue(handle.Disposed);
	}

	[TestMethod]
	public void Run_Timeout_Exceeded_TerminatesProcessTreeAndReturnsTimedOut()
	{
		var handle = new FakeProcessHandle(onTimedWaitForExit: _ => false);
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle));

		ProcessRunResult result = runner.Run(CreateRequest(timeout: TimeSpan.FromMilliseconds(350)));

		Assert.IsTrue(result.Started);
		Assert.IsTrue(result.TimedOut);
		Assert.IsFalse(result.Cancelled);
		Assert.IsTrue(handle.TimedWaitForExitCalls > 0);
		Assert.AreEqual(1, handle.KillEntireProcessTreeCalls);
		Assert.AreEqual(0, handle.KillCalls);
		Assert.IsTrue(handle.Disposed);
	}

	[TestMethod]
	public void Run_TimeoutWithTreeKillFailure_FallsBackToSingleKill()
	{
		var handle = new FakeProcessHandle(
			onTimedWaitForExit: _ => false,
			onKillEntireProcessTree: () => throw new InvalidOperationException("Process has already exited"));
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle));

		ProcessRunResult result = runner.Run(CreateRequest(timeout: TimeSpan.FromMilliseconds(350)));

		Assert.IsTrue(result.TimedOut);
		Assert.AreEqual(1, handle.KillEntireProcessTreeCalls);
		Assert.AreEqual(1, handle.KillCalls);
		Assert.IsTrue(handle.Disposed);
	}

	[TestMethod]
	public void Run_Cancelled_TerminatesProcessTreeAndReturnsCancelled()
	{
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var handle = new FakeProcessHandle();
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle));

		ProcessRunResult result = runner.Run(CreateRequest(), cancellation.Token);

		Assert.IsTrue(result.Started);
		Assert.IsTrue(result.Cancelled);
		Assert.IsFalse(result.TimedOut);
		Assert.AreEqual(1, handle.KillEntireProcessTreeCalls);
		Assert.IsTrue(handle.Disposed);
	}

	[TestMethod]
	public void Run_StartFailure_ReturnsStartedFalse()
	{
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => null));

		ProcessRunResult result = runner.Run(CreateRequest());

		Assert.IsFalse(result.Started);
		Assert.IsFalse(result.TimedOut);
		Assert.IsFalse(result.Cancelled);
	}

	[TestMethod]
	public void Run_StartException_Propagates()
	{
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => throw new InvalidOperationException("process start failed")));

		Assert.ThrowsExactly<InvalidOperationException>(() => runner.Run(CreateRequest()));
	}

	[TestMethod]
	public void Run_WhenWaitThrows_PropagatesAndDisposes()
	{
		var handle = new FakeProcessHandle(onTimedWaitForExit: _ => throw new InvalidOperationException("process wait failed"));
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle));

		Assert.ThrowsExactly<InvalidOperationException>(() => runner.Run(CreateRequest(timeout: TimeSpan.FromSeconds(1))));

		Assert.IsTrue(handle.Disposed);
	}

	[TestMethod]
	public void Run_RequestPassedToLauncherUnchanged()
	{
		var launcher = new FakeProcessLauncher(_ => new FakeProcessHandle());
		var runner = new ProcessRunner(launcher);

		ProcessRunRequest request = CreateRequest(
			workingDirectory: @"C:\work",
			useShellExecute: true,
			environment: new Dictionary<string, string> { ["KEY"] = "value" },
			standardOutputEncoding: Encoding.UTF8,
			redirectStandardOutput: true,
			timeout: TimeSpan.FromSeconds(5));

		runner.Run(request);

		Assert.AreSame(request, launcher.LastRequest);
		Assert.IsNotNull(launcher.LastRequest);
		Assert.AreEqual(@"C:\work", launcher.LastRequest.WorkingDirectory);
		Assert.IsTrue(launcher.LastRequest.UseShellExecute);
		Assert.AreEqual("value", launcher.LastRequest.EnvironmentVariables["KEY"]);
		Assert.AreEqual(Encoding.UTF8, launcher.LastRequest.StandardOutputEncoding);
		Assert.IsTrue(launcher.LastRequest.RedirectStandardOutput);
		Assert.AreEqual(TimeSpan.FromSeconds(5), launcher.LastRequest.Timeout);
	}

	[TestMethod]
	public void Run_CapturesStandardOutputAndError()
	{
		var handle = new FakeProcessHandle(standardOutput: "output", standardError: "error");
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle));

		ProcessRunResult result = runner.Run(CreateRequest(redirectStandardOutput: true, redirectStandardError: true));

		Assert.AreEqual("output", result.StandardOutput);
		Assert.AreEqual("error", result.StandardError);
	}

	[TestMethod]
	public void Run_DoesNotCaptureOutputWhenNotRequested()
	{
		var handle = new FakeProcessHandle(standardOutput: "output", standardError: "error");
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle));

		ProcessRunResult result = runner.Run(CreateRequest());

		Assert.IsNull(result.StandardOutput);
		Assert.IsNull(result.StandardError);
	}

	[TestMethod]
	public void Run_NullRequest_Throws()
	{
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => null));

		Assert.ThrowsExactly<ArgumentNullException>(() => runner.Run(null!));
	}

	[TestMethod]
	public void Start_ReturnsHandleFromLauncher()
	{
		var handle = new FakeProcessHandle();
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle));

		IProcessHandle started = runner.Start(CreateRequest());

		Assert.AreSame(handle, started);
	}

	[TestMethod]
	public void Start_NullRequest_Throws()
	{
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => null));

		Assert.ThrowsExactly<ArgumentNullException>(() => runner.Start(null!));
	}

	private static ProcessRunRequest CreateRequest(
		string? workingDirectory = null,
		bool useShellExecute = false,
		IReadOnlyDictionary<string, string>? environment = null,
		Encoding? standardOutputEncoding = null,
		Encoding? standardErrorEncoding = null,
		bool redirectStandardOutput = false,
		bool redirectStandardError = false,
		TimeSpan? timeout = null)
	{
		return new ProcessRunRequest
		{
			FileName = @"C:\tools\compiler.exe",
			Arguments = "-compile",
			WorkingDirectory = workingDirectory,
			UseShellExecute = useShellExecute,
			EnvironmentVariables = environment ?? new Dictionary<string, string>(),
			StandardOutputEncoding = standardOutputEncoding,
			StandardErrorEncoding = standardErrorEncoding,
			RedirectStandardOutput = redirectStandardOutput,
			RedirectStandardError = redirectStandardError,
			Timeout = timeout
		};
	}

	private sealed class FakeProcessLauncher : IProcessLauncher
	{
		private readonly Func<ProcessRunRequest, IProcessHandle?> _start;

		public FakeProcessLauncher(Func<ProcessRunRequest, IProcessHandle?> start)
			=> _start = start;

		public ProcessRunRequest? LastRequest { get; private set; }

		public IProcessHandle? Start(ProcessRunRequest request)
		{
			LastRequest = request;
			return _start(request);
		}
	}

	private sealed class FakeProcessHandle : IProcessHandle
	{
		private readonly Action? _onWaitForExit;
		private readonly Func<int, bool>? _onTimedWaitForExit;
		private readonly Action? _onKillEntireProcessTree;
		private readonly Action? _onKill;

		public FakeProcessHandle(
			int exitCode = 0,
			string? standardOutput = null,
			string? standardError = null,
			Action? onWaitForExit = null,
			Func<int, bool>? onTimedWaitForExit = null,
			Action? onKillEntireProcessTree = null,
			Action? onKill = null)
		{
			ExitCode = exitCode;
			StandardOutput = standardOutput;
			StandardError = standardError;
			_onWaitForExit = onWaitForExit;
			_onTimedWaitForExit = onTimedWaitForExit;
			_onKillEntireProcessTree = onKillEntireProcessTree;
			_onKill = onKill;
		}

		public int ExitCode { get; }

		public string? StandardOutput { get; }

		public string? StandardError { get; }

		public bool WaitForExitCalled { get; private set; }

		public int TimedWaitForExitCalls { get; private set; }

		public int KillCalls { get; private set; }

		public int KillEntireProcessTreeCalls { get; private set; }

		public bool Disposed { get; private set; }

		public void WaitForExit()
		{
			WaitForExitCalled = true;
			_onWaitForExit?.Invoke();
		}

		public bool WaitForExit(int timeoutMilliseconds)
		{
			TimedWaitForExitCalls++;
			return _onTimedWaitForExit?.Invoke(timeoutMilliseconds) ?? true;
		}

		public void Kill()
		{
			KillCalls++;
			_onKill?.Invoke();
		}

		public void KillEntireProcessTree()
		{
			KillEntireProcessTreeCalls++;
			_onKillEntireProcessTree?.Invoke();
		}

		public void Dispose()
			=> Disposed = true;
	}
}

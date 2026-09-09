using Microsoft.Extensions.Logging;
using StreamJsonRpc;
using System.Diagnostics;

namespace Nickelony.LanguageServer.Client.Tests;

public partial class LanguageServerClientTests
{
	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public void Dispose_WritesGracefulShutdownMessagesAndLogsGracefulAttempt()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		using Process process = StartDisposableProcess();
		using var serverOutputStream = new PendingReadStream();
		using var serverInputStream = new RecordingStream();
		using var client = new LanguageServerClient([@"C:\Workspace"], process.StartInfo.FileName, s_defaultClientOptions, logScope.CreateLogger<LanguageServerClient>());
		LanguageServerTransportSession session = CreateTransportSession(client, 1, process, serverOutputStream, serverInputStream, startListening: true);

		SetActiveSession(client, session);

		client.Dispose();

		string writtenPayload = serverInputStream.GetWrittenText();

		Assert.IsTrue(writtenPayload.Contains("\"method\":\"shutdown\"", StringComparison.Ordinal), writtenPayload);
		Assert.IsTrue(writtenPayload.Contains("\"method\":\"exit\"", StringComparison.Ordinal), writtenPayload);

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Info|", StringComparison.Ordinal)
			&& log.Contains("Attempting graceful shutdown", StringComparison.Ordinal)
			&& log.Contains("generation 1", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task DisposeAsync_WritesGracefulShutdownMessages()
	{
		using Process process = StartDisposableProcess();
		using var serverOutputStream = new PendingReadStream();
		using var serverInputStream = new RecordingStream();
		await using var client = new LanguageServerClient([@"C:\Workspace"], process.StartInfo.FileName, s_defaultClientOptions);
		LanguageServerTransportSession session = CreateTransportSession(client, 1, process, serverOutputStream, serverInputStream, startListening: true);

		SetActiveSession(client, session);

		await client.DisposeAsync().ConfigureAwait(false);

		string writtenPayload = serverInputStream.GetWrittenText();

		Assert.IsTrue(writtenPayload.Contains("\"method\":\"shutdown\"", StringComparison.Ordinal), writtenPayload);
		Assert.IsTrue(writtenPayload.Contains("\"method\":\"exit\"", StringComparison.Ordinal), writtenPayload);
	}

	[TestMethod]
	public async Task Dispose_AndDisposeAsync_DisposeStartLockAfterCleanup()
	{
		for (int i = 0; i < 2; i++)
		{
			var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
			SemaphoreSlim startLock = client.StartLock;

			if (i == 0)
				client.Dispose();
			else
				await client.DisposeAsync().ConfigureAwait(false);

			Assert.ThrowsExactly<ObjectDisposedException>(() => startLock.Wait(0));
		}
	}

	[TestMethod]
	public async Task StartAsync_DisposeDuringStartupWait_ReturnsFalseWithoutObjectDisposedException()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		SemaphoreSlim startLock = client.StartLock;

		startLock.Wait();

		try
		{
			Task<bool> startTask = client.StartAsync(CancellationToken.None);

			// StartAsync runs synchronously up to the startup-gate wait, so disposal here deterministically races a
			// blocked startup attempt without an extra delay.
			client.Dispose();

			Assert.IsFalse(await startTask.ConfigureAwait(false));
		}
		finally
		{
			startLock.Release();
		}
	}

	[TestMethod]
	public async Task StartAsync_WhenDisposalAlreadyBegan_DoesNotReportReadySuccess()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		LanguageServerTransportSession session = CreateTransportSession(client, 1, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		bool disposeStarted = client.TryBeginDispose();

		Assert.IsTrue(disposeStarted);
		Assert.IsTrue(client.IsReady, "The regression test expects readiness to still be visible immediately after disposal begins.");

		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () =>
			await client.StartAsync(CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task StartAsync_WhenDisposalWinsBeforeTransportAttachment_TerminatesSpawnedProcess()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		var processStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowStartupToContinue = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		int? startedProcessId = null;

		var options = new LanguageServerClientOptions(static () => new { })
		{
			ShutdownRequestTimeout = TimeSpan.FromMilliseconds(1500),
			DisposeWaitTimeout = TimeSpan.FromMilliseconds(2000),
			// ping keeps the child alive without reading stdin, so only forced termination can end it.
			ServerArguments = ["/c", "ping 127.0.0.1 -n 30 > nul"]
		};

		await using var client = new LanguageServerClient(
			[@"C:\Workspace"],
			Path.Combine(Environment.SystemDirectory, "cmd.exe"),
			options,
			logScope.CreateLogger<LanguageServerClient>(),
			processStartedTestHook: async (process, _) =>
			{
				startedProcessId = process.Id;
				processStarted.TrySetResult(true);

				await allowStartupToContinue.Task.ConfigureAwait(false);
			});

		Task<bool> startTask = client.StartAsync(CancellationToken.None);

		await processStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		// Disposal wins the race while the process hook is still holding startup: the session is never configured,
		// so teardown must terminate the live process directly instead of leaving it running.
		Assert.IsTrue(client.TryBeginDispose());
		allowStartupToContinue.TrySetResult(true);

		Assert.IsFalse(await startTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
		Assert.IsNotNull(startedProcessId);
		Assert.IsTrue(await WaitForProcessExitAsync(startedProcessId.Value).ConfigureAwait(false));

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Info|", StringComparison.Ordinal)
			&& log.Contains("because disposal reached the session before its transport was attached", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task StartAsync_WhenStartupFailsBeforeSessionActivation_KillsSpawnedProcess()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		Process? startedProcess = null;
		int? startedProcessId = null;

		using var client = new LanguageServerClient(
			[@"C:\Workspace"],
			Path.Combine(Environment.SystemDirectory, "cmd.exe"),
			s_defaultClientOptions,
			logScope.CreateLogger<LanguageServerClient>(),
			processStartedTestHook: async (process, _) =>
			{
				startedProcess = process;
				startedProcessId = process.Id;
				throw new InvalidOperationException("Simulated startup failure before session activation.");
			});

		bool started = await client.StartAsync(CancellationToken.None).ConfigureAwait(false);

		Assert.IsFalse(started);
		Assert.IsNotNull(startedProcess);
		Assert.IsNotNull(startedProcessId);
		Assert.IsTrue(await WaitForProcessExitAsync(startedProcessId.Value).ConfigureAwait(false));
		Assert.IsFalse(client.IsReady);

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("Startup cleanup is forcing language server process termination", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task StartAsync_WhenCallerCancelsBeforeSessionActivation_CleansStartupProcessAndRethrows()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		using var startupCancellation = new CancellationTokenSource();
		var processStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowStartupToContinue = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		int? startedProcessId = null;

		using var client = new LanguageServerClient(
			[@"C:\Workspace"],
			Path.Combine(Environment.SystemDirectory, "cmd.exe"),
			s_defaultClientOptions,
			logScope.CreateLogger<LanguageServerClient>(),
			processStartedTestHook: async (process, cancellationToken) =>
			{
				startedProcessId = process.Id;
				processStarted.TrySetResult(true);

				Task completedTask = await Task.WhenAny(
					allowStartupToContinue.Task,
					Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)).ConfigureAwait(false);

				if (!ReferenceEquals(completedTask, allowStartupToContinue.Task))
					cancellationToken.ThrowIfCancellationRequested();

				await allowStartupToContinue.Task.ConfigureAwait(false);
			});

		Task<bool> startTask = client.StartAsync(startupCancellation.Token);

		await processStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		startupCancellation.Cancel();
		allowStartupToContinue.TrySetResult(true);

		await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
			await startTask.ConfigureAwait(false)).ConfigureAwait(false);

		Assert.IsNotNull(startedProcessId);
		Assert.IsTrue(await WaitForProcessExitAsync(startedProcessId.Value).ConfigureAwait(false));
		Assert.IsFalse(client.IsReady);

		Assert.IsTrue(logScope.Logs.Any(log =>
			(log.StartsWith("Debug|", StringComparison.Ordinal)
				&& log.Contains("after cancellation before session activation completed", StringComparison.OrdinalIgnoreCase))
			|| (log.StartsWith("Info|", StringComparison.Ordinal)
				&& log.Contains("Attempting graceful shutdown", StringComparison.OrdinalIgnoreCase))),
			string.Join(Environment.NewLine, logScope.Logs));

		Assert.IsFalse(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("Startup cleanup", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task StartAsync_WhenClientIsDisposedBeforeSessionActivation_CleansStartupProcessAndReturnsFalse()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		var processStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowStartupToContinue = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		int? startedProcessId = null;

		using var client = new LanguageServerClient(
			[@"C:\Workspace"],
			Path.Combine(Environment.SystemDirectory, "cmd.exe"),
			s_defaultClientOptions,
			logScope.CreateLogger<LanguageServerClient>(),
			processStartedTestHook: async (process, cancellationToken) =>
			{
				startedProcessId = process.Id;
				processStarted.TrySetResult(true);

				Task completedTask = await Task.WhenAny(
					allowStartupToContinue.Task,
					Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)).ConfigureAwait(false);

				if (!ReferenceEquals(completedTask, allowStartupToContinue.Task))
					cancellationToken.ThrowIfCancellationRequested();

				await allowStartupToContinue.Task.ConfigureAwait(false);
			});

		Task<bool> startTask = client.StartAsync(CancellationToken.None);

		await processStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		client.Dispose();
		allowStartupToContinue.TrySetResult(true);

		Assert.IsFalse(await startTask.ConfigureAwait(false));
		Assert.IsNotNull(startedProcessId);
		Assert.IsTrue(await WaitForProcessExitAsync(startedProcessId.Value).ConfigureAwait(false));
		Assert.IsFalse(client.IsReady);

		Assert.IsTrue(logScope.Logs.Any(log =>
			(log.StartsWith("Debug|", StringComparison.Ordinal)
				&& log.Contains("after cancellation before session activation completed", StringComparison.OrdinalIgnoreCase))
			|| (log.StartsWith("Info|", StringComparison.Ordinal)
				&& log.Contains("Attempting graceful shutdown", StringComparison.OrdinalIgnoreCase))),
			string.Join(Environment.NewLine, logScope.Logs));

		Assert.IsFalse(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("Startup cleanup", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task DisposeStartupSessionResourcesAsync_WhenCancellationIsExpected_LogsDebugWithoutWarning()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		using Process process = StartDisposableProcess();
		using var client = new LanguageServerClient([@"C:\Workspace"], process.StartInfo.FileName, s_defaultClientOptions, logScope.CreateLogger<LanguageServerClient>());
		int processId = process.Id;

		await client.TransportHost.DisposeStartupSessionResourcesAsync(null, process, true).ConfigureAwait(false);

		Assert.IsTrue(await WaitForProcessExitAsync(processId).ConfigureAwait(false));

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Debug|", StringComparison.Ordinal)
			&& log.Contains("after cancellation before session activation completed", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));

		Assert.IsFalse(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("Startup cleanup", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task Dispose_WhenShutdownRequestTimesOut_LogsForcedTermination()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		using Process process = StartDisposableProcess();
		int processId = process.Id;
		using var serverOutputStream = new PendingReadStream();
		using var serverInputStream = new RecordingStream();
		using var client = new LanguageServerClient([@"C:\Workspace"], process.StartInfo.FileName, s_defaultClientOptions, logScope.CreateLogger<LanguageServerClient>());
		LanguageServerTransportSession session = CreateTransportSession(client, 7, process, serverOutputStream, serverInputStream, startListening: true);

		SetActiveSession(client, session);
		RecordStandardErrorLine(session, "The example language server did not respond to shutdown.");

		client.Dispose();

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("did not acknowledge shutdown", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("generation 7", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("forcing process termination", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("generation 7", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("recent language server stderr", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("The example language server did not respond to shutdown.", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));

		// The warning must correspond to a real forced termination, not just a logged line.
		Assert.IsTrue(await WaitForProcessExitAsync(processId).ConfigureAwait(false));
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task Dispose_WhenShutdownAcknowledgesWithinConfiguredBudget_DoesNotLogTimeoutOrForcedTermination()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		// Use a long-lived process and a persistent response stream so the graceful acknowledgment path runs to
		// completion; a short-lived process or an ending stream would let the assertions pass vacuously.
		using Process process = StartDisposableProcess();
		int processId = process.Id;

		using var serverOutputStream = new DeferredPersistentJsonRpcResponseStream();
		using var serverInputStream = new RecordingStream();

		using var client = new LanguageServerClient([@"C:\Workspace"], process.StartInfo.FileName, new LanguageServerClientOptions(static () => new { })
		{
			ShutdownRequestTimeout = TimeSpan.FromSeconds(5),
			DisposeWaitTimeout = TimeSpan.FromSeconds(1)
		}, logScope.CreateLogger<LanguageServerClient>());

		LanguageServerTransportSession session = CreateTransportSession(client, 8, process, serverOutputStream, serverInputStream, startListening: true);

		SetActiveSession(client, session);

		Task disposeTask = Task.Run(client.Dispose);

		int shutdownRequestId = await WaitForRequestIdAsync(serverInputStream).ConfigureAwait(false);
		serverOutputStream.SetPayload(CreateJsonRpcResultMessage(shutdownRequestId, resultJson: "null"));

		await disposeTask.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

		Assert.IsTrue(await WaitForProcessExitAsync(processId).ConfigureAwait(false));
		Assert.IsTrue(serverInputStream.GetWrittenText().Contains("\"exit\"", StringComparison.Ordinal),
			"Expected the exit notification to be sent after the shutdown acknowledgment.");

		Assert.IsFalse(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("did not acknowledge shutdown", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public void Dispose_WhenShutdownRequestTimesOut_UsesConfiguredTimeoutInLog()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		using Process process = StartDisposableProcess();
		using var serverOutputStream = new PendingReadStream();
		using var serverInputStream = new RecordingStream();

		using var client = new LanguageServerClient([@"C:\Workspace"], process.StartInfo.FileName, new LanguageServerClientOptions(static () => new { })
		{
			ShutdownRequestTimeout = TimeSpan.FromMilliseconds(50)
		}, logScope.CreateLogger<LanguageServerClient>());

		LanguageServerTransportSession session = CreateTransportSession(client, 9, process, serverOutputStream, serverInputStream, startListening: true);

		SetActiveSession(client, session);

		client.Dispose();

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("did not acknowledge shutdown", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("within 50 ms", StringComparison.Ordinal)
			&& log.Contains("generation 9", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public async Task DisposeStartLockAsync_WhenStartupGateStaysBusy_LogsTimeoutWithoutDisposingGate()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions, logScope.CreateLogger<LanguageServerClient>());
		SemaphoreSlim startLock = client.StartLock;
		bool reacquiredStartLock = false;

		startLock.Wait();

		try
		{
			await client.DisposeStartLockAsync(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
		}
		finally
		{
			startLock.Release();
		}

		try
		{
			reacquiredStartLock = startLock.Wait(0);
			Assert.IsTrue(reacquiredStartLock);
		}
		finally
		{
			if (reacquiredStartLock)
				startLock.Release();
		}

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("startup gate did not become available", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("within 50 ms", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task StartAsync_WhenCanceledDuringHandshake_DetachesPublishedSessionAndLeavesClientNotReady()
	{
		var sessionActivated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var startupCancellation = new CancellationTokenSource();

		await using var client = new LanguageServerClient(
			[@"C:\Workspace"],
			Path.Combine(Environment.SystemDirectory, "cmd.exe"),
			s_defaultClientOptions,
			null,
			processStartedTestHook: null,
			sessionActivatedTestHook: cancellationToken => WaitForStartupCancellationAsync(sessionActivated, cancellationToken));

		Task<bool> startTask = client.StartAsync(startupCancellation.Token);

		await sessionActivated.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		startupCancellation.Cancel();

		await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await startTask.ConfigureAwait(false)).ConfigureAwait(false);

		Assert.IsFalse(client.IsReady);
		Assert.IsNull(client.CapabilityStore.ActiveSession);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task StartAsync_WhenInitializationTimesOut_UsesConfiguredTimeoutAndLeavesClientNotReady()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		var initializeStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		using var client = new LanguageServerClient(
			[@"C:\Workspace"],
			Path.Combine(Environment.SystemDirectory, "cmd.exe"),
			new LanguageServerClientOptions(static () => new { })
			{
				InitializeTimeout = TimeSpan.FromMilliseconds(50)
			},
			logScope.CreateLogger<LanguageServerClient>(),
			processStartedTestHook: null,
			sessionActivatedTestHook: null,
			beforeInitializeRequestTestHook: async cancellationToken =>
			{
				initializeStarted.TrySetResult(true);
				await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
			});

		Task<bool> startTask = client.StartAsync(CancellationToken.None);

		await initializeStarted.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

		bool started = await startTask.ConfigureAwait(false);

		Assert.IsFalse(started);
		Assert.IsFalse(client.IsReady);

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("did not complete initialization within 50 ms", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void JsonRpc_Disconnected_UnexpectedDisconnect_LogsRecentStderrContext()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions, logScope.CreateLogger<LanguageServerClient>());
		LanguageServerTransportSession session = CreateTransportSession(client, 11, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);
		SetReadyState(client, true);
		RecordStandardErrorLine(session, "The example language server handshake failed near initialize.");

		client.TransportHost.HandleJsonRpcDisconnected(
			session,
			new JsonRpcDisconnectedEventArgs("stream closed", DisconnectedReason.StreamError));

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("disconnected unexpectedly", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("workspace", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("recreate the session", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("generation 11", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("recent language server stderr", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("The example language server handshake failed near initialize.", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void LanguageServerClientOptions_RejectsInvalidTimeoutValues()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new LanguageServerClientOptions(static () => new { })
		{
			InitializeTimeout = TimeSpan.Zero
		});

		ArgumentOutOfRangeException infiniteTimeoutException = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new LanguageServerClientOptions(static () => new { })
		{
			ShutdownRequestTimeout = Timeout.InfiniteTimeSpan
		});

		StringAssert.Contains(infiniteTimeoutException.Message, "Infinite timeouts are not supported");

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new LanguageServerClientOptions(static () => new { })
		{
			DisposeWaitTimeout = TimeSpan.FromMilliseconds(-1)
		});
	}

	[TestMethod]
	public void LanguageServerClientOptions_RejectsTimeoutsAboveThePlatformTimerLimit()
	{
		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new LanguageServerClientOptions(static () => new { })
		{
			InitializeTimeout = TimeSpan.FromDays(50)
		});

		StringAssert.Contains(exception.Message, "platform limit");
	}

	[TestMethod]
	public void LanguageServerClientOptions_ServerWorkingDirectory_DefaultsToNullAndRejectsBlankValues()
	{
		Assert.IsNull(new LanguageServerClientOptions(static () => new { }).ServerWorkingDirectory);

		Assert.ThrowsExactly<ArgumentException>(() => new LanguageServerClientOptions(static () => new { })
		{
			ServerWorkingDirectory = " "
		});

		Assert.AreEqual(@"C:\Workspace", new LanguageServerClientOptions(static () => new { })
		{
			ServerWorkingDirectory = @"C:\Workspace"
		}.ServerWorkingDirectory);
	}

	[TestMethod]
	public async Task Dispose_ConcurrentSyncAndAsyncCalls_DoNotFault()
	{
		// Repeat to cover scheduling interleavings between the three dispose callers; each iteration asserts
		// deterministic outcomes and holds no timing assumption.
		for (int i = 0; i < 25; i++)
		{
			await using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
			LanguageServerTransportSession session = CreateTransportSession(client, i + 1, process: null, Stream.Null, Stream.Null, startListening: true);

			SetActiveSession(client, session);

			Task[] disposeTasks =
			[
				Task.Run(client.Dispose),
				Task.Run(async () => await client.DisposeAsync().ConfigureAwait(false)),
				Task.Run(client.Dispose)
			];

			await Task.WhenAll(disposeTasks).ConfigureAwait(false);

			await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() =>
				client.SendNotificationAsync("workspace/didChangeConfiguration", new { settings = new { } }, CancellationToken.None))
				.ConfigureAwait(false);
		}
	}

	[TestMethod]
	public void Dispose_UsesOneOverallDisposeBudgetAcrossTeardownStages()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", new LanguageServerClientOptions(static () => new { })
		{
			DisposeWaitTimeout = TimeSpan.FromMilliseconds(100)
		});

		LanguageServerTransportSession session = CreateTransportSession(client, 1, process: null, Stream.Null, Stream.Null);
		var pendingRpcCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var pendingStderrLoop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		SemaphoreSlim startLock = client.StartLock;

		session.RpcCompletionTask = pendingRpcCompletion.Task;
		session.StderrLoopTask = pendingStderrLoop.Task;
		SetActiveSession(client, session);

		startLock.Wait();
		var stopwatch = Stopwatch.StartNew();

		try
		{
			client.Dispose();
		}
		finally
		{
			stopwatch.Stop();

			try
			{
				startLock.Release();
			}
			catch (ObjectDisposedException)
			{ }
			catch (SemaphoreFullException)
			{ }
		}

		// Wall-clock bound: a generous multiple of the 100 ms budget, so it only fails red when teardown ignores
		// the shared budget entirely instead of measuring accurate timing.
		Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromMilliseconds(1000),
			$"Dispose should honor a single overall budget (100 ms), but took {stopwatch.Elapsed.TotalMilliseconds:F0} ms.");
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task StartAsync_InterleavedWithDisposeAsync_DoesNotLeaveReadyClientReachable()
	{
		// Repeat to cover interleavings between startup and dispose; the real cmd.exe process and the 5 s waits are
		// upper bounds that only fail red when startup or teardown never completes.
		for (int i = 0; i < 10; i++)
		{
			var sessionActivated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

			await using var client = new LanguageServerClient(
				[@"C:\Workspace"],
				Path.Combine(Environment.SystemDirectory, "cmd.exe"),
				s_defaultClientOptions,
				null,
				processStartedTestHook: null,
				sessionActivatedTestHook: cancellationToken => WaitForStartupCancellationAsync(sessionActivated, cancellationToken));

			Task<bool> startTask = client.StartAsync(CancellationToken.None);

			await sessionActivated.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

			await client.DisposeAsync().ConfigureAwait(false);

			Assert.IsFalse(await startTask.ConfigureAwait(false));
			Assert.IsFalse(client.IsReady);
			Assert.ThrowsExactly<ObjectDisposedException>(() => client.StartLock.Wait(0));

			await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() =>
				client.SendNotificationAsync("workspace/didChangeConfiguration", new { settings = new { } }, CancellationToken.None))
				.ConfigureAwait(false);
		}
	}
}

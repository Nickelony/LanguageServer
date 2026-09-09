using System.Collections.Concurrent;

namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class LatestRequestCoordinatorTests
{
	[TestMethod]
	public async Task RunAsync_AppliesLatestResult()
	{
		var coordinator = new LatestRequestCoordinator();
		int appliedValue = 0;

		RequestOutcome outcome = await coordinator.RunAsync(
			21,
			static (state, token) => Task.FromResult(state * 2),
			static (state, result) => true,
			result => appliedValue = result);

		Assert.AreEqual(RequestOutcome.Completed, outcome);
		Assert.AreEqual(42, appliedValue);
	}

	[TestMethod]
	[Timeout(30_000)]
	public async Task RunAsync_SupersedesEarlierRequest()
	{
		var coordinator = new LatestRequestCoordinator();
		var firstStart = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondStart = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
		int appliedCount = 0;

		Task<RequestOutcome> first = coordinator.RunAsync(
			firstStart,
			static (state, token) => state.Task,
			static (state, result) => true,
			_ => appliedCount++);

		Task<RequestOutcome> second = coordinator.RunAsync(
			secondStart,
			static (state, token) => state.Task,
			static (state, result) => true,
			_ => appliedCount++);

		secondStart.SetResult(42);
		Assert.AreEqual(RequestOutcome.Completed, await second);

		firstStart.SetResult(1);
		Assert.AreEqual(RequestOutcome.Superseded, await first);
		Assert.AreEqual(1, appliedCount);
	}

	[TestMethod]
	public async Task RunAsync_NewerRequestStartedInsideCanApply_DoesNotRetractAppliedResult()
	{
		var coordinator = new LatestRequestCoordinator();
		var appliedValues = new List<int>();

		RequestOutcome outcome = await coordinator.RunAsync(
			1,
			static (state, token) => Task.FromResult(state),
			(state, result) =>
			{
				// A newer request starts inside the documented publication window; the result that is
				// already being applied is not retracted, so the host should keep its own state check
				// in canApply when requests can start concurrently with publication.
				_ = coordinator.RunAsync(
					2,
					static (state, token) => Task.FromResult(state * 10),
					static (state, result) => true,
					appliedValues.Add);

				return true;
			},
			appliedValues.Add);

		Assert.AreEqual(RequestOutcome.Completed, outcome);
		CollectionAssert.AreEqual(new[] { 20, 1 }, appliedValues);
	}

	[TestMethod]
	[Timeout(30_000)]
	public async Task RunAsync_InvalidatedRequestIsDiscarded()
	{
		var coordinator = new LatestRequestCoordinator();
		var start = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
		int appliedCount = 0;

		Task<RequestOutcome> request = coordinator.RunAsync(
			start,
			static (state, token) => state.Task,
			static (state, result) => true,
			_ => appliedCount++);

		coordinator.Invalidate();
		start.SetResult(42);

		Assert.AreEqual(RequestOutcome.Superseded, await request);
		Assert.AreEqual(0, appliedCount);
	}

	[TestMethod]
	[Timeout(30_000)]
	public async Task RunAsync_CancelPendingRequest_SignalsCancellationAndDiscardsResult()
	{
		var coordinator = new LatestRequestCoordinator();
		var start = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
		bool computeObservedCancellation = false;
		int appliedCount = 0;

		Task<RequestOutcome> request = coordinator.RunAsync(
			start,
			(state, token) =>
			{
				token.Register(() => computeObservedCancellation = true);
				return state.Task;
			},
			static (state, result) => true,
			_ => appliedCount++);

		coordinator.CancelPendingRequest();
		start.SetResult(42);

		Assert.AreEqual(RequestOutcome.Canceled, await request);
		Assert.IsTrue(computeObservedCancellation);
		Assert.AreEqual(0, appliedCount);
	}

	[TestMethod]
	[Timeout(30_000)]
	public async Task RunAsync_ThrowingCallbackOnSupersededRun_DoesNotFaultTheNewRequest()
	{
		var coordinator = new LatestRequestCoordinator();
		var firstCompute = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
		var callbackRegistered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		Task<RequestOutcome> first = coordinator.RunAsync(
			1,
			(state, token) =>
			{
				token.Register(static () => throw new InvalidOperationException("Cancellation callback failure."));
				callbackRegistered.SetResult();
				return firstCompute.Task;
			},
			static (state, result) => true,
			static _ => { });

		await callbackRegistered.Task;

		// Superseding cancels the first run, which runs its throwing callback. The aggregate must not
		// fault this new request or leak its cancellation source.
		RequestOutcome secondOutcome = await coordinator.RunAsync(
			2,
			static (state, token) => Task.FromResult(state * 10),
			static (state, result) => true,
			static _ => { });

		Assert.AreEqual(RequestOutcome.Completed, secondOutcome);

		firstCompute.SetResult(1);
		Assert.AreEqual(RequestOutcome.Superseded, await first);
	}

	[TestMethod]
	[Timeout(30_000)]
	public async Task CancelPendingRequest_ThrowingCallbackOnTheRun_DoesNotEscapeTheCancelCall()
	{
		var coordinator = new LatestRequestCoordinator();
		var computeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var start = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

		Task<RequestOutcome> request = coordinator.RunAsync(
			start,
			(state, token) =>
			{
				token.Register(static () => throw new InvalidOperationException("Cancellation callback failure."));
				computeStarted.SetResult();
				return state.Task;
			},
			static (state, result) => true,
			static _ => { });

		await computeStarted.Task;

		// Canceling runs the run token's throwing callback. The fault belongs to the run, so it must
		// be absorbed exactly like the supersede path absorbs it, not escape this call.
		coordinator.CancelPendingRequest();

		start.SetResult(42);

		Assert.AreEqual(RequestOutcome.Canceled, await request);
	}

	[TestMethod]
	[Timeout(30_000)]
	public async Task RunAsync_CallerTokenCancel_IsObservedByTheComputeDelegate()
	{
		var coordinator = new LatestRequestCoordinator();
		using var callerCancellation = new CancellationTokenSource();
		var computeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		bool computeObservedCancellation = false;

		Task<RequestOutcome> request = coordinator.RunAsync(
			1,
			(state, token) =>
			{
				computeStarted.SetResult();
				callerCancellation.Cancel();
				computeObservedCancellation = token.IsCancellationRequested;
				return Task.FromResult(state);
			},
			static (state, result) => true,
			static _ => { },
			cancellationToken: callerCancellation.Token);

		await computeStarted.Task;

		Assert.IsTrue(computeObservedCancellation);
		Assert.AreEqual(RequestOutcome.Canceled, await request);
	}

	[TestMethod]
	[Timeout(30_000)]
	public async Task RunAsync_CallerCancellationDiscardsResult()
	{
		var coordinator = new LatestRequestCoordinator();
		var start = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var cancellation = new CancellationTokenSource();
		int appliedCount = 0;

		Task<RequestOutcome> request = coordinator.RunAsync(
			start,
			static (state, token) => state.Task,
			static (state, result) => true,
			_ => appliedCount++,
			cancellationToken: cancellation.Token);

		cancellation.Cancel();
		start.SetResult(42);

		Assert.AreEqual(RequestOutcome.Canceled, await request);
		Assert.AreEqual(0, appliedCount);
	}

	[TestMethod]
	public async Task RunAsync_PreCanceledCallerToken_SkipsComputeAndReportsCanceled()
	{
		var coordinator = new LatestRequestCoordinator();
		using var cancellation = new CancellationTokenSource();
		int computeRuns = 0;

		cancellation.Cancel();

		RequestOutcome outcome = await coordinator.RunAsync(
			0,
			(state, token) =>
			{
				computeRuns++;
				return Task.FromResult(42);
			},
			static (state, result) => true,
			_ => { },
			cancellationToken: cancellation.Token);

		Assert.AreEqual(RequestOutcome.Canceled, outcome);
		Assert.AreEqual(0, computeRuns);
	}

	[TestMethod]
	public async Task RunAsync_CanApplyReceivesStateAndResult()
	{
		var coordinator = new LatestRequestCoordinator();
		int? observedState = null;
		int? observedResult = null;
		int appliedCount = 0;

		RequestOutcome outcome = await coordinator.RunAsync(
			7,
			static (state, token) => Task.FromResult(state * 3),
			(state, result) =>
			{
				observedState = state;
				observedResult = result;
				return true;
			},
			_ => appliedCount++);

		Assert.AreEqual(RequestOutcome.Completed, outcome);
		Assert.AreEqual(7, observedState);
		Assert.AreEqual(21, observedResult);
		Assert.AreEqual(1, appliedCount);
	}

	[TestMethod]
	public async Task RunAsync_CanApplyFalse_ReportsRejectedByCurrentState()
	{
		var coordinator = new LatestRequestCoordinator();
		int appliedCount = 0;

		RequestOutcome outcome = await coordinator.RunAsync(
			0,
			static (state, token) => Task.FromResult(42),
			static (state, result) => false,
			_ => appliedCount++);

		Assert.AreEqual(RequestOutcome.RejectedByCurrentState, outcome);
		Assert.AreEqual(0, appliedCount);
	}

	[TestMethod]
	public async Task RunAsync_PropagatesComputeException()
	{
		var coordinator = new LatestRequestCoordinator();

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
			coordinator.RunAsync<int, int>(
				0,
				static (state, token) => throw new InvalidOperationException("boom"),
				static (state, result) => true,
				_ => { }));
	}

	[TestMethod]
	public async Task RunAsync_OperationCanceledException_ReportsCanceled()
	{
		var coordinator = new LatestRequestCoordinator();

		RequestOutcome outcome = await coordinator.RunAsync<int, int>(
			0,
			static (state, token) => throw new OperationCanceledException(token),
			static (state, result) => true,
			_ => { });

		Assert.AreEqual(RequestOutcome.Canceled, outcome);
	}

	[TestMethod]
	[Timeout(30_000)]
	public async Task RunAsync_OperationCanceledException_AfterSupersedeReportsSuperseded()
	{
		var coordinator = new LatestRequestCoordinator();
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		Task<RequestOutcome> first = coordinator.RunAsync<int, int>(
			0,
			async (state, token) =>
			{
				await release.Task.ConfigureAwait(true);
				throw new OperationCanceledException(token);
			},
			static (state, result) => true,
			_ => { });

		Assert.AreEqual(RequestOutcome.Completed, await coordinator.RunAsync(
			1,
			static (state, token) => Task.FromResult(state),
			static (state, result) => true,
			_ => { }));

		// The delegate cancels because the second request canceled its token, so the run reports
		// the same outcome as a superseded run that returns normally.
		release.SetResult();

		Assert.AreEqual(RequestOutcome.Superseded, await first);
	}

	[TestMethod]
	public async Task RunAsync_ApplyOperationCanceled_Propagates()
	{
		var coordinator = new LatestRequestCoordinator();

		// A cancellation thrown by the host's apply delegate is a real failure, not a supersession.
		await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
			coordinator.RunAsync(
				0,
				static (state, token) => Task.FromResult(42),
				static (state, result) => true,
				_ => throw new OperationCanceledException()));
	}

	[TestMethod]
	public async Task RunAsync_CanApplyOperationCanceled_Propagates()
	{
		var coordinator = new LatestRequestCoordinator();

		await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
			coordinator.RunAsync(
				0,
				static (state, token) => Task.FromResult(42),
				static (state, result) => throw new OperationCanceledException(),
				_ => { }));
	}

	[TestMethod]
	[Timeout(30_000)]
	public async Task RunAsync_SupersedesEarlierRequestWithThreadPoolContinuation()
	{
		var coordinator = new LatestRequestCoordinator();
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		int appliedCount = 0;

		Task<RequestOutcome> first = coordinator.RunAsync<int, int>(
			0,
			async (state, token) =>
			{
				await release.Task.ConfigureAwait(true);
				return state;
			},
			static (state, result) => true,
			_ => appliedCount++);

		Task<RequestOutcome> second = coordinator.RunAsync<int, int>(
			1,
			static (state, token) => Task.Run(() => state, token),
			static (state, result) => true,
			_ => appliedCount++);

		Assert.AreEqual(RequestOutcome.Completed, await second);

		// The second request superseded the first and canceled its token, so the first completion
		// is discarded as soon as it observes the cancellation.
		release.SetResult();

		Assert.AreEqual(RequestOutcome.Superseded, await first);
		Assert.AreEqual(1, appliedCount);
	}

	[TestMethod]
	public async Task RunAsync_ConsecutiveRequestsApplyInOrder()
	{
		var coordinator = new LatestRequestCoordinator();
		var applied = new List<int>();

		Assert.AreEqual(RequestOutcome.Completed, await coordinator.RunAsync(
			1,
			static (state, token) => Task.FromResult(state),
			static (state, result) => true,
			applied.Add));

		Assert.AreEqual(RequestOutcome.Completed, await coordinator.RunAsync(
			2,
			static (state, token) => Task.FromResult(state),
			static (state, result) => true,
			applied.Add));

		Assert.AreEqual(RequestOutcome.Completed, await coordinator.RunAsync(
			3,
			static (state, token) => Task.FromResult(state),
			static (state, result) => true,
			applied.Add));

		CollectionAssert.AreEqual(new[] { 1, 2, 3 }, applied);
	}

	[TestMethod]
	[Timeout(30_000)]
	public async Task RunAsync_SupersedingRequestCancelsEarlierToken()
	{
		var coordinator = new LatestRequestCoordinator();
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		bool firstTokenObservedCancellation = false;

		Task<RequestOutcome> first = coordinator.RunAsync<int, int>(
			0,
			async (state, token) =>
			{
				token.Register(() => firstTokenObservedCancellation = true);
				await release.Task.ConfigureAwait(true);
				return state;
			},
			static (state, result) => true,
			_ => { });

		Assert.AreEqual(RequestOutcome.Completed, await coordinator.RunAsync(
			1,
			static (state, token) => Task.FromResult(state),
			static (state, result) => true,
			_ => { }));

		release.SetResult();

		Assert.AreEqual(RequestOutcome.Superseded, await first);
		Assert.IsTrue(firstTokenObservedCancellation);
	}

	[TestMethod]
	public void RunAsync_ConcurrentAdmissions_NeverApplyMoreThanOneWinner()
	{
		const int runCount = 16;

		var coordinator = new LatestRequestCoordinator();
		var applied = new List<int>();
		var outcomesByRun = new RequestOutcome[runCount];
		var barrier = new Barrier(runCount);
		var threads = new Thread[runCount];

		for (int i = 0; i < runCount; i++)
		{
			int runIndex = i;

			threads[runIndex] = new Thread(() =>
			{
				// Every run is admitted concurrently, so the latest-request check races across threads.
				barrier.SignalAndWait();

				outcomesByRun[runIndex] = coordinator.RunAsync(
					runIndex,
					static (state, token) => Task.FromResult(state),
					static (state, result) => true,
					value =>
					{
						lock (applied)
							applied.Add(value);
					}).GetAwaiter().GetResult();
			})
			{
				IsBackground = true,
				Name = $"LatestRequestCoordinator race {runIndex}"
			};

			threads[runIndex].Start();
		}

		foreach (Thread thread in threads)
			Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30.0)), "A concurrent request thread did not finish in time.");

		// The last admitted run is always eligible, so at least one run completes; every other run is
		// superseded without applying its result.
		Assert.IsTrue(outcomesByRun.Contains(RequestOutcome.Completed), "Expected at least one completed run.");

		for (int runIndex = 0; runIndex < runCount; runIndex++)
		{
			bool appliedByRun;

			lock (applied)
				appliedByRun = applied.Contains(runIndex);

			if (outcomesByRun[runIndex] == RequestOutcome.Completed)
				Assert.IsTrue(appliedByRun, $"Run {runIndex} reported Completed without applying its result.");
			else
			{
				Assert.AreEqual(RequestOutcome.Superseded, outcomesByRun[runIndex], $"Unexpected outcome for run {runIndex}.");
				Assert.IsFalse(appliedByRun, $"Run {runIndex} applied its result despite not completing.");
			}
		}
	}

	[TestMethod]
	[Timeout(30_000)]
	public async Task RunAsync_CancelPendingRequestRacingCompletion_ClassifiesConsistently()
	{
		const int iterations = 64;

		for (int iteration = 0; iteration < iterations; iteration++)
		{
			var coordinator = new LatestRequestCoordinator();
			var start = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
			int appliedCount = 0;

			Task<RequestOutcome> request = coordinator.RunAsync(
				start,
				static (state, token) => state.Task,
				static (state, result) => true,
				_ => Interlocked.Increment(ref appliedCount));

			// The cancel races the completing compute. Whichever wins, the classification must stay
			// consistent with whether the result was applied: a run that observes the cancel before its
			// publication checks reports Canceled and never applies, while a cancel that lands too late
			// leaves the run Completed.
			Task cancelTask = Task.Run(coordinator.CancelPendingRequest);
			start.SetResult(42);

			RequestOutcome outcome = await request;
			await cancelTask;

			if (outcome == RequestOutcome.Completed)
				Assert.AreEqual(1, appliedCount, $"Iteration {iteration}: a completed run must apply exactly once.");
			else
			{
				Assert.AreEqual(RequestOutcome.Canceled, outcome, $"Iteration {iteration}: an unapplied run must report cancellation.");
				Assert.AreEqual(0, appliedCount, $"Iteration {iteration}: a canceled run must not apply its result.");
			}
		}
	}

	[TestMethod]
	public async Task RunAsync_InvalidateInsideCanApply_DoesNotRetractAppliedResult()
	{
		var coordinator = new LatestRequestCoordinator();
		int appliedCount = 0;

		RequestOutcome outcome = await coordinator.RunAsync(
			0,
			static (state, token) => Task.FromResult(42),
			(state, result) =>
			{
				// Invalidation inside the documented publication window does not retract a result that is
				// already being published; only checks before the window treat it as supersession.
				coordinator.Invalidate();
				return true;
			},
			_ => appliedCount++);

		Assert.AreEqual(RequestOutcome.Completed, outcome);
		Assert.AreEqual(1, appliedCount);
	}

	[TestMethod]
	[Timeout(30_000)]
	public async Task RunAsync_InvalidateRacingCompletion_ClassifiesConsistently()
	{
		const int iterations = 64;

		for (int iteration = 0; iteration < iterations; iteration++)
		{
			var coordinator = new LatestRequestCoordinator();
			var start = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
			int appliedCount = 0;

			Task<RequestOutcome> request = coordinator.RunAsync(
				start,
				static (state, token) => state.Task,
				static (state, result) => true,
				_ => Interlocked.Increment(ref appliedCount));

			// The invalidation races the completing compute: a run that observes it before its
			// publication checks reports supersession without applying, while a late invalidation
			// leaves the run Completed.
			Task invalidateTask = Task.Run(coordinator.Invalidate);
			start.SetResult(42);

			RequestOutcome outcome = await request;
			await invalidateTask;

			if (outcome == RequestOutcome.Completed)
				Assert.AreEqual(1, appliedCount, $"Iteration {iteration}: a completed run must apply exactly once.");
			else
			{
				Assert.AreEqual(RequestOutcome.Superseded, outcome, $"Iteration {iteration}: an unapplied run must report supersession.");
				Assert.AreEqual(0, appliedCount, $"Iteration {iteration}: a superseded run must not apply its result.");
			}
		}
	}

	[TestMethod]
	[Timeout(30_000)]
	public void RunAsync_PublishesOnTheCapturedSynchronizationContext()
	{
		// The continuation, including canApply and apply, must run on the caller's captured
		// synchronization context (the only ConfigureAwait(true) in Core), so a host that starts a
		// request on a UI thread also publishes its result on that thread. The scenario runs on a
		// single-threaded pump so the assertion cannot be satisfied by an inline continuation.
		using var pump = new SingleThreadSynchronizationContext();
		using var completed = new ManualResetEventSlim();
		RequestOutcome outcome = default;
		int? canApplyThreadId = null;
		int? applyThreadId = null;
		Exception? failure = null;

		pump.Post(
			async _ =>
			{
				try
				{
					var coordinator = new LatestRequestCoordinator();
					var start = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

					Task<RequestOutcome> request = coordinator.RunAsync(
						start,
						static (state, token) => state.Task,
						(state, result) =>
						{
							canApplyThreadId = Environment.CurrentManagedThreadId;
							return true;
						},
						_ => applyThreadId = Environment.CurrentManagedThreadId);

					// The queued completion forces the continuation to marshal back to the context
					// instead of running inline on the completing thread.
					start.SetResult(42);

					outcome = await request;
				}
				catch (Exception exception)
				{
					failure = exception;
				}
				finally
				{
					completed.Set();
				}
			},
			null);

		Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(30.0)), "The request did not complete in time.");
		Assert.IsNull(failure, failure?.ToString());
		Assert.AreEqual(RequestOutcome.Completed, outcome);
		Assert.AreEqual(pump.ThreadId, canApplyThreadId);
		Assert.AreEqual(pump.ThreadId, applyThreadId);
	}

	[TestMethod]
	[Timeout(30_000)]
	public void RunAsync_ContinueOnCapturedContextFalse_ResumesOffTheCapturedContext()
	{
		// The documented thread-pool escape hatch: with continueOnCapturedContext set to false the
		// continuation - including canApply and apply - must not resume on the captured context.
		using var pump = new SingleThreadSynchronizationContext();
		using var completed = new ManualResetEventSlim();
		RequestOutcome outcome = default;
		int? canApplyThreadId = null;
		int? applyThreadId = null;
		Exception? failure = null;

		pump.Post(
			async _ =>
			{
				try
				{
					var coordinator = new LatestRequestCoordinator();
					var start = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

					Task<RequestOutcome> request = coordinator.RunAsync(
						start,
						static (state, token) => state.Task,
						(state, result) =>
						{
							canApplyThreadId = Environment.CurrentManagedThreadId;
							return true;
						},
						_ => applyThreadId = Environment.CurrentManagedThreadId,
						continueOnCapturedContext: false);

					start.SetResult(42);

					outcome = await request;
				}
				catch (Exception exception)
				{
					failure = exception;
				}
				finally
				{
					completed.Set();
				}
			},
			null);

		Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(30.0)), "The request did not complete in time.");
		Assert.IsNull(failure, failure?.ToString());
		Assert.AreEqual(RequestOutcome.Completed, outcome);
		Assert.AreNotEqual(pump.ThreadId, canApplyThreadId);
		Assert.AreNotEqual(pump.ThreadId, applyThreadId);
	}

	[TestMethod]
	public void BeginRequest_MintsACurrentRequestWithAnUncanceledToken()
	{
		var coordinator = new LatestRequestCoordinator();

		long requestId = coordinator.BeginRequest();

		Assert.AreNotEqual(0, requestId);
		Assert.IsTrue(coordinator.IsCurrent(requestId));
		Assert.IsFalse(coordinator.RequestCancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void IsCurrent_ZeroIdentifier_IsNeverCurrent()
	{
		var coordinator = new LatestRequestCoordinator();

		// The default identifier value cannot pass the check, even after a request was admitted.
		coordinator.BeginRequest();

		Assert.IsFalse(coordinator.IsCurrent(0));
	}

	[TestMethod]
	public void CanPublish_ReportsTheLatestUncanceledRequest()
	{
		var coordinator = new LatestRequestCoordinator();

		Assert.IsFalse(coordinator.CanPublish(0));
		Assert.IsFalse(coordinator.CanPublish(12345));

		long request = coordinator.BeginRequest();

		Assert.IsTrue(coordinator.CanPublish(request));

		coordinator.CancelPendingRequest();

		Assert.IsFalse(coordinator.CanPublish(request), "A canceled request must not be publishable.");

		long newer = coordinator.BeginRequest();

		Assert.IsFalse(coordinator.CanPublish(request));
		Assert.IsTrue(coordinator.CanPublish(newer));
	}

	[TestMethod]
	public void CanPublish_SupersededRequest_ReportsFalseEvenWhenTheNewerTokenIsLive()
	{
		var coordinator = new LatestRequestCoordinator();

		long first = coordinator.BeginRequest();
		long second = coordinator.BeginRequest();

		Assert.IsFalse(coordinator.RequestCancellationToken.IsCancellationRequested);
		Assert.IsFalse(coordinator.CanPublish(first), "The check must evaluate the token of the inspected request, not the latest one.");
		Assert.IsTrue(coordinator.CanPublish(second));
	}

	[TestMethod]
	public void BeginRequest_AdmissionPredicateRejects_ReturnsZeroAndLeavesStateUntouched()
	{
		var coordinator = new LatestRequestCoordinator();

		long first = coordinator.BeginRequest();
		CancellationToken firstToken = coordinator.RequestCancellationToken;

		long rejected = coordinator.BeginRequest(static () => false);

		Assert.AreEqual(0, rejected);
		Assert.IsTrue(coordinator.IsCurrent(first));
		Assert.IsFalse(firstToken.IsCancellationRequested, "A rejected admission must not supersede the outstanding request.");
		Assert.AreEqual(firstToken, coordinator.RequestCancellationToken);
	}

	[TestMethod]
	public void BeginRequest_AdmissionPredicateAccepts_AdmitsAndSupersedes()
	{
		var coordinator = new LatestRequestCoordinator();

		long first = coordinator.BeginRequest();
		CancellationToken firstToken = coordinator.RequestCancellationToken;

		long second = coordinator.BeginRequest(static () => true);

		Assert.AreNotEqual(0, second);
		Assert.IsTrue(firstToken.IsCancellationRequested);
		Assert.IsFalse(coordinator.IsCurrent(first));
		Assert.IsTrue(coordinator.IsCurrent(second));
	}

	[TestMethod]
	public void BeginRequest_NullAdmissionPredicate_Throws()
	{
		var coordinator = new LatestRequestCoordinator();

		Assert.ThrowsExactly<ArgumentNullException>(() => coordinator.BeginRequest(null!));
	}

	[TestMethod]
	public void CancelPendingRequest_BySupersededIdentifier_LeavesTheNewerRequestUntouched()
	{
		var coordinator = new LatestRequestCoordinator();

		long first = coordinator.BeginRequest();
		long second = coordinator.BeginRequest();

		coordinator.CancelPendingRequest(first);

		Assert.IsFalse(coordinator.RequestCancellationToken.IsCancellationRequested);
		Assert.IsTrue(coordinator.CanPublish(second));

		coordinator.CancelPendingRequest(second);

		Assert.IsTrue(coordinator.RequestCancellationToken.IsCancellationRequested);
		Assert.IsFalse(coordinator.CanPublish(second));
	}

	[TestMethod]
	public void RequestCancellationToken_BeforeAnyRequest_IsTheNoneToken()
	{
		var coordinator = new LatestRequestCoordinator();

		Assert.AreEqual(CancellationToken.None, coordinator.RequestCancellationToken);
	}

	[TestMethod]
	public void RequestCancellationToken_AfterCompletedRun_StaysRegistrable()
	{
		var coordinator = new LatestRequestCoordinator();

		RequestOutcome outcome = coordinator.RunAsync(
			0,
			static (state, _) => Task.FromResult(state),
			static (_, _) => true,
			static _ => { }).GetAwaiter().GetResult();

		Assert.AreEqual(RequestOutcome.Completed, outcome);

		// The exposed source is never disposed, so a late registration on a completed run is safe;
		// the callback does not run because the token was not canceled.
		bool callbackRan = false;
		using CancellationTokenRegistration registration = coordinator.RequestCancellationToken.Register(() => callbackRan = true);

		Assert.IsFalse(callbackRan);
	}

	[TestMethod]
	[Timeout(30_000)]
	public async Task RequestCancellationToken_CallerTokenCancellation_DoesNotCancelTheExposedToken()
	{
		var coordinator = new LatestRequestCoordinator();
		using var callerCancellation = new CancellationTokenSource();
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		Task<RequestOutcome> run = coordinator.RunAsync(
			0,
			async (state, _) =>
			{
				entered.SetResult();
				await release.Task.ConfigureAwait(true);
				return state;
			},
			static (_, _) => true,
			static _ => { },
			cancellationToken: callerCancellation.Token);

		await entered.Task;
		callerCancellation.Cancel();
		release.SetResult();

		// Canceling the caller token ends the run and classifies it as canceled, but the exposed
		// token belongs to the coordinator's own source and is not canceled by it.
		Assert.AreEqual(RequestOutcome.Canceled, await run);
		Assert.IsFalse(coordinator.RequestCancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void BeginRequest_SupersedesThePreviousRequestAndCancelsItsToken()
	{
		var coordinator = new LatestRequestCoordinator();

		long firstId = coordinator.BeginRequest();
		CancellationToken firstToken = coordinator.RequestCancellationToken;

		long secondId = coordinator.BeginRequest();
		CancellationToken secondToken = coordinator.RequestCancellationToken;

		Assert.IsTrue(firstToken.IsCancellationRequested);
		Assert.IsFalse(coordinator.IsCurrent(firstId));
		Assert.IsTrue(coordinator.IsCurrent(secondId));
		Assert.IsFalse(secondToken.IsCancellationRequested);
		Assert.AreEqual(secondToken, coordinator.RequestCancellationToken);
	}

	[TestMethod]
	public void CancelPendingRequest_KeepsTheRequestCurrentAndReportsTheCanceledToken()
	{
		var coordinator = new LatestRequestCoordinator();

		long requestId = coordinator.BeginRequest();
		CancellationToken token = coordinator.RequestCancellationToken;

		coordinator.CancelPendingRequest();

		// Canceling stops the work; it does not invalidate the request, and the canceled token stays
		// observable until a newer request begins.
		Assert.IsTrue(token.IsCancellationRequested);
		Assert.IsTrue(coordinator.IsCurrent(requestId));
		Assert.IsTrue(coordinator.RequestCancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void Invalidate_RejectsTheOutstandingRequestWithoutCancelingItsToken()
	{
		var coordinator = new LatestRequestCoordinator();

		long requestId = coordinator.BeginRequest();
		CancellationToken token = coordinator.RequestCancellationToken;

		coordinator.Invalidate();

		Assert.IsFalse(coordinator.IsCurrent(requestId));
		Assert.IsFalse(token.IsCancellationRequested);
	}

	[TestMethod]
	[Timeout(30_000)]
	public async Task BeginRequest_SupersedesAnOutstandingRun()
	{
		var coordinator = new LatestRequestCoordinator();
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var runStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		bool runTokenObservedCancellation = false;
		int appliedCount = 0;

		Task<RequestOutcome> run = coordinator.RunAsync<int, int>(
			0,
			async (state, token) =>
			{
				token.Register(() => runTokenObservedCancellation = true);
				runStarted.SetResult();
				await release.Task.ConfigureAwait(true);
				return state;
			},
			static (state, result) => true,
			_ => appliedCount++);

		await runStarted.Task;

		// A host-driven request supersedes the run: its token is canceled and its result is discarded.
		long requestId = coordinator.BeginRequest();

		release.SetResult();

		Assert.AreEqual(RequestOutcome.Superseded, await run);
		Assert.IsTrue(runTokenObservedCancellation);
		Assert.AreEqual(0, appliedCount);
		Assert.IsTrue(coordinator.IsCurrent(requestId));
	}

	[TestMethod]
	public async Task RunAsync_SupersedesAnOutstandingHostDrivenRequest()
	{
		var coordinator = new LatestRequestCoordinator();

		long requestId = coordinator.BeginRequest();
		CancellationToken ticketToken = coordinator.RequestCancellationToken;
		int appliedCount = 0;

		RequestOutcome outcome = await coordinator.RunAsync(
			42,
			static (state, token) => Task.FromResult(state),
			static (state, result) => true,
			_ => appliedCount++);

		Assert.AreEqual(RequestOutcome.Completed, outcome);
		Assert.AreEqual(1, appliedCount);
		Assert.IsTrue(ticketToken.IsCancellationRequested);
		Assert.IsFalse(coordinator.IsCurrent(requestId));
	}

	[TestMethod]
	public async Task RunAsync_CompletedRun_KeepsItsTokenObservable()
	{
		var coordinator = new LatestRequestCoordinator();
		CancellationToken observedToken = default;

		RequestOutcome outcome = await coordinator.RunAsync(
			42,
			(state, token) =>
			{
				observedToken = token;
				return Task.FromResult(state);
			},
			static (state, result) => true,
			_ => { });

		Assert.AreEqual(RequestOutcome.Completed, outcome);

		// The completed request's token remains the reported request token and was not canceled by the
		// completion itself; a later request supersedes it.
		Assert.AreEqual(observedToken, coordinator.RequestCancellationToken);
		Assert.IsFalse(coordinator.RequestCancellationToken.IsCancellationRequested);
	}

	// A single-threaded synchronization context that installs itself on its pump thread and runs
	// every posted callback there, so a captured-context continuation can only resume on that thread.
	private sealed class SingleThreadSynchronizationContext : SynchronizationContext, IDisposable
	{
		private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];
		private readonly Thread _thread;

		public SingleThreadSynchronizationContext()
		{
			_thread = new Thread(Pump) { IsBackground = true, Name = "Synchronization context pump" };
			_thread.Start();
		}

		public int ThreadId => _thread.ManagedThreadId;

		public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

		public override void Send(SendOrPostCallback d, object? state)
			=> throw new NotSupportedException("The pump context only supports asynchronous posts.");

		public void Dispose()
		{
			_queue.CompleteAdding();

			if (Environment.CurrentManagedThreadId != _thread.ManagedThreadId)
				_thread.Join();
		}

		private void Pump()
		{
			// The context must be current on the pump thread for a continuation to capture it.
			SetSynchronizationContext(this);

			foreach ((SendOrPostCallback callback, object? state) in _queue.GetConsumingEnumerable())
				callback(state);
		}
	}
}

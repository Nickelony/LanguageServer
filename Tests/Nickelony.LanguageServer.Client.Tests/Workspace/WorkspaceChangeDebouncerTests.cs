namespace Nickelony.LanguageServer.Client.Tests;

// Timing note: WorkspaceChangeDebouncer schedules real Timer callbacks from Environment.TickCount64, so this
// suite cannot inject a clock. Positive waits are generous upper bounds (they only fail red when a dispatch never
// arrives), and "must not have happened yet" windows are negative probes that cannot flake red.
[TestClass]
public sealed class WorkspaceChangeDebouncerTests
{
	[TestMethod]
	public async Task Queue_BurstOfChanges_DispatchesOnceAfterTheDebounceDelay()
	{
		int dispatchCount = 0;
		using var debouncer = new WorkspaceChangeDebouncer(
			TimeSpan.FromMilliseconds(50),
			TimeSpan.FromSeconds(30),
			() => Interlocked.Increment(ref dispatchCount));

		debouncer.Queue(@"C:\Workspace\a.ext", FileChangeKind.Created);
		debouncer.Queue(@"C:\Workspace\a.ext", FileChangeKind.Changed);
		debouncer.Queue(@"C:\Workspace\b.ext", FileChangeKind.Created);

		await TestWait.UntilAsync(() => Volatile.Read(ref dispatchCount) == 1, TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Assert.IsFalse(debouncer.IsEmpty);

		FileChangeBatch batch = debouncer.DrainBatch();

		Assert.AreEqual(2, batch.Count);
		Assert.IsTrue(debouncer.IsEmpty);
	}

	[TestMethod]
	public async Task Queue_ContinuousChangeStream_StillDispatchesWithinTheMaximumLatency()
	{
		int dispatchCount = 0;
		long firstDispatchTimestamp = 0;
		using var debouncer = new WorkspaceChangeDebouncer(
			TimeSpan.FromMilliseconds(50),
			TimeSpan.FromMilliseconds(120),
			() =>
			{
				Interlocked.Increment(ref dispatchCount);
				Interlocked.Exchange(ref firstDispatchTimestamp, Environment.TickCount64);
			});

		using var streamCancellation = new CancellationTokenSource();
		long firstQueuedTimestamp = Environment.TickCount64;

		Task changeStream = Task.Run(async () =>
		{
			while (!streamCancellation.IsCancellationRequested)
			{
				debouncer.Queue(@"C:\Workspace\a.ext", FileChangeKind.Changed);
				await Task.Delay(20).ConfigureAwait(false);
			}
		});

		await TestWait.UntilAsync(() => Volatile.Read(ref dispatchCount) >= 1, TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		streamCancellation.Cancel();
		await changeStream.ConfigureAwait(false);

		// The bound is generous (ten times the configured maximum) so CI jitter cannot flake it, while an
		// implementation that ignores the maximum dispatch delay still fails.
		long latency = Volatile.Read(ref firstDispatchTimestamp) - firstQueuedTimestamp;
		Assert.IsTrue(latency <= 10 * 120, $"The dispatch latency was {latency} ms; a continuous change stream must not exceed the maximum dispatch delay.");
	}

	[TestMethod]
	public async Task Queue_WhileARetryIsPending_DoesNotShortenTheScheduledRetry()
	{
		int dispatchCount = 0;
		using var debouncer = new WorkspaceChangeDebouncer(
			TimeSpan.FromMilliseconds(50),
			TimeSpan.FromSeconds(30),
			() => Interlocked.Increment(ref dispatchCount));

		debouncer.Queue(@"C:\Workspace\a.ext", FileChangeKind.Changed);
		FileChangeBatch batch = debouncer.DrainBatch();

		debouncer.Requeue(batch, TimeSpan.FromMilliseconds(2000));

		// A new change must not turn the pending retry backoff into a fresh (shorter) debounce window.
		debouncer.Queue(@"C:\Workspace\b.ext", FileChangeKind.Changed);

		// The window is four times the fresh-debounce delay and a small fraction of the requeue delay, so a
		// starved thread cannot turn the negative assertion into a red flake.
		await Task.Delay(TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);

		Assert.AreEqual(0, dispatchCount);

		await TestWait.UntilAsync(() => Volatile.Read(ref dispatchCount) >= 1, TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		// The scheduled retry must deliver the replayed change and the change that arrived during the backoff.
		FileChangeBatch retriedBatch = debouncer.DrainBatch();

		Assert.AreEqual(2, retriedBatch.Count);
		Assert.AreEqual(@"C:\Workspace\a.ext", retriedBatch.Entries[0].Path);
		Assert.AreEqual(@"C:\Workspace\b.ext", retriedBatch.Entries[1].Path);
	}

	[TestMethod]
	public async Task Requeue_DrainedBatch_DispatchesAfterTheExplicitDelay()
	{
		var dispatched = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var debouncer = new WorkspaceChangeDebouncer(TimeSpan.FromHours(1), TimeSpan.FromHours(2), () => dispatched.TrySetResult(true));

		debouncer.Queue(@"C:\Workspace\a.ext", FileChangeKind.Changed);
		FileChangeBatch batch = debouncer.DrainBatch();

		Assert.AreEqual(1, batch.Count);

		debouncer.Requeue(batch, TimeSpan.FromMilliseconds(50));

		await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task Stop_AfterQueuedChange_PreventsTheScheduledDispatch()
	{
		int dispatchCount = 0;
		using var debouncer = new WorkspaceChangeDebouncer(
			TimeSpan.FromMilliseconds(50),
			TimeSpan.FromSeconds(30),
			() => Interlocked.Increment(ref dispatchCount));

		debouncer.Queue(@"C:\Workspace\a.ext", FileChangeKind.Changed);
		debouncer.Stop();

		await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);

		Assert.AreEqual(0, dispatchCount);
		Assert.IsFalse(debouncer.IsEmpty);
	}

	[TestMethod]
	public async Task Dispose_ThenQueue_IgnoresChangesAndNeverDispatches()
	{
		int dispatchCount = 0;
		var debouncer = new WorkspaceChangeDebouncer(
			TimeSpan.FromMilliseconds(50),
			TimeSpan.FromSeconds(30),
			() => Interlocked.Increment(ref dispatchCount));

		debouncer.Dispose();
		debouncer.Queue(@"C:\Workspace\a.ext", FileChangeKind.Changed);

		await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);

		Assert.AreEqual(0, dispatchCount);
		Assert.IsTrue(debouncer.IsEmpty);
	}
}

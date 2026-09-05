namespace Nickelony.IDEKit.Core.Infrastructure.Tests;

[TestClass]
public sealed class LatestRequestCoordinatorTests
{
	[TestMethod]
	public async Task RunAsync_AppliesLatestResult()
	{
		var coordinator = new LatestRequestCoordinator();
		int appliedValue = 0;

		bool applied = await coordinator.RunAsync(
			21,
			static (state, token) => Task.FromResult(state * 2),
			static (state, result) => true,
			result => appliedValue = result);

		Assert.IsTrue(applied);
		Assert.AreEqual(42, appliedValue);
	}

	[TestMethod]
	public async Task RunAsync_SupersedesEarlierRequest()
	{
		var coordinator = new LatestRequestCoordinator();
		var firstStart = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondStart = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
		int appliedCount = 0;

		Task<bool> first = coordinator.RunAsync(
			firstStart,
			static (state, token) => state.Task,
			static (state, result) => true,
			_ => appliedCount++);

		Task<bool> second = coordinator.RunAsync(
			secondStart,
			static (state, token) => state.Task,
			static (state, result) => true,
			_ => appliedCount++);

		secondStart.SetResult(42);
		Assert.IsTrue(await second);

		firstStart.SetResult(1);
		Assert.IsFalse(await first);
		Assert.AreEqual(1, appliedCount);
	}

	[TestMethod]
	public async Task RunAsync_InvalidatedRequestIsDiscarded()
	{
		var coordinator = new LatestRequestCoordinator();
		var start = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
		int appliedCount = 0;

		Task<bool> request = coordinator.RunAsync(
			start,
			static (state, token) => state.Task,
			static (state, result) => true,
			_ => appliedCount++);

		coordinator.Invalidate();
		start.SetResult(42);

		Assert.IsFalse(await request);
		Assert.AreEqual(0, appliedCount);
	}

	[TestMethod]
	public async Task RunAsync_CancelPendingRequestSignalsCancellationAndDiscardsResult()
	{
		var coordinator = new LatestRequestCoordinator();
		var start = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
		bool computeObservedCancellation = false;
		int appliedCount = 0;

		Task<bool> request = coordinator.RunAsync(
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

		Assert.IsFalse(await request);
		Assert.IsTrue(computeObservedCancellation);
		Assert.AreEqual(0, appliedCount);
	}

	[TestMethod]
	public async Task RunAsync_CallerCancellationDiscardsResult()
	{
		var coordinator = new LatestRequestCoordinator();
		var start = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var cancellation = new CancellationTokenSource();
		int appliedCount = 0;

		Task<bool> request = coordinator.RunAsync(
			start,
			static (state, token) => state.Task,
			static (state, result) => true,
			_ => appliedCount++,
			cancellation.Token);

		cancellation.Cancel();
		start.SetResult(42);

		Assert.IsFalse(await request);
		Assert.AreEqual(0, appliedCount);
	}

	[TestMethod]
	public async Task RunAsync_CanApplyReceivesStateAndResult()
	{
		var coordinator = new LatestRequestCoordinator();
		int? observedState = null;
		int? observedResult = null;
		int appliedCount = 0;

		bool applied = await coordinator.RunAsync(
			7,
			static (state, token) => Task.FromResult(state * 3),
			(state, result) =>
			{
				observedState = state;
				observedResult = result;
				return true;
			},
			_ => appliedCount++);

		Assert.IsTrue(applied);
		Assert.AreEqual(7, observedState);
		Assert.AreEqual(21, observedResult);
		Assert.AreEqual(1, appliedCount);
	}

	[TestMethod]
	public async Task RunAsync_CanApplyFalseSkipsApply()
	{
		var coordinator = new LatestRequestCoordinator();
		int appliedCount = 0;

		bool applied = await coordinator.RunAsync(
			0,
			static (state, token) => Task.FromResult(42),
			static (state, result) => false,
			_ => appliedCount++);

		Assert.IsFalse(applied);
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
	public async Task RunAsync_OperationCanceledExceptionReturnsFalse()
	{
		var coordinator = new LatestRequestCoordinator();

		bool applied = await coordinator.RunAsync<int, int>(
			0,
			static (state, token) => throw new OperationCanceledException(token),
			static (state, result) => true,
			_ => { });

		Assert.IsFalse(applied);
	}

	[TestMethod]
	public async Task RunAsync_SupersedesEarlierThreadPoolRequest()
	{
		var coordinator = new LatestRequestCoordinator();
		int appliedCount = 0;

		Task<bool> first = coordinator.RunAsync<int, int>(
			0,
			static (state, token) => Task.Run(() => state, token),
			static (state, result) => true,
			_ => appliedCount++);

		Task<bool> second = coordinator.RunAsync<int, int>(
			1,
			static (state, token) => Task.Run(() => state, token),
			static (state, result) => true,
			_ => appliedCount++);

		Assert.IsTrue(await second);
		Assert.IsFalse(await first);
		Assert.AreEqual(1, appliedCount);
	}

	[TestMethod]
	public async Task RunAsync_ConsecutiveRequestsApplyInOrder()
	{
		var coordinator = new LatestRequestCoordinator();
		var applied = new List<int>();

		Assert.IsTrue(await coordinator.RunAsync(
			1,
			static (state, token) => Task.FromResult(state),
			static (state, result) => true,
			applied.Add));

		Assert.IsTrue(await coordinator.RunAsync(
			2,
			static (state, token) => Task.FromResult(state),
			static (state, result) => true,
			applied.Add));

		Assert.IsTrue(await coordinator.RunAsync(
			3,
			static (state, token) => Task.FromResult(state),
			static (state, result) => true,
			applied.Add));

		CollectionAssert.AreEqual(new[] { 1, 2, 3 }, applied);
	}
}

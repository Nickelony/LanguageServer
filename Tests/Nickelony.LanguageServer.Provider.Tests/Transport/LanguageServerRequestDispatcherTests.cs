using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class LanguageServerRequestDispatcherTests
{
	private static LanguageServerRequestDispatcher CreateDispatcher(
		FakeLanguageServerClient? client,
		Func<CancellationToken, Task<bool>>? ensureStartedAsync = null,
		int restartThreshold = 2,
		TimeSpan? requestTimeout = null,
		CancellationToken disposeToken = default,
		Func<bool>? isDisposedAccessor = null)
		=> new(
			@"C:\Workspace",
			client,
			requestTimeout ?? TimeSpan.FromMilliseconds(50),
			restartThreshold,
			isDisposedAccessor ?? (() => false),
			ensureStartedAsync ?? (_ => Task.FromResult(true)),
			NullLogger.Instance,
			disposeToken);

	private static Func<string, object, CancellationToken, Task<object?>> DelayUntilCanceled { get; } =
		async (_, _, cancellationToken) =>
		{
			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
			return null;
		};

	[TestMethod]
	public async Task SendAsync_WhenNoClientIsConfigured_ReturnsTheFallbackValueWithoutStarting()
	{
		bool ensureStartedCalled = false;
		LanguageServerRequestDispatcher dispatcher = CreateDispatcher(
			client: null,
			ensureStartedAsync: _ =>
			{
				ensureStartedCalled = true;
				return Task.FromResult(true);
			});

		string? result = await dispatcher.SendAsync("textDocument/hover", new object(), "fallback", CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual("fallback", result);
		Assert.IsFalse(ensureStartedCalled);
	}

	[TestMethod]
	public async Task SendAsync_WhenTheRequestSucceeds_ReturnsTheResponse()
	{
		var client = new FakeLanguageServerClient { TransportGeneration = 7 };
		client.SendRequestHandler = (_, _, _) => Task.FromResult<object?>("response");
		LanguageServerRequestDispatcher dispatcher = CreateDispatcher(client);

		string? result = await dispatcher.SendAsync("textDocument/hover", new object(), "fallback", CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual("response", result);
		Assert.AreEqual(1, client.SendRequestCount);
		Assert.AreEqual(0, client.MarkedUnhealthyGenerations.Count);
	}

	[TestMethod]
	public async Task SendAsync_WhenTheCallerTokenIsAlreadyCanceled_ThrowsOperationCanceled()
	{
		var client = new FakeLanguageServerClient();
		client.SendRequestHandler = DelayUntilCanceled;
		LanguageServerRequestDispatcher dispatcher = CreateDispatcher(client);

		using var cancellationTokenSource = new CancellationTokenSource();
		cancellationTokenSource.Cancel();

		await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
			await dispatcher.SendAsync<string>("textDocument/hover", new object(), "fallback", cancellationTokenSource.Token).ConfigureAwait(false)).ConfigureAwait(false);

		Assert.AreEqual(0, client.SendRequestCount);
	}

	[TestMethod]
	public async Task SendAsync_WhenTheCallerCancelsDuringTheRequest_ThrowsOperationCanceled()
	{
		var client = new FakeLanguageServerClient();
		client.SendRequestHandler = DelayUntilCanceled;
		LanguageServerRequestDispatcher dispatcher = CreateDispatcher(client, requestTimeout: TimeSpan.FromSeconds(30));

		using var cancellationTokenSource = new CancellationTokenSource();
		Task<string> requestTask = dispatcher.SendAsync("textDocument/hover", new object(), "fallback", cancellationTokenSource.Token);

		cancellationTokenSource.Cancel();

		await Assert.ThrowsAsync<OperationCanceledException>(async () =>
			await requestTask.ConfigureAwait(false)).ConfigureAwait(false);
		Assert.AreEqual(0, client.MarkedUnhealthyGenerations.Count);
	}

	[TestMethod]
	public async Task SendAsync_WhenTheProviderIsDisposedDuringTheRequest_ReturnsTheFallbackValue()
	{
		var client = new FakeLanguageServerClient();
		client.SendRequestHandler = DelayUntilCanceled;

		using var disposeTokenSource = new CancellationTokenSource();
		disposeTokenSource.Cancel();
		LanguageServerRequestDispatcher dispatcher = CreateDispatcher(client, disposeToken: disposeTokenSource.Token);

		string? result = await dispatcher.SendAsync("textDocument/hover", new object(), "fallback", CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual("fallback", result);
		Assert.AreEqual(0, client.MarkedUnhealthyGenerations.Count);
	}

	[TestMethod]
	public async Task SendAsync_WhenTheRequestTimesOut_ReturnsTheFallbackValueAndCountsOneTimeout()
	{
		var client = new FakeLanguageServerClient { TransportGeneration = 3 };
		client.SendRequestHandler = DelayUntilCanceled;
		LanguageServerRequestDispatcher dispatcher = CreateDispatcher(client, restartThreshold: 2);

		string? result = await dispatcher.SendAsync("textDocument/hover", new object(), "fallback", CancellationToken.None)
			.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Assert.AreEqual("fallback", result);
		Assert.AreEqual(1, client.SendRequestCount);
		Assert.AreEqual(0, client.MarkedUnhealthyGenerations.Count);
	}

	[TestMethod]
	public async Task SendAsync_WhenTimeoutsReachTheThreshold_MarksTheTransportUnhealthyOncePerGeneration()
	{
		var client = new FakeLanguageServerClient { TransportGeneration = 5 };
		client.SendRequestHandler = DelayUntilCanceled;
		LanguageServerRequestDispatcher dispatcher = CreateDispatcher(client, restartThreshold: 2);

		for (int i = 0; i < 3; i++)
		{
			await dispatcher.SendAsync("textDocument/hover", new object(), "fallback", CancellationToken.None)
				.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
		}

		CollectionAssert.AreEqual(new long[] { 5 }, client.MarkedUnhealthyGenerations);
	}

	[TestMethod]
	public async Task SendAsync_WhenTheMarkAttemptIsRejectedByTheClient_StillReturnsTheFallbackValue()
	{
		var client = new FakeLanguageServerClient { TransportGeneration = 5, TryMarkTransportUnhealthyResult = false };
		client.SendRequestHandler = DelayUntilCanceled;
		LanguageServerRequestDispatcher dispatcher = CreateDispatcher(client, restartThreshold: 1);

		string? result = await dispatcher.SendAsync("textDocument/hover", new object(), "fallback", CancellationToken.None)
			.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Assert.AreEqual("fallback", result);
		CollectionAssert.AreEqual(new long[] { 5 }, client.MarkedUnhealthyGenerations);
	}

	[TestMethod]
	public async Task SendAsync_WhenTimeoutsSpanTwoGenerations_CountsThemPerGeneration()
	{
		var client = new FakeLanguageServerClient { TransportGeneration = 5 };
		client.SendRequestHandler = DelayUntilCanceled;
		LanguageServerRequestDispatcher dispatcher = CreateDispatcher(client, restartThreshold: 2);

		await dispatcher.SendAsync("textDocument/hover", new object(), "fallback", CancellationToken.None)
			.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		client.TransportGeneration = 6;

		await dispatcher.SendAsync("textDocument/hover", new object(), "fallback", CancellationToken.None)
			.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Assert.AreEqual(0, client.MarkedUnhealthyGenerations.Count);
	}

	[TestMethod]
	public async Task SendAsync_WhenARejectionBreaksTheTimeoutStreak_DoesNotMarkTheTransportUnhealthy()
	{
		int callCount = 0;
		var client = new FakeLanguageServerClient { TransportGeneration = 5 };
		client.SendRequestHandler = (method, parameters, cancellationToken) =>
		{
			callCount++;

			return callCount == 2
				? Task.FromException<object?>(new LanguageServerRequestRejectedException(-32603, "Simulated rejection.", null))
				: DelayUntilCanceled(method, parameters, cancellationToken);
		};
		LanguageServerRequestDispatcher dispatcher = CreateDispatcher(client, restartThreshold: 2);

		// timeout -> rejection -> timeout: the rejection proves the transport is responsive, so the streak must
		// restart instead of reaching the threshold and forcing a transport restart.
		for (int i = 0; i < 3; i++)
		{
			await dispatcher.SendAsync("textDocument/hover", new object(), "fallback", CancellationToken.None)
				.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
		}

		Assert.AreEqual(3, client.SendRequestCount);
		Assert.AreEqual(0, client.MarkedUnhealthyGenerations.Count);
	}

	[TestMethod]
	public void Constructor_WithTooLargeRequestTimeout_ThrowsArgumentOutOfRangeException()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			CreateDispatcher(client: null, requestTimeout: TimeSpan.FromMilliseconds((double)int.MaxValue + 1)));
	}

	[TestMethod]
	public async Task ResetTimeoutTracking_ClearsTheConsecutiveTimeoutCount()
	{
		var client = new FakeLanguageServerClient { TransportGeneration = 5 };
		client.SendRequestHandler = DelayUntilCanceled;
		LanguageServerRequestDispatcher dispatcher = CreateDispatcher(client, restartThreshold: 2);

		await dispatcher.SendAsync("m", new object(), "fallback", CancellationToken.None)
			.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		dispatcher.ResetTimeoutTracking(5);

		await dispatcher.SendAsync("m", new object(), "fallback", CancellationToken.None)
			.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Assert.AreEqual(0, client.MarkedUnhealthyGenerations.Count);

		await dispatcher.SendAsync("m", new object(), "fallback", CancellationToken.None)
			.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		CollectionAssert.AreEqual(new long[] { 5 }, client.MarkedUnhealthyGenerations);
	}

	[TestMethod]
	public async Task SendAsync_WhenAnUnownedCancellationOccurs_ReturnsTheFallbackValueWithoutMarking()
	{
		var client = new FakeLanguageServerClient { TransportGeneration = 5 };
		client.SendRequestHandler = (_, _, _) => throw new OperationCanceledException();
		LanguageServerRequestDispatcher dispatcher = CreateDispatcher(client, restartThreshold: 1);

		string? result = await dispatcher.SendAsync("textDocument/hover", new object(), "fallback", CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual("fallback", result);
		Assert.AreEqual(0, client.MarkedUnhealthyGenerations.Count);
	}

	[TestMethod]
	public async Task SendAsync_WhenTheClientIsDisposedMidSend_ReturnsTheFallbackValue()
	{
		var client = new FakeLanguageServerClient();
		client.SendRequestHandler = (_, _, _) => throw new ObjectDisposedException(nameof(FakeLanguageServerClient));
		LanguageServerRequestDispatcher dispatcher = CreateDispatcher(client, isDisposedAccessor: () => true);

		string? result = await dispatcher.SendAsync("textDocument/hover", new object(), "fallback", CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual("fallback", result);
		Assert.AreEqual(1, client.SendRequestCount);
	}

	[TestMethod]
	public async Task SendAsync_WhenObjectDisposedIsRaisedWithoutDisposalState_PropagatesTheException()
	{
		var client = new FakeLanguageServerClient();
		client.SendRequestHandler = (_, _, _) => throw new ObjectDisposedException(nameof(FakeLanguageServerClient));
		LanguageServerRequestDispatcher dispatcher = CreateDispatcher(client);

		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () =>
			await dispatcher.SendAsync("textDocument/hover", new object(), "fallback", CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task SendAsync_WhenTheTransportChanged_RetriesOnceOnTheRefreshedGeneration()
	{
		var client = new FakeLanguageServerClient { TransportGeneration = 7 };
		int handlerCallCount = 0;

		client.SendRequestHandler = (_, _, _) =>
		{
			handlerCallCount++;

			if (handlerCallCount == 1)
				throw new LanguageServerTransportChangedException();

			return Task.FromResult<object?>("response");
		};

		int ensureStartedCount = 0;
		LanguageServerRequestDispatcher dispatcher = CreateDispatcher(client, ensureStartedAsync: _ =>
		{
			ensureStartedCount++;
			client.TransportGeneration = 8;
			return Task.FromResult(true);
		});

		string? result = await dispatcher.SendAsync("textDocument/hover", new object(), "fallback", CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual("response", result);
		Assert.AreEqual(2, client.SendRequestCount);
		Assert.AreEqual(1, ensureStartedCount);
		CollectionAssert.AreEqual(new long[] { 7, 8 }, client.SendRequestGenerations);
	}

	[TestMethod]
	public async Task SendAsync_WhenEnsureStartedFails_ReturnsTheFallbackValueWithoutRetrying()
	{
		var client = new FakeLanguageServerClient { TransportGeneration = 7 };
		client.SendRequestHandler = (_, _, _) => throw new LanguageServerTransportUnavailableException();
		LanguageServerRequestDispatcher dispatcher = CreateDispatcher(client, ensureStartedAsync: _ => Task.FromResult(false));

		string? result = await dispatcher.SendAsync("textDocument/hover", new object(), "fallback", CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual("fallback", result);
		Assert.AreEqual(1, client.SendRequestCount);
	}

	[TestMethod]
	public async Task SendAsync_WhenTheRetryAlsoFailsWithTransportUnavailable_ReturnsTheFallbackValue()
	{
		var client = new FakeLanguageServerClient { TransportGeneration = 7 };
		client.SendRequestHandler = (_, _, _) => throw new LanguageServerTransportUnavailableException();
		LanguageServerRequestDispatcher dispatcher = CreateDispatcher(client);

		string? result = await dispatcher.SendAsync("textDocument/hover", new object(), "fallback", CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual("fallback", result);
		Assert.AreEqual(2, client.SendRequestCount);
		Assert.AreEqual(0, client.MarkedUnhealthyGenerations.Count);
	}

	[TestMethod]
	public async Task SendAsync_WhenTheRetryTimesOut_RecordsTheTimeoutAgainstTheRefreshedGeneration()
	{
		var client = new FakeLanguageServerClient { TransportGeneration = 7 };
		int handlerCallCount = 0;

		client.SendRequestHandler = async (_, _, cancellationToken) =>
		{
			handlerCallCount++;

			if (handlerCallCount == 1)
				throw new LanguageServerTransportUnavailableException();

			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
			return null;
		};

		LanguageServerRequestDispatcher dispatcher = CreateDispatcher(client, restartThreshold: 1);

		string? result = await dispatcher.SendAsync("textDocument/hover", new object(), "fallback", CancellationToken.None)
			.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Assert.AreEqual("fallback", result);
		Assert.AreEqual(2, client.SendRequestCount);
		CollectionAssert.AreEqual(new long[] { 7 }, client.MarkedUnhealthyGenerations);
	}

	[TestMethod]
	public async Task SendAsync_WhenNoReadySessionIsReportedOnThePrimaryAttempt_RetriesOnce()
	{
		var client = new FakeLanguageServerClient { TransportGeneration = 7 };
		int handlerCallCount = 0;

		client.SendRequestHandler = (_, _, _) =>
		{
			handlerCallCount++;

			if (handlerCallCount == 1)
				throw new IOException("The language server transport is not ready.");

			return Task.FromResult<object?>("response");
		};

		LanguageServerRequestDispatcher dispatcher = CreateDispatcher(client);

		string? result = await dispatcher.SendAsync("textDocument/hover", new object(), "fallback", CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual("response", result);
		Assert.AreEqual(2, client.SendRequestCount);
		Assert.AreEqual(0, client.MarkedUnhealthyGenerations.Count);
	}

	[TestMethod]
	public async Task SendAsync_WhenNoReadySessionPersists_ReturnsTheFallbackValue()
	{
		var client = new FakeLanguageServerClient { TransportGeneration = 7 };
		client.SendRequestHandler = (_, _, _) => throw new IOException("The language server transport is not ready.");
		LanguageServerRequestDispatcher dispatcher = CreateDispatcher(client);

		string? result = await dispatcher.SendAsync("textDocument/hover", new object(), "fallback", CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual("fallback", result);
		Assert.AreEqual(2, client.SendRequestCount);
		Assert.AreEqual(0, client.MarkedUnhealthyGenerations.Count);
	}

	[TestMethod]
	public async Task SendAsync_WhenTheServerRejectsTheRequest_ReturnsTheFallbackValueWithoutRetrying()
	{
		var client = new FakeLanguageServerClient { TransportGeneration = 7 };
		client.SendRequestHandler = (_, _, _) => throw new LanguageServerRequestRejectedException(-32602, "Invalid parameters.", null);
		LanguageServerRequestDispatcher dispatcher = CreateDispatcher(client);

		string? result = await dispatcher.SendAsync("textDocument/hover", new object(), "fallback", CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual("fallback", result);
		Assert.AreEqual(1, client.SendRequestCount);
		Assert.AreEqual(0, client.MarkedUnhealthyGenerations.Count);
	}

	[TestMethod]
	public async Task SendAsync_WhenTheResponsePayloadCannotBeProcessed_ReturnsTheFallbackValueWithoutMarking()
	{
		var client = new FakeLanguageServerClient { TransportGeneration = 5 };
		client.SendRequestHandler = (_, _, _) => throw new JsonException("Simulated unprocessable response payload.");
		LanguageServerRequestDispatcher dispatcher = CreateDispatcher(client);

		string? result = await dispatcher.SendAsync("textDocument/hover", new object(), "fallback", CancellationToken.None).ConfigureAwait(false);

		// An unusable response payload is a completed exchange with a responsive transport: the documented fallback
		// value is returned and the transport is not marked unhealthy.
		Assert.AreEqual("fallback", result);
		Assert.AreEqual(1, client.SendRequestCount);
		Assert.AreEqual(0, client.MarkedUnhealthyGenerations.Count);
	}
}

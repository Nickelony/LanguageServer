namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Covers the provider state machine directly: startup fencing per transport generation, failure
/// counters, one-time failure reporting, and the terminal disposed state.
/// </summary>
[TestClass]
public sealed class LanguageServerStartupStateTests
{
	private static LanguageServerStartupState CreateState(FakeLanguageServerClient client, Func<bool>? isDisposedAccessor = null)
		=> new(
			client,
			initialState: LanguageServerProviderState.Unavailable,
			readyState: LanguageServerProviderState.Ready,
			disposedState: LanguageServerProviderState.Disposed,
			isDisposedAccessor ?? (static () => false));

	[TestMethod]
	public async Task TryCompleteSuccessfulStart_OnTheCurrentReadyGeneration_TransitionsToReady()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		await client.StartAsync(CancellationToken.None).ConfigureAwait(false);
		LanguageServerStartupState state = CreateState(client);

		Assert.IsTrue(state.TryCompleteSuccessfulStart(client.TransportGeneration, out LanguageServerProviderState previousState));

		Assert.AreEqual(LanguageServerProviderState.Unavailable, previousState);
		Assert.AreEqual(LanguageServerProviderState.Ready, state.State);
		Assert.IsTrue(state.GetStartupSucceeded());
	}

	[TestMethod]
	public async Task TryCompleteSuccessfulStart_WhenClientIsNotReady_FailsWithoutTransitioning()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		await client.StartAsync(CancellationToken.None).ConfigureAwait(false);

		// Isolate the not-ready condition: the generation is valid and current, only readiness fails.
		client.IsReady = false;

		LanguageServerStartupState state = CreateState(client);

		Assert.IsFalse(state.TryCompleteSuccessfulStart(client.TransportGeneration, out _));
		Assert.IsFalse(state.GetStartupSucceeded());
		Assert.AreEqual(LanguageServerProviderState.Unavailable, state.State);
	}

	[TestMethod]
	public async Task TryCompleteSuccessfulStart_WhenGenerationWasReportedUnavailable_Fails()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		await client.StartAsync(CancellationToken.None).ConfigureAwait(false);
		LanguageServerStartupState state = CreateState(client);

		Assert.IsFalse(state.OnClientTransportUnavailable(client.TransportGeneration));
		Assert.IsFalse(state.TryCompleteSuccessfulStart(client.TransportGeneration, out _));
		Assert.IsFalse(state.GetStartupSucceeded());
	}

	[TestMethod]
	public async Task OnClientTransportUnavailable_InvalidatesOnlyTheStartupOfThatGeneration()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		await client.StartAsync(CancellationToken.None).ConfigureAwait(false);
		LanguageServerStartupState state = CreateState(client);

		Assert.IsTrue(state.TryCompleteSuccessfulStart(client.TransportGeneration, out _));
		Assert.IsTrue(state.OnClientTransportUnavailable(client.TransportGeneration));
		Assert.IsFalse(state.GetStartupSucceeded());

		// A start on the next generation succeeds, and a late notification for the previous
		// generation no longer invalidates it.
		client.IsReady = false;
		await client.StartAsync(CancellationToken.None).ConfigureAwait(false);
		Assert.IsTrue(state.TryCompleteSuccessfulStart(client.TransportGeneration, out _));
		Assert.IsFalse(state.OnClientTransportUnavailable(client.TransportGeneration - 1));
		Assert.IsTrue(state.GetStartupSucceeded());
	}

	[TestMethod]
	public async Task FailureReporting_IsOncePerPermanence_AndResetsAfterASuccessfulStart()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		LanguageServerStartupState state = CreateState(client);

		Assert.AreEqual(1, state.RegisterStartupFailure());
		Assert.AreEqual(2, state.RegisterStartupFailure());
		Assert.AreEqual(2, state.GetConsecutiveStartupFailures());

		Assert.IsTrue(state.TryMarkStartupFailureReported(isPermanentFailure: false));
		Assert.IsFalse(state.TryMarkStartupFailureReported(isPermanentFailure: false));
		Assert.IsTrue(state.TryMarkStartupFailureReported(isPermanentFailure: true));
		Assert.IsFalse(state.TryMarkStartupFailureReported(isPermanentFailure: true));

		// A successful start resets the counters and the reporting flags.
		await client.StartAsync(CancellationToken.None).ConfigureAwait(false);
		Assert.IsTrue(state.TryCompleteSuccessfulStart(client.TransportGeneration, out _));
		Assert.AreEqual(0, state.GetConsecutiveStartupFailures());
		Assert.IsTrue(state.TryMarkStartupFailureReported(isPermanentFailure: false));
	}

	[TestMethod]
	public void TrySetState_HonorsTheTerminalDisposedState()
	{
		using var client = new FakeLanguageServerClient();
		bool isDisposed = false;
		LanguageServerStartupState state = CreateState(client, () => isDisposed);

		Assert.IsTrue(state.TrySetState(LanguageServerProviderState.Ready, notifyCapabilitiesChanged: true));

		isDisposed = true;

		// Once disposal started, only the disposed transition is still accepted.
		Assert.IsFalse(state.TrySetState(LanguageServerProviderState.Starting, notifyCapabilitiesChanged: true));
		Assert.AreEqual(LanguageServerProviderState.Ready, state.State);

		state.TrySetState(LanguageServerProviderState.Disposed, notifyCapabilitiesChanged: false);
		Assert.AreEqual(LanguageServerProviderState.Disposed, state.State);

		// The disposed state is terminal.
		Assert.IsFalse(state.TrySetState(LanguageServerProviderState.Ready, notifyCapabilitiesChanged: true));
		Assert.AreEqual(LanguageServerProviderState.Disposed, state.State);
	}
}

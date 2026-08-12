using Nickelony.LanguageServer.Abstractions.Hover;
using Nickelony.LanguageServer.Abstractions.Infrastructure.Provider;

namespace Nickelony.LanguageServer.Lua.Tests;

public partial class LuaLanguageServerIntelliSenseProviderTests
{
	[TestMethod]
	public async Task HealthyFastPathDoesNotPublishStartingOrCapabilitiesTransition()
	{
		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider(@"C:\Workspace", client);
		int capabilitiesChangedCount = 0;

		provider.CapabilitiesChanged += () => capabilitiesChangedCount++;

		await provider.GetHoverAsync(@"C:\Workspace\Scripts\test.lua", "return 1", 0, 0);

		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);
		Assert.AreEqual(1, capabilitiesChangedCount);

		await provider.GetHoverAsync(@"C:\Workspace\Scripts\test.lua", "return 2", 0, 0);

		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);
		Assert.IsTrue(provider.IsAvailable);
		Assert.AreEqual(1, capabilitiesChangedCount);
		Assert.AreEqual(1, client.StartCallCount);
	}

	[TestMethod]
	public async Task DisposeDuringStartupLeavesDisposedAsTerminalState()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		client.BlockNextStartAsync();

		using var provider = new LuaLanguageServerIntelliSenseProvider(@"C:\Workspace", client);
		Task<TextHoverInfo?> request = provider.GetHoverAsync(@"C:\Workspace\Scripts\test.lua", "return 1", 0, 0);
		DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);

		while (client.StartCallCount == 0 && DateTime.UtcNow < deadline)
			await Task.Delay(10).ConfigureAwait(false);

		Assert.AreEqual(1, client.StartCallCount);

		provider.Dispose();
		client.ReleaseStartAsync();

		Assert.IsNull(await request.ConfigureAwait(false));
		Assert.AreEqual(LanguageServerProviderState.Disposed, provider.State);
	}

	[TestMethod]
	public async Task ActiveTransportLossPublishesUnavailableCapabilitiesOnceAndRestartRecovers()
	{
		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider(@"C:\Workspace", client);
		int capabilitiesChangedCount = 0;

		provider.CapabilitiesChanged += () => capabilitiesChangedCount++;

		await provider.GetHoverAsync(@"C:\Workspace\Scripts\test.lua", "return 1", 0, 0);

		long activeGeneration = client.TransportGeneration;
		client.PublishTransportUnavailable(activeGeneration);

		Assert.AreEqual(0L, client.TransportGeneration);
		Assert.AreEqual(LanguageServerProviderState.Unavailable, provider.State);
		Assert.IsFalse(provider.IsAvailable);
		Assert.IsFalse(provider.SupportsReferences);
		Assert.AreEqual(2, capabilitiesChangedCount);

		await provider.GetHoverAsync(@"C:\Workspace\Scripts\test.lua", "return 2", 0, 0);

		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);
		Assert.IsTrue(client.TransportGeneration > activeGeneration);
		Assert.IsTrue(provider.SupportsReferences);
		Assert.AreEqual(3, capabilitiesChangedCount);
	}

	[TestMethod]
	public async Task TransportLossBeforeReadyPublicationCannotBeOverwrittenByStartupCompletion()
	{
		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider(@"C:\Workspace", client);
		int capabilitiesChangedCount = 0;

		provider.CapabilitiesChanged += () => capabilitiesChangedCount++;
		client.BeforeReturningStartResult = () => client.PublishTransportUnavailable();

		TextHoverInfo? hover = await provider.GetHoverAsync(
			@"C:\Workspace\Scripts\test.lua", "return 1", 0, 0);

		Assert.IsNull(hover);
		Assert.AreEqual(0L, client.TransportGeneration);
		Assert.AreEqual(LanguageServerProviderState.Unavailable, provider.State);
		Assert.IsFalse(provider.IsAvailable);
		Assert.AreEqual(1, capabilitiesChangedCount);
	}

	[TestMethod]
	public async Task StaleTransportLossCannotChangeCurrentReadySession()
	{
		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider(@"C:\Workspace", client);

		await provider.GetHoverAsync(@"C:\Workspace\Scripts\test.lua", "return 1", 0, 0);

		long staleGeneration = client.TransportGeneration;
		client.IsReady = false;

		await provider.GetHoverAsync(@"C:\Workspace\Scripts\test.lua", "return 2", 0, 0);

		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);

		client.PublishTransportUnavailable(staleGeneration);

		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);
		Assert.IsTrue(provider.IsAvailable);
	}

	[TestMethod]
	public async Task TransportLossRacingDisposeCannotPublishCallbackOrOverwriteDisposedState()
	{
		using var client = new FakeLanguageServerClient();
		var provider = new LuaLanguageServerIntelliSenseProvider(@"C:\Workspace", client);
		using var transportCallbackCaptured = new ManualResetEventSlim();
		using var releaseTransportCallback = new ManualResetEventSlim();
		int capabilitiesChangedCount = 0;

		provider.CapabilitiesChanged += () => capabilitiesChangedCount++;

		await provider.GetHoverAsync(@"C:\Workspace\Scripts\test.lua", "return 1", 0, 0);

		long activeGeneration = client.TransportGeneration;

		Assert.AreEqual(1, capabilitiesChangedCount);

		client.BeforePublishingTransportUnavailable = () =>
		{
			transportCallbackCaptured.Set();
			releaseTransportCallback.Wait();
		};

		Task transportLoss = Task.Run(() => client.PublishTransportUnavailable(activeGeneration));

		try
		{
			Assert.IsTrue(transportCallbackCaptured.Wait(TimeSpan.FromSeconds(1)));
		}
		finally
		{
			provider.Dispose();
			releaseTransportCallback.Set();
		}

		await transportLoss;

		Assert.AreEqual(LanguageServerProviderState.Disposed, provider.State);
		Assert.IsFalse(provider.IsAvailable);
		Assert.AreEqual(1, capabilitiesChangedCount);
	}
}

using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.IntelliSense.Completion;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionTestHost;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

/// <summary>
/// Verifies the completion request session's token semantics: which operation cancels or invalidates
/// the current token without touching the other mechanism, and that manually driven requests and the
/// standard request pipeline supersede each other.
/// </summary>
[STATestClass]
public sealed class TextCompletionControllerRequestTokenTests
{
	[TestMethod]
	public void InvalidateRequests_RejectsRequestsWithoutCancelingTheToken()
	{
		using TextCompletionController controller = CreateController(CreateEditor());

		long requestToken = controller.Requests.BeginRequest();
		CancellationToken token = controller.Requests.CurrentRequestCancellationToken;

		Assert.IsTrue(controller.Requests.IsCurrent(requestToken));
		Assert.IsFalse(token.IsCancellationRequested);

		controller.Requests.InvalidateRequests();

		// Invalidating only rejects the result; the provider call keeps running until it is canceled explicitly.
		Assert.IsFalse(controller.Requests.IsCurrent(requestToken));
		Assert.IsFalse(token.IsCancellationRequested);
	}

	[TestMethod]
	public void CancelInFlightRequest_CancelsTheTokenWithoutInvalidatingTheRequest()
	{
		using TextCompletionController controller = CreateController(CreateEditor());

		long requestToken = controller.Requests.BeginRequest();
		CancellationToken token = controller.Requests.CurrentRequestCancellationToken;

		controller.Requests.CancelInFlightRequest();

		// Canceling stops the provider call; the request token stays current until it is invalidated.
		Assert.IsTrue(token.IsCancellationRequested);
		Assert.IsTrue(controller.Requests.IsCurrent(requestToken));
	}

	[TestMethod]
	public void BeginRequest_SupersededToken_IsCanceled()
	{
		using TextCompletionController controller = CreateController(CreateEditor());

		long firstToken = controller.Requests.BeginRequest();
		CancellationToken firstCancellationToken = controller.Requests.CurrentRequestCancellationToken;

		controller.Requests.BeginRequest();

		// The superseded request's token is canceled immediately; the property reports the newest token.
		Assert.IsTrue(firstCancellationToken.IsCancellationRequested);
		Assert.IsFalse(controller.Requests.CurrentRequestCancellationToken.IsCancellationRequested);
		Assert.IsFalse(controller.Requests.IsCurrent(firstToken));
	}

	[TestMethod]
	public void CancelInFlightRequest_CurrentRequestCancellationToken_ReturnsTheCanceledToken()
	{
		using TextCompletionController controller = CreateController(CreateEditor());

		controller.Requests.BeginRequest();

		Assert.IsFalse(controller.Requests.CurrentRequestCancellationToken.IsCancellationRequested);

		controller.Requests.CancelInFlightRequest();

		// After cancellation the property keeps returning the canceled token of the last request.
		Assert.IsTrue(controller.Requests.CurrentRequestCancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void BeginRequest_TokenCanceledByInvalidationAndCancellation_IsCanceled()
	{
		using TextCompletionController controller = CreateController(CreateEditor());

		controller.Requests.BeginRequest();
		CancellationToken token = controller.Requests.CurrentRequestCancellationToken;

		controller.Requests.InvalidateRequests();
		controller.Requests.CancelInFlightRequest();

		Assert.IsTrue(token.IsCancellationRequested);
		Assert.IsTrue(controller.Requests.CurrentRequestCancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void BeginRequest_NewRequest_CancelsPreviousRequestToken()
	{
		using TextCompletionController controller = CreateController(CreateEditor());

		controller.Requests.BeginRequest();
		CancellationToken firstToken = controller.Requests.CurrentRequestCancellationToken;

		controller.Requests.BeginRequest();
		CancellationToken secondToken = controller.Requests.CurrentRequestCancellationToken;

		Assert.IsTrue(firstToken.IsCancellationRequested);
		Assert.IsFalse(secondToken.IsCancellationRequested);
	}

	[TestMethod]
	public void BeginRequest_WhileStandardRequestInFlight_SupersedesTheStandardRequest()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				var providerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
				var providerGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

				Task<bool> request = hosted.Controller.RequestAsync(async _ =>
				{
					providerStarted.SetResult();
					await providerGate.Task.ConfigureAwait(true);
					return TextCompletionSessionDecision.Open([new TextCompletionItem("stale")], 0, 5);
				});

				DispatcherTestUtils.PumpUntil(() => providerStarted.Task.IsCompleted);

				// Both flows share one request lifetime: a manually driven request supersedes the standard
				// request, whose late decision must not open a window.
				long requestToken = hosted.Controller.Requests.BeginRequest();

				providerGate.SetResult();
				DispatcherTestUtils.PumpUntil(() => request.IsCompleted);

				Assert.IsFalse(request.GetAwaiter().GetResult());
				Assert.IsTrue(hosted.Controller.Requests.IsCurrent(requestToken));
				Assert.IsNull(hosted.Coordinator.ActiveWindow);
			}
		}
	}

	[TestMethod]
	public void RequestAsync_WhileManualRequestTracked_SupersedesTheManualRequest()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				long requestToken = hosted.Controller.Requests.BeginRequest();
				CancellationToken manualToken = hosted.Controller.Requests.CurrentRequestCancellationToken;

				Task<bool> request = hosted.Controller.RequestAsync(
					_ => Task.FromResult(TextCompletionSessionDecision.Open([new TextCompletionItem("sample")], 0, 5)));

				DispatcherTestUtils.PumpUntil(() => request.IsCompleted);

				// The standard request supersedes the tracked manual request: its token is canceled and its
				// identifier no longer passes the current check.
				Assert.IsTrue(request.GetAwaiter().GetResult());
				Assert.IsTrue(manualToken.IsCancellationRequested);
				Assert.IsFalse(hosted.Controller.Requests.IsCurrent(requestToken));
			}
		}
	}
}

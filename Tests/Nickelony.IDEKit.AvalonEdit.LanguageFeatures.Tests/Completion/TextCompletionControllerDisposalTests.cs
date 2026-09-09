using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.IntelliSense.Completion;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionTestHost;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

[STATestClass]
public sealed class TextCompletionControllerDisposalTests
{
	[TestMethod]
	public void Dispose_CancelsInFlightRequestToken()
	{
		using TextCompletionController controller = CreateController(CreateEditor());

		controller.Requests.BeginRequest();
		CancellationToken token = controller.Requests.CurrentRequestCancellationToken;

		controller.Dispose();

		Assert.IsTrue(token.IsCancellationRequested);
	}

	[TestMethod]
	public async Task PublicOperations_AfterDisposal_ReturnSafeDefaultsAndDoNotThrow()
	{
		using TextCompletionController controller = CreateController(CreateEditor());

		controller.Dispose();

		controller.ScheduleRequest();
		controller.CancelScheduledRequest();
		controller.CloseWindow();
		controller.CancelToolTipUpdate();
		controller.Requests.InvalidateRequests();
		controller.Requests.CancelInFlightRequest();
		controller.ScheduleCloseIfEmpty();

		// State queries return safe defaults after disposal, and the calls above complete without throwing.
		Assert.IsNull(controller.ActiveWindow);
		Assert.AreEqual(-1, controller.Requests.BeginRequest());
		Assert.IsFalse(controller.Requests.IsCurrent(1));
		Assert.IsFalse(controller.OpenOrRefresh([], 0, 0));
		Assert.IsFalse(controller.ApplyDecision(TextCompletionSessionDecision.None));

		// A request that starts after disposal is never admitted and reports its documented not-applied
		// result instead of touching the closed session.
		bool requested = false;
		Assert.IsFalse(await controller.RequestAsync(_ =>
		{
			requested = true;
			return Task.FromResult(TextCompletionSessionDecision.None);
		}));
		Assert.IsFalse(requested);
	}

	[TestMethod]
	public void Dispose_DropsPendingRequestAndTooltipDebounces_AndIsIdempotent()
	{
		int requestCount = 0;
		int resolveCount = 0;
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(hooks: new TextCompletionControllerHooks
			{
				ResolveDescriptionAsync = (_, _) =>
				{
					resolveCount++;
					return Task.FromResult<object?>("resolved description");
				},
				ScheduledRequestAsync = () =>
				{
					requestCount++;
					return Task.CompletedTask;
				}
			});

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([new TestCompletionData("sample", "Sample documentation.")], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				// Arm both debounces: the request timer, and the tooltip timer through the selection change.
				hosted.Controller.ScheduleRequest();
				completionWindow.CompletionList.SelectItem("sample");

				Assert.IsTrue(hosted.Controller.CurrentPresentation.IsRequestScheduled);

				hosted.Controller.Dispose();
				hosted.Controller.Dispose();

				// Disposal is idempotent and drops the armed debounces: pumping past both delays must not
				// deliver the scheduled request or the tooltip resolution.
				DispatcherTestUtils.PumpFrames(TimeSpan.FromMilliseconds(50.0));

				Assert.AreEqual(0, requestCount);
				Assert.AreEqual(0, resolveCount);
			}
		}
	}

	[TestMethod]
	public void ScheduleRequest_ArmedRepeatedly_RunsTheScheduledRequestOnce()
	{
		int requestCount = 0;
		var controller = CreateController(CreateEditor(), hooks: new TextCompletionControllerHooks
		{
			ScheduledRequestAsync = () =>
			{
				requestCount++;
				return Task.CompletedTask;
			}
		});

		try
		{
			// Arming again restarts the debounce delay, so the last schedule runs the scheduled request
			// exactly once.
			controller.ScheduleRequest();
			controller.ScheduleRequest();
			controller.ScheduleRequest();

			DispatcherTestUtils.PumpUntil(() => requestCount > 0);
			DispatcherTestUtils.PumpFrames(TimeSpan.FromMilliseconds(20.0));

			Assert.AreEqual(1, requestCount);
			Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);
		}
		finally
		{
			controller.Dispose();
		}
	}

	[TestMethod]
	public void TrackedWindow_ClosingItself_ResetsWindowAndTooltipState()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([new TestCompletionData("sample", "Sample documentation.")], 0, 5));
				Assert.IsTrue(hosted.Controller.CurrentPresentation.IsListVisible);

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				// Closing the window without going through the controller models a window that closes itself, for
				// example after a commit, Escape, or focus loss.
				completionWindow.Close();

				Assert.IsNull(hosted.Coordinator.ActiveWindow);
				Assert.IsNull(hosted.Controller.ActiveWindow);
				Assert.IsFalse(hosted.Controller.CurrentPresentation.IsListVisible);
				Assert.IsFalse(hosted.Controller.CurrentPresentation.IsDetailVisible);
				Assert.IsNull(hosted.Controller.CurrentPresentation.DetailContent);
			}
		}
	}

	[TestMethod]
	public void Dispose_WhenWindowIsOpen_ClosesTheWindowAndClearsState()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			Assert.IsTrue(hosted.Controller.OpenOrRefresh([new TestCompletionData("sample", "Sample documentation.")], 0, 5));
			Assert.IsTrue(hosted.Controller.CurrentPresentation.IsListVisible);

			hosted.Controller.Dispose();

			Assert.IsNull(hosted.Coordinator.ActiveWindow);
			Assert.IsNull(hosted.Controller.ActiveWindow);
			Assert.IsFalse(hosted.Controller.CurrentPresentation.IsListVisible);
		}
	}

	[TestMethod]
	public void OpenOrRefresh_AfterDisposal_WithItems_ReturnsFalseAndOpensNothing()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			hosted.Controller.Dispose();

			// The non-empty call is the disposal path the empty-list coverage does not reach: it must report its
			// documented not-applied result and must not create a window.
			Assert.IsFalse(hosted.Controller.OpenOrRefresh([new TestCompletionData("item")], 0, 1));
			Assert.IsFalse(hosted.Coordinator.IsWindowOpen);
			Assert.IsNull(hosted.Controller.ActiveWindow);
		}
	}

	[TestMethod]
	public void ScheduleCloseIfEmpty_WithItemsPresent_KeepsTheWindowOpen()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([new TestCompletionData("sample", "Sample documentation.")], 0, 5));

				hosted.Controller.ScheduleCloseIfEmpty();
				DispatcherTestUtils.PumpFrames(TimeSpan.FromMilliseconds(50.0));

				// The posted empty-list check ran, but the list still has items, so the window stays open.
				Assert.IsTrue(hosted.Coordinator.IsWindowOpen);
			}
		}
	}
}

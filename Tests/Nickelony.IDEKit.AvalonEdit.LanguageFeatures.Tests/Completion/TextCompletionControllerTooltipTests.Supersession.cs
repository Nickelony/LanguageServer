using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using System.Windows.Controls;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionTestHost;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

public sealed partial class TextCompletionControllerTooltipTests
{
	[TestMethod]
	public void Tooltip_ThrowingResolver_HidesTooltipAndDoesNotThrow()
	{
		var logger = new CapturingLogger();
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(hooks: new TextCompletionControllerHooks
			{
				ResolveDescriptionAsync = (_, _) => throw new InvalidOperationException("Tooltip resolution failed.")
			}, logger: logger);

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample", description: null)], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				completionWindow.CompletionList.SelectItem("sample");
				PumpPastTooltipDebounce(hosted.Controller);

				Assert.IsTrue(CompletionWindowToolTipAccess.TryGetToolTip(completionWindow, out ToolTip? tooltip));
				Assert.IsNotNull(tooltip);

				// A failing resolver closes the tooltip instead of escaping the timer callback, and reports the
				// documented tooltip event id.
				Assert.IsFalse(tooltip.IsOpen);
				Assert.IsFalse(hosted.Controller.CurrentPresentation.IsDetailVisible);
				Assert.IsNull(hosted.Controller.CurrentPresentation.DetailContent);
				Assert.AreEqual(1, logger.Entries.Count);
				Assert.AreEqual(1001, logger.Entries[0].EventId.Id);
			}
		}
	}

	[TestMethod]
	public void Tooltip_ResolverResultAfterCancel_IsNotApplied()
	{
		var completion = new TaskCompletionSource<object?>();
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(hooks: new TextCompletionControllerHooks
			{
				ResolveDescriptionAsync = (_, _) => completion.Task
			});

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample", description: null)], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				Assert.IsTrue(CompletionWindowToolTipAccess.TryGetToolTip(completionWindow, out ToolTip? tooltip));
				Assert.IsNotNull(tooltip);

				completionWindow.CompletionList.SelectItem("sample");
				PumpPastTooltipDebounce(hosted.Controller);

				// Cancel the in-flight tooltip update, then let the old resolver finish late.
				hosted.Controller.CancelToolTipUpdate();
				completion.TrySetResult("late resolved description");

				// Pump after the late result so the rejection actually runs before the assertions instead of
				// depending on continuation scheduling luck.
				DispatcherTestUtils.PumpFrames(TimeSpan.FromMilliseconds(20.0));

				// The stale result must not reopen the tooltip or update the presentation state.
				Assert.IsFalse(tooltip.IsOpen);
				Assert.IsFalse(hosted.Controller.CurrentPresentation.IsDetailVisible);
				Assert.IsNull(hosted.Controller.CurrentPresentation.DetailContent);
			}
		}
	}

	[TestMethod]
	public void Tooltip_SelectionChangesWhileResolveIsInFlight_LateResultForThePreviousItemIsIgnored()
	{
		var firstResolution = new TaskCompletionSource<object?>();
		int resolveCount = 0;
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(hooks: new TextCompletionControllerHooks
			{
				// The first resolve stays in flight until the test completes it; later resolves finish
				// immediately so the new selection's tooltip is shown before the first one completes.
				ResolveDescriptionAsync = (_, _) =>
				{
					resolveCount++;

					return resolveCount == 1
						? firstResolution.Task
						: Task.FromResult<object?>("second description");
				}
			});

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh(
					[CreateItem("sample", description: null), CreateItem("second", description: null)],
					0,
					5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				Assert.IsTrue(CompletionWindowToolTipAccess.TryGetToolTip(completionWindow, out ToolTip? tooltip));
				Assert.IsNotNull(tooltip);

				// Selecting the first item starts a resolve that remains in flight.
				completionWindow.CompletionList.SelectItem("sample");
				PumpPastTooltipDebounce(hosted.Controller);
				Assert.AreEqual(1, resolveCount);

				// Selecting another item supersedes the in-flight resolve instead of waiting for it: the
				// new selection's resolve runs and its content is applied.
				completionWindow.CompletionList.SelectItem("second");
				PumpPastTooltipDebounce(hosted.Controller);

				Assert.AreEqual(2, resolveCount);

				var content = tooltip.Content as TextBlock;

				Assert.IsNotNull(content);
				Assert.AreEqual("second description", content.Text);
				Assert.AreEqual("second description", hosted.Controller.CurrentPresentation.DetailContent);

				// The superseded resolve ignores the canceled token and finishes late; its stale result
				// must not overwrite the newer selection's tooltip or presentation state.
				firstResolution.TrySetResult("late first description");
				DispatcherTestUtils.PumpFrames(TimeSpan.FromMilliseconds(20.0));

				var lateContent = tooltip.Content as TextBlock;

				Assert.IsNotNull(lateContent);
				Assert.AreEqual("second description", lateContent.Text);
				Assert.AreEqual("second description", hosted.Controller.CurrentPresentation.DetailContent);
				Assert.IsTrue(tooltip.IsOpen);
			}
		}
	}

	[TestMethod]
	public void Tooltip_ResolverRegisteringAfterCancellation_DoesNotThrow()
	{
		var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var resolverStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		bool observedCanceledRegistration = false;

		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(hooks: new TextCompletionControllerHooks
			{
				ResolveDescriptionAsync = async (_, cancellationToken) =>
				{
					resolverStarted.TrySetResult();
					await completion.Task.ConfigureAwait(true);

					// The superseded resolve may still register on its canceled token; the registration must
					// observe cancellation instead of an ObjectDisposedException.
					using CancellationTokenRegistration registration = cancellationToken.Register(static () => { });
					observedCanceledRegistration = true;
					return (object?)"resolved too late";
				}
			});

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample", description: null)], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				Assert.IsTrue(CompletionWindowToolTipAccess.TryGetToolTip(completionWindow, out ToolTip? tooltip));
				Assert.IsNotNull(tooltip);

				completionWindow.CompletionList.SelectItem("sample");
				PumpPastTooltipDebounce(hosted.Controller);
				resolverStarted.Task.GetAwaiter().GetResult();

				// Supersede the in-flight resolve, then let it finish; the late result must not be applied.
				hosted.Controller.CancelToolTipUpdate();
				completion.TrySetResult(null);
				DispatcherTestUtils.PumpFrames(TimeSpan.FromMilliseconds(20.0));

				Assert.IsTrue(observedCanceledRegistration);
				Assert.IsFalse(tooltip.IsOpen);
				Assert.IsFalse(hosted.Controller.CurrentPresentation.IsDetailVisible);
			}
		}
	}
}

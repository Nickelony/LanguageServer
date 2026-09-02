using Nickelony.IDEKit.AvalonEdit.IntelliSense.Signatures;
using Nickelony.IDEKit.IntelliSense.Signatures;
using System.Reflection;
using System.Windows.Threading;

namespace Nickelony.IDEKit.AvalonEdit.IntelliSense.Tests;

[TestClass]
public class TextSignatureHelpControllerTests
{
	[TestMethod]
	public void RequestAsync_ThrowingProvider_DoesNotEscapeAndRemainsNotVisible()
	{
		STATestHelper.RunInSTA(() =>
		{
			var controller = new TextSignatureHelpController(
				() => 0,
				(offset, requestToken) => throw new InvalidOperationException("Signature help failed."),
				_ => { },
				() => { });

			controller.RequestAsync(5).GetAwaiter().GetResult();

			// A provider failure must not escape; signature help remains neither visible nor pending.
			Assert.IsFalse(controller.IsVisible);
			Assert.IsFalse(controller.IsActiveOrPending);
		});
	}

	[TestMethod]
	public void RequestAsync_StaleRequestAfterInvalidation_DoesNotShowSignatureHelp()
	{
		STATestHelper.RunInSTA(() =>
		{
			bool shown = false;
			TextSignatureHelpController? controller = null;

			controller = new TextSignatureHelpController(
				() => 0,
				(offset, requestToken) =>
				{
					// Invalidate the request before returning its result so the controller must drop it.
					controller!.InvalidateRequests();

					return Task.FromResult<TextSignatureHelpInfo?>(
						new TextSignatureHelpInfo("spawn(room)", 0, "Spawns an object.", []));
				},
				_ => shown = true,
				() => { });

			controller.RequestAsync(5).GetAwaiter().GetResult();

			Assert.IsFalse(shown);
			Assert.IsFalse(controller.IsVisible);
		});
	}

	[TestMethod]
	public void RequestAsync_AfterDisposal_ReturnsCompletedTaskAndDoesNotShow()
	{
		STATestHelper.RunInSTA(() =>
		{
			bool shown = false;

			var controller = new TextSignatureHelpController(
				() => 0,
				(offset, requestToken) => Task.FromResult<TextSignatureHelpInfo?>(
					new TextSignatureHelpInfo("spawn(room)", 0, "Spawns an object.", [])),
				_ => shown = true,
				() => { });

			controller.Dispose();
			Task requestTask = controller.RequestAsync(5);

			Assert.IsTrue(requestTask.IsCompletedSuccessfully);
			Assert.IsFalse(shown);
			Assert.IsFalse(controller.IsVisible);
		});
	}

	[TestMethod]
	public void RequestAsync_InFlightRequestCompletingAfterDisposal_DoesNotShowSignatureHelp()
	{
		STATestHelper.RunInSTA(() =>
		{
			bool shown = false;
			var completion = new TaskCompletionSource<TextSignatureHelpInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
			TextSignatureHelpController? controller = null;

			controller = new TextSignatureHelpController(
				() => 0,
				(offset, requestToken) => completion.Task,
				_ => shown = true,
				() => { });

			Task requestTask = controller.RequestAsync(5);
			controller.Dispose();
			completion.TrySetResult(new TextSignatureHelpInfo("spawn(room)", 0, "Spawns an object.", []));

			requestTask.GetAwaiter().GetResult();

			// A request that completes after disposal must not show signature help.
			Assert.IsFalse(shown);
			Assert.IsFalse(controller.IsVisible);
		});
	}

	[TestMethod]
	public void PublicOperations_AfterDisposal_LeaveSignatureHelpInactive()
	{
		STATestHelper.RunInSTA(() =>
		{
			bool shown = false;

			var controller = new TextSignatureHelpController(
				() => 0,
				(offset, requestToken) => Task.FromResult<TextSignatureHelpInfo?>(
					new TextSignatureHelpInfo("spawn(room)", 0, "Spawns an object.", [])),
				_ => shown = true,
				() => { });

			controller.Dispose();

			controller.Dismiss();
			controller.ScheduleRefresh();
			controller.CancelPendingRefresh();
			controller.InvalidateRequests();

			Assert.IsFalse(shown);
			Assert.IsFalse(controller.IsVisible);
			Assert.IsFalse(controller.IsActiveOrPending);
		});
	}

	[TestMethod]
	public void RequestAsync_ProviderReturnsNull_WhenNotVisible_DismissesSignatureHelp()
	{
		STATestHelper.RunInSTA(() =>
		{
			int dismissCount = 0;
			int showCount = 0;

			var controller = new TextSignatureHelpController(
				() => 0,
				(offset, requestToken) => Task.FromResult<TextSignatureHelpInfo?>(null),
				_ => showCount++,
				() => dismissCount++);

			controller.RequestAsync(5).GetAwaiter().GetResult();

			// A null result with nothing visible dismisses signature help.
			Assert.AreEqual(1, dismissCount);
			Assert.AreEqual(0, showCount);
			Assert.IsFalse(controller.IsVisible);
			Assert.IsNull(controller.CurrentSignatureHelp);
		});
	}

	[TestMethod]
	public void RequestAsync_ProviderReturnsNull_WhenVisible_PreservesVisibleSignatureHelp()
	{
		STATestHelper.RunInSTA(() =>
		{
			int servedResponses = 0;
			int dismissCount = 0;
			int showCount = 0;

			var controller = new TextSignatureHelpController(
				() => 0,
				(offset, requestToken) =>
				{
					servedResponses++;
					return Task.FromResult<TextSignatureHelpInfo?>(servedResponses == 1
						? new TextSignatureHelpInfo("spawn(room)", 0, "Spawns an object.", [])
						: null);
				},
				_ => showCount++,
				() => dismissCount++);

			controller.RequestAsync(5).GetAwaiter().GetResult();
			controller.RequestAsync(9).GetAwaiter().GetResult();

			// A null refresh result must not tear down an already-visible signature popup.
			Assert.AreEqual(1, showCount);
			Assert.AreEqual(0, dismissCount);
			Assert.IsTrue(controller.IsVisible);
			Assert.IsNotNull(controller.CurrentSignatureHelp);
		});
	}

	[TestMethod]
	public void RequestAsync_ProviderReturnsSignature_ShowsSignatureHelp()
	{
		STATestHelper.RunInSTA(() =>
		{
			var signature = new TextSignatureHelpInfo("spawn(room)", 0, "Spawns an object.", []);
			TextSignatureHelpInfo? shownSignature = null;

			var controller = new TextSignatureHelpController(
				() => 0,
				(offset, requestToken) => Task.FromResult<TextSignatureHelpInfo?>(signature),
				info => shownSignature = info,
				() => { });

			controller.RequestAsync(5).GetAwaiter().GetResult();

			Assert.IsNotNull(shownSignature);
			Assert.AreEqual(signature, shownSignature);
			Assert.IsTrue(controller.IsVisible);
			Assert.AreEqual(signature, controller.CurrentSignatureHelp);
		});
	}

	[TestMethod]
	public void RequestAsync_SupersedingRequest_InvokesCancelHookAndDropsInFlightResult()
	{
		STATestHelper.RunInSTA(() =>
		{
			int cancelHookCalls = 0;
			bool shown = false;
			var completion = new TaskCompletionSource<TextSignatureHelpInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);

			var controller = new TextSignatureHelpController(
				() => 0,
				(offset, requestToken) => completion.Task,
				_ => shown = true,
				() => { },
				cancelInFlightRequest: () => cancelHookCalls++);

			Task firstRequest = controller.RequestAsync(5);
			Task secondRequest = controller.RequestAsync(9);

			// A superseding request invokes the cancellation hook and marks the active request stale.
			Assert.AreEqual(1, cancelHookCalls);

			completion.TrySetResult(new TextSignatureHelpInfo("spawn(room)", 0, "Spawns an object.", []));
			firstRequest.GetAwaiter().GetResult();
			secondRequest.GetAwaiter().GetResult();

			// The superseded in-flight result is dropped.
			Assert.IsFalse(shown);
			Assert.IsFalse(controller.IsVisible);
		});
	}

	[TestMethod]
	public void Dispose_UnsubscribesRefreshTimerTickHandler()
	{
		STATestHelper.RunInSTA(() =>
		{
			var controller = new TextSignatureHelpController(
				() => 0,
				(offset, requestToken) => Task.FromResult<TextSignatureHelpInfo?>(null),
				_ => { },
				() => { });

			DispatcherTimer refreshTimer = WPFTestHost.GetPrivateField<DispatcherTimer>(controller, "_refreshTimer");

			Assert.AreEqual(1, GetTickHandlerCount(refreshTimer));

			controller.Dispose();

			// Disposal must unsubscribe the refresh timer's tick handler.
			Assert.AreEqual(0, GetTickHandlerCount(refreshTimer));
		});
	}

	private static int GetTickHandlerCount(DispatcherTimer timer)
	{
		FieldInfo field = typeof(DispatcherTimer).GetField(
			"Tick",
			BindingFlags.Instance | BindingFlags.NonPublic)
			?? throw new InvalidOperationException("DispatcherTimer.Tick field was not found.");

		var handler = (EventHandler?)field.GetValue(timer);
		return handler?.GetInvocationList().Length ?? 0;
	}
}

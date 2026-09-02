using Nickelony.IDEKit.AvalonEdit.IntelliSense.Completion;
using Nickelony.IDEKit.IntelliSense.Completion;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Threading;

namespace Nickelony.IDEKit.AvalonEdit.IntelliSense.Tests;

[TestClass]
public class TextCompletionControllerDisposalTests
{
	[TestMethod]
	public void BeginRequest_ProvidesCancellationToken_CancelledOnInvalidate()
	{
		STATestHelper.RunInSTA(() =>
		{
			using TextCompletionController controller = CreateController();

			int requestToken = controller.BeginRequest();
			CancellationToken token = controller.CurrentRequestCancellationToken;

			Assert.IsTrue(controller.IsRequestCurrent(requestToken));
			Assert.IsFalse(token.IsCancellationRequested);

			controller.InvalidateRequests();

			Assert.IsFalse(controller.IsRequestCurrent(requestToken));
			Assert.IsTrue(token.IsCancellationRequested);
		});
	}

	[TestMethod]
	public void BeginRequest_NewRequest_CancelsPreviousRequestToken()
	{
		STATestHelper.RunInSTA(() =>
		{
			using TextCompletionController controller = CreateController();

			controller.BeginRequest();
			CancellationToken firstToken = controller.CurrentRequestCancellationToken;

			controller.BeginRequest();
			CancellationToken secondToken = controller.CurrentRequestCancellationToken;

			Assert.IsTrue(firstToken.IsCancellationRequested);
			Assert.IsFalse(secondToken.IsCancellationRequested);
		});
	}

	[TestMethod]
	public void Dispose_CancelsInFlightRequestToken()
	{
		STATestHelper.RunInSTA(() =>
		{
			var controller = CreateController();

			controller.BeginRequest();
			CancellationToken token = controller.CurrentRequestCancellationToken;

			controller.Dispose();

			Assert.IsTrue(token.IsCancellationRequested);
		});
	}

	[TestMethod]
	public void PublicOperations_AfterDisposal_ReturnSafeDefaultsAndDoNotThrow()
	{
		STATestHelper.RunInSTA(() =>
		{
			var controller = CreateController();

			controller.Dispose();

			controller.InitializeScheduling(() => Task.CompletedTask);
			controller.ScheduleRequest();
			controller.CancelPendingRequest();
			controller.CloseWindow();
			controller.CancelTooltipUpdate();
			controller.InvalidateRequests();
			controller.ScheduleCloseIfEmpty();

			// Query members return safe defaults instead of touching editor-owned state.
			Assert.IsNull(controller.ActiveWindow);
			Assert.AreEqual(-1, controller.BeginRequest());
			Assert.IsFalse(controller.IsRequestCurrent(1));
			Assert.IsFalse(controller.OpenOrRefresh([]));
			Assert.IsFalse(controller.ApplyDecision(TextCompletionSessionDecision.None));
		});
	}

	[TestMethod]
	public void Dispose_IsIdempotent_AndUnsubscribesTimerHandlers()
	{
		STATestHelper.RunInSTA(() =>
		{
			var controller = CreateController();
			controller.InitializeScheduling(() => Task.CompletedTask);

			DispatcherTimer requestTimer = WPFTestHost.GetPrivateField<DispatcherTimer>(controller, "_requestTimer");
			DispatcherTimer toolTipUpdateTimer = WPFTestHost.GetPrivateField<DispatcherTimer>(controller, "_toolTipUpdateTimer");

			Assert.AreEqual(1, GetTickHandlerCount(requestTimer));
			Assert.AreEqual(1, GetTickHandlerCount(toolTipUpdateTimer));

			controller.Dispose();
			controller.Dispose();

			// Disposal must be idempotent and must unsubscribe both timer handlers.
			Assert.AreEqual(0, GetTickHandlerCount(requestTimer));
			Assert.AreEqual(0, GetTickHandlerCount(toolTipUpdateTimer));
		});
	}

	[TestMethod]
	public void InitializeScheduling_RepeatedInitialization_DoesNotDuplicateTimerHandlers()
	{
		STATestHelper.RunInSTA(() =>
		{
			using TextCompletionController controller = CreateController();

			controller.InitializeScheduling(() => Task.CompletedTask);
			controller.InitializeScheduling(() => Task.CompletedTask);
			controller.InitializeScheduling(() => Task.CompletedTask);

			DispatcherTimer requestTimer = WPFTestHost.GetPrivateField<DispatcherTimer>(controller, "_requestTimer");
			DispatcherTimer toolTipUpdateTimer = WPFTestHost.GetPrivateField<DispatcherTimer>(controller, "_toolTipUpdateTimer");

			// Re-initializing scheduling must replace the subscription, not accumulate handlers.
			Assert.AreEqual(1, GetTickHandlerCount(requestTimer));
			Assert.AreEqual(1, GetTickHandlerCount(toolTipUpdateTimer));
		});
	}

	private static TextCompletionController CreateController()
	{
		var editor = new ICSharpCode.AvalonEdit.TextEditor();
		var coordinator = new CompletionWindowCoordinator(
			new CompletionWindowHost(editor.TextArea),
			Brushes.Gray,
			Brushes.Black,
			Brushes.White);

		return new TextCompletionController(editor, coordinator);
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

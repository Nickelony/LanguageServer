using Nickelony.IDEKit.AvalonEdit.IntelliSense.Hover;
using Nickelony.IDEKit.IntelliSense.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Hover;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Nickelony.IDEKit.AvalonEdit.IntelliSense.Tests;

[TestClass]
public class TextHoverControllerTests
{
	[TestMethod]
	public void HandleMouseHoverAsync_ThrowingProvider_FallsBackToDiagnosticWithoutThrowing()
	{
		STATestHelper.RunInSTA(() =>
		{
			var owner = new Border();
			Window hostWindow = WPFTestHost.ShowInHostWindow(owner);

			try
			{
				bool diagnosticShown = false;

				var controller = new TextHoverController(
					owner,
					_ => 5,
					_ => new TextHoverRequestState(
						ShouldRequestHover: true,
						RequestOffset: 5,
						CanShowToolTip: true,
						CanShowDiagnosticFallback: true,
						DiagnosticInfo: new TextEditorDiagnostic(TextEditorDiagnosticSeverity.Error, "diagnostic", 0, 1)),
					(offset, cancellationToken) => throw new InvalidOperationException("Hover failed."),
					_ => 5,
					_ => diagnosticShown = true,
					_ => { },
					(info, diagnosticInfo) => { });

				var eventArgs = new MouseEventArgs(Mouse.PrimaryDevice, 0)
				{
					RoutedEvent = Mouse.MouseMoveEvent
				};

				controller.HandleMouseHoverAsync(eventArgs).GetAwaiter().GetResult();

				// A provider failure must not propagate; available diagnostic content is shown instead.
				Assert.IsTrue(diagnosticShown);
			}
			finally
			{
				hostWindow.Close();
			}
		});
	}

	[TestMethod]
	public void HandleMouseHoverAsync_AfterDisposal_DoesNotShowTooltipsOrApplyState()
	{
		STATestHelper.RunInSTA(() =>
		{
			var owner = new Border();
			Window hostWindow = WPFTestHost.ShowInHostWindow(owner);

			try
			{
				bool tooltipShown = false;
				bool diagnosticShown = false;
				bool stateApplied = false;

				var controller = new TextHoverController(
					owner,
					_ => 5,
					_ => new TextHoverRequestState(
						ShouldRequestHover: true,
						RequestOffset: 5,
						CanShowToolTip: true,
						CanShowDiagnosticFallback: true,
						DiagnosticInfo: new TextEditorDiagnostic(TextEditorDiagnosticSeverity.Error, "diagnostic", 0, 1)),
					(offset, cancellationToken) => Task.FromResult<TextHoverInfo?>(
						new TextHoverInfo("hover", TextHoverContentKind.PlainText, "symbol")),
					_ => 5,
					_ => diagnosticShown = true,
					_ => tooltipShown = true,
					(info, diagnosticInfo) => { },
					_ => stateApplied = true);

				var eventArgs = new MouseEventArgs(Mouse.PrimaryDevice, 0)
				{
					RoutedEvent = Mouse.MouseMoveEvent
				};

				controller.Dispose();
				controller.HandleMouseHoverAsync(eventArgs).GetAwaiter().GetResult();

				// A disposed controller must not show hover or diagnostic tooltips or apply state.
				Assert.IsFalse(tooltipShown);
				Assert.IsFalse(diagnosticShown);
				Assert.IsFalse(stateApplied);
			}
			finally
			{
				hostWindow.Close();
			}
		});
	}

	[TestMethod]
	public void CancelPendingRequest_WhileRequestInFlight_DoesNotShowTooltip()
	{
		STATestHelper.RunInSTA(() =>
		{
			var owner = new Border();
			Window hostWindow = WPFTestHost.ShowInHostWindow(owner);

			try
			{
				bool tooltipShown = false;
				var completion = new TaskCompletionSource<TextHoverInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
				TextHoverController? controller = null;

				controller = new TextHoverController(
					owner,
					_ => 5,
					_ => new TextHoverRequestState(
						ShouldRequestHover: true,
						RequestOffset: 5,
						CanShowToolTip: true,
						CanShowDiagnosticFallback: false,
						DiagnosticInfo: null),
					(offset, cancellationToken) => completion.Task,
					_ => 5,
					_ => { },
					_ => tooltipShown = true,
					(info, diagnosticInfo) => { });

				var eventArgs = new MouseEventArgs(Mouse.PrimaryDevice, 0)
				{
					RoutedEvent = Mouse.MouseMoveEvent
				};

				Task hoverTask = controller.HandleMouseHoverAsync(eventArgs);
				controller.CancelPendingRequest();
				completion.TrySetResult(new TextHoverInfo("hover", TextHoverContentKind.PlainText, "symbol"));

				hoverTask.GetAwaiter().GetResult();

				// A cancelled in-flight request must not show a tooltip.
				Assert.IsFalse(tooltipShown);
			}
			finally
			{
				hostWindow.Close();
			}
		});
	}

	[TestMethod]
	public void CancelPendingRequest_And_InvalidateRequests_AfterDisposal_DoNotThrow()
	{
		STATestHelper.RunInSTA(() =>
		{
			var owner = new Border();

			var controller = new TextHoverController(
				owner,
				_ => 5,
				_ => new TextHoverRequestState(
					ShouldRequestHover: false,
					RequestOffset: -1,
					CanShowToolTip: false,
					CanShowDiagnosticFallback: false,
					DiagnosticInfo: null),
				(offset, cancellationToken) => Task.FromResult<TextHoverInfo?>(null),
				_ => 5,
				_ => { },
				_ => { },
				(info, diagnosticInfo) => { });

			controller.Dispose();
			controller.CancelPendingRequest();
			controller.InvalidateRequests();
		});
	}

	[TestMethod]
	public void HandleMouseHoverAsync_InFlightRequestCompletingAfterDisposal_DoesNotShowTooltip()
	{
		STATestHelper.RunInSTA(() =>
		{
			var owner = new Border();
			Window hostWindow = WPFTestHost.ShowInHostWindow(owner);

			try
			{
				bool tooltipShown = false;
				var completion = new TaskCompletionSource<TextHoverInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
				TextHoverController? controller = null;

				controller = new TextHoverController(
					owner,
					_ => 5,
					_ => new TextHoverRequestState(
						ShouldRequestHover: true,
						RequestOffset: 5,
						CanShowToolTip: true,
						CanShowDiagnosticFallback: true,
						DiagnosticInfo: null),
					(offset, cancellationToken) => completion.Task,
					_ => 5,
					_ => { },
					_ => tooltipShown = true,
					(info, diagnosticInfo) => { });

				var eventArgs = new MouseEventArgs(Mouse.PrimaryDevice, 0)
				{
					RoutedEvent = Mouse.MouseMoveEvent
				};

				Task hoverTask = controller.HandleMouseHoverAsync(eventArgs);
				controller.Dispose();
				completion.TrySetResult(new TextHoverInfo("hover", TextHoverContentKind.PlainText, "symbol"));

				hoverTask.GetAwaiter().GetResult();

				// A request that completes after disposal must not show a tooltip.
				Assert.IsFalse(tooltipShown);
			}
			finally
			{
				hostWindow.Close();
			}
		});
	}
}

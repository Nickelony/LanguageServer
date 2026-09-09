using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Hover;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Hover;
using System.Windows;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

[STATestClass]
public sealed class TextHoverControllerTests
{
	[TestMethod]
	public async Task HandleMouseHoverAsync_ThrowingProvider_ReportsTheDiagnosticFallbackWithoutThrowing()
	{
		var logger = new CapturingLogger();
		using var host = new HoverTestHost
		{
			BuildEvaluationState = _ => CreateState(canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic()),
			RequestHoverAsync = (_, _) => throw new InvalidOperationException("Hover failed.")
		};

		using var controller = host.CreateController(logger);
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// A provider failure must not propagate; available diagnostic content is shown instead, and the
		// failure carries the documented hover request event id.
		Assert.AreEqual(1, host.DisplayCalls.Count);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.AreEqual("diagnostic", host.DisplayCalls[0].DiagnosticInfo?.Message);
		Assert.AreEqual(1, logger.Entries.Count);
		Assert.AreEqual(1010, logger.Entries[0].EventId.Id);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_ThrowingOffsetResolution_LogsTheFailureWithoutDisplayDecision()
	{
		var logger = new CapturingLogger();
		using var host = new HoverTestHost
		{
			GetOffsetFromPoint = _ => throw new InvalidOperationException("Offset resolution failed.")
		};

		using var controller = host.CreateController(logger);

		// A failure before a hovered offset exists has no display target, so only the log reports it and
		// the host tooltip is not touched.
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		CollectionAssert.AreEqual(Array.Empty<(TextHoverInfo?, TextDiagnostic?)>(), host.DisplayCalls);
		CollectionAssert.AreEqual(Array.Empty<int>(), host.RequestOffsets);
		Assert.AreEqual(1, logger.Entries.Count);
		Assert.AreEqual(1010, logger.Entries[0].EventId.Id);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_WithoutCurrentRequestOffsetHook_UsesTheHoveredOffset()
	{
		using var host = new HoverTestHost();
		using var controller = host.CreateController();

		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// Without the optional hook the hovered offset doubles as the request offset.
		Assert.AreEqual(1, host.DisplayCalls.Count);
		Assert.AreEqual("hover", host.DisplayCalls[0].HoverInfo?.Content);
		Assert.IsNull(host.DisplayCalls[0].DiagnosticInfo);
		CollectionAssert.AreEqual(new[] { 5 }, host.RequestOffsets);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_ThrowingDisplayCallback_IsContained()
	{
		var logger = new CapturingLogger();
		using var host = new HoverTestHost
		{
			BuildEvaluationState = _ => CreateState(canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic()),
			RequestHoverAsync = (_, _) => throw new InvalidOperationException("Hover failed."),
			OnDisplay = (_, _) => throw new InvalidOperationException("Diagnostic tooltip failed.")
		};

		using var controller = host.CreateController(logger);

		// The provider failure falls back to the diagnostic tooltip, whose own failure must be contained
		// instead of escaping the hover evaluation, and reported with the host-callback event id.
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		Assert.AreEqual(1, host.DisplayCalls.Count);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.IsNotNull(host.DisplayCalls[0].DiagnosticInfo);

		// The provider failure is reported first, then the callback failure of the diagnostic fallback.
		Assert.AreEqual(2, logger.Entries.Count);
		Assert.AreEqual(1010, logger.Entries[0].EventId.Id);
		Assert.AreEqual(1011, logger.Entries[1].EventId.Id);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_ContextVersionProviderThrowing_IsContainedAndFallsBackToTheDiagnostic()
	{
		int contextVersionCalls = 0;

		using var host = new HoverTestHost
		{
			BuildEvaluationState = _ => CreateState(canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic()),
			ContextVersionProvider = () =>
			{
				contextVersionCalls++;

				// The first call captures the version before the request; the host's liveness check then
				// fails with a non-cancellation exception.
				if (contextVersionCalls > 1)
					throw new InvalidOperationException("The host session ended.");

				return 1;
			}
		};

		var logger = new CapturingLogger();
		using var controller = host.CreateController(logger);

		// A throwing liveness check is contained like a provider failure: the evaluation falls back to the
		// diagnostic tooltip allowed by the initial state and the failure is logged.
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		Assert.AreEqual(1, host.DisplayCalls.Count);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.AreEqual("diagnostic", host.DisplayCalls[0].DiagnosticInfo?.Message);
		Assert.AreEqual(1010, logger.Entries[0].EventId.Id);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_SuccessfulRequestWithFallbackDisabled_StillCombinesBoth()
	{
		using var host = new HoverTestHost
		{
			BuildEvaluationState = _ => CreateState(canShowDiagnosticFallback: false, diagnosticInfo: CreateDiagnostic())
		};

		using var controller = host.CreateController();
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// The fallback flag gates only the not-made and failed paths; a successful request combines the
		// hover content with the diagnostic even when the fallback is disabled.
		Assert.AreEqual(1, host.DisplayCalls.Count);
		Assert.AreEqual("hover", host.DisplayCalls[0].HoverInfo?.Content);
		Assert.AreEqual("diagnostic", host.DisplayCalls[0].DiagnosticInfo?.Message);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_SuccessfulRequestWithDiagnostic_DisplaysBothInOneCall()
	{
		using var host = new HoverTestHost
		{
			BuildEvaluationState = _ => CreateState(canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic())
		};

		using var controller = host.CreateController();
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// A successful hover next to diagnostic content is reported as one combined display decision.
		Assert.AreEqual(1, host.DisplayCalls.Count);
		Assert.AreEqual("hover", host.DisplayCalls[0].HoverInfo?.Content);
		Assert.AreEqual("diagnostic", host.DisplayCalls[0].DiagnosticInfo?.Message);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_HoverNotRequested_DisplaysTheDiagnosticFallbackAndSkipsTheProvider()
	{
		using var host = new HoverTestHost
		{
			BuildEvaluationState = _ => CreateState(
				shouldRequestHover: false,
				requestOffset: -1,
				canShowHoverContent: false,
				canShowDiagnosticFallback: true,
				diagnosticInfo: CreateDiagnostic(severity: TextDiagnosticSeverity.Warning))
		};

		using var controller = host.CreateController();
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// When hover is not applicable, only the diagnostic tooltip is shown and no provider call is made.
		Assert.AreEqual(1, host.DisplayCalls.Count);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.AreEqual("diagnostic", host.DisplayCalls[0].DiagnosticInfo?.Message);
		CollectionAssert.AreEqual(Array.Empty<int>(), host.RequestOffsets);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_AfterDisposal_ReportsNoDisplayDecision()
	{
		using var host = new HoverTestHost();
		using var controller = host.CreateController();

		controller.Dispose();
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// A disposed controller must not report hover, diagnostic, or hide decisions.
		CollectionAssert.AreEqual(Array.Empty<(TextHoverInfo?, TextDiagnostic?)>(), host.DisplayCalls);
		CollectionAssert.AreEqual(Array.Empty<int>(), host.RequestOffsets);
	}

	[TestMethod]
	public void CancelInFlightRequest_WhileRequestInFlight_CancelsTheTokenAndSuppressesTheResult()
	{
		var completion = new TaskCompletionSource<TextHoverInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var host = new HoverTestHost
		{
			RequestHoverAsync = (_, _) => completion.Task
		};

		using var controller = host.CreateController();

		Task hoverTask = controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());
		controller.CancelInFlightRequest();

		// The provider observes the cancellation through the token it received, and the completed result is
		// rejected even though the provider ignored the cancellation.
		Assert.IsTrue(host.RequestTokens[0].IsCancellationRequested);

		completion.TrySetResult(new TextHoverInfo("hover") { SymbolName = "symbol" });
		DispatcherTestUtils.PumpUntil(() => hoverTask.IsCompleted);
		hoverTask.GetAwaiter().GetResult();

		CollectionAssert.AreEqual(Array.Empty<(TextHoverInfo?, TextDiagnostic?)>(), host.DisplayCalls);
	}

	[TestMethod]
	public void InvalidateRequests_WhileRequestInFlight_RejectsTheResultWithoutCancelingTheToken()
	{
		var completion = new TaskCompletionSource<TextHoverInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var host = new HoverTestHost
		{
			RequestHoverAsync = (_, _) => completion.Task
		};

		using var controller = host.CreateController();

		Task hoverTask = controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());
		controller.InvalidateRequests();

		// Invalidation rejects outstanding results without asking the provider to stop.
		Assert.IsFalse(host.RequestTokens[0].IsCancellationRequested);

		completion.TrySetResult(new TextHoverInfo("hover") { SymbolName = "symbol" });
		DispatcherTestUtils.PumpUntil(() => hoverTask.IsCompleted);
		hoverTask.GetAwaiter().GetResult();

		CollectionAssert.AreEqual(Array.Empty<(TextHoverInfo?, TextDiagnostic?)>(), host.DisplayCalls);
	}

	[TestMethod]
	public void Dispose_WhileRequestInFlight_CancelsTheTokenAndSuppressesTheResult()
	{
		var completion = new TaskCompletionSource<TextHoverInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var host = new HoverTestHost
		{
			RequestHoverAsync = (_, _) => completion.Task
		};

		using var controller = host.CreateController();

		Task hoverTask = controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());
		controller.Dispose();

		// Disposal cancels pending work and a request that completes afterwards must not publish.
		Assert.IsTrue(host.RequestTokens[0].IsCancellationRequested);

		completion.TrySetResult(new TextHoverInfo("hover") { SymbolName = "symbol" });
		DispatcherTestUtils.PumpUntil(() => hoverTask.IsCompleted);
		hoverTask.GetAwaiter().GetResult();

		CollectionAssert.AreEqual(Array.Empty<(TextHoverInfo?, TextDiagnostic?)>(), host.DisplayCalls);
	}

	[TestMethod]
	public void CancelInFlightRequest_And_InvalidateRequests_AfterDisposal_AreNoOps()
	{
		using var host = new HoverTestHost();
		using var controller = host.CreateController();

		controller.Dispose();
		controller.CancelInFlightRequest();
		controller.InvalidateRequests();

		// The post-disposal calls are no-ops: no request is started and no decision is displayed.
		CollectionAssert.AreEqual(Array.Empty<int>(), host.RequestOffsets);
		CollectionAssert.AreEqual(Array.Empty<(TextHoverInfo?, TextDiagnostic?)>(), host.DisplayCalls);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_SupersededByNoOffset_CancelsTheInFlightTokenAndSuppressesTheResult()
	{
		var completion = new TaskCompletionSource<TextHoverInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var host = new HoverTestHost
		{
			RequestHoverAsync = (_, _) => completion.Task
		};

		using var controller = host.CreateController();

		Task firstTask = controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// The latest evaluation finds no hover target, which supersedes the in-flight request.
		host.GetOffsetFromPoint = static _ => null;
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		Assert.IsTrue(host.RequestTokens[0].IsCancellationRequested);

		// The older result must not be published after the latest evaluation superseded it; the only display
		// decision is the hide request of the newest evaluation.
		completion.TrySetResult(new TextHoverInfo("hover") { SymbolName = "symbol" });
		DispatcherTestUtils.PumpUntil(() => firstTask.IsCompleted);
		firstTask.GetAwaiter().GetResult();

		Assert.AreEqual(1, host.DisplayCalls.Count);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.IsNull(host.DisplayCalls[0].DiagnosticInfo);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_SupersededBySuppressedHover_CancelsTheInFlightTokenAndReportsHide()
	{
		var completion = new TaskCompletionSource<TextHoverInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var host = new HoverTestHost
		{
			RequestHoverAsync = (_, _) => completion.Task
		};

		using var controller = host.CreateController();

		Task firstTask = controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// The latest evaluation decides that no request should be made, which supersedes the in-flight one.
		host.BuildEvaluationState = _ => CreateState(shouldRequestHover: false, requestOffset: -1, canShowHoverContent: false);
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		Assert.IsTrue(host.RequestTokens[0].IsCancellationRequested);

		completion.TrySetResult(new TextHoverInfo("hover") { SymbolName = "symbol" });
		DispatcherTestUtils.PumpUntil(() => firstTask.IsCompleted);
		firstTask.GetAwaiter().GetResult();

		// The older result must not be published; the newest evaluation reports the hide decision.
		Assert.AreEqual(1, host.DisplayCalls.Count);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.IsNull(host.DisplayCalls[0].DiagnosticInfo);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_SuccessfulRequest_DisplaysTheHoverContent()
	{
		using var host = new HoverTestHost();
		using var controller = host.CreateController();

		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// The resolved hover content is reported through the display callback with no diagnostic.
		Assert.AreEqual(1, host.DisplayCalls.Count);
		Assert.AreEqual("hover", host.DisplayCalls[0].HoverInfo?.Content);
		Assert.IsNull(host.DisplayCalls[0].DiagnosticInfo);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_NoHoveredOffset_RequestsNothingAndReportsHide()
	{
		using var host = new HoverTestHost
		{
			GetOffsetFromPoint = static _ => null
		};

		using var controller = host.CreateController();
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// A hover that maps to no document offset supersedes pending work, requests nothing, and tells the
		// host to hide its tooltip.
		CollectionAssert.AreEqual(Array.Empty<int>(), host.RequestOffsets);
		Assert.AreEqual(1, host.DisplayCalls.Count);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.IsNull(host.DisplayCalls[0].DiagnosticInfo);
	}

	[TestMethod]
	public void HandleMouseHoverAsync_RequestOffsetHookReturningNull_DiscardsTheCompletedResult()
	{
		var completion = new TaskCompletionSource<TextHoverInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
		bool vetoRequestOffset = false;

		using var host = new HoverTestHost
		{
			RequestHoverAsync = (_, _) => completion.Task,
			ResolveRequestOffset = _ => vetoRequestOffset ? null : 5,

			// The pointer hook keeps the liveness re-check away from Mouse.GetPosition, whose WPF input
			// API requires the UI thread while the provider continuation may resume on a pool thread.
			GetCurrentPointerPosition = static () => new Point(0.0, 0.0)
		};

		using var controller = host.CreateController();

		Task hoverTask = controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// The host vetoes the position while the request is in flight; the null answer must not fall
		// back to the raw hovered offset, which equals the request offset here.
		vetoRequestOffset = true;
		completion.TrySetResult(new TextHoverInfo("hover") { SymbolName = "symbol" });
		DispatcherTestUtils.PumpUntil(() => hoverTask.IsCompleted);
		hoverTask.GetAwaiter().GetResult();

		// A rejected result produces no display decision at all, so the previous tooltip state is untouched.
		CollectionAssert.AreEqual(
			Array.Empty<(TextHoverInfo?, TextDiagnostic?)>(),
			host.DisplayCalls,
			string.Join("; ", host.DisplayCalls.Select(call => $"{(call.HoverInfo?.Content ?? "<null>")}|{(call.DiagnosticInfo?.Message ?? "<null>")}")));
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_RequestOffsetHookRemappingTheOffset_DiscardsTheCompletedResult()
	{
		using var host = new HoverTestHost
		{
			ResolveRequestOffset = static _ => 9
		};

		using var controller = host.CreateController();
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// The hook remaps the hovered offset to a different request offset, so the completed result no
		// longer matches the current request.
		CollectionAssert.AreEqual(Array.Empty<(TextHoverInfo?, TextDiagnostic?)>(), host.DisplayCalls);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_RequestOffsetHookRemappingTwoDistinctOffsets_RebuildsTheDisplayState()
	{
		int offsetCalls = 0;

		using var host = new HoverTestHost
		{
			// The initial evaluation hovers offset 5; the liveness re-check reports offset 9, which the
			// request-offset hook maps to the same request offset as offset 5.
			GetOffsetFromPoint = _ => offsetCalls++ == 0 ? 5 : 9,
			ResolveRequestOffset = static _ => 7,
			BuildEvaluationState = offset => offset == 5
				? CreateState(requestOffset: 7, canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic("first"))
				: CreateState(requestOffset: 7, canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic("second"))
		};

		using var controller = host.CreateController();
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// The pointer moved to a different hovered offset that maps to the same request, so the display
		// state is rebuilt from the latest evaluation instead of reusing the earlier state.
		Assert.AreEqual(1, host.DisplayCalls.Count);
		Assert.AreEqual("hover", host.DisplayCalls[0].HoverInfo?.Content);
		Assert.AreEqual("second", host.DisplayCalls[0].DiagnosticInfo?.Message);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_RemappedRequestOffsetWithoutHook_LogsWarningOnceWithEventId()
	{
		var logger = new CapturingLogger();
		using var host = new HoverTestHost
		{
			BuildEvaluationState = _ => CreateState(requestOffset: 7),
			RequestHoverAsync = static (_, _) => Task.FromResult<TextHoverInfo?>(null)
		};

		using var controller = host.CreateController(logger);

		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// A remapped request offset without the matching hook can never match the pointer position, so
		// the host is warned once with the documented hover event id instead of silently discarding every
		// completed result.
		Assert.AreEqual(1, logger.Entries.Count);
		Assert.AreEqual(Microsoft.Extensions.Logging.LogLevel.Warning, logger.Entries[0].Level);
		Assert.AreEqual(1012, logger.Entries[0].EventId.Id);
		StringAssert.Contains(logger.Messages[0], "ResolveRequestOffset");
	}

	[TestMethod]
	public void HandleMouseHoverAsync_WithPointerPositionHook_UsesTheHookPositionForTheLivenessCheck()
	{
		int offsetCalls = 0;
		Point livenessPosition = default;
		var completion = new TaskCompletionSource<TextHoverInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);

		using var host = new HoverTestHost
		{
			// The initial evaluation accepts the event position; the liveness re-check reports the
			// pointer as left the target, so the completed result must be discarded.
			GetOffsetFromPoint = point =>
			{
				if (offsetCalls++ == 0)
					return 5;

				livenessPosition = point;
				return null;
			},
			RequestHoverAsync = (_, _) => completion.Task,
			GetCurrentPointerPosition = static () => new Point(7.0, 7.0)
		};

		using var controller = host.CreateController();

		Task hoverTask = controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());
		completion.TrySetResult(new TextHoverInfo("hover") { SymbolName = "symbol" });
		DispatcherTestUtils.PumpUntil(() => hoverTask.IsCompleted);
		hoverTask.GetAwaiter().GetResult();

		// The pointer hook, not the mouse, supplied the re-check position.
		Assert.AreEqual(new Point(7.0, 7.0), livenessPosition);
		CollectionAssert.AreEqual(Array.Empty<(TextHoverInfo?, TextDiagnostic?)>(), host.DisplayCalls);
	}

	[TestMethod]
	public void HandleMouseHoverAsync_AsyncProviderWithoutSynchronizationContext_DisplaysOnTheOwnerThread()
	{
		var providerGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		int displayThreadId = 0;

		using var host = new HoverTestHost
		{
			RequestHoverAsync = async (_, _) =>
			{
				await providerGate.Task.ConfigureAwait(false);
				return new TextHoverInfo("hover") { SymbolName = "symbol" };
			}
		};

		int ownerThreadId = host.HostWindow.Dispatcher.Thread.ManagedThreadId;
		host.OnDisplay = (_, _) => displayThreadId = Environment.CurrentManagedThreadId;

		using var controller = host.CreateController();

		// The STA test thread carries no synchronization context, so the provider continuation resumes on a
		// thread-pool thread; the pointer resolution and the display callback must still run on the owner's
		// thread instead of touching mouse and tooltip state from there.
		Task hoverTask = controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		DispatcherTestUtils.PumpFrames(TimeSpan.FromMilliseconds(20.0));
		providerGate.SetResult();
		DispatcherTestUtils.PumpUntil(() => hoverTask.IsCompleted);

		Assert.AreEqual(1, host.DisplayCalls.Count);
		Assert.AreEqual(ownerThreadId, displayThreadId);
	}

	[TestMethod]
	public void HandleMouseHoverAsync_ContextVersionChangedWhileRequestInFlight_DoesNotShowTooltip()
	{
		int contextVersion = 1;
		var completion = new TaskCompletionSource<TextHoverInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);

		using var host = new HoverTestHost
		{
			RequestHoverAsync = (_, _) => completion.Task,
			ContextVersionProvider = () => contextVersion
		};

		using var controller = host.CreateController();

		Task hoverTask = controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// The host context version changes while the request is in flight, so the completed result
		// belongs to a context that no longer exists and must not be published.
		contextVersion = 2;
		completion.TrySetResult(new TextHoverInfo("hover") { SymbolName = "symbol" });
		DispatcherTestUtils.PumpUntil(() => hoverTask.IsCompleted);
		hoverTask.GetAwaiter().GetResult();

		CollectionAssert.AreEqual(Array.Empty<(TextHoverInfo?, TextDiagnostic?)>(), host.DisplayCalls);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_ContextVersionProviderThrowingOperationCanceled_EndsSilently()
	{
		int contextVersionCalls = 0;

		using var host = new HoverTestHost
		{
			BuildEvaluationState = _ => CreateState(canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic()),
			ContextVersionProvider = () =>
			{
				contextVersionCalls++;

				// The first call captures the version before the request; the host's session ends by the
				// time the completed result is checked, so the check cancels the evaluation.
				if (contextVersionCalls > 1)
					throw new OperationCanceledException();

				return 1;
			}
		};

		using var controller = host.CreateController();

		// The explicit cancellation must not escape and must not fall back to the diagnostic tooltip,
		// even though fallback content is available.
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		CollectionAssert.AreEqual(Array.Empty<(TextHoverInfo?, TextDiagnostic?)>(), host.DisplayCalls);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_ProviderThrowingOperationCanceled_EndsWithoutDisplay()
	{
		var logger = new CapturingLogger();
		using var host = new HoverTestHost
		{
			RequestHoverAsync = static (_, _) => throw new OperationCanceledException()
		};

		using var controller = host.CreateController(logger);
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// A canceled provider reports cancellation rather than a failure: nothing is displayed and nothing
		// is logged.
		CollectionAssert.AreEqual(Array.Empty<(TextHoverInfo?, TextDiagnostic?)>(), host.DisplayCalls);
		CollectionAssert.AreEqual(Array.Empty<string>(), logger.Messages);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_CompletedRequestWithHoverContentForbidden_ReportsHide()
	{
		using var host = new HoverTestHost
		{
			BuildEvaluationState = _ => CreateState(canShowHoverContent: false, diagnosticInfo: CreateDiagnostic())
		};

		using var controller = host.CreateController();
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// A completed request whose display state forbids hover content resolves to a hide decision, even
		// when a diagnostic was resolved alongside it.
		Assert.AreEqual(1, host.DisplayCalls.Count);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.IsNull(host.DisplayCalls[0].DiagnosticInfo);
		CollectionAssert.AreEqual(new[] { 5 }, host.RequestOffsets);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_ThrowingProviderWithoutDiagnosticFallback_ReportsHide()
	{
		var logger = new CapturingLogger();
		using var host = new HoverTestHost
		{
			RequestHoverAsync = (_, _) => throw new InvalidOperationException("Hover failed.")
		};

		using var controller = host.CreateController(logger);
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// Without fallback permission the failure resolves to a hide decision, and the failure is still
		// reported with the documented request event id.
		Assert.AreEqual(1, host.DisplayCalls.Count);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.IsNull(host.DisplayCalls[0].DiagnosticInfo);
		Assert.AreEqual(1, logger.Entries.Count);
		Assert.AreEqual(1010, logger.Entries[0].EventId.Id);
	}

	[TestMethod]
	public void Constructor_NullOwnerOrHooks_ThrowsArgumentNullException()
	{
		using var host = new HoverTestHost();
		var hooks = new TextHoverControllerHooks
		{
			GetOffsetFromPoint = static _ => 5,
			BuildEvaluationState = _ => CreateState(),
			RequestHoverAsync = static (_, _) => Task.FromResult<TextHoverInfo?>(null),
			ShowToolTip = static (_, _) => { }
		};

		Assert.ThrowsExactly<ArgumentNullException>(() => new TextHoverController(null!, hooks));
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextHoverController(host.Owner, null!));
	}

	[TestMethod]
	public void Constructor_EachNullRequiredHook_ThrowsArgumentNullException()
	{
		using var host = new HoverTestHost();

		ArgumentNullException offsetNull = Assert.ThrowsExactly<ArgumentNullException>(() => new TextHoverController(host.Owner, new TextHoverControllerHooks
		{
			GetOffsetFromPoint = null!,
			BuildEvaluationState = static _ => new TextHoverEvaluationState(true, 5, true, false, null),
			RequestHoverAsync = static (_, _) => Task.FromResult<TextHoverInfo?>(null),
			ShowToolTip = static (_, _) => { }
		}));
		StringAssert.Contains(offsetNull.ParamName, "GetOffsetFromPoint");

		ArgumentNullException stateNull = Assert.ThrowsExactly<ArgumentNullException>(() => new TextHoverController(host.Owner, new TextHoverControllerHooks
		{
			GetOffsetFromPoint = static _ => 5,
			BuildEvaluationState = null!,
			RequestHoverAsync = static (_, _) => Task.FromResult<TextHoverInfo?>(null),
			ShowToolTip = static (_, _) => { }
		}));
		StringAssert.Contains(stateNull.ParamName, "BuildEvaluationState");

		ArgumentNullException requestNull = Assert.ThrowsExactly<ArgumentNullException>(() => new TextHoverController(host.Owner, new TextHoverControllerHooks
		{
			GetOffsetFromPoint = static _ => 5,
			BuildEvaluationState = static _ => new TextHoverEvaluationState(true, 5, true, false, null),
			RequestHoverAsync = null!,
			ShowToolTip = static (_, _) => { }
		}));
		StringAssert.Contains(requestNull.ParamName, "RequestHoverAsync");

		ArgumentNullException showNull = Assert.ThrowsExactly<ArgumentNullException>(() => new TextHoverController(host.Owner, new TextHoverControllerHooks
		{
			GetOffsetFromPoint = static _ => 5,
			BuildEvaluationState = static _ => new TextHoverEvaluationState(true, 5, true, false, null),
			RequestHoverAsync = static (_, _) => Task.FromResult<TextHoverInfo?>(null),
			ShowToolTip = null!
		}));
		StringAssert.Contains(showNull.ParamName, "ShowToolTip");
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_NullEventArgs_ThrowsArgumentNullException()
	{
		using var host = new HoverTestHost();
		using var controller = host.CreateController();

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => controller.HandleMouseHoverAsync(null!));
	}

	private static TextHoverEvaluationState CreateState(
		bool shouldRequestHover = true,
		int requestOffset = 5,
		bool canShowHoverContent = true,
		bool canShowDiagnosticFallback = false,
		TextDiagnostic? diagnosticInfo = null)
		=> new(shouldRequestHover, requestOffset, canShowHoverContent, canShowDiagnosticFallback, diagnosticInfo);

	private static TextDiagnostic CreateDiagnostic(
		string message = "diagnostic",
		TextDiagnosticSeverity severity = TextDiagnosticSeverity.Error)
		=> new(severity, message, 0, 1);
}

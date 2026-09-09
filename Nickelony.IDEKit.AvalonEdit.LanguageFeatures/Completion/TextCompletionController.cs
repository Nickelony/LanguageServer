using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.IDEKit.AvalonEdit.Documents;
using Nickelony.IDEKit.Core.Requests;
using Nickelony.IDEKit.Infrastructure;
using Nickelony.IDEKit.IntelliSense.Completion;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

/// <summary>
/// Coordinates completion request scheduling, window lifecycle, sizing, tooltip presentation, and the
/// commit-character input policy.
/// The window lifecycle is delegated to a <see cref="CompletionWindowCoordinator"/> the controller creates
/// for the text area; it shows at most one window and replaces it when a new one is opened. The tooltip
/// pipeline is delegated to an internal presenter, and the request lifetime runs on the shared core request
/// coordinator behind the <see cref="Requests"/> session.
/// </summary>
/// <remarks>
/// <para>
/// The controller must be created and used on the thread that owns the text area, because it touches the text
/// area's document and the completion window directly and its debounce timers run on the text area's
/// dispatcher. Provider request continuations are marshalled back to that thread before the decision is
/// applied, even when the creating thread captured no synchronization context.
/// </para>
/// <para>
/// The controller installs the commit-character input policy on the text area while it is alive (see
/// <see cref="TextCompletionControllerOptions.AcceptOnCommitCharacters"/>): a typed character that the
/// selected item declares through <see cref="ICommitCharacterCompletionData"/> commits the item while the
/// character itself is typed, so one keystroke both accepts the item and inserts the character. Disposal
/// removes the policy with the controller's other subscriptions.
/// </para>
/// </remarks>
public sealed class TextCompletionController : IDisposable
{
	// A query that cannot match a completion item; it forces AvalonEdit's completion list to invalidate its
	// memoized query when a refresh has to display a replaced item set for an empty query (see RefreshWindow).
	private const string FilterResetQuery = "\0";

	private readonly TextArea _textArea;
	private readonly TextCompletionControllerOptions _options;
	private readonly Action<CompletionWindow>? _configureWindow;
	private readonly Func<TextCompletionItem, ICompletionData>? _completionItemFactory;
	private readonly Func<ICompletionData, (string Text, string? Detail)>? _getDisplayInfo;
	private readonly Func<ICompletionData, double>? _measureItemWidth;
	private readonly CompletionRequestScheduler _requestScheduler;
	private readonly CompletionToolTipPresenter _toolTipPresenter;
	private bool _isDisposed;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextCompletionController"/> class.
	/// </summary>
	/// <param name="textArea">The text area the controller serves.</param>
	/// <param name="skin">The skin applied to created completion windows.</param>
	/// <param name="options">The controller options, or <see langword="null"/> to use the defaults.</param>
	/// <param name="hooks">The optional host hooks, or <see langword="null"/> when the host needs none.</param>
	/// <param name="logger">An optional logger for request failures.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textArea"/> or <paramref name="skin"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The supplied options cannot fit the minimum content width plus the horizontal window chrome into the
	/// maximum window width, or a supplied tooltip skin contains a non-finite offset or a non-finite or
	/// negative thickness.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The caller is not on the thread that owns <paramref name="textArea"/>.
	/// </exception>
	public TextCompletionController(
		TextArea textArea,
		CompletionWindowSkin skin,
		TextCompletionControllerOptions? options = null,
		TextCompletionControllerHooks? hooks = null,
		ILogger? logger = null)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentNullException.ThrowIfNull(skin);

		// The controller touches the text area's document and its windows directly and its debounce timers
		// run on the text area's dispatcher, so creating it off the text area's thread is a caller error.
		textArea.Dispatcher.VerifyAccess();

		ILogger resolvedLogger = logger ?? NullLogger.Instance;
		_textArea = textArea;
		_options = options ?? TextCompletionControllerOptions.Default;

		ValidateOptions(_options);
		ValidateToolTipSkin(hooks?.ToolTipSkin, nameof(hooks));

		WindowCoordinator = new CompletionWindowCoordinator(textArea, skin);

		_configureWindow = hooks?.ConfigureWindow;
		_completionItemFactory = hooks?.CompletionItemFactory;
		_getDisplayInfo = hooks?.GetDisplayInfo;
		_measureItemWidth = hooks?.MeasureItemWidth;

		// The presenter arms its resolve timer on construction, so debounced description updates work
		// even when the host schedules no requests.
		_toolTipPresenter = new CompletionToolTipPresenter(
			_options,
			hooks?.ToolTipSkin ?? CompletionToolTipSkin.Default,
			textArea.Dispatcher,
			() => ActiveWindow,
			SetToolTipState,
			hooks,
			resolvedLogger);

		_requestScheduler = new CompletionRequestScheduler(
			_options,
			hooks?.ScheduledRequestAsync,
			SetRequestScheduled,
			resolvedLogger);
		Requests = new TextCompletionRequestSession();

		// The coordinator belongs to this controller and raises the event for every shown window it closes,
		// so the controller observes window closures regardless of whether the host, the user, or a
		// replacement closed the window.
		WindowCoordinator.WindowClosed += HandleTrackedWindowClosed;

		// The commit-character policy runs on both text-input stages; the handlers document why each
		// stage is needed.
		_textArea.PreviewTextInput += HandlePreviewTextInput;
		_textArea.TextEntering += HandleTextEntering;
	}

	// The individual option values validate themselves when they are assigned; only the relationship between
	// the content-space floor and the window-space maximum still needs a check that spans three options.
	private static void ValidateOptions(TextCompletionControllerOptions options)
	{
		if (options.WindowMaxWidth < options.WindowMinContentWidth + options.WindowHorizontalChrome)
		{
			throw new ArgumentOutOfRangeException(
				nameof(options),
				options.WindowMaxWidth,
				"The maximum window width must be large enough for the minimum content width plus the horizontal window chrome.");
		}
	}

	// The tooltip skin comes from the hooks argument, so a validation failure names "hooks" - the argument the
	// caller actually passed - instead of the unrelated window skin parameter.
	private static void ValidateToolTipSkin(CompletionToolTipSkin? skin, string parameterName)
	{
		if (skin is null)
			return;

		if (!double.IsFinite(skin.HorizontalOffset))
			throw new ArgumentOutOfRangeException(parameterName, skin.HorizontalOffset, "The tooltip skin horizontal offset must be finite.");

		ValidateThickness(skin.BorderThickness, parameterName);
		ValidateThickness(skin.Padding, parameterName);

		static void ValidateThickness(Thickness thickness, string parameterName)
		{
			if (!NumericValidation.IsFiniteNonNegative(thickness.Left)
				|| !NumericValidation.IsFiniteNonNegative(thickness.Top)
				|| !NumericValidation.IsFiniteNonNegative(thickness.Right)
				|| !NumericValidation.IsFiniteNonNegative(thickness.Bottom))
			{
				throw new ArgumentOutOfRangeException(parameterName, thickness, "The tooltip skin thickness values must be finite and non-negative.");
			}
		}
	}

	/// <summary>
	/// Gets the current completion presentation state.
	/// </summary>
	public TextCompletionPresentationState CurrentPresentation { get; private set; } = TextCompletionPresentationState.Empty;

	/// <summary>
	/// Gets the completion window coordinator this controller created for the text area, so a host can observe
	/// the managed window and its closures and can close the window without going through the controller.
	/// </summary>
	/// <remarks>
	/// The controller owns the window lifecycle: windows are created, configured, sized, and shown only by the
	/// controller through <see cref="OpenOrRefresh"/> (or its decision-applying callers), so the coordinator's
	/// reported window and <see cref="CurrentPresentation"/> always describe the same window. Use
	/// <see cref="CloseWindow"/> to end the session with the controller's pending-work cleanup, or
	/// <c>WindowCoordinator.Close()</c> for a pure window close; both raise
	/// <see cref="CompletionWindowCoordinator.WindowClosed"/> for a shown window.
	/// </remarks>
	public CompletionWindowCoordinator WindowCoordinator { get; }

	/// <summary>
	/// Gets the request-tracking session for hosts that drive the completion request pipeline themselves
	/// instead of using <see cref="RequestAsync"/>: it begins and tracks requests, reports their
	/// cancellation token, and cancels or invalidates outstanding results.
	/// </summary>
	/// <remarks>
	/// The session shares its request lifetime with <see cref="RequestAsync"/>, so a manually driven
	/// request and a standard request supersede each other. Disposing the controller ends the session.
	/// </remarks>
	public TextCompletionRequestSession Requests { get; }

	/// <summary>
	/// Gets a value indicating whether the referenced AvalonEdit version exposes the completion tooltip this
	/// controller styles and resolves descriptions for. The value is constant for the process.
	/// </summary>
	/// <remarks>
	/// This is the package's version-compat probe: the window's tooltip is reached through a reflected private
	/// field, so a host can query whether the running AvalonEdit still exposes it before relying on description
	/// tooltips. When the field is missing, description tooltips degrade (one warning is logged with event
	/// id 1002) while every other feature keeps working.
	/// </remarks>
	public static bool IsToolTipSupported => CompletionWindowToolTipAccess.IsFieldAvailable;

	/// <summary>
	/// Gets the currently tracked completion window, or <see langword="null"/> when none is tracked or
	/// the controller has been disposed.
	/// </summary>
	public CompletionWindow? ActiveWindow => _isDisposed ? null : WindowCoordinator.ActiveWindow;

	/// <summary>
	/// Schedules a completion request to run after the configured debounce delay. Has no effect when the
	/// controller's hooks carry no <see cref="TextCompletionControllerHooks.ScheduledRequestAsync"/> callback.
	/// </summary>
	/// <remarks>
	/// Arming again while a request is scheduled restarts the debounce delay with the newest schedule call.
	/// The scheduled callback runs on the text area's dispatcher; a failure it throws is logged and leaves the
	/// tracked state intact, and a cancellation it reports is ignored.
	/// </remarks>
	public void ScheduleRequest()
		=> _requestScheduler.ScheduleRequest();

	/// <summary>
	/// Cancels the debounced completion request scheduled by <see cref="ScheduleRequest"/>, if one is pending.
	/// The in-flight provider request is not affected; use
	/// <see cref="TextCompletionRequestSession.CancelInFlightRequest"/> or
	/// <see cref="TextCompletionRequestSession.InvalidateRequests"/> on <see cref="Requests"/> for that.
	/// </summary>
	public void CancelScheduledRequest()
		=> _requestScheduler.CancelScheduledRequest();

	/// <summary>
	/// Closes the active completion window and hides any completion tooltip.
	/// </summary>
	/// <remarks>
	/// Closing does not invalidate in-flight requests: a completion result that was already requested can still
	/// reopen a window when the host applies its decision afterwards. A host that treats the close as the end of
	/// the completion session calls <see cref="TextCompletionRequestSession.InvalidateRequests"/> (or
	/// <see cref="TextCompletionRequestSession.CancelInFlightRequest"/> when the provider should stop early) on
	/// <see cref="Requests"/> together with this method.
	/// </remarks>
	public void CloseWindow()
	{
		if (_isDisposed)
			return;

		CloseWindowCore();
	}

	private void CloseWindowCore()
	{
		CancelPendingCompletionWork();

		if (WindowCoordinator.ActiveWindow is CompletionWindow completionWindow)
			CompletionToolTipPresenter.CloseToolTip(completionWindow);

		WindowCoordinator.Close();
		ResetWindowPresentation();
	}

	// A tracked window can close itself through a commit, Escape, or focus loss. The coordinator no longer
	// tracks a window afterwards, so stale window or tooltip state must be cleared.
	private void HandleTrackedWindowClosed(object? sender, EventArgs e)
	{
		CancelPendingCompletionWork();
		ResetWindowPresentation();
	}

	private void CancelPendingCompletionWork()
	{
		_requestScheduler.CancelScheduledRequest();
		_toolTipPresenter.CancelUpdate();
	}

	private void ResetWindowPresentation()
	{
		SetWindowState(false);
		SetToolTipState(null, false);
	}

	/// <summary>
	/// Opens a completion window with the given items for the replacement range, or refreshes the tracked
	/// window in place when its replacement start is unchanged. The window sizes to its content up to the
	/// configured maximum height.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The replacement range uses zero-based document offsets with an exclusive end offset. A raw offset beyond the
	/// document end is clamped to the document length, so the replacement range can never extend past the current
	/// document. A negative offset or a raw end offset before the raw start offset is rejected instead, because
	/// such a range is always a caller error. Both offsets are compared before clamping, so an out-of-document
	/// start and end are only accepted when they are ordered correctly.
	/// </para>
	/// <para>
	/// A refresh keeps the open window when its replacement start still matches the supplied start: its items and
	/// end offset are replaced and the sizing and window hook re-applied, so per-keystroke completions do not
	/// recreate the window. A different start closes the old window and opens a new one, which also covers a
	/// window whose anchors moved with an edit. The supplied items are the candidate set for the typed word;
	/// AvalonEdit's completion list re-ranks and filters them against the text between the two offsets. An empty
	/// item collection returns <see langword="false"/> without opening or refreshing a window, and - unless
	/// <see cref="TextCompletionControllerOptions.CloseWhenEmpty"/> is disabled - a window whose list becomes
	/// empty after filtering is closed rather than showing AvalonEdit's empty template. After an open, the
	/// controller establishes the initial item selection on the dispatcher at <c>ContextIdle</c> priority (a
	/// refresh re-selects synchronously as part of its re-filtering), so the best-matching item is selected and
	/// the tooltip reflects the typed query; an item the provider marked as preselected through
	/// <see cref="TextCompletionItem.IsPreselected"/> (exposed through
	/// <see cref="IPreselectedCompletionData"/> and implemented by the default adapter) wins over the best
	/// match when it survived the filtering and is scrolled into view, because it is the provider's explicit
	/// answer to the query. Preselection is deliberate library policy rather than an option, because the
	/// selection drives both the tooltip and the default commit target.
	/// </para>
	/// </remarks>
	/// <param name="items">The completion items to show.</param>
	/// <param name="startOffset">The zero-based start offset of the replacement range.</param>
	/// <param name="endOffset">The exclusive end offset of the replacement range.</param>
	/// <returns>
	/// <see langword="true"/> when the operation was applied (the window was opened or refreshed), even when the
	/// window closes itself right afterwards because its filtered list became empty - the close is part of the
	/// operation; otherwise, <see langword="false"/>. A text area without a document reports
	/// <see langword="false"/> as well, because the replacement range cannot be anchored.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="items"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="startOffset"/> or <paramref name="endOffset"/> is negative, or <paramref name="endOffset"/> is
	/// smaller than <paramref name="startOffset"/>.
	/// </exception>
	public bool OpenOrRefresh(IEnumerable<ICompletionData> items, int startOffset, int endOffset)
	{
		ArgumentNullException.ThrowIfNull(items);
		ArgumentOutOfRangeException.ThrowIfNegative(startOffset, nameof(startOffset));
		ArgumentOutOfRangeException.ThrowIfNegative(endOffset, nameof(endOffset));

		// The raw offsets are compared before clamping so a malformed range is rejected even when both raw
		// offsets would clamp to the same document offset.
		if (endOffset < startOffset)
			throw new ArgumentOutOfRangeException(nameof(endOffset), endOffset, "The end offset must not be smaller than the start offset.");

		if (_isDisposed)
			return false;

		ICompletionData[] completionItems = items.ToArray();

		if (completionItems.Length == 0)
			return false;

		// A detached document (a text area can outlive its document) cannot anchor the replacement range,
		// so the operation reports no-op instead of failing.
		TextDocument? document = _textArea.Document;

		if (document is null)
			return false;

		int clampedStartOffset = document.ClampOffset(startOffset);
		int clampedEndOffset = document.ClampOffset(endOffset);

		// A refresh keeps the open window while the replacement start is unchanged; the window re-anchors its
		// start on edits, so a shifted anchor falls back to a complete reopen.
		if (WindowCoordinator.ActiveWindow is CompletionWindow activeWindow && activeWindow.StartOffset == clampedStartOffset)
			return RefreshWindow(activeWindow, completionItems, clampedEndOffset);

		return OpenWindow(completionItems, clampedStartOffset, clampedEndOffset);
	}

	private bool OpenWindow(ICompletionData[] completionItems, int startOffset, int endOffset)
	{
		CloseWindow();
		CompletionWindow completionWindow = WindowCoordinator.Create(maxHeight: _options.WindowMaxHeight);

		try
		{
			_toolTipPresenter.Style(completionWindow);
			ConfigureNonActivatingWindow(completionWindow);
			completionWindow.StartOffset = startOffset;
			completionWindow.EndOffset = endOffset;

			foreach (ICompletionData item in completionItems)
				completionWindow.CompletionList.CompletionData.Add(item);

			ResizeWindow(completionWindow, completionItems);

			// The hook runs after the controller applied its baseline sizing, offsets, and items, immediately
			// before the window is shown, so anything the hook sets wins.
			_configureWindow?.Invoke(completionWindow);

			WindowCoordinator.Show();
		}
		catch
		{
			// A throwing host hook or a failed show must not strand a created window or leave stale state behind.
			WindowCoordinator.Close();
			ResetWindowPresentation();
			throw;
		}

		SetWindowState(WindowCoordinator.IsWindowOpen);
		ScheduleInitialSelection();
		return true;
	}

	private bool RefreshWindow(CompletionWindow completionWindow, ICompletionData[] completionItems, int endOffset)
	{
		try
		{
			// The visible tooltip describes the previously selected item, so it is invalidated together with
			// the item set; the selection change below re-arms the tooltip update for the new selection.
			_toolTipPresenter.CancelUpdate();
			CompletionToolTipPresenter.CloseToolTip(completionWindow);

			completionWindow.EndOffset = endOffset;

			IList<ICompletionData> completionData = completionWindow.CompletionList.CompletionData;
			completionData.Clear();

			foreach (ICompletionData item in completionItems)
				completionData.Add(item);

			ResizeWindow(completionWindow, completionItems);
			_configureWindow?.Invoke(completionWindow);

			// AvalonEdit's completion list returns early when it is asked to select the query it already
			// applied, which would leave the filtered list of the previous item set visible. The empty query
			// resets the list to every new item and the real query re-filters it, but the list memoizes the
			// empty query like any other, so a refresh whose query is empty must first invalidate that memo
			// with a query that cannot match; otherwise both calls are no-ops and the replaced item set is
			// never displayed. The sequence relies on two AvalonEdit 6.3 behaviors: the memoized early return
			// in SelectItem, and the ItemsSource swap to a filtered collection while IsFiltering is enabled
			// (the default); re-verify both before changing the call sequence.
			CompletionList completionList = completionWindow.CompletionList;
			string query = GetCompletionWindowQuery(completionWindow);

			if (query.Length == 0)
				completionList.SelectItem(FilterResetQuery);

			completionList.SelectItem(string.Empty);
			completionList.SelectItem(query);
			ApplyPreselection(completionWindow);

			CloseWindowIfEmpty();
		}
		catch
		{
			// A throwing host hook during a refresh must not leave a half-updated window behind.
			CloseWindow();
			throw;
		}

		SetWindowState(WindowCoordinator.IsWindowOpen);
		return true;
	}

	// The completion window is shown non-activatable by default, so the click handler selects the clicked item
	// explicitly; the selection change is also what schedules the tooltip update. The handler is harmless when
	// WPF already selected the item natively, and it is skipped when the host opts into an activatable window,
	// where the native selection path applies.
	private void ConfigureNonActivatingWindow(CompletionWindow completionWindow)
	{
		if (!_options.NonActivatingWindow)
			return;

		CompletionWindowInterop.MakeNonActivatable(completionWindow);

		ListBox listBox = completionWindow.CompletionList.ListBox;
		listBox.PreviewMouseLeftButtonUp += (s, e) => HandleCompletionListClick(listBox, e);
	}

	/// <summary>
	/// Runs a completion request through the standard pipeline: the callback computes the decision (including
	/// any provider round trips), and the controller admits the request, rejects a superseded or canceled
	/// result, and applies the decision.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the convenience entry point for hosts that follow the standard completion scenario: call it
	/// from the trigger policy and let the callback produce the decision from the shared completion-session
	/// kernel or its own provider logic. The request is admitted through the shared core request coordinator
	/// behind <see cref="Requests"/>, so starting it cancels the previous request's token - whether that
	/// request was another standard request or a manually driven one - and a callback that completes after a
	/// newer request started or after <see cref="TextCompletionRequestSession.CancelInFlightRequest"/> was
	/// called is discarded, even when the provider ignored the token.
	/// </para>
	/// <para>
	/// The callback may observe the supplied cancellation token and may report cancellation by throwing
	/// <see cref="OperationCanceledException"/> or letting the provider throw it, which this method reports as
	/// <see langword="false"/>. Any other failure is contained the same way a failure on the scheduled path is:
	/// it is logged with event id 1000 and the method reports <see langword="false"/>, so timer, dispatcher, and
	/// event-handler callers never observe provider exceptions.
	/// </para>
	/// </remarks>
	/// <param name="requestAsync">The callback that computes the decision for this request.</param>
	/// <returns>
	/// <see langword="true"/> when the request was applied and opened or refreshed a completion window;
	/// <see langword="false"/> when the request was superseded, canceled, failed, or produced no window.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="requestAsync"/> is <see langword="null"/>.</exception>
	public async Task<bool> RequestAsync(
		Func<CancellationToken, Task<TextCompletionSessionDecision>> requestAsync)
	{
		ArgumentNullException.ThrowIfNull(requestAsync);

		if (_isDisposed)
			return false;

		bool decisionApplied = false;

		// The coordinator admits the request as the latest one, cancels the superseded request's token, and
		// classifies the outcome: a superseded or canceled request is never applied, even when its provider
		// ignored the token. The admission check and the apply step run after the provider's await, which
		// resumes on a thread-pool thread when the creating thread captured no synchronization context, so
		// they are marshalled to the text area thread explicitly instead of trusting the ambient context.
		Task<RequestOutcome> request = Requests.Coordinator.RunAsync(
			state: requestAsync,
			computeAsync: static (callback, cancellationToken) => callback(cancellationToken),
			canApply: (_, _) => !_isDisposed,
			apply: decision => decisionApplied = DispatcherInvocation.Run(
				_textArea.Dispatcher,
				() => ApplyDecision(decision)));

		try
		{
			RequestOutcome outcome = await request.ConfigureAwait(true);

			// The run completes only after the decision was applied, so the recorded result carries whether
			// the applied decision actually produced a window.
			return outcome == RequestOutcome.Completed && decisionApplied;
		}
		catch (OperationCanceledException)
		{
			// A superseded or canceled request reports cancellation, not a failure.
			return false;
		}
		catch (Exception exception)
		{
			// The callback runs inside a controller-owned pipeline (a trigger handler, a command, or the
			// scheduled path), so provider and host-callback failures are contained and logged instead of
			// faulting the caller, matching the hover and signature help controllers.
			if (!_isDisposed)
				_requestScheduler.LogRequestFailed(exception);

			return false;
		}
	}

	/// <summary>
	/// Applies a completion session decision. A requested close is performed first; when the decision
	/// requests items, the method maps them and opens or refreshes a completion window for the decision's
	/// replacement range.
	/// </summary>
	/// <remarks>
	/// Items are mapped through the <see cref="TextCompletionControllerHooks.CompletionItemFactory"/> constructor
	/// hook when it is set, and through the package's default <see cref="TextCompletionItemCompletionData"/> adapter
	/// otherwise, so the standard scenario works without a host mapper. A host that already holds shared
	/// <see cref="TextCompletionItem"/> instances opens them directly with
	/// <c>ApplyDecision(TextCompletionSessionDecision.Open(items, startOffset, endOffset))</c> and gets the
	/// same mapping. An empty item list returns <see langword="false"/> without mapping anything. The shared
	/// decision type guarantees that a decision carrying items also carries a valid, ordered replacement
	/// range, and <see cref="OpenOrRefresh"/> clamps offsets beyond the document end.
	/// </remarks>
	/// <param name="decision">The decision to apply.</param>
	/// <returns>
	/// <see langword="true"/> when a completion window was opened or refreshed; otherwise, <see langword="false"/>.
	/// </returns>
	/// <exception cref="InvalidOperationException">
	/// The item factory returned <see langword="null"/> for one of the decision's items.
	/// </exception>
	public bool ApplyDecision(TextCompletionSessionDecision decision)
	{
		if (_isDisposed)
			return false;

		if (decision.ShouldClose)
			CloseWindow();

		// A decision without items requests no session, and an empty list would not open a window.
		if (decision.Items is not { Count: > 0 } decisionItems)
			return false;

		// The shared decision type ties the replacement range to the items, so both offsets are present
		// whenever items are.
		if (decision.StartOffset is not int startOffset || decision.EndOffset is not int endOffset)
			return false;

		Func<TextCompletionItem, ICompletionData> itemFactory = _completionItemFactory ?? CreateDefaultCompletionData;

		var items = new ICompletionData[decisionItems.Count];

		for (int i = 0; i < decisionItems.Count; i++)
		{
			ICompletionData item = itemFactory(decisionItems[i]);

			// A null return violates the factory contract in a way no caller can act on as a parameter
			// error, so it is reported as an invalid operation naming the offending item.
			if (item is null)
				throw new InvalidOperationException($"The completion item factory returned null for the item '{decisionItems[i].Label}'.");

			items[i] = item;
		}

		return OpenOrRefresh(items, startOffset, endOffset);
	}

	private static TextCompletionItemCompletionData CreateDefaultCompletionData(TextCompletionItem item) => new(item);

	/// <summary>
	/// Schedules the completion window to close if it is empty.
	/// </summary>
	/// <remarks>
	/// Empty completion windows are closed instead of showing AvalonEdit's empty template. The close check is
	/// posted to the text area's dispatcher and runs asynchronously, so the window may still be open when this
	/// method returns.
	/// </remarks>
	public void ScheduleCloseIfEmpty()
	{
		if (_isDisposed)
			return;

		_textArea.Dispatcher.BeginInvoke(new Action(() => CloseWindowIfEmpty()), DispatcherPriority.Background);
	}

	/// <summary>
	/// Cancels any pending completion tooltip update.
	/// </summary>
	public void CancelToolTipUpdate()
	{
		if (_isDisposed)
			return;

		_toolTipPresenter.CancelUpdate();
	}

	// Test seam: lets the tooltip debounce be observed behaviorally without reflecting private fields.
	internal bool IsToolTipUpdatePending => _toolTipPresenter.IsUpdatePending;

	/// <summary>
	/// Stops completion scheduling and closes any open completion window or tooltip.
	/// </summary>
	/// <remarks>
	/// Disposal is synchronous: the debounce timers stop, an in-flight request token is canceled and
	/// invalidated, and the open window is closed. The controller does not start or continue any work
	/// afterwards, and a host that inspects <see cref="CurrentPresentation"/> after disposal sees the state of
	/// the closed session (no window, no scheduled request, no tooltip).
	/// </remarks>
	public void Dispose()
	{
		if (_isDisposed)
			return;

		_isDisposed = true;
		WindowCoordinator.WindowClosed -= HandleTrackedWindowClosed;
		_textArea.PreviewTextInput -= HandlePreviewTextInput;
		_textArea.TextEntering -= HandleTextEntering;
		_requestScheduler.Dispose();
		Requests.Dispose();
		_toolTipPresenter.Dispose();
		CloseWindowCore();
	}

	private static void HandleCompletionListClick(ListBox listBox, MouseButtonEventArgs e)
	{
		if (e.Handled || e.OriginalSource is not DependencyObject originalSource)
			return;

		// ContainerFromElement resolves the item container from any clicked element, including non-visual
		// content such as a Run in an inline-based item template. Walking the visual tree instead would fail
		// because VisualTreeHelper rejects elements that are not Visuals.
		ListBoxItem? listBoxItem = ItemsControl.ContainerFromElement(listBox, originalSource) as ListBoxItem;
		object? item = listBoxItem?.DataContext;

		if (item is null)
			return;

		// The non-activatable window keeps the text area's keyboard focus, so the click selects the item
		// explicitly; selecting a different item raises SelectionChanged, which schedules the tooltip update.
		if (!ReferenceEquals(listBox.SelectedItem, item))
			listBox.SelectedItem = item;

		listBox.ScrollIntoView(item);
	}

	// The commit-character policy is installed on both text-input stages. The preview stage runs before any
	// text-composition subscriber (for example an auto-closing service) acts on the character, so a commit
	// character is accepted deterministically however the host ordered its input services; the text-entering
	// stage repeats the check for input that bypasses the preview stage, which is how
	// TextArea.PerformTextInput delivers programmatic input. Neither handler consumes the event: the typed
	// character is still inserted after the committed text, so one keystroke both accepts the item and types
	// the character.
	private void HandlePreviewTextInput(object sender, TextCompositionEventArgs e)
		=> TryCommitOnCommitCharacter(e);

	private void HandleTextEntering(object sender, TextCompositionEventArgs e)
		=> TryCommitOnCommitCharacter(e);

	private void TryCommitOnCommitCharacter(TextCompositionEventArgs e)
	{
		if (_isDisposed || !_options.AcceptOnCommitCharacters || e.Handled)
			return;

		// Commit characters are single characters; any other input (for example a multi-character
		// composition result) never matches, so the window stays open.
		if (e.Text.Length != 1)
			return;

		if (WindowCoordinator.ActiveWindow is not CompletionWindow completionWindow)
			return;

		if (completionWindow.CompletionList.SelectedItem is not ICommitCharacterCompletionData { CommitCharacters.Count: > 0 } completionData)
			return;

		if (!ContainsCommitCharacter(completionData.CommitCharacters, e.Text))
			return;

		// The list closes the window and completes the selected item before the text pipeline inserts the
		// typed character at the caret the completion left behind.
		completionWindow.CompletionList.RequestInsertion(e);
	}

	private static bool ContainsCommitCharacter(IReadOnlyList<string> commitCharacters, string typedText)
	{
		for (int index = 0; index < commitCharacters.Count; index++)
		{
			if (string.Equals(commitCharacters[index], typedText, StringComparison.Ordinal))
				return true;
		}

		return false;
	}

	private void SetRequestScheduled(bool isRequestScheduled)
		=> UpdatePresentationState(CurrentPresentation with { IsRequestScheduled = isRequestScheduled });

	private void SetWindowState(bool hasOpenWindow)
		=> UpdatePresentationState(CurrentPresentation with { IsListVisible = hasOpenWindow });

	private void SetToolTipState(object? toolTipContent, bool isToolTipVisible)
		=> UpdatePresentationState(CurrentPresentation with { DetailContent = toolTipContent, IsDetailVisible = isToolTipVisible });

	private void UpdatePresentationState(TextCompletionPresentationState state)
		=> CurrentPresentation = state;

	private void ResizeWindow(CompletionWindow completionWindow, ICompletionData[] items)
	{
		double requiredWidth = CompletionWindowSizing.MeasureRequiredWidth(
			_textArea,
			_options,
			_getDisplayInfo,
			_measureItemWidth,
			items);

		// MeasureRequiredWidth already floors the content width at WindowMinContentWidth, so only the maximum
		// can bind here; the chrome is added on top of the measured content width.
		completionWindow.Width = Math.Min(_options.WindowMaxWidth, requiredWidth + _options.WindowHorizontalChrome);
	}

	private void ScheduleInitialSelection()
		=> _textArea.Dispatcher.BeginInvoke(new Action(SelectInitialItem), DispatcherPriority.ContextIdle);

	private void SelectInitialItem()
	{
		if (ActiveWindow is not CompletionWindow completionWindow)
			return;

		completionWindow.CompletionList.SelectItem(GetCompletionWindowQuery(completionWindow));
		ApplyPreselection(completionWindow);
		CloseWindowIfEmpty();
	}

	// A provider may mark one item as preselected (LSP's "preselect"); when such an item survived the
	// filtering it becomes the selection, because it is the server's explicit answer to the query. Without a
	// hint the library keeps AvalonEdit's best-match selection. The flag is read through
	// IPreselectedCompletionData, so the default adapter and custom host data follow the same policy. The
	// hint is re-applied on an in-place refresh as well, because a refresh replaces the item set with a fresh
	// provider answer - the newest answer to the current query - and a replaced item set cannot carry the
	// previously selected item over either way. The selection is scrolled into view explicitly: AvalonEdit's
	// selected-item setter does not scroll while its own selection path centers the best match, so a
	// preselected item ranked below the visible window would otherwise stay invisible.
	private static void ApplyPreselection(CompletionWindow completionWindow)
	{
		ItemCollection items = completionWindow.CompletionList.ListBox.Items;

		for (int index = 0; index < items.Count; index++)
		{
			if (items[index] is not ICompletionData completionItem)
				continue;

			if (completionItem is not IPreselectedCompletionData { IsPreselected: true })
				continue;

			completionWindow.CompletionList.ListBox.SelectedItem = completionItem;
			completionWindow.CompletionList.ScrollIntoView(completionItem);
			return;
		}
	}

	private void CloseWindowIfEmpty()
	{
		if (!_options.CloseWhenEmpty)
			return;

		if (ActiveWindow is not CompletionWindow completionWindow)
			return;

		if (completionWindow.CompletionList.ListBox.HasItems)
			return;

		CloseWindow();
	}

	private string GetCompletionWindowQuery(CompletionWindow completionWindow)
	{
		// A detached document has no text to query, so the query is empty; otherwise both offsets are
		// clamped to the current document so an edit between the window update and this query cannot
		// produce an out-of-range read.
		TextDocument? document = _textArea.Document;

		if (document is null)
			return string.Empty;

		int startOffset = document.ClampOffset(completionWindow.StartOffset);
		int endOffset = Math.Max(startOffset, document.ClampOffset(completionWindow.EndOffset));

		return endOffset > startOffset
			? document.GetText(startOffset, endOffset - startOffset)
			: string.Empty;
	}
}

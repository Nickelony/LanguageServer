using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.IntelliSense.Completion;
using System.Windows.Controls;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

/// <summary>
/// Groups the optional host hooks used by the <see cref="TextCompletionController"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every hook is optional. A controller without hooks still opens windows, tracks presentation state,
/// maps decision items through the default completion data adapter, and shows the synchronous
/// descriptions that AvalonEdit provides, but it schedules requests only when
/// <see cref="ScheduledRequestAsync"/> is supplied, and it cannot configure the window and tooltip or
/// resolve asynchronous tooltip descriptions.
/// </para>
/// <para>
/// Host hooks should not throw. Failures inside <see cref="ResolveDescriptionAsync"/> are contained and
/// logged, so they cannot escape timer or dispatcher callbacks; failures inside the other hooks propagate to
/// the caller that opened or refreshed the window, after the tracked state is reset and the created window is
/// closed (an open window is closed as well when the failure happens during a refresh). The individual
/// properties document their exact rollback scope.
/// </para>
/// </remarks>
public sealed class TextCompletionControllerHooks
{
	/// <summary>
	/// Gets the hook that configures a completion window before it is shown, or reconfigures an open window
	/// when a refresh replaces its items.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The hook is invoked after the controller applied its baseline window settings (sizing, replacement
	/// offsets, and items) and immediately before the window is shown, so any value the hook sets - including
	/// <see cref="System.Windows.FrameworkElement.Width"/> - wins over the controller's sizing. The hook runs
	/// again when an open window is refreshed in place, after the new items were applied.
	/// </para>
	/// <para>
	/// The controller measures the window width from the editor's font configuration before the hook runs,
	/// so a host that renders items with a different font sizes the window for the editor's font unless it
	/// supplies <see cref="MeasureItemWidth"/>; setting the window font in this hook does not change the
	/// measurement.
	/// </para>
	/// <para>
	/// An exception it throws propagates to the <see cref="TextCompletionController.OpenOrRefresh"/> caller
	/// after the window is closed and the tracked state is reset.
	/// </para>
	/// </remarks>
	public Action<CompletionWindow>? ConfigureWindow { get; init; }

	/// <summary>
	/// Gets the hook that configures the completion tooltip after the controller applies its skin.
	/// </summary>
	/// <remarks>
	/// The controller applies the <see cref="ToolTipSkin"/> chrome before invoking this hook, so a host can
	/// override any of those values for its own skin (the tooltip keeps AvalonEdit's open-on-hover behavior).
	/// An exception the hook throws propagates to the <see cref="TextCompletionController.OpenOrRefresh"/> caller
	/// after the window is closed (the created window, or the open window when the failure happens during a
	/// refresh) and the tracked state is reset. When the AvalonEdit tooltip cannot be resolved, this hook is not
	/// invoked.
	/// </remarks>
	public Action<ToolTip>? ConfigureToolTip { get; init; }

	/// <summary>
	/// Gets the factory that maps provider items to completion data, or <see langword="null"/> for the
	/// package's default <see cref="TextCompletionItemCompletionData"/> adapter.
	/// </summary>
	/// <remarks>
	/// The factory is the single mapping seam for decision items: it applies wherever the controller maps
	/// shared items to completion data (<see cref="TextCompletionController.ApplyDecision"/> and
	/// <see cref="TextCompletionController.RequestAsync"/>). A factory that returns <see langword="null"/> for
	/// an item makes the mapping fail with <see cref="System.InvalidOperationException"/>, because the item is
	/// neither skippable nor representable; a host should only map shapes it can actually present.
	/// </remarks>
	public Func<TextCompletionItem, ICompletionData>? CompletionItemFactory { get; init; }

	/// <summary>
	/// Gets the hook that resolves the selected item's description asynchronously.
	/// </summary>
	/// <remarks>
	/// The hook is invoked for every selected item after the item's synchronous
	/// <see cref="ICompletionData.Description"/> was applied. The returned content replaces the currently shown
	/// tooltip content; returning <see langword="null"/> content hides the tooltip instead. Hosts whose items
	/// only have a synchronous description do not set this hook, or return the item's current description to
	/// keep it visible. The token is canceled when a newer tooltip update starts, when the completion window is
	/// closed, and when the controller is disposed; implementations should observe it and cancel pending work.
	/// </remarks>
	public Func<ICompletionData, CancellationToken, Task<object?>>? ResolveDescriptionAsync { get; init; }

	/// <summary>
	/// Gets the completion tooltip skin, or <see langword="null"/> for <see cref="CompletionToolTipSkin.Default"/>.
	/// </summary>
	/// <remarks>
	/// The skin describes the chrome the controller applies to AvalonEdit's completion tooltip (placement,
	/// offset, border, padding, and the two optional brushes); <see cref="ConfigureToolTip"/> runs after the
	/// skin and can override any value the skin applied (and style whatever the record does not cover). The
	/// skin is validated when the controller is constructed.
	/// </remarks>
	public CompletionToolTipSkin? ToolTipSkin { get; init; }

	/// <summary>
	/// Gets the hook that supplies the display text and detail used for width measurement.
	/// </summary>
	public Func<ICompletionData, (string Text, string? Detail)>? GetDisplayInfo { get; init; }

	/// <summary>
	/// Gets the hook that measures the content width of a completion item, replacing the default
	/// measurement (display text plus the optional icon column and detail gap).
	/// </summary>
	/// <remarks>
	/// <para>
	/// The hook returns the item's content width in device-independent pixels, excluding
	/// <see cref="TextCompletionControllerOptions.WindowHorizontalChrome"/>. Items are measured until the
	/// content width the window can display is reached, and the window width is the largest measurement (or
	/// <see cref="TextCompletionControllerOptions.WindowMinContentWidth"/> when nothing is measured) plus the
	/// chrome, clamped between the minimum content width and the maximum window width.
	/// </para>
	/// <para>
	/// The returned width must be finite; a non-finite value is rejected with
	/// <see cref="InvalidOperationException"/> because it would silently turn the window width into an
	/// auto-sized value. A negative width cannot widen the window and is ignored.
	/// </para>
	/// <para>
	/// This hook lets hosts with a different item template measure their own layout; when it is omitted, the
	/// default measurement uses <see cref="TextCompletionControllerOptions.ItemIconWidth"/>,
	/// <see cref="TextCompletionControllerOptions.ItemDetailSpacing"/>, and the display information hook.
	/// </para>
	/// </remarks>
	public Func<ICompletionData, double>? MeasureItemWidth { get; init; }

	/// <summary>
	/// Gets the callback a scheduled completion request runs, or <see langword="null"/> when the controller
	/// should not schedule requests.
	/// </summary>
	/// <remarks>
	/// The controller hands this callback to its request scheduler, so
	/// <see cref="TextCompletionController.ScheduleRequest"/> runs it after the configured debounce delay when
	/// the host's trigger policy calls it; without the callback, scheduling is a no-op and the host drives
	/// requests only through <see cref="TextCompletionController.RequestAsync"/> or the
	/// <see cref="TextCompletionController.Requests"/> session. The callback typically invokes
	/// <see cref="TextCompletionController.RequestAsync"/> from a host field or property, because the hooks
	/// object is constructed before the controller it belongs to. A failure the callback throws is logged and
	/// leaves the tracked state intact, and a cancellation it reports is ignored.
	/// </remarks>
	public Func<Task>? ScheduledRequestAsync { get; init; }
}

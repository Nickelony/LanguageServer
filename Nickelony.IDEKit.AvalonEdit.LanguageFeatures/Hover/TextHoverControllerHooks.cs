using Nickelony.IDEKit.IntelliSense.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Hover;
using System.Windows;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Hover;

/// <summary>
/// Groups the required and optional host hooks used by the <see cref="TextHoverController"/>. The
/// controller reports every display decision through one hook.
/// </summary>
/// <remarks>
/// <para>
/// The offset resolution, request-state, request, and tooltip hooks (<see cref="GetOffsetFromPoint"/>,
/// <see cref="BuildEvaluationState"/>, <see cref="RequestHoverAsync"/>, and <see cref="ShowToolTip"/>) are
/// required; the request-offset, pointer-position, and context-version hooks are optional. See the
/// individual properties for the hook contracts.
/// </para>
/// <para>
/// Display is a single decision, so the controller exposes one tooltip sink instead of separate
/// diagnostic, hover, and combined callbacks: it calls <see cref="ShowToolTip"/> with the hover content,
/// the diagnostic, both, or neither, and the host renders or hides in one place.
/// </para>
/// </remarks>
public sealed class TextHoverControllerHooks
{
	/// <summary>
	/// Gets the hook that resolves the zero-based document offset for a point in the owner, or
	/// <see langword="null"/> when the point does not map to a hoverable target (for example a margin area).
	/// </summary>
	public required Func<Point, int?> GetOffsetFromPoint { get; init; }

	/// <summary>
	/// Gets the hook that builds the hover evaluation state for a hovered offset.
	/// </summary>
	/// <remarks>
	/// It is invoked once per hover evaluation, plus once more when a completed result is about to be accepted
	/// and the pointer maps to a hovered offset different from the evaluated one, so it should be cheap or cache
	/// its result.
	/// </remarks>
	public required Func<int, TextHoverEvaluationState> BuildEvaluationState { get; init; }

	/// <summary>
	/// Gets the hook that resolves hover information asynchronously for an offset.
	/// </summary>
	public required Func<int, CancellationToken, Task<TextHoverInfo?>> RequestHoverAsync { get; init; }

	/// <summary>
	/// Gets the optional hook that resolves the request offset for a hovered offset. Returning
	/// <see langword="null"/> vetoes the completed result even when the hovered offset itself did not change;
	/// the hook must agree with <see cref="BuildEvaluationState"/> so a completed result can be matched to the
	/// latest hover target.
	/// </summary>
	/// <remarks>
	/// When the hook is omitted, the controller uses the hovered offset itself as the request offset, which
	/// matches the common case where <see cref="BuildEvaluationState"/> requests hover for the hovered offset.
	/// Hosts that gate or remap the hover target (for example to only resolve complete words) supply the
	/// hook so a completed result is discarded when the pointer no longer maps to the same target.
	/// </remarks>
	public Func<int, int?>? ResolveRequestOffset { get; init; }

	/// <summary>
	/// Gets the optional hook that resolves the current pointer position in the owner's coordinate space.
	/// </summary>
	/// <remarks>
	/// The hook uses the position to re-check that the pointer still hovers the same offset before the
	/// controller publishes a completed request. The hook is invoked once per accepted result after the request
	/// completes, so it should be cheap. When it is omitted, the current mouse position against the owner is
	/// used; hosts that drive hover from pen, touch, or synthetic input supply the hook so the liveness
	/// check follows the same pointer source as the hover events.
	/// </remarks>
	public Func<Point>? GetCurrentPointerPosition { get; init; }

	/// <summary>
	/// Gets the hook that shows or hides the host's hover tooltip for a hover evaluation.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The controller calls this once for every concluded hover evaluation with what should be displayed: a
	/// hover tooltip, a diagnostic tooltip, or both. Passing <see langword="null"/> for both arguments means
	/// the evaluation has nothing to show - the pointer left the hover target, the request state disallows the
	/// tooltip, or the request failed - and the host should hide its hover tooltip instead of leaving stale
	/// content visible. Displaying and hiding live in one callback, so the newest evaluation always wins.
	/// </para>
	/// <para>
	/// The controller never touches host UI directly, so a host that also hides on pointer-leave events must
	/// keep its hide path idempotent. The callback should not throw; a failure escaping it is contained and
	/// logged like a request failure.
	/// </para>
	/// </remarks>
	public required Action<TextHoverInfo?, TextDiagnostic?> ShowToolTip { get; init; }

	/// <summary>
	/// Gets the optional provider of a host context version that guards asynchronous hover work.
	/// </summary>
	/// <remarks>
	/// The controller captures the version before a request starts and discards the completed result when the
	/// version changed while the request was in flight. The provider must be cheap to call and return a value
	/// that changes when pending work admitted for an earlier host state must be abandoned, for example a
	/// document session, load, or replacement generation counter.
	/// </remarks>
	public Func<int>? ContextVersionProvider { get; init; }
}

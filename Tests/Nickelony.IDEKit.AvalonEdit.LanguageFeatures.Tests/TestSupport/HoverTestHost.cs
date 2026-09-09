using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Hover;
using Nickelony.IDEKit.IntelliSense.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Hover;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

/// <summary>
/// Hosts a hover controller over a test border and records every display decision and provider call, so
/// hover tests share one host instead of repeating the full hook set.
/// </summary>
/// <remarks>
/// The properties are mutable on purpose: a test starts from the defaults and swaps in the hook behavior it
/// needs, and the recorder lists observe what the controller reported. <see cref="DisplayCalls"/> records
/// every display decision including the hide requests, so neither a stale tooltip nor an unexpected hide can
/// pass unnoticed.
/// </remarks>
internal sealed class HoverTestHost : IDisposable
{
	/// <summary>
	/// Initializes a new instance of the <see cref="HoverTestHost"/> class and shows the owner border in a
	/// host window.
	/// </summary>
	public HoverTestHost()
	{
		Owner = new Border();
		HostWindow = WPFTestHost.ShowInHostWindow(Owner);
	}

	/// <summary>
	/// Gets the element hover positions are resolved against.
	/// </summary>
	public Border Owner { get; }

	/// <summary>
	/// Gets the host window that owns the border; dispose the host to close it.
	/// </summary>
	public Window HostWindow { get; }

	/// <summary>
	/// Gets or sets the offset resolution hook. Defaults to the constant offset <c>5</c>.
	/// </summary>
	public Func<Point, int?> GetOffsetFromPoint { get; set; } = static _ => 5;

	/// <summary>
	/// Gets or sets the request-state hook. Defaults to a requesting state at offset <c>5</c> that allows a
	/// tooltip and has no diagnostic and no fallback.
	/// </summary>
	public Func<int, TextHoverEvaluationState> BuildEvaluationState { get; set; } = static _ => new TextHoverEvaluationState(
		ShouldRequestHover: true,
		RequestOffset: 5,
		CanShowHoverContent: true,
		CanShowDiagnosticFallback: false,
		DiagnosticInfo: null);

	/// <summary>
	/// Gets or sets the provider hook. Defaults to an immediately completing hover result without a diagnostic.
	/// </summary>
	public Func<int, CancellationToken, Task<TextHoverInfo?>> RequestHoverAsync { get; set; } =
		static (_, _) => Task.FromResult<TextHoverInfo?>(new TextHoverInfo("hover") { SymbolName = "symbol" });

	/// <summary>
	/// Gets or sets the optional request-offset hook, or <see langword="null"/> when the host does not remap.
	/// </summary>
	public Func<int, int?>? ResolveRequestOffset { get; set; }

	/// <summary>
	/// Gets or sets the optional pointer-position hook, or <see langword="null"/> to use the current mouse
	/// position.
	/// </summary>
	public Func<Point>? GetCurrentPointerPosition { get; set; }

	/// <summary>
	/// Gets or sets the optional context-version provider, or <see langword="null"/> when the host version
	/// never changes.
	/// </summary>
	public Func<int>? ContextVersionProvider { get; set; }

	/// <summary>
	/// Gets the display decisions the controller reported, in order, including hide requests
	/// (<see langword="null"/> content and no diagnostic).
	/// </summary>
	public List<(TextHoverInfo? HoverInfo, TextDiagnostic? DiagnosticInfo)> DisplayCalls { get; } = [];

	/// <summary>
	/// Gets the offsets the controller requested hover for, in order.
	/// </summary>
	public List<int> RequestOffsets { get; } = [];

	/// <summary>
	/// Gets the cancellation tokens the controller passed to the provider hook, in order.
	/// </summary>
	public List<CancellationToken> RequestTokens { get; } = [];

	/// <summary>
	/// Gets or sets an optional callback invoked after a display decision has been recorded, for example to
	/// throw and exercise the failure containment.
	/// </summary>
	public Action<TextHoverInfo?, TextDiagnostic?>? OnDisplay { get; set; }

	/// <summary>
	/// Creates a hover controller wired to this host's hooks.
	/// </summary>
	/// <param name="logger">The optional logger for controller failures.</param>
	/// <returns>The created controller.</returns>
	public TextHoverController CreateController(Microsoft.Extensions.Logging.ILogger? logger = null)
		=> new(
			Owner,
			new TextHoverControllerHooks
			{
				GetOffsetFromPoint = point => GetOffsetFromPoint(point),
				BuildEvaluationState = offset => BuildEvaluationState(offset),
				RequestHoverAsync = (offset, cancellationToken) =>
				{
					RequestOffsets.Add(offset);
					RequestTokens.Add(cancellationToken);
					return RequestHoverAsync(offset, cancellationToken);
				},
				ResolveRequestOffset = ResolveRequestOffset is null
					? null
					: offset => ResolveRequestOffset(offset),
				GetCurrentPointerPosition = GetCurrentPointerPosition,
				ContextVersionProvider = ContextVersionProvider,
				ShowToolTip = (hoverInfo, diagnosticInfo) =>
				{
					DisplayCalls.Add((hoverInfo, diagnosticInfo));
					OnDisplay?.Invoke(hoverInfo, diagnosticInfo);
				}
			},
			logger);

	/// <summary>
	/// Creates the mouse-hover event arguments the controller handles in these tests.
	/// </summary>
	/// <returns>The event arguments.</returns>
	public static MouseEventArgs CreateMouseEventArgs() => new(Mouse.PrimaryDevice, 0)
	{
		RoutedEvent = Mouse.MouseMoveEvent
	};

	/// <inheritdoc/>
	public void Dispose() => HostWindow.Close();
}

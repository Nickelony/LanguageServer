using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

/// <summary>
/// Describes the chrome the completion controller applies to AvalonEdit's completion tooltip.
/// </summary>
/// <remarks>
/// <para>
/// The skin covers what the controller places and colors: the tooltip is anchored to the right of the item
/// list by default, with zero border and padding, and the nullable brushes leave the WPF theme's tooltip
/// colors in place (AvalonEdit itself sets no tooltip colors). Every non-nullable value - placement, offset,
/// border, and padding - is applied unconditionally, so a host that wants different chrome overrides these
/// values or configures the tooltip in the <see cref="TextCompletionControllerHooks.ConfigureToolTip"/> hook.
/// That hook runs after the skin, so it can override any value the skin applied and style whatever the record
/// does not cover.
/// </para>
/// <para>
/// The presenter anchors the tooltip to the item list rather than to the completion window (AvalonEdit's
/// stock placement target). The default template fills the window with the list, so both anchors render
/// identically.
/// </para>
/// <para>
/// The chrome is applied to the tooltip AvalonEdit creates for each completion window. When a referenced
/// AvalonEdit version no longer exposes that tooltip, a host cannot see the skin at all; the stock tooltip
/// is used unchanged and the one-time access warning is logged.
/// </para>
/// </remarks>
public sealed record CompletionToolTipSkin
{
	/// <summary>
	/// Gets the side of the item list the tooltip is placed on. Defaults to
	/// <see cref="PlacementMode.Right"/>.
	/// </summary>
	public PlacementMode Placement { get; init; } = PlacementMode.Right;

	/// <summary>
	/// Gets the horizontal offset between the item list and the tooltip in device-independent pixels.
	/// Must be finite; an invalid value is rejected when the controller is constructed. Defaults to 10.
	/// </summary>
	public double HorizontalOffset { get; init; } = 10.0;

	/// <summary>
	/// Gets the tooltip border thickness. Must be finite and non-negative; an invalid value is rejected
	/// when the controller is constructed. Defaults to zero.
	/// </summary>
	public Thickness BorderThickness { get; init; }

	/// <summary>
	/// Gets the tooltip padding. Must be finite and non-negative; an invalid value is rejected when the
	/// controller is constructed. Defaults to zero.
	/// </summary>
	public Thickness Padding { get; init; }

	/// <summary>
	/// Gets the tooltip background brush, or <see langword="null"/> to keep the WPF theme's tooltip
	/// background.
	/// </summary>
	public Brush? Background { get; init; }

	/// <summary>
	/// Gets the tooltip border brush, or <see langword="null"/> to keep the WPF theme's tooltip border.
	/// </summary>
	public Brush? BorderBrush { get; init; }

	/// <summary>
	/// Gets the default tooltip skin: placed to the right of the item list, offset by 10 device-independent
	/// pixels, with zero border and padding and the WPF theme's tooltip colors.
	/// </summary>
	public static CompletionToolTipSkin Default { get; } = new();
}

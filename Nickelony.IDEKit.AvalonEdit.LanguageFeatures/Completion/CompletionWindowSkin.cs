using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

/// <summary>
/// Describes the default chrome applied to a created completion window.
/// </summary>
/// <param name="BorderBrush">The window border brush.</param>
/// <param name="Background">The window background brush.</param>
/// <param name="Foreground">The window foreground brush.</param>
/// <param name="BorderThickness">The window border thickness. Defaults to <c>1</c>.</param>
/// <remarks>
/// The skin supplies the window chrome - its border thickness and colors; item templates keep their own
/// styling. The window is never user-resizable and keeps AvalonEdit's borderless window style, so a host
/// that wants a different frame shape overrides it in the
/// <see cref="TextCompletionControllerHooks.ConfigureWindow"/> hook. The brushes are assigned to the window
/// when it is created, so hosts that share them across threads should freeze them.
/// </remarks>
public sealed record CompletionWindowSkin(
	Brush BorderBrush,
	Brush Background,
	Brush Foreground,
	double BorderThickness = 1.0);

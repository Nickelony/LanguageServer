using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;

/// <summary>
/// Describes the default colors applied to a created code-action menu.
/// </summary>
/// <param name="BorderBrush">The menu border brush.</param>
/// <param name="Background">The menu background brush.</param>
/// <param name="Foreground">The menu foreground brush.</param>
/// <remarks>
/// The skin only supplies the menu chrome colors; the standard WPF menu items keep their own styling,
/// so the highlight of the selected item follows the system menu highlight, matching how the
/// completion window's item templates keep their own styling. The brushes are assigned to the menu
/// when it is created, so hosts that share them across threads should freeze them.
/// </remarks>
public sealed record TextCodeActionMenuSkin(
	Brush BorderBrush,
	Brush Background,
	Brush Foreground);

using Nickelony.IDEKit.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.TextMate.Tests;

internal static class TextMateThemeTestHelpers
{
	internal static TextMateTokenTheme CreateTheme(params TextMateTokenThemeRule[] rules)
		=> new() { Rules = rules };

	internal static string GetForegroundColor(TextRunStyle style)
	{
		Assert.IsNotNull(style.Foreground);
		return ((SolidColorBrush)style.Foreground).Color.ToString();
	}
}

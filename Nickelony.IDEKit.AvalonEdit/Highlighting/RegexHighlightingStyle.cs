using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;

namespace Nickelony.IDEKit.AvalonEdit.Highlighting;

/// <summary>
/// Describes the declarative style applied to a <see cref="RegexHighlightingRule"/>. The color is
/// kept as a string so empty or malformed values can use a fallback color when the style is converted
/// to an AvalonEdit <see cref="HighlightingColor"/>.
/// </summary>
/// <param name="HtmlColor">A color string accepted by WPF's <see cref="ColorConverter"/>; empty or malformed values fall back.</param>
/// <param name="IsBold">Whether the highlighted text is bold.</param>
/// <param name="IsItalic">Whether the highlighted text is italic.</param>
public sealed record RegexHighlightingStyle(string? HtmlColor = null, bool IsBold = false, bool IsItalic = false)
{
	/// <summary>
	/// Converts the style to an AvalonEdit highlighting color, falling back to
	/// <paramref name="fallbackColor"/> when the color value is empty or malformed.
	/// </summary>
	/// <param name="fallbackColor">The color used when the color value cannot be parsed.</param>
	/// <returns>The converted highlighting color.</returns>
	public HighlightingColor ToHighlightingColor(Color fallbackColor)
	{
		Color foreground = TryParseColor(HtmlColor, out Color color) ? color : fallbackColor;

		return new HighlightingColor
		{
			Foreground = new SimpleHighlightingBrush(foreground),
			FontWeight = IsBold ? FontWeights.Bold : FontWeights.Normal,
			FontStyle = IsItalic ? FontStyles.Italic : FontStyles.Normal
		};
	}

	private static bool TryParseColor(string? colorValue, out Color color)
	{
		color = default;

		if (string.IsNullOrWhiteSpace(colorValue))
			return false;

		try
		{
			if (ColorConverter.ConvertFromString(colorValue) is Color parsedColor)
			{
				color = parsedColor;
				return true;
			}
		}
		catch (FormatException)
		{ }
		catch (NotSupportedException)
		{ }

		return false;
	}
}

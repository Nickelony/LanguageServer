using ICSharpCode.AvalonEdit.Highlighting;
using System.Windows;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.Highlighting;

/// <summary>
/// Defines the color and font styles for text matched by a <see cref="RegexHighlightingRule"/>.
/// The color is parsed during conversion. <see langword="null"/>, blank, or invalid values use the fallback.
/// </summary>
/// <param name="HtmlColor">
/// A color string accepted by WPF's <see cref="ColorConverter"/>.
/// <see langword="null"/>, whitespace-only, or invalid values use the fallback color.
/// </param>
/// <param name="IsBold">Whether matched text uses a bold font weight.</param>
/// <param name="IsItalic">Whether matched text uses an italic font style.</param>
public sealed record RegexHighlightingStyle(string? HtmlColor = null, bool IsBold = false, bool IsItalic = false)
{
	/// <summary>
	/// Converts this style to an AvalonEdit <see cref="HighlightingColor"/>.
	/// Missing or invalid color values use <paramref name="fallbackColor"/>.
	/// </summary>
	/// <param name="fallbackColor">The foreground color used when the configured color cannot be parsed.</param>
	/// <returns>A highlighting color with the resolved foreground color and configured font settings.</returns>
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

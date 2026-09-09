using ICSharpCode.AvalonEdit.Highlighting;
using Nickelony.IDEKit.Infrastructure;
using System.Windows;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.Highlighting;

/// <summary>
/// Defines the color and font styles for text matched by a <see cref="RegexHighlightingRule"/>
/// or highlighted by a <see cref="RegexHighlightingSpan"/>.
/// </summary>
/// <remarks>
/// <para>
/// The color is parsed during conversion.
/// A missing or blank color leaves the foreground unset so the editor's theme color is used,
/// and an invalid value leaves it unset too unless a fallback color is supplied.
/// </para>
/// <para>
/// Bold and italic are applied only when requested, so other styles can still be inherited.
/// </para>
/// </remarks>
/// <param name="ColorText">
/// A color specification string accepted by WPF's <see cref="ColorConverter"/>, for example
/// <c>#AARRGGBB</c> or a named color.
/// <see langword="null"/> or whitespace-only values leave the foreground unset,
/// while values that cannot be parsed use the conversion's fallback color when one is supplied.
/// </param>
/// <param name="IsBold">Whether matched text uses a bold font weight.</param>
/// <param name="IsItalic">Whether matched text uses an italic font style.</param>
public sealed record RegexHighlightingStyle(string? ColorText = null, bool IsBold = false, bool IsItalic = false)
{
	/// <summary>
	/// Converts this style to an AvalonEdit <see cref="HighlightingColor"/>.
	/// A missing or blank color leaves the foreground unset so the editor's theme color is used.
	/// A color value that cannot be parsed uses <paramref name="fallbackColor"/> when one is supplied and
	/// otherwise leaves the foreground unset as well.
	/// </summary>
	/// <param name="fallbackColor">
	/// The foreground color used when the configured color cannot be parsed, or <see langword="null"/> to
	/// leave the foreground unset so the editor's theme color is used.
	/// </param>
	/// <returns>A highlighting color with the resolved foreground color and configured font settings.</returns>
	public HighlightingColor ToHighlightingColor(Color? fallbackColor)
	{
		var highlightingColor = new HighlightingColor();

		if (BrushHelpers.TryParseColor(ColorText, out Color color))
			highlightingColor.Foreground = new SimpleHighlightingBrush(color);
		else if (!string.IsNullOrWhiteSpace(ColorText) && fallbackColor is Color fallback)
			highlightingColor.Foreground = new SimpleHighlightingBrush(fallback);

		if (IsBold)
			highlightingColor.FontWeight = FontWeights.Bold;

		if (IsItalic)
			highlightingColor.FontStyle = FontStyles.Italic;

		return highlightingColor;
	}
}

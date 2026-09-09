using System.Runtime.CompilerServices;
using System.Windows.Media;
using static Nickelony.IDEKit.Infrastructure.BrushHelpers;

namespace Nickelony.IDEKit.AvalonEdit.Markdown;

/// <summary>
/// Provides the assets shared across the Markdown rendering pipeline: line-ending normalization and
/// the theme-derived brushes used by quotes, tables, thematic breaks, and code blocks.
/// </summary>
/// <remarks>
/// Derived brushes depend only on the theme instance, so they are computed once per theme and cached,
/// instead of being blended again once per quote, table cell, thematic break, and inline-code
/// container.
/// </remarks>
internal static class MarkdownRenderingAssets
{
	private static readonly ConditionalWeakTable<MarkdownToolTipTheme, DerivedBrushes> s_derivedBrushes = new();

	/// <summary>
	/// Normalizes the line endings of the given text to line feeds.
	/// </summary>
	/// <param name="text">The text to normalize.</param>
	/// <returns>The text with CRLF and lone CR sequences replaced by line feeds.</returns>
	internal static string NormalizeLineEndings(string text)
	{
		return text
			.Replace("\r\n", "\n", StringComparison.Ordinal)
			.Replace('\r', '\n');
	}

	/// <summary>
	/// Gets the theme-derived background brush for code blocks.
	/// </summary>
	/// <param name="theme">The theme to derive the brush from.</param>
	/// <returns>A frozen brush blended toward the contrasting pole of the theme's surface.</returns>
	internal static SolidColorBrush CreateCodeBackground(MarkdownToolTipTheme theme)
		=> GetDerivedBrushes(theme).CodeBackground;

	/// <summary>
	/// Gets the theme-derived border brush for quotes, tables, thematic breaks, and code blocks.
	/// </summary>
	/// <param name="theme">The theme to derive the brush from.</param>
	/// <returns>A frozen brush blended toward the contrasting pole of the theme's surface.</returns>
	internal static SolidColorBrush CreateBorderBrush(MarkdownToolTipTheme theme)
		=> GetDerivedBrushes(theme).Border;

	private static DerivedBrushes GetDerivedBrushes(MarkdownToolTipTheme theme)
		=> s_derivedBrushes.GetValue(theme, static key => CreateDerivedBrushes(key));

	private static DerivedBrushes CreateDerivedBrushes(MarkdownToolTipTheme theme)
	{
		Color baseColor = GetBaseColor(theme);

		// Blend toward the contrasting pole so derived colors stay visible on light and dark surfaces
		// alike; blending toward black alone would make the code background invisible on black.
		Color targetColor = GetLuma(baseColor) >= 0.5 ? Colors.Black : Colors.White;

		return new DerivedBrushes(
			CreateFrozenBrush(Blend(baseColor, targetColor, theme.BorderBlendRatio)),
			CreateFrozenBrush(Blend(baseColor, targetColor, theme.CodeBackgroundBlendRatio)));
	}

	private static Color GetBaseColor(MarkdownToolTipTheme theme)
		=> theme.SurfaceBackground is SolidColorBrush solidBrush ? solidBrush.Color : Colors.White;

	// Weighted average of the gamma-compressed channels (Rec. 601 luma); the exact luminance model does
	// not matter here because the value only decides whether the surface is light or dark.
	private static double GetLuma(Color color)
		=> ((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B)) / 255.0;

	private static Color Blend(Color first, Color second, double ratio)
	{
		double clampedRatio = Math.Max(0.0, Math.Min(1.0, ratio));
		double inverseRatio = 1.0 - clampedRatio;

		return Color.FromArgb(
			(byte)Math.Round(first.A * inverseRatio + second.A * clampedRatio),
			(byte)Math.Round(first.R * inverseRatio + second.R * clampedRatio),
			(byte)Math.Round(first.G * inverseRatio + second.G * clampedRatio),
			(byte)Math.Round(first.B * inverseRatio + second.B * clampedRatio));
	}

	private sealed class DerivedBrushes
	{
		public DerivedBrushes(SolidColorBrush border, SolidColorBrush codeBackground)
		{
			Border = border;
			CodeBackground = codeBackground;
		}

		public SolidColorBrush Border { get; }
		public SolidColorBrush CodeBackground { get; }
	}
}

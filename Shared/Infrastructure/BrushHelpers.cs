using System.Windows.Media;

namespace Nickelony.IDEKit.Infrastructure;

/// <summary>
/// Creates frozen WPF brushes and pens for reuse by IDEKit components.
/// </summary>
/// <remarks>
/// This file is compiled into every WPF package that needs it through a <c>&lt;Compile Include&gt;</c>
/// link. The <c>Nickelony.IDEKit.Infrastructure</c> namespace is intentionally shared by those linked
/// copies instead of following one project's folder-to-namespace convention, so the helper keeps a
/// single identity across packages.
/// </remarks>
internal static class BrushHelpers
{
	/// <summary>
	/// Creates a frozen brush from a color.
	/// </summary>
	/// <param name="color">The color of the brush.</param>
	/// <returns>A frozen brush with the given color.</returns>
	public static SolidColorBrush CreateFrozenBrush(Color color)
	{
		var brush = new SolidColorBrush(color);
		brush.Freeze();
		return brush;
	}

	/// <summary>
	/// Parses a WPF color string and creates a frozen brush from it.
	/// </summary>
	/// <param name="colorValue">The color string to parse.</param>
	/// <returns>A frozen brush with the parsed color.</returns>
	/// <exception cref="ArgumentException">
	/// <paramref name="colorValue"/> is blank or is not a valid color string.
	/// </exception>
	public static SolidColorBrush CreateFrozenBrush(string colorValue)
	{
		if (!TryParseColor(colorValue, out Color color))
			throw new ArgumentException($"'{colorValue}' is not a valid color.", nameof(colorValue));

		var brush = new SolidColorBrush(color);
		brush.Freeze();
		return brush;
	}

	/// <summary>
	/// Tries to parse a WPF color string.
	/// </summary>
	/// <remarks>
	/// This is the single parse implementation for color strings in the WPF packages: a blank or
	/// unparseable value reports <see langword="false"/> instead of throwing, so callers decide whether
	/// an invalid value is an error or a fallback.
	/// </remarks>
	/// <param name="colorValue">The color string to parse.</param>
	/// <param name="color">
	/// The parsed color when parsing succeeds; otherwise, the default color.
	/// </param>
	/// <returns>
	/// <see langword="true"/> when <paramref name="colorValue"/> is a valid color string; otherwise,
	/// <see langword="false"/>.
	/// </returns>
	public static bool TryParseColor(string? colorValue, out Color color)
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
		{
			// Unparseable text is not a color.
		}
		catch (NotSupportedException)
		{
			// A value without a type converter is not a color either.
		}

		return false;
	}

	/// <summary>
	/// Creates a frozen pen with the supplied brush and thickness.
	/// </summary>
	/// <remarks>
	/// The returned pen is frozen so it can be shared between elements. A <see langword="null"/> brush
	/// produces a pen that strokes nothing.
	/// </remarks>
	/// <param name="brush">The brush used to stroke the pen.</param>
	/// <param name="thickness">The pen thickness.</param>
	/// <returns>A frozen pen.</returns>
	public static Pen CreateFrozenPen(Brush brush, double thickness)
	{
		var pen = new Pen(brush, thickness);
		pen.Freeze();
		return pen;
	}

	/// <summary>
	/// Creates a frozen pen with the supplied thickness and dash pattern, using round line caps and
	/// starting the pattern at offset zero.
	/// </summary>
	/// <remarks>
	/// The returned pen is frozen so it can be shared between elements. A <see langword="null"/> brush
	/// produces a pen that strokes nothing.
	/// </remarks>
	/// <param name="brush">The brush used to stroke the pen.</param>
	/// <param name="thickness">The pen thickness.</param>
	/// <param name="dashPattern">
	/// The dash pattern. WPF interprets each value as a multiple of the pen's thickness, so
	/// <c>[1.0, 3.0]</c> draws a dash one thickness long and a gap three thicknesses long, not 1- and
	/// 3-DIP lengths.
	/// </param>
	/// <returns>A frozen pen.</returns>
	public static Pen CreateFrozenDashedPen(Brush brush, double thickness, double[] dashPattern)
	{
		var pen = new Pen(brush, thickness)
		{
			DashStyle = new DashStyle(dashPattern, 0.0),
			StartLineCap = PenLineCap.Round,
			EndLineCap = PenLineCap.Round
		};

		pen.Freeze();
		return pen;
	}
}

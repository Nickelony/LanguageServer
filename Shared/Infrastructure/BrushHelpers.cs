using System.Windows.Media;

namespace Nickelony.IDEKit.Infrastructure;

/// <summary>
/// Creates frozen WPF brushes and pens for reuse by IDEKit components.
/// </summary>
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
	public static SolidColorBrush CreateFrozenBrush(string colorValue)
	{
		if (string.IsNullOrWhiteSpace(colorValue))
			throw new ArgumentException("Color value must not be empty.", nameof(colorValue));

		object converted = ColorConverter.ConvertFromString(colorValue);

		if (converted is not Color color)
			throw new ArgumentException($"'{colorValue}' is not a valid color.", nameof(colorValue));

		var brush = new SolidColorBrush(color);
		brush.Freeze();
		return brush;
	}

	/// <summary>
	/// Creates a frozen pen with the supplied brush and thickness.
	/// </summary>
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
	/// <param name="brush">The brush used to stroke the pen.</param>
	/// <param name="thickness">The pen thickness.</param>
	/// <param name="dashPattern">The dash pattern; values alternate between dash and gap lengths.</param>
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

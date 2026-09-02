using System.Windows;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.Extras.Markdown;

/// <summary>
/// Defines the fonts, sizes, brushes, and layout settings used to render Markdown tooltip content.
/// </summary>
public sealed record MarkdownToolTipTheme
{
	/// <summary>Gets the default theme values.</summary>
	public static MarkdownToolTipTheme Default { get; } = new();

	/// <summary>Gets or initializes the font family used for body text.</summary>
	public FontFamily BodyFontFamily { get; init; } = SystemFonts.MessageFontFamily;

	/// <summary>Gets or initializes the font size used for body text.</summary>
	public double BodyFontSize { get; init; } = 14.0;

	/// <summary>Gets or initializes the font family used for code blocks and inline code.</summary>
	public FontFamily CodeFontFamily { get; init; } = new("Consolas");

	/// <summary>Gets or initializes the font size used for code blocks and inline code.</summary>
	public double CodeFontSize { get; init; } = 13.0;

	/// <summary>Gets or initializes the foreground brush used for rendered text and code.</summary>
	public Brush Foreground { get; init; } = Brushes.Black;

	/// <summary>Gets or initializes the base brush used to derive code backgrounds and borders.</summary>
	public Brush Background { get; init; } = Brushes.White;

	/// <summary>Gets or initializes the foreground brush used for hyperlinks.</summary>
	public Brush LinkForeground { get; init; } = CreateFrozenBrush(Color.FromRgb(0, 102, 204));

	/// <summary>Gets or initializes the maximum width of the rendered tooltip.</summary>
	public double MaxWidth { get; init; } = 540.0;

	/// <summary>Gets or initializes the maximum height of the rendered tooltip.</summary>
	public double MaxHeight { get; init; } = 420.0;

	/// <summary>Gets or initializes the width used to wrap text and code inside the tooltip.</summary>
	public double TextMaxWidth { get; init; } = 500.0;

	/// <summary>
	/// Gets or initializes the blend ratio used to derive the code background from <see cref="Background"/>.
	/// Values outside the range from <c>0.0</c> to <c>1.0</c> are clamped when rendered.
	/// </summary>
	public double CodeBackgroundBlendRatio { get; init; } = 0.32;

	/// <summary>
	/// Gets or initializes the blend ratio used to derive the code border from <see cref="Background"/>.
	/// Values outside the range from <c>0.0</c> to <c>1.0</c> are clamped when rendered.
	/// </summary>
	public double CodeBorderBlendRatio { get; init; } = 0.18;

	/// <summary>
	/// Gets or initializes the maximum number of code lines used to size a code block.
	/// When scrolling is allowed, additional lines can be scrolled into view.
	/// </summary>
	public int MaxVisibleCodeBlockLines { get; init; } = 14;

	/// <summary>Gets or initializes the spacing applied below block-level elements.</summary>
	public double BlockSpacing { get; init; } = 6.0;

	/// <summary>
	/// Gets or initializes the font-size multiplier for each heading level, starting with level 1.
	/// The last available multiplier is reused for deeper heading levels.
	/// </summary>
	public IReadOnlyList<double> HeadingFontSizeScales { get; init; } = [1.30, 1.20, 1.12, 1.06, 1.03, 1.0];

	internal static Brush CreateFrozenBrush(Color color)
	{
		var brush = new SolidColorBrush(color);
		brush.Freeze();
		return brush;
	}
}

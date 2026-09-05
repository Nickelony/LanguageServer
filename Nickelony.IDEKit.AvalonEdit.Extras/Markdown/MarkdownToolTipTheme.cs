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

	/// <summary>Gets or initializes the foreground brush used for regular rendered text and code.</summary>
	public Brush Foreground { get; init; } = Brushes.Black;

	/// <summary>
	/// Gets or initializes the base brush used to derive code backgrounds and borders.
	/// The color is used when the brush is a <see cref="SolidColorBrush"/>; other brush types use white.
	/// </summary>
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
	/// For a non-solid background, the effective base color is white. Values outside the range from <c>0.0</c>
	/// to <c>1.0</c> are clamped when rendered.
	/// </summary>
	public double CodeBackgroundBlendRatio { get; init; } = 0.32;

	/// <summary>
	/// Gets or initializes the blend ratio used to derive the code border from <see cref="Background"/>.
	/// For a non-solid background, the effective base color is white. Values outside the range from <c>0.0</c>
	/// to <c>1.0</c> are clamped when rendered.
	/// </summary>
	public double CodeBorderBlendRatio { get; init; } = 0.18;

	/// <summary>
	/// Gets or initializes the maximum number of visible editor line heights used to size a code block.
	/// Wrapped content can use more than one line height per source line. When scrolling is allowed,
	/// additional content can be scrolled into view.
	/// </summary>
	public int MaxVisibleCodeBlockLines { get; init; } = 14;

	/// <summary>Gets or initializes the spacing applied below paragraphs, quotes, lists, and tables.</summary>
	public double BlockSpacing { get; init; } = 6.0;

	/// <summary>
	/// Gets or initializes the font-size multiplier for each heading level, starting with level 1.
	/// The collection must contain at least one multiplier. The last available multiplier is reused for deeper
	/// heading levels.
	/// </summary>
	public IReadOnlyList<double> HeadingFontSizeScales { get; init; } = [1.30, 1.20, 1.12, 1.06, 1.03, 1.0];

	internal static Brush CreateFrozenBrush(Color color)
	{
		var brush = new SolidColorBrush(color);
		brush.Freeze();
		return brush;
	}
}

using Nickelony.IDEKit.Infrastructure;
using System.Windows;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.Markdown;

/// <summary>
/// Defines the fonts, sizes, brushes, and layout settings used to render Markdown tooltip content.
/// </summary>
/// <remarks>
/// <see cref="Default"/> is a self-contained platform baseline (system message font, Consolas code font,
/// black on white), not an integration with the host's own color palette or editor theme. Hosts that
/// render dark or themed content should construct a theme from their palette instead of relying on it.
/// A theme instance is treated as immutable once it has been used for rendering: derived brushes (code
/// backgrounds, borders, and separators) are cached per instance, so a changed brush or blend ratio
/// requires a new theme instance.
/// </remarks>
public sealed record MarkdownToolTipTheme
{
	private IReadOnlyList<double> _headingFontSizeScales = [1.30, 1.20, 1.12, 1.06, 1.03, 1.0];
	private double _bodyFontSize = 14.0;
	private double _codeFontSize = 13.0;
	private double _maxWidth = 540.0;
	private double _maxHeight = 420.0;
	private double _codeMaxWidth = 500.0;
	private int _maxVisibleCodeBlockLines = 14;
	private double _blockSpacing = 6.0;

	/// <summary>Gets the default theme values.</summary>
	public static MarkdownToolTipTheme Default { get; } = new();

	/// <summary>Gets or initializes the font family used for body text.</summary>
	public FontFamily BodyFontFamily { get; init; } = SystemFonts.MessageFontFamily;

	/// <summary>Gets or initializes the font size used for body text. Values must be positive and finite; sizes the layout engine rejects fail during rendering.</summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="value"/> is zero, negative, or not finite.
	/// </exception>
	public double BodyFontSize
	{
		get => _bodyFontSize;
		init
		{
			ThrowIfNotPositiveFinite(value);
			_bodyFontSize = value;
		}
	}

	/// <summary>Gets or initializes the font family used for code blocks and inline code.</summary>
	public FontFamily CodeFontFamily { get; init; } = new("Consolas");

	/// <summary>Gets or initializes the font size used for code blocks and inline code. Values must be positive and finite; sizes the layout engine rejects fail during rendering.</summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="value"/> is zero, negative, or not finite.
	/// </exception>
	public double CodeFontSize
	{
		get => _codeFontSize;
		init
		{
			ThrowIfNotPositiveFinite(value);
			_codeFontSize = value;
		}
	}

	/// <summary>Gets or initializes the foreground brush used for regular rendered text and code.</summary>
	public Brush Foreground { get; init; } = Brushes.Black;

	/// <summary>
	/// Gets or initializes the surface background from which code backgrounds and borders are derived.
	/// It is not applied as the viewer background; the rendered document is transparent so that the
	/// hosting tooltip supplies the visible surface. Its color is used when the brush is a
	/// <see cref="SolidColorBrush"/>; other brush types use white.
	/// </summary>
	public Brush SurfaceBackground { get; init; } = Brushes.White;

	/// <summary>Gets or initializes the foreground brush used for hyperlinks.</summary>
	public Brush LinkForeground { get; init; } = BrushHelpers.CreateFrozenBrush(Color.FromRgb(0, 102, 204));

	/// <summary>Gets or initializes the maximum width of the rendered tooltip. Values must be positive and finite.</summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="value"/> is zero, negative, or not finite.
	/// </exception>
	public double MaxWidth
	{
		get => _maxWidth;
		init
		{
			ThrowIfNotPositiveFinite(value);
			_maxWidth = value;
		}
	}

	/// <summary>Gets or initializes the maximum height of the rendered tooltip. Values must be positive and finite.</summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="value"/> is zero, negative, or not finite.
	/// </exception>
	public double MaxHeight
	{
		get => _maxHeight;
		init
		{
			ThrowIfNotPositiveFinite(value);
			_maxHeight = value;
		}
	}

	/// <summary>
	/// Gets or initializes the maximum width of code blocks and inline code. Values must be positive
	/// and finite.
	/// </summary>
	/// <remarks>
	/// Body text is not constrained by this value: it wraps at the width the document or viewer is
	/// given, up to <see cref="MaxWidth"/>. Code is constrained because code wraps and scrolls as a
	/// block of its own, independently of the text around it.
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="value"/> is zero, negative, or not finite.
	/// </exception>
	public double CodeMaxWidth
	{
		get => _codeMaxWidth;
		init
		{
			ThrowIfNotPositiveFinite(value);
			_codeMaxWidth = value;
		}
	}

	/// <summary>
	/// Gets or initializes the blend ratio used to derive the code background from <see cref="SurfaceBackground"/>.
	/// The surface color is blended toward the contrasting pole (black on light surfaces, white on dark
	/// surfaces) so the code background stays distinguishable on any surface. For a non-solid background,
	/// the effective surface color is white. Values outside the range from <c>0.0</c> to <c>1.0</c> are
	/// clamped when rendered.
	/// </summary>
	public double CodeBackgroundBlendRatio { get; init; } = 0.32;

	/// <summary>
	/// Gets or initializes the blend ratio used to derive code borders, quote bars, table cell borders,
	/// and separators from <see cref="SurfaceBackground"/>. The surface color is blended toward black on light
	/// surfaces and toward white on dark surfaces, so the derived color contrasts with the visible
	/// surface in both cases. For a non-solid background, the effective surface color is white. Values
	/// outside the range from <c>0.0</c> to <c>1.0</c> are clamped when rendered.
	/// </summary>
	public double BorderBlendRatio { get; init; } = 0.18;

	/// <summary>
	/// Gets or initializes the number of visual lines after which a code block starts scrolling.
	/// Wrapped content can use more than one line height per source line, so the effective limit is a
	/// visual-line count rather than a source-line count. Values must be at least <c>1</c>. When scrolling
	/// is allowed, additional content can be scrolled into view.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="value"/> is less than <c>1</c>.
	/// </exception>
	public int MaxVisibleCodeBlockLines
	{
		get => _maxVisibleCodeBlockLines;
		init
		{
			ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
			_maxVisibleCodeBlockLines = value;
		}
	}

	/// <summary>Gets or initializes the spacing applied below paragraphs, quotes, lists, and tables. Values must be non-negative and finite.</summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="value"/> is negative or not finite.
	/// </exception>
	public double BlockSpacing
	{
		get => _blockSpacing;
		init
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);

			if (!double.IsFinite(value))
				throw new ArgumentOutOfRangeException(nameof(value), value, "The value must be finite.");

			_blockSpacing = value;
		}
	}

	/// <summary>
	/// Gets or initializes the font-size multiplier for each heading level, starting with level 1.
	/// </summary>
	/// <remarks>
	/// The assigned collection is copied on assignment. When the collection is empty, headings use the
	/// body font size. The last available multiplier is reused for deeper heading levels. Values must be
	/// positive and finite.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="value"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="value"/> contains a scale that is zero, negative, or not finite.
	/// </exception>
	public IReadOnlyList<double> HeadingFontSizeScales
	{
		get => _headingFontSizeScales;
		init
		{
			ArgumentNullException.ThrowIfNull(value);

			foreach (double scale in value)
				ThrowIfNotPositiveFinite(scale);

			_headingFontSizeScales = Array.AsReadOnly([.. value]);
		}
	}

	/// <summary>
	/// Throws when the given value is not a positive finite number.
	/// </summary>
	/// <param name="value">The value to validate.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="value"/> is zero, negative, or not finite.
	/// </exception>
	private static void ThrowIfNotPositiveFinite(double value)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);

		if (!double.IsFinite(value))
			throw new ArgumentOutOfRangeException(nameof(value), value, "The value must be finite.");
	}
}

using System.Windows;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.Extras.TextMate.Highlighting;

/// <summary>
/// Describes the visual formatting resolved for a TextMate token run.
/// </summary>
public sealed class TextMateHighlightingStyle
{
	/// <summary>
	/// Gets a style that requests no additional formatting.
	/// </summary>
	public static TextMateHighlightingStyle Empty { get; } = new(null, false, false, null);

	/// <summary>
	/// Initializes a new instance of the <see cref="TextMateHighlightingStyle"/> class.
	/// </summary>
	/// <param name="foreground">The foreground brush, when the style sets one.</param>
	/// <param name="isBold">Whether the style requests bold text.</param>
	/// <param name="isItalic">Whether the style requests italic text.</param>
	/// <param name="textDecorations">The text decorations requested by the style, when any.</param>
	public TextMateHighlightingStyle(Brush? foreground, bool isBold, bool isItalic, TextDecorationCollection? textDecorations)
	{
		Foreground = foreground;
		IsBold = isBold;
		IsItalic = isItalic;
		TextDecorations = textDecorations;
	}

	/// <summary>
	/// Gets the foreground brush requested by the style, when one is set.
	/// </summary>
	public Brush? Foreground { get; }

	/// <summary>
	/// Gets a value indicating whether the style requests bold text.
	/// </summary>
	public bool IsBold { get; }

	/// <summary>
	/// Gets a value indicating whether the style requests italic text.
	/// </summary>
	public bool IsItalic { get; }

	/// <summary>
	/// Gets the text decorations requested by the style, when any.
	/// </summary>
	public TextDecorationCollection? TextDecorations { get; }

	/// <summary>
	/// Gets a value indicating whether the style contains any formatting.
	/// </summary>
	public bool HasFormatting
		=> Foreground is not null || IsBold || IsItalic || TextDecorations is not null;

	/// <summary>
	/// Creates a typeface by applying this style's bold and italic settings to a base typeface.
	/// </summary>
	/// <param name="baseTypeface">The base typeface to derive from.</param>
	/// <returns>The derived typeface.</returns>
	public Typeface CreateTypeface(Typeface baseTypeface)
	{
		FontStyle fontStyle = IsItalic ? FontStyles.Italic : baseTypeface.Style;
		FontWeight fontWeight = IsBold ? FontWeights.Bold : baseTypeface.Weight;

		return new Typeface(baseTypeface.FontFamily, fontStyle, fontWeight, baseTypeface.Stretch);
	}
}

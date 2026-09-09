using ICSharpCode.AvalonEdit.Rendering;
using System.Windows;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.Rendering;

/// <summary>
/// Applies <see cref="ITextRunStyle"/> values to AvalonEdit visual line elements.
/// </summary>
/// <remarks>
/// <para>
/// The helpers contain the paint-time style application shared by colorizing transformers: the
/// foreground brush is set when the style carries one, the typeface is changed only when the style
/// requests bold or italic text, and the text decorations are set when the style carries a non-empty
/// collection. Text decorations are applied with AvalonEdit's <c>SetTextDecorations</c> semantics,
/// which union the requested decorations with the decorations already present on the element.
/// </para>
/// <para>
/// The generic overloads apply struct styles without boxing them; the struct-based contract avoids the
/// per-element <c>HighlightingBrush.GetBrush(context)</c> call and the heap allocation that AvalonEdit's
/// <c>HighlightingColor</c> requires. A colorizing transformer that already builds a
/// <c>HighlightingColor</c> for AvalonEdit's own pipeline can keep using it; the two models serve
/// different pipelines.
/// </para>
/// <para>
/// The typeface overload accepts a caller-derived typeface, so a transformer can cache the derived
/// value per style and base typeface instead of constructing one for every element it paints. Caching
/// is the caller's responsibility: this helper performs no caching. The supplied typeface is applied
/// under the same condition as the derived one: only when the style requests bold or italic text.
/// </para>
/// <para>
/// Brushes and decoration collections should be frozen before they are applied: AvalonEdit's property
/// setters accept mutable freezables but only emit a debug-only performance warning, and a frozen value
/// can be shared between elements without allocating.
/// </para>
/// </remarks>
public static class TextRunStyleApplier
{
	/// <summary>
	/// Applies the style to the visual line element, deriving the typeface from the element's current
	/// typeface when the style requests bold or italic text.
	/// </summary>
	/// <param name="element">The visual line element to apply the style to.</param>
	/// <param name="style">The style to apply.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="element"/> or <paramref name="style"/> is <see langword="null"/>.
	/// </exception>
	public static void Apply(VisualLineElement element, ITextRunStyle style)
		=> Apply<ITextRunStyle>(element, style);

	/// <summary>
	/// Applies the style to the visual line element, deriving the typeface from the element's current
	/// typeface when the style requests bold or italic text, without boxing a struct style.
	/// </summary>
	/// <typeparam name="TTextRunStyle">The style type; pass the concrete type to avoid boxing.</typeparam>
	/// <param name="element">The visual line element to apply the style to.</param>
	/// <param name="style">The style to apply.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="element"/> is <see langword="null"/>, or <paramref name="style"/> is a
	/// <see langword="null"/> reference.
	/// </exception>
	public static void Apply<TTextRunStyle>(VisualLineElement element, TTextRunStyle style)
		where TTextRunStyle : ITextRunStyle
	{
		ArgumentNullException.ThrowIfNull(element);

		if (style is null)
			throw new ArgumentNullException(nameof(style));

		VisualLineElementTextRunProperties properties = element.TextRunProperties;

		if (style.Foreground is not null)
			properties.SetForegroundBrush(style.Foreground);

		if (style.IsBold || style.IsItalic)
			properties.SetTypeface(CreateTypeface(properties.Typeface, style));

		if (style.TextDecorations is { Count: > 0 })
			properties.SetTextDecorations(style.TextDecorations);
	}

	/// <summary>
	/// Applies the style to the visual line element, using the supplied typeface for the element when
	/// the style requests bold or italic text.
	/// </summary>
	/// <param name="element">The visual line element to apply the style to.</param>
	/// <param name="style">The style to apply.</param>
	/// <param name="typeface">
	/// The typeface to apply when the style requests bold or italic text, typically a value derived
	/// with <see cref="CreateTypeface"/> from the element's current typeface and cached by the caller.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="element"/>, <paramref name="style"/>, or <paramref name="typeface"/> is
	/// <see langword="null"/>.
	/// </exception>
	public static void Apply(VisualLineElement element, ITextRunStyle style, Typeface typeface)
		=> Apply<ITextRunStyle>(element, style, typeface);

	/// <summary>
	/// Applies the style to the visual line element, using the supplied typeface for the element when
	/// the style requests bold or italic text, without boxing a struct style.
	/// </summary>
	/// <typeparam name="TTextRunStyle">The style type; pass the concrete type to avoid boxing.</typeparam>
	/// <param name="element">The visual line element to apply the style to.</param>
	/// <param name="style">The style to apply.</param>
	/// <param name="typeface">
	/// The typeface to apply when the style requests bold or italic text, typically a value derived
	/// with <see cref="CreateTypeface{TTextRunStyle}(Typeface, TTextRunStyle)"/> from the element's current
	/// typeface and cached by the caller.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="element"/> or <paramref name="typeface"/> is <see langword="null"/>, or
	/// <paramref name="style"/> is a <see langword="null"/> reference.
	/// </exception>
	public static void Apply<TTextRunStyle>(VisualLineElement element, TTextRunStyle style, Typeface typeface)
		where TTextRunStyle : ITextRunStyle
	{
		ArgumentNullException.ThrowIfNull(element);
		ArgumentNullException.ThrowIfNull(typeface);

		if (style is null)
			throw new ArgumentNullException(nameof(style));

		VisualLineElementTextRunProperties properties = element.TextRunProperties;

		if (style.Foreground is not null)
			properties.SetForegroundBrush(style.Foreground);

		if (style.IsBold || style.IsItalic)
			properties.SetTypeface(typeface);

		if (style.TextDecorations is { Count: > 0 })
			properties.SetTextDecorations(style.TextDecorations);
	}

	/// <summary>
	/// Creates a typeface by applying the style's bold and italic settings to a base typeface.
	/// A disabled setting preserves the corresponding weight or style from the base typeface.
	/// </summary>
	/// <param name="baseTypeface">The base typeface to derive from.</param>
	/// <param name="style">The style whose bold and italic settings are applied.</param>
	/// <returns>The derived typeface.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="baseTypeface"/> or <paramref name="style"/> is <see langword="null"/>.
	/// </exception>
	public static Typeface CreateTypeface(Typeface baseTypeface, ITextRunStyle style)
		=> CreateTypeface<ITextRunStyle>(baseTypeface, style);

	/// <summary>
	/// Creates a typeface by applying the style's bold and italic settings to a base typeface,
	/// without boxing a struct style.
	/// A disabled setting preserves the corresponding weight or style from the base typeface.
	/// </summary>
	/// <typeparam name="TTextRunStyle">The style type; pass the concrete type to avoid boxing.</typeparam>
	/// <param name="baseTypeface">The base typeface to derive from.</param>
	/// <param name="style">The style whose bold and italic settings are applied.</param>
	/// <returns>The derived typeface.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="baseTypeface"/> is <see langword="null"/>, or <paramref name="style"/> is a
	/// <see langword="null"/> reference.
	/// </exception>
	public static Typeface CreateTypeface<TTextRunStyle>(Typeface baseTypeface, TTextRunStyle style)
		where TTextRunStyle : ITextRunStyle
	{
		ArgumentNullException.ThrowIfNull(baseTypeface);

		if (style is null)
			throw new ArgumentNullException(nameof(style));

		FontStyle fontStyle = style.IsItalic ? FontStyles.Italic : baseTypeface.Style;
		FontWeight fontWeight = style.IsBold ? FontWeights.Bold : baseTypeface.Weight;

		return new Typeface(baseTypeface.FontFamily, fontStyle, fontWeight, baseTypeface.Stretch);
	}
}

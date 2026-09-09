using System.Windows;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.Rendering;

/// <summary>
/// Describes the resolved visual formatting of a text run: a foreground brush, bold and italic
/// settings, and text decorations.
/// </summary>
/// <param name="Foreground">The foreground brush to apply, when one is resolved.</param>
/// <param name="IsBold">Whether the run is rendered bold. A disabled setting preserves the base typeface's weight.</param>
/// <param name="IsItalic">Whether the run is rendered italic. A disabled setting preserves the base typeface's style.</param>
/// <param name="TextDecorations">The text decorations to apply, when any are resolved.</param>
/// <remarks>
/// Brushes and decoration collections are applied on every paint pass of a text view, so styles that a
/// resolver shares between calls should hold frozen instances. Use <see cref="TextRunStyleApplier"/> to
/// apply the style to a visual line element.
/// </remarks>
public readonly record struct TextRunStyle(
	Brush? Foreground,
	bool IsBold,
	bool IsItalic,
	TextDecorationCollection? TextDecorations) : ITextRunStyle
{
	/// <summary>
	/// Gets a style that requests no formatting.
	/// </summary>
	public static TextRunStyle Empty { get; } = new(null, false, false, null);

	/// <inheritdoc/>
	public bool HasFormatting => Foreground is not null || IsBold || IsItalic || TextDecorations is { Count: > 0 };

	/// <summary>
	/// Creates a typeface by applying this style's bold and italic settings to a base typeface.
	/// A disabled setting preserves the corresponding weight or style from the base typeface.
	/// </summary>
	/// <param name="baseTypeface">The base typeface to derive from.</param>
	/// <returns>The derived typeface.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="baseTypeface"/> is <see langword="null"/>.
	/// </exception>
	public Typeface CreateTypeface(Typeface baseTypeface)
		=> TextRunStyleApplier.CreateTypeface(baseTypeface, this);
}

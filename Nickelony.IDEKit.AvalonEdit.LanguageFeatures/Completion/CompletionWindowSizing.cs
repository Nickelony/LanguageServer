using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Editing;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

/// <summary>
/// Measures the content-driven width of a completion window.
/// </summary>
/// <remarks>
/// The default measurement renders the display text with the text area's font configuration - family, style,
/// weight, stretch, size, and foreground - and the text area's flow direction, because the window inherits the
/// text area's typography in the library's default presentation. The display text is the item's displayed
/// content (a text-block content resolves to its text), falling back to the item's filter text when the
/// content is a rich element this measurement cannot read. An item that supplies an image also reserves
/// <see cref="TextCompletionControllerOptions.ItemIconWidth"/> for its icon column, and one that supplies
/// detail text reserves <see cref="TextCompletionControllerOptions.ItemDetailSpacing"/> before it; an item
/// that supplies neither is measured as its text alone. A host whose item template uses a different font
/// supplies <see cref="TextCompletionControllerHooks.MeasureItemWidth"/>, and a host that changes the
/// window font in <see cref="TextCompletionControllerHooks.ConfigureWindow"/> should do the same so the
/// measured width keeps matching the rendered width.
/// </remarks>
internal static class CompletionWindowSizing
{
	/// <summary>
	/// Measures the required content width for the given completion items, never reporting less than
	/// <see cref="TextCompletionControllerOptions.WindowMinContentWidth"/>. Items are measured until the content
	/// width the window can display is reached, so the window is sized by its widest items rather than by an
	/// arbitrary sample; identical display strings are measured only once per pass. The measurement re-runs for
	/// every open and in-place refresh (a deliberate trade-off: the early-exit cap keeps the scan bounded), and
	/// the caller adds the chrome and clamps the result into the window width bounds.
	/// </summary>
	/// <param name="textArea">The text area whose font configuration and DPI the measurement uses.</param>
	/// <param name="options">The sizing options.</param>
	/// <param name="getDisplayInfo">An optional callback that supplies the display text and detail for an item.</param>
	/// <param name="measureItemWidth">
	/// An optional callback that measures an item's content width, replacing the default measurement (the
	/// display text, plus an icon column for items that supply an image, plus the detail gap when detail text
	/// is present). The returned width must be finite; a negative value cannot widen the window and is ignored.
	/// </param>
	/// <param name="items">The items to measure.</param>
	/// <returns>The required content width before the window chrome is added.</returns>
	/// <exception cref="InvalidOperationException">
	/// <paramref name="measureItemWidth"/> returned a non-finite width, which would silently turn the window
	/// width into an auto-sized value.
	/// </exception>
	internal static double MeasureRequiredWidth(
		TextArea textArea,
		TextCompletionControllerOptions options,
		Func<ICompletionData, (string Text, string? Detail)>? getDisplayInfo,
		Func<ICompletionData, double>? measureItemWidth,
		IReadOnlyList<ICompletionData> items)
	{
		double requiredWidth = options.WindowMinContentWidth;

		if (items.Count == 0)
			return requiredWidth;

		// Once the measured content width reaches the width the completed window can display
		// (WindowMaxWidth minus the horizontal chrome), no further item can widen the window, so the scan
		// stops instead of measuring every item on every open and refresh.
		double contentWidthCap = options.WindowMaxWidth - options.WindowHorizontalChrome;

		if (measureItemWidth is not null)
		{
			for (int i = 0; i < items.Count; i++)
			{
				double measuredWidth = measureItemWidth(items[i]);

				// A non-finite measurement would flow into the window width, where WPF silently reads NaN as
				// auto-sizing and defeats the configured maximum; a negative measurement cannot widen the window.
				if (!double.IsFinite(measuredWidth))
					throw new InvalidOperationException("The MeasureItemWidth hook returned a non-finite item width.");

				requiredWidth = Math.Max(requiredWidth, measuredWidth);

				if (requiredWidth >= contentWidthCap)
					break;
			}

			return requiredWidth;
		}

		// The DPI, typeface, and text cache are invariant for the whole measurement pass, so they are
		// resolved once instead of once per measured string.
		var context = new MeasurementContext(textArea, options, getDisplayInfo);

		for (int i = 0; i < items.Count; i++)
		{
			requiredWidth = Math.Max(requiredWidth, context.MeasureItemWidth(items[i]));

			if (requiredWidth >= contentWidthCap)
				break;
		}

		return requiredWidth;
	}

	private sealed class MeasurementContext(
		TextArea textArea,
		TextCompletionControllerOptions options,
		Func<ICompletionData, (string Text, string? Detail)>? getDisplayInfo)
	{
		private readonly Dictionary<string, double> _textWidthCache = new(StringComparer.Ordinal);

		// The typeface follows the text area's configured font, so a bold, italic, or condensed font
		// measures with the same style it renders with.
		private readonly Typeface _typeface = new(textArea.FontFamily, textArea.FontStyle, textArea.FontWeight, textArea.FontStretch);
		private readonly double _pixelsPerDip = VisualTreeHelper.GetDpi(textArea).PixelsPerDip;

		public double MeasureItemWidth(ICompletionData completionData)
		{
			(string text, string? detail) = getDisplayInfo is not null
				? getDisplayInfo(completionData)
				: (GetDisplayText(completionData), null);

			double width = MeasureTextWidth(text);

			// The icon column exists only when the item supplies an image, so an item without one is
			// measured exactly as wide as its text (and detail).
			if (completionData.Image is not null)
				width += options.ItemIconWidth;

			if (!string.IsNullOrWhiteSpace(detail))
				width += options.ItemDetailSpacing + MeasureTextWidth(detail);

			return width;
		}

		// The default measurement sizes the window for what the list displays. AvalonEdit's completion-data
		// contract keeps the displayed content in Content (a string or a rich element) and the filter key in
		// Text; the package's own adapter renders a struck-through TextBlock for deprecated items, so a
		// text-block content resolves to its text. The filter text is the last resort for a content shape this
		// measurement cannot read as text.
		private static string GetDisplayText(ICompletionData completionData)
			=> completionData.Content switch
			{
				string text => text,
				TextBlock textBlock => textBlock.Text,
				_ => completionData.Text
			};

		private double MeasureTextWidth(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return 0.0;

			if (_textWidthCache.TryGetValue(text, out double cachedWidth))
				return cachedWidth;

			// CurrentUICulture is used deliberately so measurement follows the UI language, like WPF UI text,
			// rather than the formatting culture FormattedText defaults to. The flow direction follows the
			// text area so a right-to-left host measures the same direction it renders.
			var formattedText = new FormattedText(
				text,
				CultureInfo.CurrentUICulture,
				textArea.FlowDirection,
				_typeface,
				textArea.FontSize,
				textArea.Foreground,
				_pixelsPerDip);

			double width = formattedText.WidthIncludingTrailingWhitespace;
			_textWidthCache[text] = width;
			return width;
		}
	}
}

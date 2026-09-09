using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AvalonTextEditor = ICSharpCode.AvalonEdit.TextEditor;

namespace Nickelony.IDEKit.AvalonEdit.Markdown;

/// <summary>
/// Creates the read-only code-block editors used by <see cref="MarkdownToolTipRenderer"/> and wraps them
/// in their bordered host element.
/// </summary>
/// <remarks>
/// All members create and touch WPF elements and must run on the UI thread that owns the target
/// elements. Language resolution follows the behavior documented on
/// <see cref="MarkdownToolTipRenderer.CreateCodeBlockEditor"/>; the element factory measures the
/// editor with the editor's own text view so tabs and inherited word-wrap indentation are accounted
/// for, and it clamps the block to <see cref="MarkdownToolTipTheme.MaxVisibleCodeBlockLines"/>.
/// </remarks>
internal static class MarkdownCodeBlockFactory
{
	private const double CodeBlockHorizontalPadding = 8.0;
	private const double CodeBlockVerticalPadding = 6.0;
	private const double CodeBlockBorderThickness = 1.0;

	// Extra height added to the measured code text so the final line's descenders are not clipped, and
	// the minimum height that keeps a single short line from touching the border.
	private const double CodeBlockMeasuredHeightTolerance = 2.0;
	private const double CodeBlockMinimumHeightTolerance = 4.0;

	private static readonly Action<ILogger, string, Exception?> s_unresolvedLanguageLogger = LoggerMessage.Define<string>(
		LogLevel.Debug,
		new EventId(3, "UnresolvedCodeBlockLanguage"),
		"No syntax highlighting definition was found for code block language '{Language}'.");

	/// <summary>
	/// Creates a read-only code block editor with the given language and code.
	/// </summary>
	/// <param name="language">The language used to resolve syntax highlighting, or <see langword="null"/>.</param>
	/// <param name="code">The code to display.</param>
	/// <param name="theme">The theme to render with.</param>
	/// <param name="options">The rendering options.</param>
	/// <returns>The code block editor.</returns>
	internal static AvalonTextEditor CreateEditor(string? language, string code, MarkdownToolTipTheme theme, MarkdownToolTipOptions options)
	{
		string normalizedCode = NormalizeCodeBlockText(code);

		var editor = new AvalonTextEditor
		{
			Text = normalizedCode,
			IsReadOnly = true,
			Background = Brushes.Transparent,
			Foreground = theme.Foreground,
			BorderThickness = new Thickness(0.0),
			Margin = new Thickness(0.0),
			Padding = new Thickness(0.0),
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			FontFamily = theme.CodeFontFamily,
			FontSize = theme.CodeFontSize,
			ShowLineNumbers = false,
			WordWrap = true,
			Focusable = false,
			IsTabStop = false
		};

		editor.Options.EnableHyperlinks = false;
		editor.Options.EnableEmailHyperlinks = false;
		editor.TextArea.Margin = new Thickness(0.0);
		editor.TextArea.Focusable = false;
		editor.TextArea.IsTabStop = false;
		KeyboardNavigation.SetIsTabStop(editor, false);
		KeyboardNavigation.SetIsTabStop(editor.TextArea, false);

		// Code-block editors are passive; let the host provide highlighting before trying built-in resolution.
		if (options.CustomHighlightingInstaller?.Invoke(editor, language) is not true)
		{
			editor.SyntaxHighlighting = ResolveHighlighting(language, options);

			if (editor.SyntaxHighlighting is null && !string.IsNullOrWhiteSpace(language) && options.Logger is { } logger)
				s_unresolvedLanguageLogger(logger, language.Trim(), null);
		}

		return editor;
	}

	/// <summary>
	/// Creates the bordered host element for a code block, including the measured editor height and the
	/// scroll-bar and wheel-chaining behavior configured through the options.
	/// </summary>
	/// <param name="language">The language used to resolve syntax highlighting, or <see langword="null"/>.</param>
	/// <param name="code">The code to display.</param>
	/// <param name="theme">The theme to render with.</param>
	/// <param name="options">The rendering options.</param>
	/// <returns>The bordered code block element.</returns>
	internal static Border CreateCodeBlockElement(string? language, string code, MarkdownToolTipTheme theme, MarkdownToolTipOptions options)
	{
		AvalonTextEditor editor = CreateEditor(language, code, theme, options);

		// The editor stretches inside the border, so the measured wrap width is the border's own content
		// width. The width is measured without reserving space for the vertical scroll bar that appears
		// when the measured content overflows: the block scrolls in that case, so wrapped lines stay
		// reachable and the drift is accepted.
		double codeBlockWrapWidth = Math.Max(
			1.0,
			theme.CodeMaxWidth - (2.0 * (CodeBlockHorizontalPadding + CodeBlockBorderThickness)));

		double lineHeight = GetEditorLineHeight(editor);
		double maxVisibleHeight = Math.Max(
			lineHeight + CodeBlockMinimumHeightTolerance,
			Math.Ceiling(theme.MaxVisibleCodeBlockLines * lineHeight) + CodeBlockMeasuredHeightTolerance);
		double measuredHeight = Math.Max(
			lineHeight + CodeBlockMinimumHeightTolerance,
			MeasureEditorHeight(editor, codeBlockWrapWidth));

		// The block is clamped to the visible-line limit; below the limit the editor keeps an automatic
		// height so the layout that hosts it reports the exact height for the width it is given. The
		// scroll bar is shown only when scrolling is allowed and the content actually overflows, which
		// the editor's own scroll viewer decides.
		editor.MaxHeight = maxVisibleHeight;

		if (measuredHeight > maxVisibleHeight)
			editor.Height = maxVisibleHeight;

		editor.VerticalScrollBarVisibility = options.AllowScrolling
			? ScrollBarVisibility.Auto
			: ScrollBarVisibility.Disabled;

		if (options.AllowScrolling)
			ToolTipScrollChaining.Attach(editor);

		return new Border
		{
			Background = MarkdownRenderingAssets.CreateCodeBackground(theme),
			BorderBrush = MarkdownRenderingAssets.CreateBorderBrush(theme),
			BorderThickness = new Thickness(CodeBlockBorderThickness),
			CornerRadius = new CornerRadius(3.0),
			Padding = new Thickness(CodeBlockHorizontalPadding, CodeBlockVerticalPadding, CodeBlockHorizontalPadding, CodeBlockVerticalPadding),
			Margin = new Thickness(0.0, 4.0, 0.0, 6.0),
			MaxWidth = theme.CodeMaxWidth,
			Child = editor
		};
	}

	private static double GetEditorLineHeight(AvalonTextEditor editor)
	{
		double lineHeight = editor.TextArea.TextView.DefaultLineHeight;

		if (!double.IsNaN(lineHeight) && lineHeight > 0.0)
			return Math.Ceiling(lineHeight);

		var typeface = new Typeface(editor.FontFamily, editor.FontStyle, editor.FontWeight, editor.FontStretch);
		var formattedText = new FormattedText(
			"Ag",
			CultureInfo.CurrentCulture,
			editor.FlowDirection,
			typeface,
			editor.FontSize,
			Brushes.Transparent,
			1.0);

		return Math.Ceiling(Math.Max(1.0, formattedText.Height));
	}

	private static double MeasureEditorHeight(AvalonTextEditor editor, double width)
	{
		// The editor itself cannot be measured before it is templated (an unrooted editor reports an
		// empty size), but its text view is available and lays out exactly what the editor will render:
		// word wrap, AvalonEdit's tab stops, and inherited word-wrap indentation. Measuring the text
		// view at the block's content width therefore matches the rendered extent, unlike a separate
		// text measurement, which uses WPF's own text metrics and ignores the editor's indentation.
		TextView textView = editor.TextArea.TextView;
		textView.Measure(new Size(width, double.PositiveInfinity));

		return Math.Ceiling(textView.DesiredSize.Height) + CodeBlockMeasuredHeightTolerance;
	}

	private static string NormalizeCodeBlockText(string code)
	{
		string normalizedCode = MarkdownRenderingAssets.NormalizeLineEndings(code);

		if (string.IsNullOrEmpty(normalizedCode))
			return string.Empty;

		string[] lines = normalizedCode.Split('\n');
		int lastContentLineIndex = lines.Length - 1;

		// Trim trailing blank lines: fenced code conventionally ends with a newline, and keeping the
		// resulting empty line would leave blank space at the bottom of the block.
		while (lastContentLineIndex >= 0 && string.IsNullOrWhiteSpace(lines[lastContentLineIndex]))
			lastContentLineIndex--;

		if (lastContentLineIndex < 0)
			return string.Empty;

		return string.Join("\n", lines, 0, lastContentLineIndex + 1);
	}

	private static IHighlightingDefinition? ResolveHighlighting(string? language, MarkdownToolTipOptions options)
	{
		if (string.IsNullOrWhiteSpace(language))
			return null;

		string trimmedLanguage = language.Trim();

		// Definition names are case-sensitive in AvalonEdit, so the supplied casing is tried first and a
		// case-insensitive scan follows before falling back to extensions and the configured aliases.
		IHighlightingDefinition? definition = HighlightingManager.Instance.GetDefinition(trimmedLanguage);

		if (definition is not null)
			return definition;

		definition = FindDefinitionByName(trimmedLanguage);

		if (definition is not null)
			return definition;

		definition = HighlightingManager.Instance.GetDefinitionByExtension(trimmedLanguage.StartsWith('.')
			? trimmedLanguage
			: "." + trimmedLanguage);

		if (definition is not null)
			return definition;

		if (!TryGetAlias(options.HighlightingAliases, trimmedLanguage, out string? alias))
			return null;

		string trimmedAlias = alias.Trim();

		if (trimmedAlias.StartsWith('.'))
			return HighlightingManager.Instance.GetDefinitionByExtension(trimmedAlias);

		return HighlightingManager.Instance.GetDefinition(trimmedAlias)
			?? FindDefinitionByName(trimmedAlias)
			?? HighlightingManager.Instance.GetDefinitionByExtension("." + trimmedAlias);
	}

	private static IHighlightingDefinition? FindDefinitionByName(string name)
	{
		foreach (IHighlightingDefinition definition in HighlightingManager.Instance.HighlightingDefinitions)
		{
			if (string.Equals(definition.Name, name, StringComparison.OrdinalIgnoreCase))
				return definition;
		}

		return null;
	}

	private static bool TryGetAlias(IReadOnlyDictionary<string, string> aliases, string language, [NotNullWhen(true)] out string? alias)
	{
		// Options copy every assigned map into a case-insensitive frozen dictionary, so the direct
		// lookup already matches any casing the fence uses.
		if (aliases.TryGetValue(language, out alias) && !string.IsNullOrWhiteSpace(alias))
			return true;

		alias = null;
		return false;
	}
}

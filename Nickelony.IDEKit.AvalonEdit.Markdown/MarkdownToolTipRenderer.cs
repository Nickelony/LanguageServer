using Microsoft.Extensions.Logging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AvalonTextEditor = ICSharpCode.AvalonEdit.TextEditor;

namespace Nickelony.IDEKit.AvalonEdit.Markdown;

/// <summary>
/// Renders supported Markdown content into WPF elements for tooltip display.
/// </summary>
/// <remarks>
/// <para>
/// All entry points create and touch WPF elements and must run on the UI thread that owns the target
/// window's dispatcher. Theme brushes are applied directly to the created elements, so hosts should
/// pass frozen brushes when the elements may be used across threads.
/// </para>
/// <para>
/// The elements returned by <see cref="CreateContent"/> implement tooltip hosting behavior: the rendered
/// viewer, its plain-text fallback, and code blocks are not focusable and do not allow text selection.
/// Use <see cref="CreateCodeBlockEditor"/> when an embeddable read-only code editor is needed instead,
/// or <see cref="CreateFlowDocument"/> when the rendering should be hosted in a caller-controlled container.
/// </para>
/// <para>
/// Rendering failures and hyperlink-open failures are reported as warnings through
/// <see cref="MarkdownToolTipOptions.Logger"/> when one is configured; without a logger they are not
/// reported, and rendering still falls back to plain text. A code-block language that resolves to no
/// highlighting is reported through the logger at debug level when a logger is configured.
/// </para>
/// </remarks>
public static class MarkdownToolTipRenderer
{
	private static readonly Action<ILogger, Exception?> s_renderFailedLogger = LoggerMessage.Define(
		LogLevel.Warning,
		new EventId(1, "MarkdownRenderFailed"),
		"Markdown tooltip rendering failed; showing plain text instead.");

	/// <summary>
	/// Creates a WPF element that renders the given Markdown content.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The rendering pipeline supports CommonMark plus pipe tables with GFM column alignment,
	/// strikethrough, and autolinks. Other Markdig extensions (for example footnotes, mathematics,
	/// definition lists, task lists, custom containers, and the subscript, superscript, marked, and
	/// inserted emphasis variants) are not enabled; their constructs are rendered as literal text.
	/// </para>
	/// <para>
	/// Raw HTML blocks and inline HTML are omitted, and image links render their link text without
	/// loading an image. Whitespace-only content or an exception while parsing or rendering yields a
	/// plain-text element instead; the exception is reported through the configured logger when one is
	/// set. Only absolute links whose schemes are listed in the effective options can be opened;
	/// clicking an openable link calls <see cref="MarkdownToolTipOptions.OpenHyperlink"/> when it is
	/// set, and a callback result other than <see langword="true"/> (or no callback at all) opens the
	/// link through the operating system's default protocol handler, while any ambient navigation
	/// request is suppressed so the link is activated exactly once. A link that cannot be opened keeps
	/// the hyperlink color and underline and is distinguishable only by its cursor.
	/// </para>
	/// <para>
	/// The rendered content is not selectable and not focusable; see the type remarks for the contract.
	/// </para>
	/// </remarks>
	/// <param name="content">The Markdown content to render.</param>
	/// <param name="theme">The theme to render with, or <see langword="null"/> for the default theme.</param>
	/// <param name="options">The rendering options, or <see langword="null"/> for the default options.</param>
	/// <returns>
	/// <list type="bullet">
	/// <item>A <see cref="FlowDocumentScrollViewer"/> for rendered content;</item>
	/// <item>a <see cref="ScrollViewer"/> for the plain-text fallback.</item>
	/// </list>
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="content"/> is <see langword="null"/>.
	/// </exception>
	public static FrameworkElement CreateContent(string content, MarkdownToolTipTheme? theme = null, MarkdownToolTipOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(content);

		theme ??= MarkdownToolTipTheme.Default;
		options ??= MarkdownToolTipOptions.Default;

		string normalizedContent = MarkdownRenderingAssets.NormalizeLineEndings(content);

		if (string.IsNullOrWhiteSpace(normalizedContent))
			return CreatePlainTextContent(string.Empty, theme, options);

		try
		{
			FlowDocument flowDocument = MarkdownFlowDocumentBuilder.Render(normalizedContent, theme, options);

			return CreateViewer(flowDocument, theme, options);
		}
		catch (Exception exception)
		{
			// The fallback keeps tooltips resilient, but the failure stays observable.
			ReportRenderFailure(exception, options);
			return CreatePlainTextContent(normalizedContent, theme, options);
		}
	}

	/// <summary>
	/// Renders the given Markdown content into a standalone <see cref="FlowDocument"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Unlike <see cref="CreateContent"/>, the result is not wrapped in a tooltip-shaped viewer, so a host
	/// can present the same rendering in its own container and decide the selection, focus, and sizing
	/// behavior itself. The rendering pipeline and code-block behavior are the same.
	/// </para>
	/// <para>
	/// The document keeps WPF's default single-column layout metrics, which the tooltip viewer renders
	/// as one continuously flowing column. A host that paginates the document itself (for example a
	/// <c>FlowDocumentReader</c> or a print paginator) controls the column layout through
	/// <c>ColumnWidth</c> and the page size.
	/// </para>
	/// <para>
	/// Whitespace-only content yields an empty document. A parsing or rendering failure yields a document
	/// that shows the content as plain text, and the failure is reported as a warning.
	/// </para>
	/// </remarks>
	/// <param name="content">The Markdown content to render.</param>
	/// <param name="theme">The theme to render with, or <see langword="null"/> for the default theme.</param>
	/// <param name="options">The rendering options, or <see langword="null"/> for the default options.</param>
	/// <returns>The rendered document.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="content"/> is <see langword="null"/>.
	/// </exception>
	public static FlowDocument CreateFlowDocument(string content, MarkdownToolTipTheme? theme = null, MarkdownToolTipOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(content);

		theme ??= MarkdownToolTipTheme.Default;
		options ??= MarkdownToolTipOptions.Default;

		string normalizedContent = MarkdownRenderingAssets.NormalizeLineEndings(content);

		if (string.IsNullOrWhiteSpace(normalizedContent))
			return MarkdownFlowDocumentBuilder.CreateBaseFlowDocument(theme);

		try
		{
			return MarkdownFlowDocumentBuilder.Render(normalizedContent, theme, options);
		}
		catch (Exception exception)
		{
			ReportRenderFailure(exception, options);
			return MarkdownFlowDocumentBuilder.CreatePlainTextFlowDocument(normalizedContent, theme);
		}
	}

	/// <summary>
	/// Creates a plain-text element that renders the given content.
	/// </summary>
	/// <remarks>
	/// The content is displayed literally and is not parsed as Markdown. The rendered content is not
	/// selectable and not focusable; see the type remarks for the contract.
	/// </remarks>
	/// <param name="content">The text content to render.</param>
	/// <param name="theme">The theme to render with, or <see langword="null"/> for the default theme.</param>
	/// <param name="options">The rendering options, or <see langword="null"/> for the default options.</param>
	/// <returns>A <see cref="ScrollViewer"/> containing the rendered text.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="content"/> is <see langword="null"/>.
	/// </exception>
	public static FrameworkElement CreatePlainTextContent(string content, MarkdownToolTipTheme? theme = null, MarkdownToolTipOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(content);
		return CreateFallbackContent(content, theme ?? MarkdownToolTipTheme.Default, options ?? MarkdownToolTipOptions.Default);
	}

	/// <summary>
	/// Creates a read-only code block editor with the given language and code.
	/// </summary>
	/// <remarks>
	/// <para>
	/// When configured, the custom highlighting callback is invoked before built-in resolution. If it
	/// returns <see langword="true"/>, built-in resolution is skipped; otherwise, built-in resolution is
	/// used. The callback owns any state it installs and should attach cleanup for disposable state to
	/// the editor's lifecycle; the renderer never disposes host-installed state.
	/// </para>
	/// <para>
	/// Built-in resolution tries the language as a highlighting definition name with the supplied casing,
	/// then case-insensitively against the registered definition names, then as a file extension, and
	/// finally through the aliases configured in <see cref="MarkdownToolTipOptions.HighlightingAliases"/>;
	/// the default aliases map <c>csharp</c> to <c>.cs</c> and <c>json5</c> to <c>.json</c>. Definitions
	/// that AvalonEdit does not ship (for example Lua or TypeScript) must be registered by the host or
	/// supplied through <see cref="MarkdownToolTipOptions.CustomHighlightingInstaller"/>.
	/// The displayed text has its line endings normalized to line feeds, and trailing blank lines are
	/// removed. The editor is read-only and does not accept keyboard focus; it is an extension point for
	/// hosts that render code outside tooltip content.
	/// </para>
	/// <para>
	/// The returned editor is not height-clamped. When the renderer embeds a code block in tooltip
	/// content, the block is limited to <see cref="MarkdownToolTipTheme.MaxVisibleCodeBlockLines"/> visual
	/// lines and the rest scrolls, or is clipped when <see cref="MarkdownToolTipOptions.AllowScrolling"/> is
	/// <see langword="false"/>.
	/// </para>
	/// </remarks>
	/// <param name="language">The language used to resolve syntax highlighting, or <see langword="null"/>.</param>
	/// <param name="code">The code to display.</param>
	/// <param name="theme">The theme to render with, or <see langword="null"/> for the default theme.</param>
	/// <param name="options">The rendering options, or <see langword="null"/> for the default options.</param>
	/// <returns>The code block editor.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="code"/> is <see langword="null"/>.
	/// </exception>
	public static AvalonTextEditor CreateCodeBlockEditor(string? language, string code, MarkdownToolTipTheme? theme = null, MarkdownToolTipOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(code);

		theme ??= MarkdownToolTipTheme.Default;
		options ??= MarkdownToolTipOptions.Default;

		return MarkdownCodeBlockFactory.CreateEditor(language, code, theme, options);
	}

	private static FlowDocumentScrollViewer CreateViewer(FlowDocument document, MarkdownToolTipTheme theme, MarkdownToolTipOptions options)
	{
		var viewer = new FlowDocumentScrollViewer
		{
			Document = document,
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0.0),
			Padding = new Thickness(0.0),
			Margin = new Thickness(0.0),
			IsToolBarVisible = false,
			VerticalScrollBarVisibility = options.AllowScrolling
				? ScrollBarVisibility.Auto
				: ScrollBarVisibility.Disabled,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			HorizontalAlignment = HorizontalAlignment.Left,
			IsSelectionEnabled = false,
			Focusable = false,
			IsTabStop = false,
			MaxHeight = theme.MaxHeight,
			MaxWidth = theme.MaxWidth
		};

		if (options.AllowScrolling)
			ToolTipScrollChaining.Attach(viewer);

		return viewer;
	}

	private static ScrollViewer CreateFallbackContent(string content, MarkdownToolTipTheme theme, MarkdownToolTipOptions options)
	{
		// The scroll viewer constrains the text to the theme width, so the text block itself stays
		// unconstrained and wraps at the width it is given. The horizontal scroll bar must stay
		// disabled: re-enabling it would let the text block claim its full ideal width and stop
		// honoring the theme's maximum width.
		var textBlock = new TextBlock
		{
			Foreground = theme.Foreground,
			Text = content,
			TextWrapping = TextWrapping.Wrap,
			FontFamily = theme.BodyFontFamily,
			FontSize = theme.BodyFontSize
		};

		var scrollViewer = new ScrollViewer
		{
			Content = textBlock,
			MaxHeight = theme.MaxHeight,
			MaxWidth = theme.MaxWidth,
			VerticalScrollBarVisibility = options.AllowScrolling
				? ScrollBarVisibility.Auto
				: ScrollBarVisibility.Disabled,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			CanContentScroll = true,
			Focusable = false,
			IsTabStop = false
		};

		if (options.AllowScrolling)
			ToolTipScrollChaining.Attach(scrollViewer);

		return scrollViewer;
	}

	private static void ReportRenderFailure(Exception exception, MarkdownToolTipOptions options)
	{
		if (options.Logger is { } logger)
			s_renderFailedLogger(logger, exception);
	}
}

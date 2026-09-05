using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using ICSharpCode.AvalonEdit.Highlighting;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using AvalonTextEditor = ICSharpCode.AvalonEdit.TextEditor;
using Block = System.Windows.Documents.Block;
using Inline = System.Windows.Documents.Inline;
using List = System.Windows.Documents.List;
using MarkdigBlock = Markdig.Syntax.Block;
using MarkdigInline = Markdig.Syntax.Inlines.Inline;
using MarkdigMarkdown = Markdig.Markdown;
using MarkdigTable = Markdig.Extensions.Tables.Table;
using MarkdigTableCell = Markdig.Extensions.Tables.TableCell;
using MarkdigTableRow = Markdig.Extensions.Tables.TableRow;
using Table = System.Windows.Documents.Table;
using TableCell = System.Windows.Documents.TableCell;
using TableRow = System.Windows.Documents.TableRow;

namespace Nickelony.IDEKit.AvalonEdit.Extras.Markdown;

/// <summary>
/// Renders supported Markdown content into WPF elements for tooltip display.
/// </summary>
public static class MarkdownToolTipRenderer
{
	private static readonly MarkdownPipeline s_pipeline = new MarkdownPipelineBuilder()
		.UseAdvancedExtensions()
		.Build();

	/// <summary>
	/// Creates a WPF element that renders the given markdown content.
	/// </summary>
	/// <param name="content">The Markdown content to render.</param>
	/// <param name="theme">The theme to render with, or <see langword="null"/> for the default theme.</param>
	/// <param name="options">The rendering options, or <see langword="null"/> for the default options.</param>
	/// <returns>The rendered framework element.</returns>
	/// <remarks>
	/// The renderer supports the Markdown features enabled by its Markdig pipeline. Raw HTML blocks and inline
	/// HTML are omitted, and image links render their link text without loading an image.
	/// Whitespace-only content or an exception while parsing or rendering yields a plain-text element instead.
	/// Only absolute links whose schemes are listed in the effective options can be opened.
	/// </remarks>
	public static FrameworkElement CreateContent(string content, MarkdownToolTipTheme? theme = null, MarkdownToolTipOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(content);

		theme ??= MarkdownToolTipTheme.Default;
		options ??= MarkdownToolTipOptions.Default;

		string normalizedContent = NormalizeLineEndings(content);

		if (string.IsNullOrWhiteSpace(normalizedContent))
			return CreatePlainTextContent(string.Empty, theme, options);

		try
		{
			MarkdownDocument document = MarkdigMarkdown.Parse(normalizedContent, s_pipeline);
			FlowDocument flowDocument = RenderDocument(document, theme, options);

			return CreateViewer(flowDocument, theme, options);
		}
		catch (Exception)
		{
			return CreatePlainTextContent(normalizedContent, theme, options);
		}
	}

	/// <summary>
	/// Creates a plain-text element that renders the given content.
	/// </summary>
	/// <param name="content">The text content to render.</param>
	/// <param name="theme">The theme to render with, or <see langword="null"/> for the default theme.</param>
	/// <param name="options">The rendering options, or <see langword="null"/> for the default options.</param>
	/// <returns>The rendered framework element.</returns>
	/// <remarks>The content is displayed literally and is not parsed as Markdown.</remarks>
	public static FrameworkElement CreatePlainTextContent(string content, MarkdownToolTipTheme? theme = null, MarkdownToolTipOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(content);
		return CreateFallbackContent(content, theme ?? MarkdownToolTipTheme.Default, options ?? MarkdownToolTipOptions.Default);
	}

	/// <summary>
	/// Creates a read-only code block editor with the given language and code.
	/// </summary>
	/// <param name="language">The language used to resolve syntax highlighting, or <see langword="null"/>.</param>
	/// <param name="code">The code to display.</param>
	/// <param name="theme">The theme to render with, or <see langword="null"/> for the default theme.</param>
	/// <param name="options">The rendering options, or <see langword="null"/> for the default options.</param>
	/// <returns>The code block editor.</returns>
	/// <remarks>
	/// When configured, the custom highlighting callback is invoked before built-in resolution. If it returns
	/// <see langword="true"/>, built-in resolution is skipped; otherwise, built-in resolution is used.
	/// Built-in resolution recognizes a highlighting name or extension and common aliases such as
	/// <c>cs</c>, <c>csharp</c>, <c>js</c>, <c>ts</c>, and <c>json5</c>. The editor is read-only and does not
	/// accept keyboard focus.
	/// </remarks>
	public static AvalonTextEditor CreateCodeBlockEditor(string? language, string code, MarkdownToolTipTheme? theme = null, MarkdownToolTipOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(code);

		theme ??= MarkdownToolTipTheme.Default;
		options ??= MarkdownToolTipOptions.Default;

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
			Width = theme.TextMaxWidth,
			MaxWidth = theme.TextMaxWidth,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			FontFamily = theme.CodeFontFamily,
			FontSize = theme.CodeFontSize,
			ShowLineNumbers = false,
			WordWrap = true,
			Focusable = false,
			IsTabStop = false
		};

		editor.Options.AllowScrollBelowDocument = false;
		editor.Options.EnableHyperlinks = false;
		editor.Options.EnableEmailHyperlinks = false;
		editor.Options.HighlightCurrentLine = false;
		editor.Options.ShowBoxForControlCharacters = false;
		editor.TextArea.Margin = new Thickness(0.0);
		editor.TextArea.Focusable = false;
		editor.TextArea.IsTabStop = false;
		KeyboardNavigation.SetIsTabStop(editor, false);
		KeyboardNavigation.SetIsTabStop(editor.TextArea, false);

		// Code-block editors are passive; let the host provide highlighting before trying built-in resolution.
		if (options.InstallCustomHighlighting?.Invoke(editor, language) is not true)
			editor.SyntaxHighlighting = ResolveHighlighting(language);

		return editor;
	}

	internal static string NormalizeLineEndings(string text)
	{
		return text
			.Replace("\r\n", "\n", StringComparison.Ordinal)
			.Replace('\r', '\n');
	}

	private static FlowDocument RenderDocument(MarkdownDocument document, MarkdownToolTipTheme theme, MarkdownToolTipOptions options)
	{
		var flowDocument = new FlowDocument
		{
			FontFamily = theme.BodyFontFamily,
			FontSize = theme.BodyFontSize,
			Foreground = theme.Foreground,
			Background = Brushes.Transparent,
			PagePadding = new Thickness(0.0),
			ColumnWidth = theme.TextMaxWidth
		};

		foreach (MarkdigBlock block in document)
		{
			Block? renderedBlock = RenderBlock(block, theme, options);

			if (renderedBlock is not null)
				flowDocument.Blocks.Add(renderedBlock);
		}

		return flowDocument;
	}

	private static Block? RenderBlock(MarkdigBlock block, MarkdownToolTipTheme theme, MarkdownToolTipOptions options)
	{
		switch (block)
		{
			case ParagraphBlock paragraphBlock:
				return RenderParagraph(paragraphBlock.Inline, theme, options);

			case HeadingBlock headingBlock:
				return RenderHeading(headingBlock, theme, options);

			case FencedCodeBlock fencedCodeBlock:
				return RenderFencedCodeBlock(fencedCodeBlock, theme, options);

			case CodeBlock codeBlock:
				return RenderIndentedCodeBlock(codeBlock, theme, options);

			case QuoteBlock quoteBlock:
				return RenderQuoteBlock(quoteBlock, theme, options);

			case ListBlock listBlock:
				return RenderListBlock(listBlock, theme, options);

			case ThematicBreakBlock:
				return RenderThematicBreak(theme);

			case MarkdigTable tableBlock:
				return RenderTableBlock(tableBlock, theme, options);

			case HtmlBlock:
				return null;

			default:
				return block is LeafBlock leafBlock && leafBlock.Inline is not null
					? RenderParagraph(leafBlock.Inline, theme, options)
					: null;
		}
	}

	private static Paragraph RenderParagraph(ContainerInline? inline, MarkdownToolTipTheme theme, MarkdownToolTipOptions options)
	{
		var paragraph = new Paragraph
		{
			Margin = new Thickness(0.0, 0.0, 0.0, theme.BlockSpacing)
		};

		if (inline is not null)
			RenderInlines(paragraph.Inlines, inline, theme, options);

		return paragraph;
	}

	private static Paragraph RenderHeading(HeadingBlock headingBlock, MarkdownToolTipTheme theme, MarkdownToolTipOptions options)
	{
		var paragraph = new Paragraph
		{
			Margin = new Thickness(0.0, 4.0, 0.0, 4.0),
			FontSize = GetHeadingFontSize(theme, headingBlock.Level),
			FontWeight = FontWeights.Bold
		};

		if (headingBlock.Inline is not null)
			RenderInlines(paragraph.Inlines, headingBlock.Inline, theme, options);

		return paragraph;
	}

	private static double GetHeadingFontSize(MarkdownToolTipTheme theme, int level)
	{
		int index = Math.Max(0, Math.Min(theme.HeadingFontSizeScales.Count - 1, level - 1));

		return theme.BodyFontSize * theme.HeadingFontSizeScales[index];
	}

	private static BlockUIContainer RenderFencedCodeBlock(FencedCodeBlock fencedCodeBlock, MarkdownToolTipTheme theme, MarkdownToolTipOptions options)
	{
		string language = fencedCodeBlock.Info?.Trim() ?? string.Empty;
		string code = fencedCodeBlock.Lines.ToString();

		return new BlockUIContainer(CreateCodeBlockElement(language, code, theme, options));
	}

	private static BlockUIContainer RenderIndentedCodeBlock(CodeBlock codeBlock, MarkdownToolTipTheme theme, MarkdownToolTipOptions options)
	{
		string code = codeBlock.Lines.ToString();

		return new BlockUIContainer(CreateCodeBlockElement(null, code, theme, options));
	}

	private static Section RenderQuoteBlock(QuoteBlock quoteBlock, MarkdownToolTipTheme theme, MarkdownToolTipOptions options)
	{
		var section = new Section
		{
			Margin = new Thickness(0.0, 0.0, 0.0, theme.BlockSpacing),
			Padding = new Thickness(10.0, 4.0, 10.0, 4.0),
			BorderBrush = CreateCodeBorder(theme),
			BorderThickness = new Thickness(3.0, 0.0, 0.0, 0.0)
		};

		foreach (MarkdigBlock child in quoteBlock)
		{
			Block? renderedBlock = RenderBlock(child, theme, options);

			if (renderedBlock is not null)
				section.Blocks.Add(renderedBlock);
		}

		return section;
	}

	private static List RenderListBlock(ListBlock listBlock, MarkdownToolTipTheme theme, MarkdownToolTipOptions options)
	{
		var list = new List
		{
			Margin = new Thickness(18.0, 0.0, 0.0, theme.BlockSpacing),
			MarkerStyle = listBlock.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc
		};

		if (listBlock.IsOrdered)
			list.StartIndex = int.TryParse(listBlock.OrderedStart, NumberStyles.None, CultureInfo.InvariantCulture, out int start) ? start : 1;

		foreach (MarkdigBlock item in listBlock)
		{
			if (item is ListItemBlock listItemBlock)
				list.ListItems.Add(RenderListItemBlock(listItemBlock, theme, options));
		}

		return list;
	}

	private static ListItem RenderListItemBlock(ListItemBlock listItemBlock, MarkdownToolTipTheme theme, MarkdownToolTipOptions options)
	{
		var listItem = new ListItem();

		foreach (MarkdigBlock child in listItemBlock)
		{
			Block? renderedBlock = RenderBlock(child, theme, options);

			if (renderedBlock is not null)
				listItem.Blocks.Add(renderedBlock);
		}

		return listItem;
	}

	private static BlockUIContainer RenderThematicBreak(MarkdownToolTipTheme theme)
	{
		return new BlockUIContainer(new Border
		{
			Height = 1.0,
			Background = CreateCodeBorder(theme),
			Margin = new Thickness(0.0, 4.0, 0.0, 8.0)
		});
	}

	private static Table RenderTableBlock(MarkdigTable tableBlock, MarkdownToolTipTheme theme, MarkdownToolTipOptions options)
	{
		var table = new Table
		{
			CellSpacing = 0.0,
			Margin = new Thickness(0.0, 0.0, 0.0, theme.BlockSpacing)
		};

		var rowGroup = new TableRowGroup();
		table.RowGroups.Add(rowGroup);

		foreach (MarkdigTableRow markdownRow in tableBlock)
		{
			var row = new TableRow();
			rowGroup.Rows.Add(row);

			foreach (MarkdigTableCell markdownCell in markdownRow)
			{
				var cell = new TableCell
				{
					BorderBrush = CreateCodeBorder(theme),
					BorderThickness = new Thickness(0.5),
					Padding = new Thickness(6.0, 2.0, 6.0, 2.0),
					ColumnSpan = Math.Max(1, markdownCell.ColumnSpan),
					RowSpan = Math.Max(1, markdownCell.RowSpan),
					FontWeight = markdownRow.IsHeader ? FontWeights.Bold : FontWeights.Normal
				};

				foreach (MarkdigBlock child in markdownCell)
				{
					Block? renderedBlock = RenderBlock(child, theme, options);

					if (renderedBlock is not null)
						cell.Blocks.Add(renderedBlock);
				}

				row.Cells.Add(cell);
			}
		}

		return table;
	}

	private static void RenderInlines(InlineCollection target, ContainerInline container, MarkdownToolTipTheme theme, MarkdownToolTipOptions options)
	{
		MarkdigInline? current = container.FirstChild;

		while (current is not null)
		{
			MarkdigInline? next = current.NextSibling;
			Inline? rendered = RenderInline(current, theme, options);

			if (rendered is not null)
				target.Add(rendered);

			current = next;
		}
	}

	private static Inline? RenderInline(MarkdigInline inline, MarkdownToolTipTheme theme, MarkdownToolTipOptions options)
	{
		switch (inline)
		{
			case LiteralInline literalInline:
				return new Run(literalInline.Content.ToString());

			case CodeInline codeInline:
				return CreateInlineCodeContainer(codeInline.Content.ToString(), theme);

			case LinkInline linkInline:
				return RenderLink(linkInline, theme, options);

			case AutolinkInline autolinkInline:
				return RenderAutolink(autolinkInline, theme, options);

			case EmphasisInline emphasisInline:
				return RenderEmphasis(emphasisInline, theme, options);

			case LineBreakInline:
				return new LineBreak();

			case HtmlInline:
				return null;

			default:
				if (inline is ContainerInline container)
				{
					var span = new Span();
					RenderInlines(span.Inlines, container, theme, options);
					return span;
				}

				return null;
		}
	}

	private static Inline RenderLink(LinkInline linkInline, MarkdownToolTipTheme theme, MarkdownToolTipOptions options)
	{
		if (linkInline.IsImage)
		{
			var span = new Span();
			RenderInlines(span.Inlines, linkInline, theme, options);
			return span;
		}

		Hyperlink hyperlink = CreateHyperlink(linkInline.Url, theme, options);
		RenderInlines(hyperlink.Inlines, linkInline, theme, options);
		return hyperlink;
	}

	private static Hyperlink RenderAutolink(AutolinkInline autolinkInline, MarkdownToolTipTheme theme, MarkdownToolTipOptions options)
	{
		Hyperlink hyperlink = CreateHyperlink(autolinkInline.Url, theme, options);
		hyperlink.Inlines.Add(new Run(autolinkInline.Url));
		return hyperlink;
	}

	private static Hyperlink CreateHyperlink(string? url, MarkdownToolTipTheme theme, MarkdownToolTipOptions options)
	{
		bool canOpen = Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && IsSupportedHyperlink(uri, options);

		return new Hyperlink
		{
			NavigateUri = canOpen ? uri : null,
			Foreground = theme.LinkForeground,
			TextDecorations = TextDecorations.Underline,
			Cursor = canOpen ? Cursors.Hand : Cursors.Arrow,
			Focusable = false
		};
	}

	private static Span RenderEmphasis(EmphasisInline emphasisInline, MarkdownToolTipTheme theme, MarkdownToolTipOptions options)
	{
		Span span = emphasisInline.DelimiterChar == '~'
			? new Span { TextDecorations = TextDecorations.Strikethrough }
			: emphasisInline.DelimiterCount >= 2
				? new Bold()
				: new Italic();

		RenderInlines(span.Inlines, emphasisInline, theme, options);
		return span;
	}

	private static InlineUIContainer CreateInlineCodeContainer(string text, MarkdownToolTipTheme theme)
	{
		return new InlineUIContainer(
			new Border
			{
				Background = CreateCodeBackground(theme),
				BorderBrush = CreateCodeBorder(theme),
				BorderThickness = new Thickness(1.0),
				CornerRadius = new CornerRadius(2.0),
				Padding = new Thickness(4.0, 1.0, 4.0, 1.0),
				Child = new TextBlock
				{
					Text = text ?? string.Empty,
					Foreground = theme.Foreground,
					FontFamily = theme.CodeFontFamily,
					FontSize = theme.CodeFontSize,
					TextWrapping = TextWrapping.NoWrap
				}
			})
		{
			BaselineAlignment = BaselineAlignment.Center
		};
	}

	private static Border CreateCodeBlockElement(string? language, string code, MarkdownToolTipTheme theme, MarkdownToolTipOptions options)
	{
		AvalonTextEditor editor = CreateCodeBlockEditor(language, code, theme, options);
		string normalizedCode = editor.Text;

		double lineHeight = GetEditorLineHeight(editor, theme);
		double maxVisibleHeight = Math.Max(lineHeight + 4.0, Math.Ceiling(theme.MaxVisibleCodeBlockLines * lineHeight) + 2.0);
		double desiredHeight = Math.Max(lineHeight + 4.0, MeasureWrappedCodeHeight(normalizedCode, theme.TextMaxWidth, lineHeight, theme));

		editor.Height = Math.Min(desiredHeight, maxVisibleHeight);
		editor.VerticalScrollBarVisibility = options.AllowScrolling && desiredHeight > maxVisibleHeight
			? ScrollBarVisibility.Auto
			: ScrollBarVisibility.Hidden;

		if (options.AllowScrolling)
			editor.PreviewMouseWheel += ScrollHost_PreviewMouseWheel;

		return new Border
		{
			Background = CreateCodeBackground(theme),
			BorderBrush = CreateCodeBorder(theme),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(3.0),
			Padding = new Thickness(8.0, 6.0, 8.0, 6.0),
			Margin = new Thickness(0.0, 4.0, 0.0, 6.0),
			MaxWidth = theme.TextMaxWidth,
			Child = editor
		};
	}

	private static double GetEditorLineHeight(AvalonTextEditor editor, MarkdownToolTipTheme theme)
	{
		double lineHeight = editor.TextArea.TextView.DefaultLineHeight;

		if (!double.IsNaN(lineHeight) && lineHeight > 0.0)
			return Math.Ceiling(lineHeight);

		var typeface = new Typeface(editor.FontFamily, editor.FontStyle, editor.FontWeight, editor.FontStretch);
		var formattedText = new FormattedText(
			"Ag",
			CultureInfo.CurrentCulture,
			FlowDirection.LeftToRight,
			typeface,
			editor.FontSize,
			Brushes.Transparent,
			1.0);

		return Math.Ceiling(Math.Max(1.0, formattedText.Height));
	}

	private static double MeasureWrappedCodeHeight(string code, double width, double lineHeight, MarkdownToolTipTheme theme)
	{
		var textBlock = new TextBlock
		{
			Text = string.IsNullOrEmpty(code) ? " " : code,
			FontFamily = theme.CodeFontFamily,
			FontSize = theme.CodeFontSize,
			TextWrapping = TextWrapping.Wrap,
			MaxWidth = width
		};

		textBlock.Measure(new Size(width, double.PositiveInfinity));
		return Math.Max(Math.Ceiling(textBlock.DesiredSize.Height) + 2.0, Math.Ceiling(lineHeight) + 4.0);
	}

	private static string NormalizeCodeBlockText(string code)
	{
		string normalizedCode = NormalizeLineEndings(code);

		if (string.IsNullOrEmpty(normalizedCode))
			return string.Empty;

		string[] lines = normalizedCode.Split('\n');
		int lastContentLineIndex = lines.Length - 1;

		while (lastContentLineIndex >= 0 && string.IsNullOrWhiteSpace(lines[lastContentLineIndex]))
			lastContentLineIndex--;

		if (lastContentLineIndex < 0)
			return string.Empty;

		return string.Join(Environment.NewLine, lines, 0, lastContentLineIndex + 1);
	}

	private static IHighlightingDefinition? ResolveHighlighting(string? language)
	{
		if (string.IsNullOrWhiteSpace(language))
			return null;

		string normalizedLanguage = language.Trim().ToLowerInvariant();
		IHighlightingDefinition? definition = HighlightingManager.Instance.GetDefinition(normalizedLanguage);

		if (definition is not null)
			return definition;

		definition = HighlightingManager.Instance.GetDefinitionByExtension(normalizedLanguage.StartsWith('.')
			? normalizedLanguage
			: "." + normalizedLanguage);

		if (definition is not null)
			return definition;

		return normalizedLanguage switch
		{
			"cs" => HighlightingManager.Instance.GetDefinitionByExtension(".cs"),
			"csharp" => HighlightingManager.Instance.GetDefinitionByExtension(".cs"),
			"js" => HighlightingManager.Instance.GetDefinitionByExtension(".js"),
			"ts" => HighlightingManager.Instance.GetDefinitionByExtension(".ts"),
			"json5" => HighlightingManager.Instance.GetDefinitionByExtension(".json"),
			_ => null
		};
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
				: ScrollBarVisibility.Hidden,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			HorizontalAlignment = HorizontalAlignment.Left,
			IsSelectionEnabled = false,
			Focusable = false,
			MaxHeight = theme.MaxHeight,
			MaxWidth = theme.MaxWidth
		};

		if (options.AllowScrolling)
			viewer.PreviewMouseWheel += ScrollHost_PreviewMouseWheel;

		viewer.PreviewMouseLeftButtonUp += (sender, e) => HyperlinkHost_PreviewMouseLeftButtonUp(sender, e, options);
		return viewer;
	}

	private static ScrollViewer CreateFallbackContent(string content, MarkdownToolTipTheme theme, MarkdownToolTipOptions options)
	{
		var textBlock = new TextBlock
		{
			Foreground = theme.Foreground,
			Text = content,
			TextWrapping = TextWrapping.Wrap,
			FontFamily = theme.BodyFontFamily,
			FontSize = theme.BodyFontSize,
			MaxWidth = theme.TextMaxWidth
		};

		var scrollViewer = new ScrollViewer
		{
			Content = textBlock,
			MaxHeight = theme.MaxHeight,
			MaxWidth = theme.MaxWidth,
			VerticalScrollBarVisibility = options.AllowScrolling
				? ScrollBarVisibility.Auto
				: ScrollBarVisibility.Hidden,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			CanContentScroll = true
		};

		if (options.AllowScrolling)
			scrollViewer.PreviewMouseWheel += ScrollHost_PreviewMouseWheel;

		return scrollViewer;
	}

	private static void HyperlinkHost_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e, MarkdownToolTipOptions options)
	{
		var hyperlink = (e.OriginalSource as DependencyObject)?.FindAncestorOrSelf<Hyperlink>();

		if (hyperlink is not null && TryOpenHyperlink(hyperlink.NavigateUri, options))
			e.Handled = true;
	}

	private static bool TryOpenHyperlink(Uri? uri, MarkdownToolTipOptions options)
	{
		if (uri is null || !IsSupportedHyperlink(uri, options))
			return false;

		try
		{
			Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}

	private static bool IsSupportedHyperlink(Uri? uri, MarkdownToolTipOptions options)
		=> uri is not null && uri.IsAbsoluteUri && options.SupportedHyperlinkSchemes.Contains(uri.Scheme);

	private static void ScrollHost_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
	{
		var scrollViewer = sender as ScrollViewer ?? (sender as DependencyObject)?.FindVisualDescendant<ScrollViewer>();

		if (scrollViewer is null || scrollViewer.ScrollableHeight <= 0.0)
			return;

		if (e.Delta > 0)
			scrollViewer.LineUp();
		else if (e.Delta < 0)
			scrollViewer.LineDown();

		e.Handled = true;
	}

	private static SolidColorBrush CreateCodeBackground(MarkdownToolTipTheme theme)
	{
		Color baseColor = theme.Background is SolidColorBrush solidBrush
			? solidBrush.Color
			: Colors.White;

		return CreateFrozenBrush(Blend(baseColor, Colors.Black, theme.CodeBackgroundBlendRatio));
	}

	private static SolidColorBrush CreateCodeBorder(MarkdownToolTipTheme theme)
	{
		Color baseColor = theme.Background is SolidColorBrush solidBrush
			? solidBrush.Color
			: Colors.White;

		return CreateFrozenBrush(Blend(baseColor, Colors.White, theme.CodeBorderBlendRatio));
	}

	private static SolidColorBrush CreateFrozenBrush(Color color)
	{
		var brush = new SolidColorBrush(color);
		brush.Freeze();
		return brush;
	}

	private static Color Blend(Color first, Color second, double ratio)
	{
		double clampedRatio = Math.Max(0.0, Math.Min(1.0, ratio));
		double inverseRatio = 1.0 - clampedRatio;

		return Color.FromArgb(
			(byte)Math.Round(first.A * inverseRatio + second.A * clampedRatio),
			(byte)Math.Round(first.R * inverseRatio + second.R * clampedRatio),
			(byte)Math.Round(first.G * inverseRatio + second.G * clampedRatio),
			(byte)Math.Round(first.B * inverseRatio + second.B * clampedRatio));
	}

	private static T? FindAncestorOrSelf<T>(this DependencyObject? current) where T : DependencyObject
	{
		while (current is not null)
		{
			if (current is T match)
				return match;

			current = current is Visual or Visual3D
				? VisualTreeHelper.GetParent(current)
				: LogicalTreeHelper.GetParent(current);
		}

		return null;
	}

	private static T? FindVisualDescendant<T>(this DependencyObject current) where T : DependencyObject
	{
		int count = VisualTreeHelper.GetChildrenCount(current);

		for (int i = 0; i < count; i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(current, i);

			if (child is T match)
				return match;

			T? result = FindVisualDescendant<T>(child);

			if (result is not null)
				return result;
		}

		return null;
	}
}

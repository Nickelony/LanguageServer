using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
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

namespace Nickelony.IDEKit.AvalonEdit.Markdown;

/// <summary>
/// Builds the WPF <see cref="FlowDocument"/> representation of Markdown content for
/// <see cref="MarkdownToolTipRenderer"/>.
/// </summary>
/// <remarks>
/// <para>
/// The builder parses Markdown with the renderer's deliberately narrow pipeline and emits the parsed
/// AST directly into WPF block and inline elements. Code blocks are delegated to
/// <see cref="MarkdownCodeBlockFactory"/>.
/// </para>
/// <para>
/// All members create and touch WPF elements and must run on the UI thread that owns the target
/// elements. The caller owns text normalization and the plain-text fallback behavior; a parsing or
/// rendering failure propagates to the caller.
/// </para>
/// </remarks>
internal static class MarkdownFlowDocumentBuilder
{
	private const double InlineCodeHorizontalPadding = 4.0;
	private const double InlineCodeVerticalPadding = 1.0;
	private const double InlineCodeBorderThickness = 1.0;

	// Deliberately narrow pipeline so unsupported Markdig constructs render as literal text instead of
	// an approximation with the wrong style; CreateContent documents the enabled extensions.
	private static readonly MarkdownPipeline s_pipeline = new MarkdownPipelineBuilder()
		.UsePipeTables()
		.UseAutoLinks()
		.UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
		.Build();

	private static readonly Action<ILogger, string, Exception?> s_hyperlinkOpenFailedLogger = LoggerMessage.Define<string>(
		LogLevel.Warning,
		new EventId(2, "HyperlinkOpenFailed"),
		"Opening hyperlink '{Uri}' failed.");

	/// <summary>
	/// Parses and renders the given Markdown into a new flow document.
	/// </summary>
	/// <param name="markdown">The Markdown content to render.</param>
	/// <param name="theme">The theme to render with.</param>
	/// <param name="options">The rendering options.</param>
	/// <returns>The rendered document.</returns>
	internal static FlowDocument Render(string markdown, MarkdownToolTipTheme theme, MarkdownToolTipOptions options)
	{
		MarkdownDocument document = MarkdigMarkdown.Parse(markdown, s_pipeline);

		return RenderDocument(document, theme, options);
	}

	private static FlowDocument RenderDocument(MarkdownDocument document, MarkdownToolTipTheme theme, MarkdownToolTipOptions options)
	{
		FlowDocument flowDocument = CreateBaseFlowDocument(theme);

		foreach (MarkdigBlock block in document)
		{
			Block? renderedBlock = RenderBlock(block, theme, options);

			if (renderedBlock is not null)
				flowDocument.Blocks.Add(renderedBlock);
		}

		return flowDocument;
	}

	internal static FlowDocument CreateBaseFlowDocument(MarkdownToolTipTheme theme)
	{
		return new FlowDocument
		{
			FontFamily = theme.BodyFontFamily,
			FontSize = theme.BodyFontSize,
			Foreground = theme.Foreground,
			Background = Brushes.Transparent,
			PagePadding = new Thickness(0.0)
		};
	}

	internal static FlowDocument CreatePlainTextFlowDocument(string content, MarkdownToolTipTheme theme)
	{
		FlowDocument flowDocument = CreateBaseFlowDocument(theme);

		flowDocument.Blocks.Add(new Paragraph(new Run(content))
		{
			Margin = new Thickness(0.0, 0.0, 0.0, theme.BlockSpacing)
		});

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
		if (theme.HeadingFontSizeScales.Count == 0)
			return theme.BodyFontSize;

		int index = Math.Max(0, Math.Min(theme.HeadingFontSizeScales.Count - 1, level - 1));

		return theme.BodyFontSize * theme.HeadingFontSizeScales[index];
	}

	private static BlockUIContainer RenderFencedCodeBlock(FencedCodeBlock fencedCodeBlock, MarkdownToolTipTheme theme, MarkdownToolTipOptions options)
	{
		string language = fencedCodeBlock.Info?.Trim() ?? string.Empty;
		string code = fencedCodeBlock.Lines.ToString();

		return new BlockUIContainer(MarkdownCodeBlockFactory.CreateCodeBlockElement(language, code, theme, options));
	}

	private static BlockUIContainer RenderIndentedCodeBlock(CodeBlock codeBlock, MarkdownToolTipTheme theme, MarkdownToolTipOptions options)
	{
		string code = codeBlock.Lines.ToString();

		return new BlockUIContainer(MarkdownCodeBlockFactory.CreateCodeBlockElement(null, code, theme, options));
	}

	private static Section RenderQuoteBlock(QuoteBlock quoteBlock, MarkdownToolTipTheme theme, MarkdownToolTipOptions options)
	{
		var section = new Section
		{
			Margin = new Thickness(0.0, 0.0, 0.0, theme.BlockSpacing),
			Padding = new Thickness(10.0, 4.0, 10.0, 4.0),
			BorderBrush = MarkdownRenderingAssets.CreateBorderBrush(theme),
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
			Background = MarkdownRenderingAssets.CreateBorderBrush(theme),
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

			int columnIndex = 0;

			foreach (MarkdigTableCell markdownCell in markdownRow)
			{
				var cell = new TableCell
				{
					BorderBrush = MarkdownRenderingAssets.CreateBorderBrush(theme),
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
					{
						// The cell already supplies its own padding, so the block spacing would leave dead
						// space below the cell content.
						SuppressBlockSpacing(renderedBlock);
						cell.Blocks.Add(renderedBlock);
					}
				}

				// GFM column alignment is carried by the table's column definitions, not by the cells. A
				// spanning cell consumes its covered columns so later cells map to their own definitions.
				if (GetColumnAlignment(tableBlock, columnIndex) is TextAlignment textAlignment)
					ApplyTextAlignment(cell, textAlignment);

				row.Cells.Add(cell);
				columnIndex += cell.ColumnSpan;
			}
		}

		return table;
	}

	private static TextAlignment? GetColumnAlignment(MarkdigTable table, int columnIndex)
	{
		if (columnIndex < 0 || columnIndex >= table.ColumnDefinitions.Count)
			return null;

		return table.ColumnDefinitions[columnIndex].Alignment switch
		{
			TableColumnAlign.Left => TextAlignment.Left,
			TableColumnAlign.Center => TextAlignment.Center,
			TableColumnAlign.Right => TextAlignment.Right,
			_ => null
		};
	}

	private static void ApplyTextAlignment(TableCell cell, TextAlignment alignment)
	{
		foreach (Block block in cell.Blocks)
		{
			if (block is Paragraph paragraph)
				paragraph.TextAlignment = alignment;
		}
	}

	private static void SuppressBlockSpacing(Block block)
	{
		Thickness margin = block.Margin;
		block.Margin = new Thickness(margin.Left, margin.Top, margin.Right, 0.0);
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

			case LineBreakInline lineBreakInline:
				// CommonMark soft breaks join the lines of a paragraph with a space; only hard breaks
				// (two trailing spaces or a trailing backslash) start a new line.
				return lineBreakInline.IsHard ? new LineBreak() : new Run(" ");

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
		// An email autolink carries the bare address as its URL, so the mailto target is added here;
		// the display text stays the address, matching CommonMark. A "mailto:" autolink keeps both the
		// target and the display text as written.
		string? target = autolinkInline.Url is { Length: > 0 } url && autolinkInline.IsEmail
			? "mailto:" + url
			: autolinkInline.Url;

		Hyperlink hyperlink = CreateHyperlink(target, theme, options);
		hyperlink.Inlines.Add(new Run(autolinkInline.Url));
		return hyperlink;
	}

	private static Hyperlink CreateHyperlink(string? url, MarkdownToolTipTheme theme, MarkdownToolTipOptions options)
	{
		bool canOpen = Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && IsSupportedHyperlink(uri, options);

		var hyperlink = new Hyperlink
		{
			NavigateUri = canOpen ? uri : null,
			Foreground = theme.LinkForeground,
			TextDecorations = TextDecorations.Underline,
			Cursor = canOpen ? Cursors.Hand : Cursors.Arrow,
			Focusable = false
		};

		if (canOpen && uri is not null)
		{
			// The renderer owns activation (the configured opener or the operating system) and marks the
			// navigation request as handled so an ambient NavigationService does not navigate twice.
			hyperlink.RequestNavigate += (_, e) => e.Handled = true;

			hyperlink.Click += (_, e) =>
			{
				if (TryOpenHyperlink(uri, options))
					e.Handled = true;
			};
		}

		return hyperlink;
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
				Background = MarkdownRenderingAssets.CreateCodeBackground(theme),
				BorderBrush = MarkdownRenderingAssets.CreateBorderBrush(theme),
				BorderThickness = new Thickness(InlineCodeBorderThickness),
				CornerRadius = new CornerRadius(2.0),
				Padding = new Thickness(InlineCodeHorizontalPadding, InlineCodeVerticalPadding, InlineCodeHorizontalPadding, InlineCodeVerticalPadding),
				Child = new TextBlock
				{
					Text = text,
					Foreground = theme.Foreground,
					FontFamily = theme.CodeFontFamily,
					FontSize = theme.CodeFontSize,

					// Inline code wraps instead of overflowing the tooltip: an identifier wider than the
					// tooltip cannot be scrolled to horizontally, so it must break inside the border.
					TextWrapping = TextWrapping.Wrap,
					MaxWidth = Math.Max(
						1.0,
						theme.CodeMaxWidth
							- (2.0 * InlineCodeHorizontalPadding)
							- (2.0 * InlineCodeBorderThickness))
				}
			})
		{
			BaselineAlignment = BaselineAlignment.Center
		};
	}

	private static bool TryOpenHyperlink(Uri? uri, MarkdownToolTipOptions options)
	{
		if (uri is null || !IsSupportedHyperlink(uri, options))
			return false;

		// A callback result other than true counts as not handled, so the operating system's default
		// protocol handler runs exactly as it would without a callback. A host that wants to suppress an
		// activation returns true without opening anything.
		if (options.OpenHyperlink is { } openHyperlink)
		{
			try
			{
				if (openHyperlink(uri))
					return true;
			}
			catch (Exception exception)
			{
				// Opening a link is a best-effort host action: report the failure and keep the default
				// behavior as the fallback.
				ReportHyperlinkOpenFailure(uri.AbsoluteUri, exception, options);
			}
		}

		try
		{
			return DefaultHyperlinkOpener(uri);
		}
		catch (Exception exception)
		{
			ReportHyperlinkOpenFailure(uri.AbsoluteUri, exception, options);
			return false;
		}
	}

	// The operating-system opener is replaceable so tests can verify the fallback behavior without
	// launching a real protocol handler.
	internal static Func<Uri, bool> DefaultHyperlinkOpener { get; set; } = OpenWithShell;

	private static bool OpenWithShell(Uri uri)
	{
		Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
		return true;
	}

	private static bool IsSupportedHyperlink(Uri? uri, MarkdownToolTipOptions options)
		=> uri is not null && uri.IsAbsoluteUri && options.SupportedHyperlinkSchemes.Contains(uri.Scheme);

	private static void ReportHyperlinkOpenFailure(string uri, Exception exception, MarkdownToolTipOptions options)
	{
		if (options.Logger is { } logger)
			s_hyperlinkOpenFailedLogger(logger, uri, exception);
	}
}

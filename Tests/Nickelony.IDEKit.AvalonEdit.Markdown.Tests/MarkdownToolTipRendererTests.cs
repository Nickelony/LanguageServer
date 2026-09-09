using ICSharpCode.AvalonEdit.Highlighting;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using AvalonTextEditor = ICSharpCode.AvalonEdit.TextEditor;
using List = System.Windows.Documents.List;

namespace Nickelony.IDEKit.AvalonEdit.Markdown.Tests;

[STATestClass]
public sealed class MarkdownToolTipRendererTests
{
	[TestMethod]
	public void CreateContent_WhitespaceOnlyContent_ReturnsFallbackScrollViewer()
	{
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("   \n  ");

		Assert.IsInstanceOfType(element, typeof(ScrollViewer));
	}

	[TestMethod]
	public void CreateContent_FencedCodeBlock_ProducesTextEditorCodeBlock()
	{
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("before\n\n```lua\nlocal value = 1\nprint(value)\n```\n\nafter");
		var viewer = (FlowDocumentScrollViewer)element;

		var container = FindAll<BlockUIContainer>(viewer.Document).Single();
		var border = (Border)container.Child;
		var editor = (AvalonTextEditor)border.Child;

		Assert.AreEqual("local value = 1\nprint(value)", editor.Text);
		Assert.IsFalse(editor.Focusable);
		Assert.IsFalse(editor.IsTabStop);
	}

	[TestMethod]
	public void CreateContent_CodeBlockInsideBlockquote_ProducesCodeBlockEditor()
	{
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("> ```\n> local x = 1\n> ```");
		var viewer = (FlowDocumentScrollViewer)element;

		var container = FindAll<BlockUIContainer>(viewer.Document).Single();
		var editor = (AvalonTextEditor)((Border)container.Child).Child;

		Assert.AreEqual("local x = 1", editor.Text);
	}

	[TestMethod]
	public void CreateContent_FencedCodeBlock_EditorStaysInsideBorderContentArea()
	{
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("before\n\n```csharp\nvar value = 1;\n```\n\nafter");
		var viewer = (FlowDocumentScrollViewer)element;

		viewer.Measure(new Size(540.0, 420.0));
		viewer.Arrange(new Rect(0.0, 0.0, 540.0, 420.0));
		viewer.UpdateLayout();

		var container = FindAll<BlockUIContainer>(viewer.Document).Single();
		var border = (Border)container.Child;
		var editor = (AvalonTextEditor)border.Child;

		Point editorPosition = editor.TranslatePoint(new Point(0.0, 0.0), border);
		double contentLeft = border.Padding.Left + border.BorderThickness.Left;
		double contentWidth = border.ActualWidth
			- border.Padding.Left
			- border.Padding.Right
			- border.BorderThickness.Left
			- border.BorderThickness.Right;

		Assert.IsTrue(editorPosition.X >= contentLeft);
		Assert.IsTrue(editor.ActualWidth <= contentWidth);
	}

	[TestMethod]
	public void CreateContent_InlineCode_ProducesStyledInlineContainer()
	{
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("Use `TextEditor` here.");
		var viewer = (FlowDocumentScrollViewer)element;

		var container = FindAll<InlineUIContainer>(viewer.Document).Single();
		var border = (Border)container.Child;
		var textBlock = (TextBlock)border.Child;

		Assert.AreEqual("TextEditor", textBlock.Text);
		Assert.AreEqual("Consolas", textBlock.FontFamily.Source);
	}

	[TestMethod]
	public void CreateContent_LongInlineCode_WrapsInsideTheTooltipWidth()
	{
		string identifier = new('x', 240);
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent($"Use `{identifier}` here.");
		var viewer = (FlowDocumentScrollViewer)element;
		Window window = WPFTestHost.ShowInHostWindow(viewer);

		try
		{
			viewer.UpdateLayout();
			WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

			var container = FindAll<InlineUIContainer>(viewer.Document).Single();
			var border = (Border)container.Child;
			var textBlock = (TextBlock)border.Child;

			Assert.AreEqual(TextWrapping.Wrap, textBlock.TextWrapping);
			Assert.AreEqual(identifier, textBlock.Text);
			Assert.IsTrue(
				border.ActualWidth <= MarkdownToolTipTheme.Default.CodeMaxWidth + 1.0,
				$"Inline code width {border.ActualWidth} must stay inside the tooltip.");
			Assert.IsTrue(
				textBlock.ActualHeight > textBlock.FontSize * 2.0,
				"The long identifier must wrap onto more than one line.");
		}
		finally
		{
			window.Close();
		}
	}

	[TestMethod]
	public void CreateContent_ShortCodeBlock_FollowsContentHeightWithoutScrollBar()
	{
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("```lua\nlocal value = 1\n```");
		var viewer = (FlowDocumentScrollViewer)element;
		var border = (Border)FindAll<BlockUIContainer>(viewer.Document).Single().Child;
		var editor = (AvalonTextEditor)border.Child;

		// The scroll bar is automatic and only appears when the content actually overflows, and a block
		// below the limit keeps an automatic height so the host layout reports the exact extent.
		Assert.AreEqual(ScrollBarVisibility.Auto, editor.VerticalScrollBarVisibility);
		Assert.IsTrue(double.IsNaN(editor.Height), "A short block must not fix its height.");
		Assert.IsTrue(editor.MaxHeight > 0.0, "A short block still carries the visible-line clamp.");
	}

	[TestMethod]
	public void CreateContent_LongCodeBlock_ClampsToTheVisibleLineLimit()
	{
		string code = string.Join("\n", Enumerable.Range(1, 40).Select(index => $"local value{index} = {index}"));
		var theme = MarkdownToolTipTheme.Default with { MaxVisibleCodeBlockLines = 5 };
		var viewer = (FlowDocumentScrollViewer)MarkdownToolTipRenderer.CreateContent($"```lua\n{code}\n```", theme);
		var border = (Border)FindAll<BlockUIContainer>(viewer.Document).Single().Child;
		var editor = (AvalonTextEditor)border.Child;

		double lineHeight = Math.Ceiling(editor.TextArea.TextView.DefaultLineHeight);
		double expectedClamp = Math.Max(lineHeight + 4.0, Math.Ceiling(5 * lineHeight) + 2.0);

		Assert.AreEqual(ScrollBarVisibility.Auto, editor.VerticalScrollBarVisibility);
		Assert.AreEqual(expectedClamp, editor.Height, "The editor height must be the visible-line clamp.");
		Assert.AreEqual(expectedClamp, editor.MaxHeight);
	}

	[TestMethod]
	public void CreateContent_OutOfRangeBlendRatio_IsClamped()
	{
		var highTheme = MarkdownToolTipTheme.Default with { CodeBackgroundBlendRatio = 5.0 };
		var highViewer = (FlowDocumentScrollViewer)MarkdownToolTipRenderer.CreateContent("Use `x` here.", highTheme);
		var highBorder = (Border)FindAll<InlineUIContainer>(highViewer.Document).Single().Child;

		// The white base color is blended fully toward black.
		Assert.AreEqual(Color.FromRgb(0, 0, 0), ((SolidColorBrush)highBorder.Background).Color);

		var lowTheme = MarkdownToolTipTheme.Default with { CodeBackgroundBlendRatio = -1.0 };
		var lowViewer = (FlowDocumentScrollViewer)MarkdownToolTipRenderer.CreateContent("Use `x` here.", lowTheme);
		var lowBorder = (Border)FindAll<InlineUIContainer>(lowViewer.Document).Single().Child;

		// Clamped to zero: the base color is used unchanged.
		Assert.AreEqual(Colors.White, ((SolidColorBrush)lowBorder.Background).Color);
	}

	[TestMethod]
	public void CreateContent_HttpsLink_CreatesNavigableHyperlink()
	{
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("[docs](https://example.com)");
		var viewer = (FlowDocumentScrollViewer)element;

		Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

		Assert.AreEqual("https://example.com/", hyperlink.NavigateUri?.AbsoluteUri);
		Assert.AreEqual(Cursors.Hand, hyperlink.Cursor);
	}

	[TestMethod]
	public void CreateContent_Autolink_CreatesHyperlink()
	{
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("See <https://example.com> for details.");
		var viewer = (FlowDocumentScrollViewer)element;

		Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

		Assert.AreEqual("https://example.com/", hyperlink.NavigateUri?.AbsoluteUri);
		Assert.AreEqual(Cursors.Hand, hyperlink.Cursor);
	}

	[TestMethod]
	public void CreateContent_UnsupportedSchemeLink_IsNotNavigable()
	{
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("[x](javascript:alert(1))");
		var viewer = (FlowDocumentScrollViewer)element;

		Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

		Assert.IsNull(hyperlink.NavigateUri);
		Assert.AreEqual(Cursors.Arrow, hyperlink.Cursor);
	}

	[TestMethod]
	public void CreateContent_HyperlinkRequestNavigate_IsSuppressed()
	{
		// The renderer owns activation, so an ambient NavigationService must never navigate the link
		// a second time; the handler marks the request as handled.
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("[docs](https://example.com)");
		var viewer = (FlowDocumentScrollViewer)element;
		Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

		var requestNavigateEvent = new RequestNavigateEventArgs(new Uri("https://example.com"), "https://example.com");
		hyperlink.RaiseEvent(requestNavigateEvent);

		Assert.IsTrue(requestNavigateEvent.Handled);
	}

	[TestMethod]
	public void CreateContent_CustomSupportedSchemes_AllowsConfiguredScheme()
	{
		var options = new MarkdownToolTipOptions
		{
			SupportedHyperlinkSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ftp" }
		};

		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("[file](ftp://example.com/file)", MarkdownToolTipTheme.Default, options);
		var viewer = (FlowDocumentScrollViewer)element;

		Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

		Assert.AreEqual("ftp", hyperlink.NavigateUri?.Scheme);
		Assert.AreEqual(Cursors.Hand, hyperlink.Cursor);
	}

	[TestMethod]
	public void CreateContent_SchemeCasingDiffersFromTheAssignedSet_OpensTheLink()
	{
		var options = new MarkdownToolTipOptions
		{
			SupportedHyperlinkSchemes = new HashSet<string>(StringComparer.Ordinal) { "FTP" }
		};

		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("[file](ftp://example.com/file)", MarkdownToolTipTheme.Default, options);
		var viewer = (FlowDocumentScrollViewer)element;

		Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

		// The options copy compares schemes case-insensitively, so the assigned "FTP" allows the "ftp"
		// scheme the URI reports.
		Assert.AreEqual("ftp", hyperlink.NavigateUri?.Scheme);
		Assert.AreEqual(Cursors.Hand, hyperlink.Cursor);
	}

	[TestMethod]
	public void DefaultOptions_HyperlinkSchemes_CannotBeMutatedThroughTheInterface()
	{
		ICollection<string> schemes = (ICollection<string>)MarkdownToolTipOptions.Default.SupportedHyperlinkSchemes;

		Assert.ThrowsExactly<NotSupportedException>(() => schemes.Add("ftp"));
	}

	[TestMethod]
	public void DefaultOptions_HighlightingAliases_CannotBeMutatedThroughTheInterface()
	{
		IDictionary<string, string> aliases = (IDictionary<string, string>)MarkdownToolTipOptions.Default.HighlightingAliases;

		Assert.ThrowsExactly<NotSupportedException>(() => aliases.Add("probe", ".cs"));
	}

	[TestMethod]
	public void Options_AssignedHyperlinkSchemes_AreCopiedFrozenAndCaseInsensitive()
	{
		var schemes = new HashSet<string>(StringComparer.Ordinal) { "ftp" };
		var options = new MarkdownToolTipOptions { SupportedHyperlinkSchemes = schemes };

		schemes.Add("file");

		// The options hold a frozen case-insensitive copy of the assigned set: the later source change
		// is not observed, the stored set cannot be mutated, and scheme casing does not matter.
		Assert.IsTrue(options.SupportedHyperlinkSchemes.Contains("FTP"));
		Assert.IsFalse(options.SupportedHyperlinkSchemes.Contains("file"));

		ICollection<string> storedSchemes = (ICollection<string>)options.SupportedHyperlinkSchemes;

		Assert.ThrowsExactly<NotSupportedException>(() => storedSchemes.Add("mailto"));
	}

	[TestMethod]
	public void Options_AssignedHighlightingAliases_AreCopiedFrozenAndCaseInsensitive()
	{
		var aliases = new Dictionary<string, string>(StringComparer.Ordinal) { ["MyLang"] = ".cs" };
		var options = new MarkdownToolTipOptions { HighlightingAliases = aliases };

		aliases["MyLang"] = ".json";
		aliases["other"] = ".txt";

		// The options hold a frozen case-insensitive copy of the assigned map: the later source changes
		// are not observed, the stored map cannot be mutated, and key casing does not matter.
		Assert.AreEqual(".cs", options.HighlightingAliases["MYLANG"]);
		Assert.IsFalse(options.HighlightingAliases.ContainsKey("other"));

		IDictionary<string, string> storedAliases = (IDictionary<string, string>)options.HighlightingAliases;

		Assert.ThrowsExactly<NotSupportedException>(() => storedAliases.Add("probe", ".cs"));
	}

	[TestMethod]
	public void CreateContent_RawHtmlBlock_IsOmitted()
	{
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("<div>raw</div>");
		var viewer = (FlowDocumentScrollViewer)element;

		Assert.AreEqual(0, viewer.Document.Blocks.Count);
	}

	[TestMethod]
	public void CreateContent_InlineHtml_IsOmittedButTextIsKept()
	{
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("before <b>bold</b> after");
		var viewer = (FlowDocumentScrollViewer)element;

		Paragraph paragraph = FindAll<Paragraph>(viewer.Document).Single();

		Assert.AreEqual("before bold after", GetParagraphText(paragraph));
	}

	[TestMethod]
	public void CreateContent_Image_RendersAltTextWithoutImageElement()
	{
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("![alt text](https://example.com/image.png)");
		var viewer = (FlowDocumentScrollViewer)element;

		Assert.AreEqual(0, FindAll<Hyperlink>(viewer.Document).Count());
		Assert.AreEqual(0, FindAll<InlineUIContainer>(viewer.Document).Count());
		Assert.AreEqual("alt text", GetParagraphText(FindAll<Paragraph>(viewer.Document).Single()));
	}

	[TestMethod]
	public void CreateContent_Heading_ScalesFontSizeAndIsBold()
	{
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("# Heading");
		var viewer = (FlowDocumentScrollViewer)element;

		Paragraph paragraph = FindAll<Paragraph>(viewer.Document).Single();

		Assert.IsTrue(paragraph.FontSize > MarkdownToolTipTheme.Default.BodyFontSize);
		Assert.AreEqual(FontWeights.Bold, paragraph.FontWeight);
	}

	[TestMethod]
	public void CreateContent_EmptyHeadingScales_StillRendersHeading()
	{
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("# Heading", new MarkdownToolTipTheme { HeadingFontSizeScales = [] });
		var viewer = (FlowDocumentScrollViewer)element;

		Paragraph paragraph = FindAll<Paragraph>(viewer.Document).Single();

		Assert.AreEqual(MarkdownToolTipTheme.Default.BodyFontSize, paragraph.FontSize);
		Assert.AreEqual(FontWeights.Bold, paragraph.FontWeight);
	}

	[TestMethod]
	public void Theme_InvalidValues_ThrowArgumentOutOfRange()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = MarkdownToolTipTheme.Default with { BodyFontSize = 0.0 });
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = MarkdownToolTipTheme.Default with { BodyFontSize = double.PositiveInfinity });
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = MarkdownToolTipTheme.Default with { CodeFontSize = -1.0 });
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = MarkdownToolTipTheme.Default with { MaxWidth = 0.0 });
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = MarkdownToolTipTheme.Default with { MaxWidth = double.NaN });
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = MarkdownToolTipTheme.Default with { MaxHeight = 0.0 });
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = MarkdownToolTipTheme.Default with { CodeMaxWidth = 0.0 });
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = MarkdownToolTipTheme.Default with { MaxVisibleCodeBlockLines = 0 });
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = MarkdownToolTipTheme.Default with { BlockSpacing = -1.0 });
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = MarkdownToolTipTheme.Default with { BlockSpacing = double.PositiveInfinity });
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = MarkdownToolTipTheme.Default with { HeadingFontSizeScales = [1.0, 0.0] });
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = MarkdownToolTipTheme.Default with { HeadingFontSizeScales = [1.0, double.PositiveInfinity] });
	}

	[TestMethod]
	public void Theme_NullHeadingScales_ThrowArgumentNull()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = MarkdownToolTipTheme.Default with { HeadingFontSizeScales = null! });
	}

	[TestMethod]
	public void CreateContent_List_ProducesWpfList()
	{
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("- one\n- two");
		var viewer = (FlowDocumentScrollViewer)element;

		List list = FindAll<List>(viewer.Document).Single();

		Assert.AreEqual(2, list.ListItems.Count);
		Assert.AreEqual(TextMarkerStyle.Disc, list.MarkerStyle);
	}

	[TestMethod]
	public void CreateContent_OrderedList_ProducesDecimalMarker()
	{
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("3. three\n4. four");
		var viewer = (FlowDocumentScrollViewer)element;

		List list = FindAll<List>(viewer.Document).Single();

		Assert.AreEqual(TextMarkerStyle.Decimal, list.MarkerStyle);
		Assert.AreEqual(3, list.StartIndex);
	}

	[TestMethod]
	public void CreateContent_Blockquote_ProducesSection()
	{
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("> quoted");
		var viewer = (FlowDocumentScrollViewer)element;

		Section section = FindAll<Section>(viewer.Document).Single();

		Assert.AreEqual(1, section.Blocks.Count);
	}

	[TestMethod]
	public void CreateContent_ThematicBreak_ProducesSeparatorBlock()
	{
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("before\n\n---\n\nafter");
		var viewer = (FlowDocumentScrollViewer)element;

		BlockUIContainer container = FindAll<BlockUIContainer>(viewer.Document).Single();
		var border = (Border)container.Child;

		Assert.AreEqual(1.0, border.Height);
	}

	[TestMethod]
	public void CreateContent_Emphasis_ProducesBoldAndItalic()
	{
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("**bold** and *italic*");
		var viewer = (FlowDocumentScrollViewer)element;

		Assert.AreEqual(1, FindAll<Bold>(viewer.Document).Count());
		Assert.AreEqual(1, FindAll<Italic>(viewer.Document).Count());
	}

	[TestMethod]
	public void CreateContent_Strikethrough_ProducesStrikethroughDecoration()
	{
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("~~gone~~");
		var viewer = (FlowDocumentScrollViewer)element;

		Span span = FindAll<Span>(viewer.Document).Single();

		Assert.IsTrue(span.TextDecorations.Count > 0);
		Assert.AreEqual(TextDecorationLocation.Strikethrough, span.TextDecorations[0].Location);
	}

	[TestMethod]
	public void CreateContent_UnsupportedEmphasisVariants_RenderAsLiteralText()
	{
		// The narrowed pipeline enables only CommonMark emphasis and strikethrough; the subscript,
		// superscript, marked, and inserted delimiters must stay literal instead of rendering
		// as a different style.
		string[] unsupportedVariants = ["~sub~", "^sup^", "==mark==", "++ins++"];

		foreach (string variant in unsupportedVariants)
		{
			FrameworkElement element = MarkdownToolTipRenderer.CreateContent(variant);
			var viewer = (FlowDocumentScrollViewer)element;
			Paragraph paragraph = FindAll<Paragraph>(viewer.Document).Single();

			Assert.AreEqual(variant, GetParagraphText(paragraph));
			Assert.AreEqual(0, FindAll<Bold>(viewer.Document).Count());
			Assert.AreEqual(0, FindAll<Italic>(viewer.Document).Count());
			Assert.AreEqual(0, FindAll<Span>(viewer.Document).Count());
		}
	}

	[TestMethod]
	public void CreateContent_SoftLineBreak_RendersAsSpace()
	{
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("alpha\nbeta");
		var viewer = (FlowDocumentScrollViewer)element;
		Paragraph paragraph = FindAll<Paragraph>(viewer.Document).Single();

		Assert.AreEqual("alpha beta", GetParagraphText(paragraph));
		Assert.AreEqual(0, FindAll<LineBreak>(viewer.Document).Count());
	}

	[TestMethod]
	public void CreateContent_HardLineBreak_ProducesLineBreak()
	{
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("alpha  \nbeta");
		var viewer = (FlowDocumentScrollViewer)element;
		Paragraph paragraph = FindAll<Paragraph>(viewer.Document).Single();

		Assert.AreEqual(1, FindAll<LineBreak>(viewer.Document).Count());

		string text = GetParagraphText(paragraph);

		StringAssert.StartsWith(text, "alpha");
		StringAssert.EndsWith(text, "beta");
	}

	[TestMethod]
	public void CreateContent_Table_ProducesWpfTable()
	{
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("| a | b |\n|---|---|\n| 1 | 2 |");
		var viewer = (FlowDocumentScrollViewer)element;

		Table table = FindAll<Table>(viewer.Document).Single();

		Assert.AreEqual(2, table.RowGroups[0].Rows.Count);
		Assert.AreEqual(2, table.RowGroups[0].Rows[0].Cells.Count);
	}

	[TestMethod]
	public void CreateContent_TableColumnAlignment_IsAppliedToCellText()
	{
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("| left | center | right |\n|:--|:-:|--:|\n| 1 | 2 | 3 |");
		var viewer = (FlowDocumentScrollViewer)element;

		Table table = FindAll<Table>(viewer.Document).Single();
		TableRow bodyRow = table.RowGroups[0].Rows[1];

		Assert.AreEqual(TextAlignment.Left, GetFirstCellParagraph(bodyRow.Cells[0]).TextAlignment);
		Assert.AreEqual(TextAlignment.Center, GetFirstCellParagraph(bodyRow.Cells[1]).TextAlignment);
		Assert.AreEqual(TextAlignment.Right, GetFirstCellParagraph(bodyRow.Cells[2]).TextAlignment);
	}

	[TestMethod]
	public void CreateContent_HeadingBeyondScaleList_ReusesLastScale()
	{
		var theme = MarkdownToolTipTheme.Default with { HeadingFontSizeScales = [2.0] };

		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("### Heading", theme);
		var viewer = (FlowDocumentScrollViewer)element;
		Paragraph paragraph = FindAll<Paragraph>(viewer.Document).Single();

		Assert.AreEqual(MarkdownToolTipTheme.Default.BodyFontSize * 2.0, paragraph.FontSize);
	}

	[TestMethod]
	public void CreatePlainTextContent_RendersTextInScrollViewer()
	{
		FrameworkElement element = MarkdownToolTipRenderer.CreatePlainTextContent("plain text");
		var scrollViewer = (ScrollViewer)element;
		var textBlock = (TextBlock)scrollViewer.Content;

		Assert.AreEqual("plain text", textBlock.Text);
	}

	[TestMethod]
	public void CreatePlainTextContent_Fallback_IsNotFocusable()
	{
		FrameworkElement element = MarkdownToolTipRenderer.CreatePlainTextContent("plain text");
		var scrollViewer = (ScrollViewer)element;

		Assert.IsFalse(scrollViewer.Focusable);
		Assert.IsFalse(scrollViewer.IsTabStop);
	}

	[TestMethod]
	public void CreateContent_DefaultThemeBorders_ContrastWithTheSurface()
	{
		// White surface: the border blends toward black (18% of the way) instead of staying white.
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("> quoted");
		var viewer = (FlowDocumentScrollViewer)element;
		Section section = FindAll<Section>(viewer.Document).Single();

		Assert.AreEqual(Color.FromRgb(0xD1, 0xD1, 0xD1), ((SolidColorBrush)section.BorderBrush).Color);

		// Dark surface: the border blends toward white and stays lighter than the surface.
		var darkTheme = MarkdownToolTipTheme.Default with { SurfaceBackground = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)) };
		var darkElement = (FlowDocumentScrollViewer)MarkdownToolTipRenderer.CreateContent("> quoted", darkTheme);
		Section darkSection = FindAll<Section>(darkElement.Document).Single();
		Color darkBorderColor = ((SolidColorBrush)darkSection.BorderBrush).Color;

		Assert.IsTrue(darkBorderColor.R > 0x1E && darkBorderColor.G > 0x1E && darkBorderColor.B > 0x1E);
	}

	[TestMethod]
	public void CreateContent_NonSolidSurface_UsesWhiteBaseColor()
	{
		// Documented fallback: a non-solid surface brush is treated as a white surface.
		var theme = MarkdownToolTipTheme.Default with { SurfaceBackground = new LinearGradientBrush(Colors.Black, Colors.Black, 0.0) };
		var viewer = (FlowDocumentScrollViewer)MarkdownToolTipRenderer.CreateContent("> quoted", theme);
		Section section = FindAll<Section>(viewer.Document).Single();

		Assert.AreEqual(Color.FromRgb(0xD1, 0xD1, 0xD1), ((SolidColorBrush)section.BorderBrush).Color);
	}

	[TestMethod]
	public void CreateContent_DarkSurface_CodeBackgroundContrastsWithSurface()
	{
		var darkSurface = Color.FromRgb(0x1E, 0x1E, 0x1E);
		var darkTheme = MarkdownToolTipTheme.Default with { SurfaceBackground = new SolidColorBrush(darkSurface) };
		var viewer = (FlowDocumentScrollViewer)MarkdownToolTipRenderer.CreateContent("Use `x` here.\n\n```\ncode\n```", darkTheme);

		Color inlineBackground = ((SolidColorBrush)((Border)FindAll<InlineUIContainer>(viewer.Document).Single().Child).Background).Color;
		Color codeBackground = ((SolidColorBrush)((Border)FindAll<BlockUIContainer>(viewer.Document).Single().Child).Background).Color;

		// Both code surfaces blend toward the contrasting pole, so they are lighter than the dark surface.
		Assert.IsTrue(inlineBackground.R > darkSurface.R && inlineBackground.G > darkSurface.G && inlineBackground.B > darkSurface.B);
		Assert.AreEqual(inlineBackground, codeBackground);
	}

	[TestMethod]
	public void NormalizeLineEndings_ConvertsCrLfAndLoneCrToLf()
	{
		Assert.AreEqual("a\nb\nc", MarkdownRenderingAssets.NormalizeLineEndings("a\r\nb\rc"));
	}

	[TestMethod]
	public void CreateCodeBlockEditor_DoesNotAcceptKeyboardFocus()
	{
		AvalonTextEditor editor = MarkdownToolTipRenderer.CreateCodeBlockEditor("lua", "local value = 1", MarkdownToolTipTheme.Default);

		Assert.IsFalse(editor.Focusable);
		Assert.IsFalse(editor.IsTabStop);
		Assert.IsFalse(editor.TextArea.Focusable);
		Assert.IsFalse(editor.TextArea.IsTabStop);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_CustomHighlightingHook_IsInvoked()
	{
		bool invoked = false;

		var options = new MarkdownToolTipOptions
		{
			CustomHighlightingInstaller = (editor, language) =>
			{
				invoked = true;
				Assert.AreEqual("lua", language);
				return false;
			}
		};

		MarkdownToolTipRenderer.CreateCodeBlockEditor("lua", "x = 1", MarkdownToolTipTheme.Default, options);

		Assert.IsTrue(invoked);
	}

	[TestMethod]
	public void CreateContent_CustomHighlightingHook_IsInvokedForFencedCode()
	{
		bool invoked = false;

		var options = new MarkdownToolTipOptions
		{
			CustomHighlightingInstaller = (editor, language) =>
			{
				invoked = true;
				return false;
			}
		};

		MarkdownToolTipRenderer.CreateContent("```lua\nx = 1\n```", MarkdownToolTipTheme.Default, options);

		Assert.IsTrue(invoked);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_DefaultAlias_ResolvesJson5ToJsonHighlighting()
	{
		AvalonTextEditor editor = MarkdownToolTipRenderer.CreateCodeBlockEditor("json5", "x = 1", MarkdownToolTipTheme.Default);

		Assert.IsNotNull(editor.SyntaxHighlighting);
		Assert.AreSame(HighlightingManager.Instance.GetDefinitionByExtension(".json"), editor.SyntaxHighlighting);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_CustomAlias_ResolvesConfiguredHighlighting()
	{
		var options = new MarkdownToolTipOptions
		{
			HighlightingAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				["mylang"] = ".cs"
			}
		};

		AvalonTextEditor editor = MarkdownToolTipRenderer.CreateCodeBlockEditor("mylang", "x = 1", MarkdownToolTipTheme.Default, options);

		Assert.IsNotNull(editor.SyntaxHighlighting);
		Assert.AreSame(HighlightingManager.Instance.GetDefinitionByExtension(".cs"), editor.SyntaxHighlighting);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_AliasMapWithOrdinalComparer_ResolvesCaseInsensitively()
	{
		var options = new MarkdownToolTipOptions
		{
			HighlightingAliases = new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["MyLang"] = ".cs"
			}
		};

		AvalonTextEditor editor = MarkdownToolTipRenderer.CreateCodeBlockEditor("mylang", "x = 1", MarkdownToolTipTheme.Default, options);

		// The options copy normalizes the map to case-insensitive keys, so a fence whose casing differs
		// from the assigned key still resolves.
		Assert.IsNotNull(editor.SyntaxHighlighting);
		Assert.AreSame(HighlightingManager.Instance.GetDefinitionByExtension(".cs"), editor.SyntaxHighlighting);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_DefaultAliases_ContainOnlyEffectiveMappings()
	{
		// cs, js, and ts are intentionally not in the default map: extension resolution already covers
		// cs/js, and a ts alias could only repeat the failing .ts extension lookup.
		IReadOnlyDictionary<string, string> aliases = MarkdownToolTipOptions.Default.HighlightingAliases;

		Assert.AreEqual(2, aliases.Count);
		Assert.AreEqual(".cs", aliases["csharp"]);
		Assert.AreEqual(".json", aliases["json5"]);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_TypeScriptFence_ResolvesNoHighlighting()
	{
		// AvalonEdit ships no TypeScript definition, and the default aliases add no lookup that could
		// succeed; hosts must register one through the custom highlighting hook.
		AvalonTextEditor editor = MarkdownToolTipRenderer.CreateCodeBlockEditor("ts", "let x = 1", MarkdownToolTipTheme.Default);

		Assert.IsNull(editor.SyntaxHighlighting);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_CommonLanguageNames_ResolveHighlighting()
	{
		// "PYTHON" resolves through the case-insensitive definition-name scan; "javascript", "cs",
		// "csharp", "js", "json", and "json5" resolve through definition names, extensions, or aliases.
		(string Language, string Extension)[] cases =
		[
			("python", ".py"),
			("PYTHON", ".py"),
			("javascript", ".js"),
			("JAVASCRIPT", ".js"),
			("cs", ".cs"),
			("csharp", ".cs"),
			("js", ".js"),
			("json", ".json"),
			("json5", ".json")
		];

		foreach ((string language, string extension) in cases)
		{
			AvalonTextEditor editor = MarkdownToolTipRenderer.CreateCodeBlockEditor(language, "x = 1", MarkdownToolTipTheme.Default);

			Assert.IsNotNull(editor.SyntaxHighlighting, $"The language '{language}' did not resolve.");
			Assert.AreSame(
				HighlightingManager.Instance.GetDefinitionByExtension(extension),
				editor.SyntaxHighlighting,
				$"The language '{language}' resolved to an unexpected definition.");
		}
	}

	[TestMethod]
	public void CreateCodeBlockEditor_HostRegisteredDefinition_ResolvesByName()
	{
		IHighlightingDefinition csharpDefinition = HighlightingManager.Instance.GetDefinition("C#")!;

		// Simulate a host definition for a language AvalonEdit does not ship (for example Lua).
		HighlightingManager.Instance.RegisterHighlighting("ProbeLua", [".probelua"], csharpDefinition);

		AvalonTextEditor editor = MarkdownToolTipRenderer.CreateCodeBlockEditor("ProbeLua", "x = 1", MarkdownToolTipTheme.Default);

		Assert.AreSame(csharpDefinition, editor.SyntaxHighlighting);

		// The registered extension also resolves with different casing.
		AvalonTextEditor extensionEditor = MarkdownToolTipRenderer.CreateCodeBlockEditor("PROBELUA", "x = 1", MarkdownToolTipTheme.Default);

		Assert.AreSame(csharpDefinition, extensionEditor.SyntaxHighlighting);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_UnresolvedLanguage_ReturnsNullHighlighting()
	{
		AvalonTextEditor editor = MarkdownToolTipRenderer.CreateCodeBlockEditor("definitely-not-a-language", "x = 1", MarkdownToolTipTheme.Default);

		Assert.IsNull(editor.SyntaxHighlighting);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_UnresolvedLanguage_LogsDebugThroughConfiguredLogger()
	{
		var logger = new CapturingLogger();
		var options = new MarkdownToolTipOptions { Logger = logger };

		MarkdownToolTipRenderer.CreateCodeBlockEditor("definitely-not-a-language", "x = 1", MarkdownToolTipTheme.Default, options);

		Assert.IsTrue(logger.Messages.Any(message => message.Contains("definitely-not-a-language", StringComparison.Ordinal)));
	}

	[TestMethod]
	public void CreateCodeBlockEditor_CustomHighlightingHookHandled_SkipsBuiltInResolution()
	{
		IHighlightingDefinition jsonDefinition = HighlightingManager.Instance.GetDefinition("Json")!;

		var options = new MarkdownToolTipOptions
		{
			CustomHighlightingInstaller = (editor, _) =>
			{
				editor.SyntaxHighlighting = jsonDefinition;
				return true;
			}
		};

		AvalonTextEditor editor = MarkdownToolTipRenderer.CreateCodeBlockEditor("csharp", "x = 1", MarkdownToolTipTheme.Default, options);

		Assert.AreSame(jsonDefinition, editor.SyntaxHighlighting);
	}

	[TestMethod]
	public void CreateContent_HighlightingHookThrows_ReturnsPlainTextAndLogsWarning()
	{
		var logger = new CapturingLogger();
		var options = new MarkdownToolTipOptions
		{
			CustomHighlightingInstaller = (_, _) => throw new InvalidOperationException("highlighting failure"),
			Logger = logger
		};

		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("```lua\nx = 1\n```", MarkdownToolTipTheme.Default, options);

		Assert.IsInstanceOfType(element, typeof(ScrollViewer));
		Assert.IsTrue(logger.Messages.Any(message => message.Contains("Markdown tooltip rendering failed", StringComparison.Ordinal)));
	}

	[TestMethod]
	public void CreateContent_HyperlinkOpenerDeclines_FallsBackToDefaultOpener()
	{
		Uri? defaultOpenedUri = null;
		Func<Uri, bool> originalOpener = MarkdownFlowDocumentBuilder.DefaultHyperlinkOpener;

		try
		{
			MarkdownFlowDocumentBuilder.DefaultHyperlinkOpener = uri =>
			{
				defaultOpenedUri = uri;
				return true;
			};

			var options = new MarkdownToolTipOptions { OpenHyperlink = _ => false };
			FrameworkElement element = MarkdownToolTipRenderer.CreateContent("[docs](https://example.com)", MarkdownToolTipTheme.Default, options);
			var viewer = (FlowDocumentScrollViewer)element;
			Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

			var clickEvent = new RoutedEventArgs(Hyperlink.ClickEvent);
			hyperlink.RaiseEvent(clickEvent);

			Assert.AreEqual("https://example.com/", defaultOpenedUri?.AbsoluteUri);
			Assert.IsTrue(clickEvent.Handled);
		}
		finally
		{
			MarkdownFlowDocumentBuilder.DefaultHyperlinkOpener = originalOpener;
		}
	}

	[TestMethod]
	public void CreateContent_HyperlinkOpenerThrows_FallsBackToDefaultOpenerAndLogsWarning()
	{
		var logger = new CapturingLogger();
		bool defaultOpenerCalled = false;
		Func<Uri, bool> originalOpener = MarkdownFlowDocumentBuilder.DefaultHyperlinkOpener;

		try
		{
			MarkdownFlowDocumentBuilder.DefaultHyperlinkOpener = _ =>
			{
				defaultOpenerCalled = true;
				return true;
			};

			var options = new MarkdownToolTipOptions
			{
				OpenHyperlink = _ => throw new InvalidOperationException("opener failure"),
				Logger = logger
			};

			FrameworkElement element = MarkdownToolTipRenderer.CreateContent("[docs](https://example.com)", MarkdownToolTipTheme.Default, options);
			var viewer = (FlowDocumentScrollViewer)element;
			Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

			hyperlink.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));

			Assert.IsTrue(defaultOpenerCalled);
			Assert.IsTrue(logger.Messages.Any(message => message.Contains("https://example.com", StringComparison.Ordinal)));
		}
		finally
		{
			MarkdownFlowDocumentBuilder.DefaultHyperlinkOpener = originalOpener;
		}
	}

	[TestMethod]
	public void CreateContent_EmailAutolink_UsesMailtoTargetWithBareAddressText()
	{
		var options = new MarkdownToolTipOptions
		{
			SupportedHyperlinkSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "mailto" }
		};

		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("Write to <user@example.com> today.", MarkdownToolTipTheme.Default, options);
		var viewer = (FlowDocumentScrollViewer)element;
		Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

		Assert.AreEqual("mailto:user@example.com", hyperlink.NavigateUri?.AbsoluteUri);
		Assert.AreEqual("user@example.com", GetHyperlinkText(hyperlink));
		Assert.AreEqual(Cursors.Hand, hyperlink.Cursor);
	}

	[TestMethod]
	public void CreateContent_EmailAutolinkWithoutMailtoScheme_IsNotNavigable()
	{
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("Write to <user@example.com> today.");
		var viewer = (FlowDocumentScrollViewer)element;
		Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

		Assert.IsNull(hyperlink.NavigateUri);
		Assert.AreEqual(Cursors.Arrow, hyperlink.Cursor);
	}

	[TestMethod]
	public void CreateContent_TripleEmphasis_ProducesBoldAndItalic()
	{
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("***strong emphasis***");
		var viewer = (FlowDocumentScrollViewer)element;

		Bold bold = FindAll<Bold>(viewer.Document).Single();
		Italic italic = FindAll<Italic>(viewer.Document).Single();

		Assert.IsTrue(
			bold.Inlines.OfType<Italic>().Any() || italic.Inlines.OfType<Bold>().Any(),
			"The strong and italic emphasis must be nested.");
	}

	[TestMethod]
	public void CreateContent_CrLfInput_RendersParagraphsAndCodeBlock()
	{
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("first\r\n\r\n```lua\r\nlocal value = 1\r\n```", MarkdownToolTipTheme.Default);
		var viewer = (FlowDocumentScrollViewer)element;

		Assert.AreEqual(2, viewer.Document.Blocks.Count);

		var border = (Border)FindAll<BlockUIContainer>(viewer.Document).Single().Child;
		var editor = (AvalonTextEditor)border.Child;

		Assert.AreEqual("local value = 1", editor.Text);
	}

	[TestMethod]
	public void CreateContent_IndentedCodeBlock_ProducesCodeBlockEditor()
	{
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("    var x = 1;");
		var viewer = (FlowDocumentScrollViewer)element;
		var border = (Border)FindAll<BlockUIContainer>(viewer.Document).Single().Child;
		var editor = (AvalonTextEditor)border.Child;

		Assert.AreEqual("var x = 1;", editor.Text);
	}

	[TestMethod]
	public void CreateContent_FenceInfoWithAttributes_ResolvesLanguageToken()
	{
		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("```csharp title=\"sample\"\nvar x = 1;\n```");
		var viewer = (FlowDocumentScrollViewer)element;
		var border = (Border)FindAll<BlockUIContainer>(viewer.Document).Single().Child;
		var editor = (AvalonTextEditor)border.Child;

		Assert.IsNotNull(editor.SyntaxHighlighting, "The first info-string token must resolve the language.");
	}

	[TestMethod]
	public void CreateContent_TableCellParagraph_DoesNotCarryBlockSpacing()
	{
		FlowDocument document = MarkdownToolTipRenderer.CreateFlowDocument("| a | b |\n| - | - |\n| c | d |");
		Table table = document.Blocks.OfType<Table>().Single();

		foreach (TableRow row in table.RowGroups[0].Rows)
		{
			foreach (TableCell cell in row.Cells)
				Assert.AreEqual(0.0, cell.Blocks.FirstBlock!.Margin.Bottom);
		}
	}

	[TestMethod]
	public void CreateFlowDocument_KeepsDefaultColumnWidth()
	{
		FlowDocument document = MarkdownToolTipRenderer.CreateFlowDocument("Some paragraph text.");

		Assert.IsTrue(double.IsNaN(document.ColumnWidth), "The renderer must not pin a minimum column width.");
	}

	[TestMethod]
	public void CreateCodeBlockEditor_NormalizesLineEndingsAndTrimsTrailingBlankLines()
	{
		AvalonTextEditor editor = MarkdownToolTipRenderer.CreateCodeBlockEditor("lua", "a\r\nb\n\n\n");

		Assert.AreEqual("a\nb", editor.Text);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_TextWithoutTrailingBlankLines_IsUnchanged()
	{
		AvalonTextEditor editor = MarkdownToolTipRenderer.CreateCodeBlockEditor("lua", "a\nb");

		Assert.AreEqual("a\nb", editor.Text);
	}

	[TestMethod]
	public void CreateContent_HyperlinkActivation_InvokesConfiguredOpener()
	{
		Uri? openedUri = null;

		var options = new MarkdownToolTipOptions
		{
			OpenHyperlink = uri =>
			{
				openedUri = uri;
				return true;
			}
		};

		FrameworkElement element = MarkdownToolTipRenderer.CreateContent("[docs](https://example.com)", MarkdownToolTipTheme.Default, options);
		var viewer = (FlowDocumentScrollViewer)element;
		Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

		var clickEvent = new RoutedEventArgs(Hyperlink.ClickEvent);
		hyperlink.RaiseEvent(clickEvent);

		Assert.AreEqual("https://example.com/", openedUri?.AbsoluteUri);
		Assert.IsTrue(clickEvent.Handled);
	}

	[TestMethod]
	public void CreateContent_WheelOverScrollableCodeBlock_ScrollsCodeBlockBeforeViewer()
	{
		(Window window, FlowDocumentScrollViewer viewer, AvalonTextEditor editor, ScrollViewer outerScrollViewer) = CreateScrollableTooltipWithCodeBlock();

		try
		{
			// The code block's scroll surface is the text area's IScrollInfo; the editor's own scroll
			// members delegate to the templated scroll viewer.
			var editorScrollInfo = (IScrollInfo)editor.TextArea;
			double editorScrollableHeight = editorScrollInfo.ExtentHeight - editorScrollInfo.ViewportHeight;

			Assert.IsTrue(editorScrollableHeight > 0.0, "The code block must be scrollable for this test.");
			Assert.IsTrue(outerScrollViewer.ScrollableHeight > 0.0, "The viewer must be scrollable for this test.");

			outerScrollViewer.ScrollToTop();
			editorScrollInfo.SetVerticalOffset(0.0);
			WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

			RaiseMouseWheel(editor, -Mouse.MouseWheelDeltaForOneLine);
			WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

			Assert.IsTrue(editorScrollInfo.VerticalOffset > 0.0, "The code block should scroll before the viewer.");
			Assert.AreEqual(0.0, outerScrollViewer.VerticalOffset, "The viewer must not steal the wheel event from a scrollable code block.");

			// At the code block's scroll end the viewer takes the wheel over.
			editorScrollInfo.SetVerticalOffset(editorScrollableHeight);
			WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

			RaiseMouseWheel(editor, -Mouse.MouseWheelDeltaForOneLine);
			WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

			Assert.IsTrue(outerScrollViewer.VerticalOffset > 0.0, "The viewer should scroll once the code block reaches its end.");
		}
		finally
		{
			window.Close();
		}
	}

	[TestMethod]
	public void CreateContent_WheelOverViewerBody_ScrollsViewer()
	{
		(Window window, FlowDocumentScrollViewer viewer, _, ScrollViewer outerScrollViewer) = CreateScrollableTooltipWithCodeBlock();

		try
		{
			outerScrollViewer.ScrollToTop();
			WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

			RaiseMouseWheel(viewer, -Mouse.MouseWheelDeltaForOneLine);
			WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

			Assert.IsTrue(outerScrollViewer.VerticalOffset > 0.0);
		}
		finally
		{
			window.Close();
		}
	}

	[TestMethod]
	public void CreateContent_ScrollingDisabled_WheelDoesNotScrollViewer()
	{
		string body = string.Join("\n\n", Enumerable.Repeat("Some tooltip paragraph text that wraps over several lines.", 8));
		var theme = MarkdownToolTipTheme.Default with { MaxHeight = 150.0 };
		var options = new MarkdownToolTipOptions { AllowScrolling = false };
		var viewer = (FlowDocumentScrollViewer)MarkdownToolTipRenderer.CreateContent(body, theme, options);
		Window window = WPFTestHost.ShowInHostWindow(viewer);

		try
		{
			viewer.UpdateLayout();
			WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

			ScrollViewer innerScrollViewer = FindVisualDescendants<ScrollViewer>(viewer).First();
			double initialOffset = innerScrollViewer.VerticalOffset;

			RaiseMouseWheel(innerScrollViewer, -Mouse.MouseWheelDeltaForOneLine);
			WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

			Assert.AreEqual(initialOffset, innerScrollViewer.VerticalOffset);
		}
		finally
		{
			window.Close();
		}
	}

	[TestMethod]
	public void CreateFlowDocument_RendersBlocks()
	{
		FlowDocument document = MarkdownToolTipRenderer.CreateFlowDocument("# Heading");

		Assert.AreEqual(1, document.Blocks.Count);
		Assert.IsInstanceOfType(document.Blocks.FirstBlock, typeof(Paragraph));
	}

	[TestMethod]
	public void CreateFlowDocument_WhitespaceOnlyContent_ReturnsEmptyDocument()
	{
		FlowDocument document = MarkdownToolTipRenderer.CreateFlowDocument("   \n  ");

		Assert.AreEqual(0, document.Blocks.Count);
	}

	[TestMethod]
	public void CreateFlowDocument_HighlightingHookThrows_ReturnsPlainTextDocumentAndLogsWarning()
	{
		var logger = new CapturingLogger();
		var options = new MarkdownToolTipOptions
		{
			CustomHighlightingInstaller = (_, _) => throw new InvalidOperationException("highlighting failure"),
			Logger = logger
		};

		FlowDocument document = MarkdownToolTipRenderer.CreateFlowDocument("```lua\nx = 1\n```", MarkdownToolTipTheme.Default, options);

		Paragraph paragraph = document.Blocks.OfType<Paragraph>().Single();
		StringAssert.Contains(GetParagraphText(paragraph), "x = 1");
		Assert.IsTrue(logger.Messages.Any(message => message.Contains("Markdown tooltip rendering failed", StringComparison.Ordinal)));
	}

	private static IEnumerable<T> FindAll<T>(FlowDocument document) where T : DependencyObject
	{
		foreach (Block block in document.Blocks)
		{
			foreach (DependencyObject element in EnumerateBlock(block))
			{
				if (element is T match)
					yield return match;
			}
		}
	}

	private static string GetParagraphText(Paragraph paragraph)
	{
		var builder = new StringBuilder();

		foreach (Inline inline in paragraph.Inlines)
			AppendInlineText(builder, inline);

		return builder.ToString();
	}

	private static void AppendInlineText(StringBuilder builder, Inline inline)
	{
		switch (inline)
		{
			case Run run:
				builder.Append(run.Text);
				break;

			case Span span:
				foreach (Inline child in span.Inlines)
					AppendInlineText(builder, child);

				break;
		}
	}

	private static IEnumerable<DependencyObject> EnumerateBlock(Block block)
	{
		yield return block;

		switch (block)
		{
			case Paragraph paragraph:
				foreach (Inline inline in paragraph.Inlines)
				{
					foreach (DependencyObject element in EnumerateInline(inline))
					{
						yield return element;
					}
				}
				break;

			case Section section:
				foreach (Block child in section.Blocks)
				{
					foreach (DependencyObject element in EnumerateBlock(child))
					{
						yield return element;
					}
				}
				break;

			case List list:
				foreach (ListItem item in list.ListItems)
				{
					yield return item;

					foreach (Block child in item.Blocks)
					{
						foreach (DependencyObject element in EnumerateBlock(child))
						{
							yield return element;
						}
					}
				}
				break;

			case Table table:
				foreach (TableRowGroup rowGroup in table.RowGroups)
				{
					yield return rowGroup;

					foreach (TableRow row in rowGroup.Rows)
					{
						yield return row;

						foreach (TableCell cell in row.Cells)
						{
							yield return cell;

							foreach (Block child in cell.Blocks)
							{
								foreach (DependencyObject element in EnumerateBlock(child))
								{
									yield return element;
								}
							}
						}
					}
				}
				break;
		}
	}

	private static IEnumerable<DependencyObject> EnumerateInline(Inline inline)
	{
		yield return inline;

		if (inline is Span span)
		{
			foreach (Inline child in span.Inlines)
			{
				foreach (DependencyObject element in EnumerateInline(child))
				{
					yield return element;
				}
			}
		}
	}

	private static Paragraph GetFirstCellParagraph(TableCell cell)
		=> (Paragraph)cell.Blocks.FirstBlock!;

	private static string GetHyperlinkText(Hyperlink hyperlink)
	{
		var builder = new StringBuilder();

		foreach (Inline inline in hyperlink.Inlines)
			AppendInlineText(builder, inline);

		return builder.ToString();
	}

	private static (Window Window, FlowDocumentScrollViewer Viewer, AvalonTextEditor Editor, ScrollViewer OuterScrollViewer) CreateScrollableTooltipWithCodeBlock()
	{
		string body = string.Join("\n\n", Enumerable.Repeat("Some tooltip paragraph text that wraps over several lines.", 8));
		string code = string.Join("\n", Enumerable.Range(1, 40).Select(index => $"local value{index} = {index}"));
		var theme = MarkdownToolTipTheme.Default with { MaxHeight = 150.0, MaxVisibleCodeBlockLines = 5 };

		var viewer = (FlowDocumentScrollViewer)MarkdownToolTipRenderer.CreateContent($"{body}\n\n```lua\n{code}\n```", theme);
		Window window = WPFTestHost.ShowInHostWindow(viewer);

		viewer.UpdateLayout();
		WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

		AvalonTextEditor editor = FindVisualDescendants<AvalonTextEditor>(viewer).First();
		ScrollViewer outerScrollViewer = FindVisualDescendants<ScrollViewer>(viewer).First(scrollViewer => scrollViewer.IsAncestorOf(editor));

		return (window, viewer, editor, outerScrollViewer);
	}

	private static void RaiseMouseWheel(UIElement source, int delta)
	{
		// Mirrors the WPF input system: the preview event is raised first, and the bubbling event is
		// raised only when no handler consumed the preview event.
		var previewEvent = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
		{
			RoutedEvent = UIElement.PreviewMouseWheelEvent
		};

		source.RaiseEvent(previewEvent);

		if (previewEvent.Handled)
			return;

		var bubbleEvent = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
		{
			RoutedEvent = UIElement.MouseWheelEvent
		};

		source.RaiseEvent(bubbleEvent);
	}

	private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root) where T : DependencyObject
	{
		int count = VisualTreeHelper.GetChildrenCount(root);

		for (int i = 0; i < count; i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(root, i);

			if (child is T match)
				yield return match;

			foreach (T descendant in FindVisualDescendants<T>(child))
				yield return descendant;
		}
	}
}

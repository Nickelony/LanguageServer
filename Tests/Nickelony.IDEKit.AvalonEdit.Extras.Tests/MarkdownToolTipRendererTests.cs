using Nickelony.IDEKit.AvalonEdit.Extras.Markdown;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using AvalonTextEditor = ICSharpCode.AvalonEdit.TextEditor;
using List = System.Windows.Documents.List;

namespace Nickelony.IDEKit.AvalonEdit.Extras.Tests;

[TestClass]
public class MarkdownToolTipRendererTests
{
	[TestMethod]
	public void CreateContent_WhitespaceOnlyContent_ReturnsFallbackScrollViewer()
		=> STATestHelper.RunInSTA(() =>
		{
			FrameworkElement element = MarkdownToolTipRenderer.CreateContent("   \n  ");

			Assert.IsInstanceOfType(element, typeof(ScrollViewer));
		});

	[TestMethod]
	public void CreateContent_FencedCodeBlock_ProducesTextEditorCodeBlock()
		=> STATestHelper.RunInSTA(() =>
		{
			FrameworkElement element = MarkdownToolTipRenderer.CreateContent("before\n\n```lua\nlocal value = 1\nprint(value)\n```\n\nafter");
			var viewer = (FlowDocumentScrollViewer)element;

			var container = FindAll<BlockUIContainer>(viewer.Document).Single();
			var border = (Border)container.Child;
			var editor = (AvalonTextEditor)border.Child;

			Assert.AreEqual(string.Join(Environment.NewLine, "local value = 1", "print(value)"), editor.Text);
			Assert.IsFalse(editor.Focusable);
			Assert.IsFalse(editor.IsTabStop);
		});

	[TestMethod]
	public void CreateContent_InlineCode_ProducesStyledInlineContainer()
		=> STATestHelper.RunInSTA(() =>
		{
			FrameworkElement element = MarkdownToolTipRenderer.CreateContent("Use `TextEditor` here.");
			var viewer = (FlowDocumentScrollViewer)element;

			var container = FindAll<InlineUIContainer>(viewer.Document).Single();
			var border = (Border)container.Child;
			var textBlock = (TextBlock)border.Child;

			Assert.AreEqual("TextEditor", textBlock.Text);
			Assert.AreEqual("Consolas", textBlock.FontFamily.Source);
		});

	[TestMethod]
	public void CreateContent_HttpsLink_CreatesNavigableHyperlink()
		=> STATestHelper.RunInSTA(() =>
		{
			FrameworkElement element = MarkdownToolTipRenderer.CreateContent("[docs](https://example.com)");
			var viewer = (FlowDocumentScrollViewer)element;

			Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

			Assert.AreEqual("https://example.com/", hyperlink.NavigateUri?.AbsoluteUri);
			Assert.AreEqual(Cursors.Hand, hyperlink.Cursor);
		});

	[TestMethod]
	public void CreateContent_Autolink_CreatesHyperlink()
		=> STATestHelper.RunInSTA(() =>
		{
			FrameworkElement element = MarkdownToolTipRenderer.CreateContent("See <https://example.com> for details.");
			var viewer = (FlowDocumentScrollViewer)element;

			Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

			Assert.AreEqual("https://example.com/", hyperlink.NavigateUri?.AbsoluteUri);
			Assert.AreEqual(Cursors.Hand, hyperlink.Cursor);
		});

	[TestMethod]
	public void CreateContent_UnsupportedSchemeLink_IsNotNavigable()
		=> STATestHelper.RunInSTA(() =>
		{
			FrameworkElement element = MarkdownToolTipRenderer.CreateContent("[x](javascript:alert(1))");
			var viewer = (FlowDocumentScrollViewer)element;

			Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

			Assert.IsNull(hyperlink.NavigateUri);
			Assert.AreEqual(Cursors.Arrow, hyperlink.Cursor);
		});

	[TestMethod]
	public void CreateContent_Heading_ScalesFontSizeAndIsBold()
		=> STATestHelper.RunInSTA(() =>
		{
			FrameworkElement element = MarkdownToolTipRenderer.CreateContent("# Heading");
			var viewer = (FlowDocumentScrollViewer)element;

			Paragraph paragraph = FindAll<Paragraph>(viewer.Document).Single();

			Assert.IsTrue(paragraph.FontSize > MarkdownToolTipTheme.Default.BodyFontSize);
			Assert.AreEqual(FontWeights.Bold, paragraph.FontWeight);
		});

	[TestMethod]
	public void CreateContent_List_ProducesWpfList()
		=> STATestHelper.RunInSTA(() =>
		{
			FrameworkElement element = MarkdownToolTipRenderer.CreateContent("- one\n- two");
			var viewer = (FlowDocumentScrollViewer)element;

			List list = FindAll<List>(viewer.Document).Single();

			Assert.AreEqual(2, list.ListItems.Count);
			Assert.AreEqual(TextMarkerStyle.Disc, list.MarkerStyle);
		});

	[TestMethod]
	public void CreateContent_OrderedList_ProducesDecimalMarker()
		=> STATestHelper.RunInSTA(() =>
		{
			FrameworkElement element = MarkdownToolTipRenderer.CreateContent("3. three\n4. four");
			var viewer = (FlowDocumentScrollViewer)element;

			List list = FindAll<List>(viewer.Document).Single();

			Assert.AreEqual(TextMarkerStyle.Decimal, list.MarkerStyle);
			Assert.AreEqual(3, list.StartIndex);
		});

	[TestMethod]
	public void CreateContent_Blockquote_ProducesSection()
		=> STATestHelper.RunInSTA(() =>
		{
			FrameworkElement element = MarkdownToolTipRenderer.CreateContent("> quoted");
			var viewer = (FlowDocumentScrollViewer)element;

			Section section = FindAll<Section>(viewer.Document).Single();

			Assert.AreEqual(1, section.Blocks.Count);
		});

	[TestMethod]
	public void CreateContent_ThematicBreak_ProducesSeparatorBlock()
		=> STATestHelper.RunInSTA(() =>
		{
			FrameworkElement element = MarkdownToolTipRenderer.CreateContent("before\n\n---\n\nafter");
			var viewer = (FlowDocumentScrollViewer)element;

			BlockUIContainer container = FindAll<BlockUIContainer>(viewer.Document).Single();
			var border = (Border)container.Child;

			Assert.AreEqual(1.0, border.Height);
		});

	[TestMethod]
	public void CreateContent_Emphasis_ProducesBoldAndItalic()
		=> STATestHelper.RunInSTA(() =>
		{
			FrameworkElement element = MarkdownToolTipRenderer.CreateContent("**bold** and *italic*");
			var viewer = (FlowDocumentScrollViewer)element;

			Assert.AreEqual(1, FindAll<Bold>(viewer.Document).Count());
			Assert.AreEqual(1, FindAll<Italic>(viewer.Document).Count());
		});

	[TestMethod]
	public void CreateContent_Strikethrough_ProducesStrikethroughDecoration()
		=> STATestHelper.RunInSTA(() =>
		{
			FrameworkElement element = MarkdownToolTipRenderer.CreateContent("~~gone~~");
			var viewer = (FlowDocumentScrollViewer)element;

			Span span = FindAll<Span>(viewer.Document).Single();

			Assert.IsTrue(span.TextDecorations.Count > 0);
			Assert.AreEqual(TextDecorationLocation.Strikethrough, span.TextDecorations[0].Location);
		});

	[TestMethod]
	public void CreateContent_Table_ProducesWpfTable()
		=> STATestHelper.RunInSTA(() =>
		{
			FrameworkElement element = MarkdownToolTipRenderer.CreateContent("| a | b |\n|---|---|\n| 1 | 2 |");
			var viewer = (FlowDocumentScrollViewer)element;

			Table table = FindAll<Table>(viewer.Document).Single();

			Assert.AreEqual(2, table.RowGroups[0].Rows.Count);
			Assert.AreEqual(2, table.RowGroups[0].Rows[0].Cells.Count);
		});

	[TestMethod]
	public void CreatePlainTextContent_RendersTextInScrollViewer()
		=> STATestHelper.RunInSTA(() =>
		{
			FrameworkElement element = MarkdownToolTipRenderer.CreatePlainTextContent("plain text");
			var scrollViewer = (ScrollViewer)element;
			var textBlock = (TextBlock)scrollViewer.Content;

			Assert.AreEqual("plain text", textBlock.Text);
		});

	[TestMethod]
	public void CreateCodeBlockEditor_DoesNotAcceptKeyboardFocus()
		=> STATestHelper.RunInSTA(() =>
		{
			AvalonTextEditor editor = MarkdownToolTipRenderer.CreateCodeBlockEditor("lua", "local value = 1", MarkdownToolTipTheme.Default);

			Assert.IsFalse(editor.Focusable);
			Assert.IsFalse(editor.IsTabStop);
			Assert.IsFalse(editor.TextArea.Focusable);
			Assert.IsFalse(editor.TextArea.IsTabStop);
		});

	[TestMethod]
	public void CreateCodeBlockEditor_CustomHighlightingHook_IsInvoked()
		=> STATestHelper.RunInSTA(() =>
		{
			bool invoked = false;

			var options = new MarkdownToolTipOptions
			{
				InstallCustomHighlighting = (editor, language) =>
				{
					invoked = true;
					Assert.AreEqual("lua", language);
					return false;
				}
			};

			MarkdownToolTipRenderer.CreateCodeBlockEditor("lua", "x = 1", MarkdownToolTipTheme.Default, options);

			Assert.IsTrue(invoked);
		});

	[TestMethod]
	public void CreateContent_CustomHighlightingHook_IsInvokedForFencedCode()
		=> STATestHelper.RunInSTA(() =>
		{
			bool invoked = false;

			var options = new MarkdownToolTipOptions
			{
				InstallCustomHighlighting = (editor, language) =>
				{
					invoked = true;
					return false;
				}
			};

			MarkdownToolTipRenderer.CreateContent("```lua\nx = 1\n```", MarkdownToolTipTheme.Default, options);

			Assert.IsTrue(invoked);
		});

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
}

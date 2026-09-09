using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

/// <summary>
/// Verifies the default completion-data adapter, including its snippet expansion at commit time.
/// </summary>
[STATestClass]
public sealed partial class TextCompletionItemCompletionDataTests
{
	[TestMethod]
	public void Complete_PlainTextItem_ReplacesTheCompletionSegment()
	{
		ICSharpCode.AvalonEdit.TextEditor editor = CompletionTestHost.CreateEditor("pr");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print") { InsertText = "print" });

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual("print", editor.Text);
	}

	[TestMethod]
	public void Complete_SnippetItem_InsertsExpandedTextAndPlacesTheCaretAtTheFinalTabstop()
	{
		ICSharpCode.AvalonEdit.TextEditor editor = CompletionTestHost.CreateEditor("sp");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("write")
		{
			InsertText = "write(${1:name})$0",
			InsertTextFormat = TextCompletionInsertTextFormat.Snippet
		});

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual("write(name)", editor.Text);
		Assert.AreEqual("write(name)".Length, editor.TextArea.Caret.Offset);
	}

	[TestMethod]
	public void Complete_SnippetItemWithDefaultedFinalTabstop_PlacesTheCaretAfterTheDefaultText()
	{
		ICSharpCode.AvalonEdit.TextEditor editor = CompletionTestHost.CreateEditor("sp");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("write")
		{
			InsertText = "write()${0:tail}",
			InsertTextFormat = TextCompletionInsertTextFormat.Snippet
		});

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual("write()tail", editor.Text);
		Assert.AreEqual("write()tail".Length, editor.TextArea.Caret.Offset);
	}

	[TestMethod]
	public void Complete_SnippetItemWithoutFinalTabstop_InsertsExpandedText()
	{
		ICSharpCode.AvalonEdit.TextEditor editor = CompletionTestHost.CreateEditor("sp");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("write")
		{
			InsertText = "write(${1:name})",
			InsertTextFormat = TextCompletionInsertTextFormat.Snippet
		});

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual("write(name)", editor.Text);
	}

	[TestMethod]
	public void Complete_SnippetItemWithMalformedSnippet_InsertsTheTextAsIs()
	{
		ICSharpCode.AvalonEdit.TextEditor editor = CompletionTestHost.CreateEditor("a");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("a")
		{
			InsertText = "a${1:b",
			InsertTextFormat = TextCompletionInsertTextFormat.Snippet
		});

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 1 }, EventArgs.Empty);

		Assert.AreEqual("a${1:b", editor.Text);
	}
}

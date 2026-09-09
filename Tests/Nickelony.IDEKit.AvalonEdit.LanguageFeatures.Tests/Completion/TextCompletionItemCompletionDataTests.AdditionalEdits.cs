using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

public sealed partial class TextCompletionItemCompletionDataTests
{
	[TestMethod]
	public void Complete_AdditionalEditBeforeTheSegment_AppliesTheWholeCommitAsOneUndoUnit()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("alpha x = 1\npr");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print")
		{
			InsertText = "print",
			AdditionalTextEdits = [new TextCompletionTextEdit(new TextRange(0, 0), newText: "include 'm'\n")]
		});

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 12, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual("include 'm'\nalpha x = 1\nprint", editor.Text);

		// The insertion and the additional edit were applied as one change, so a single undo
		// restores the whole commit.
		editor.Document.UndoStack.Undo();

		Assert.AreEqual("alpha x = 1\npr", editor.Text);
	}

	[TestMethod]
	public void Complete_AdditionalEditAfterTheSegment_UsesOriginalDocumentOffsets()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("pr tail");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print")
		{
			InsertText = "print",
			AdditionalTextEdits = [new TextCompletionTextEdit(new TextRange(2, 1), newText: "_")]
		});

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		// The edit replaced the space at its original offset; the insertion did not shift it.
		Assert.AreEqual("print_tail", editor.Text);
	}

	[TestMethod]
	public void Complete_AdditionalEditEndingAtTheSegmentStart_IsApplied()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("abpr");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print")
		{
			InsertText = "print",
			AdditionalTextEdits = [new TextCompletionTextEdit(new TextRange(0, 2), newText: "AB")]
		});

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 2, Length = 2 }, EventArgs.Empty);

		// A range that merely touches the completion segment does not overlap it.
		Assert.AreEqual("ABprint", editor.Text);
	}

	[TestMethod]
	public void Complete_StaleAdditionalEdit_IsSkipped()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("pr");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print")
		{
			InsertText = "print",
			AdditionalTextEdits = [new TextCompletionTextEdit(new TextRange(50, 1), newText: "X")]
		});

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		// The range lies outside the current document, so the entry contributed nothing.
		Assert.AreEqual("print", editor.Text);
	}

	[TestMethod]
	public void Complete_AdditionalEditOverlappingTheInsertion_IsSkipped()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("prx");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print")
		{
			InsertText = "print",
			AdditionalTextEdits = [new TextCompletionTextEdit(new TextRange(1, 2), newText: "X")]
		});

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual("printx", editor.Text);
	}

	[TestMethod]
	public void Complete_OverlappingAdditionalEdits_KeepTheFirstEntry()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("prwxyz");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print")
		{
			InsertText = "print",
			AdditionalTextEdits =
			[
				new TextCompletionTextEdit(new TextRange(2, 2), newText: "A"),
				new TextCompletionTextEdit(new TextRange(3, 2), newText: "B")
			]
		});

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		// The second entry overlaps the first accepted one, so only the first was applied.
		Assert.AreEqual("printAyz", editor.Text);
	}

	[TestMethod]
	public void Complete_AdditionalEditWithoutReplacementText_IsSkipped()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("pr");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print")
		{
			InsertText = "print",
			AdditionalTextEdits = [new TextCompletionTextEdit(new TextRange(2, 0))]
		});

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual("print", editor.Text);
	}

	[TestMethod]
	public void Complete_SnippetItemWithLeadingAdditionalEdit_KeepsTheFinalTabstopCaret()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("xx sp");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("write")
		{
			InsertText = "write(${1:name})$0",
			InsertTextFormat = TextCompletionInsertTextFormat.Snippet,
			AdditionalTextEdits = [new TextCompletionTextEdit(new TextRange(0, 0), newText: "//")]
		});

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 3, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual("//xx write(name)", editor.Text);

		// The caret follows the expanded snippet and is shifted by the leading edit's length delta.
		Assert.AreEqual("//xx write(name)".Length, editor.TextArea.Caret.Offset);
	}

	[TestMethod]
	public void Complete_PlainItemWithLeadingAdditionalEdit_LeavesTheCaretAfterTheInsertedText()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("xx pr");
		editor.CaretOffset = 5;

		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print")
		{
			InsertText = "print",
			AdditionalTextEdits = [new TextCompletionTextEdit(new TextRange(0, 0), newText: "//")]
		});

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 3, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual("//xx print", editor.Text);
		Assert.AreEqual("//xx print".Length, editor.TextArea.Caret.Offset);
	}

	[TestMethod]
	public void Complete_ZeroLengthSegment_AdditionalEditCoveringTheInsertionPoint_IsSkipped()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("prx");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print")
		{
			InsertText = "print",
			AdditionalTextEdits = [new TextCompletionTextEdit(new TextRange(2, 1), newText: "X")]
		});

		// The completion range inserts at offset 2 while the additional edit would replace [2, 3): the edit
		// covers the insertion point and extends beyond it, so no application order can honor both
		// operations; the entry is dropped like any other overlapping edit.
		data.Complete(editor.TextArea, new TextSegment { StartOffset = 2, Length = 0 }, EventArgs.Empty);

		Assert.AreEqual("prprintx", editor.Text);
	}

	[TestMethod]
	public void Complete_ZeroLengthSegment_ZeroLengthEditAtTheInsertionPoint_IsApplied()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("pr");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print")
		{
			InsertText = "print",
			AdditionalTextEdits = [new TextCompletionTextEdit(new TextRange(2, 0), newText: "X")]
		});

		// A zero-length edit at the insertion point merely touches it, so both insertions are applied with
		// the edit's text landing before the inserted text.
		data.Complete(editor.TextArea, new TextSegment { StartOffset = 2, Length = 0 }, EventArgs.Empty);

		Assert.AreEqual("prXprint", editor.Text);
	}
}

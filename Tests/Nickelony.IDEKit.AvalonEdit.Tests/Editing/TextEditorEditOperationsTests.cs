using Nickelony.IDEKit.AvalonEdit.Editing;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

[STATestClass]
public sealed class TextEditorEditOperationsTests
{
	[TestMethod]
	public void InsertText_InsertsAtOffsetAndPlacesCaretAfterText()
	{
		var editor = WPFTestHost.CreateEditor("ab");

		editor.InsertText(1, "XY");

		Assert.AreEqual("aXYb", editor.Text);
		Assert.AreEqual(3, editor.CaretOffset);
	}

	[TestMethod]
	public void InsertText_AndReplaceText_AreEachOneUndoStep()
	{
		var editor = WPFTestHost.CreateEditor("ab");
		editor.Document.UndoStack.ClearAll();

		editor.InsertText(1, "XY");

		editor.Document.UndoStack.Undo();

		Assert.AreEqual("ab", editor.Text);
		Assert.IsFalse(editor.Document.UndoStack.CanUndo);

		editor.ReplaceText(0, 2, "Z");

		editor.Document.UndoStack.Undo();

		Assert.AreEqual("ab", editor.Text);
		Assert.IsFalse(editor.Document.UndoStack.CanUndo);
	}

	[TestMethod]
	public void InsertText_EmptyDocument_InsertsAtStartAndPlacesCaretAfterText()
	{
		var editor = WPFTestHost.CreateEditor(string.Empty);

		editor.InsertText(0, "X");

		Assert.AreEqual("X", editor.Text);
		Assert.AreEqual(1, editor.CaretOffset);
	}

	[TestMethod]
	public void InsertText_WithCaretOffsetAfterEdit_PlacesCaretAtRequestedOffset()
	{
		var editor = WPFTestHost.CreateEditor("ab");

		editor.InsertText(1, "X", caretOffsetAfterEdit: 1);

		Assert.AreEqual("aXb", editor.Text);
		Assert.AreEqual(1, editor.CaretOffset);
	}

	[TestMethod]
	public void ReplaceText_ReplacesRequestedRange()
	{
		var editor = WPFTestHost.CreateEditor("abcdef");

		editor.ReplaceText(1, 3, "X");

		Assert.AreEqual("aXef", editor.Text);
		Assert.AreEqual(2, editor.CaretOffset);
	}

	[TestMethod]
	public void ReplaceText_EmptyDocument_EmptyRange_InsertsText()
	{
		var editor = WPFTestHost.CreateEditor(string.Empty);

		editor.ReplaceText(0, 0, "XY");

		Assert.AreEqual("XY", editor.Text);
		Assert.AreEqual(2, editor.CaretOffset);
	}

	[TestMethod]
	public void ReplaceText_RangeEndOverflow_ThrowsArgumentOutOfRangeExceptionForStartOffset()
	{
		var editor = WPFTestHost.CreateEditor("one");

		var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			editor.ReplaceText(int.MaxValue, 1, "x"));

		// The range guard rejects the request before it can reach a target as a negative range.
		Assert.AreEqual("startOffset", exception.ParamName);
		Assert.AreEqual("one", editor.Text);
	}

	[TestMethod]
	public void InsertText_WithContractEditTarget_AppliesToTargetAndPublishesEditorDocument()
	{
		var editor = WPFTestHost.CreateEditor("ab");
		var target = new ContractEditTarget(editor);

		editor.InsertText(1, "X", editTarget: target);

		Assert.HasCount(1, target.Operations);

		TextEditOperation operation = target.Operations[0];

		Assert.AreEqual(1, operation.StartOffset);
		Assert.AreEqual(1, operation.EndOffset);
		Assert.AreEqual("X", operation.NewText);

		// The contract-honoring target publishes the edit to the editor document before returning,
		// so the caret is derived from the updated document.
		Assert.AreEqual("aXb", target.Text);
		Assert.AreEqual("aXb", editor.Text);
		Assert.AreEqual(2, editor.CaretOffset);
	}

	[TestMethod]
	public void InsertText_WithNonUpdatingTarget_ClampsCaretAgainstTheStaleDocument()
	{
		var editor = WPFTestHost.CreateEditor("ab");
		var target = new RecordingEditTarget("ab");

		editor.InsertText(1, "XYZ", caretOffsetAfterEdit: 100, editTarget: target);

		// The double deliberately does not update the editor document, so the requested caret offset
		// is clamped against the stale editor length instead of the target's post-edit content.
		Assert.AreEqual("ab", editor.Text);
		Assert.AreEqual(2, editor.CaretOffset);
	}

	[TestMethod]
	public void InsertText_CaretOffsetAfterEditBeyondDocument_ClampsToTextLength()
	{
		var editor = WPFTestHost.CreateEditor("ab");

		editor.InsertText(1, "X", caretOffsetAfterEdit: 100);

		Assert.AreEqual(3, editor.CaretOffset);
	}

	[TestMethod]
	public void InsertText_NegativeCaretOffsetAfterEdit_ClampsToZero()
	{
		var editor = WPFTestHost.CreateEditor("ab");

		editor.InsertText(1, "X", caretOffsetAfterEdit: -5);

		Assert.AreEqual("aXb", editor.Text);
		Assert.AreEqual(0, editor.CaretOffset);
	}

	[TestMethod]
	public void InsertText_OffsetBeyondDocument_ThrowsArgumentOutOfRangeException()
	{
		var editor = WPFTestHost.CreateEditor("ab");

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => editor.InsertText(10, "X"));

		Assert.AreEqual("ab", editor.Text);
		Assert.AreEqual(0, editor.CaretOffset);
	}

	[TestMethod]
	public void ReplaceText_LengthBeyondDocument_ThrowsArgumentOutOfRangeException()
	{
		var editor = WPFTestHost.CreateEditor("ab");

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => editor.ReplaceText(1, 10, "X"));

		Assert.AreEqual("ab", editor.Text);
	}

	[TestMethod]
	public void InsertText_NegativeInsertOffset_ThrowsArgumentOutOfRangeException()
	{
		var editor = WPFTestHost.CreateEditor("ab");

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => editor.InsertText(-1, "X"));

		Assert.AreEqual("ab", editor.Text);
		Assert.AreEqual(0, editor.CaretOffset);
	}

	[TestMethod]
	public void ReplaceText_NegativeStartOffsetOrLength_ThrowsArgumentOutOfRangeException()
	{
		var editor = WPFTestHost.CreateEditor("ab");

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => editor.ReplaceText(-1, 0, "X"));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => editor.ReplaceText(0, -1, "X"));

		Assert.AreEqual("ab", editor.Text);
	}
}

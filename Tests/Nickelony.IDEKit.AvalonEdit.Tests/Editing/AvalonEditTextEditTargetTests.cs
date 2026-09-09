using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Editing;
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

[STATestClass]
public sealed class AvalonEditTextEditTargetTests
{
	[TestMethod]
	public void Text_ReturnsDocumentText()
	{
		var target = new AvalonEditTextEditTarget(WPFTestHost.CreateEditor("abcdef"));
		Assert.AreEqual("abcdef", target.Text);
	}

	[TestMethod]
	public void Apply_AppliesOperationsAndAdvancesTheStamp()
	{
		var editor = WPFTestHost.CreateEditor("abcdef");
		var target = new AvalonEditTextEditTarget(editor);

		// The stamp is captured before the content is read and compared before publishing, as the
		// ITextEditTargetVersion workflow describes; the first read attaches the change subscription.
		long capturedStamp = target.Version;

		target.Apply(new PreparedTextEdits([new TextEditOperation(1, 4, "X", 0)]));

		Assert.AreEqual("aXef", editor.Text);
		Assert.IsGreaterThan(capturedStamp, target.Version);
	}

	[TestMethod]
	public void Version_DirectEditorEdit_AdvancesTheStamp()
	{
		var editor = WPFTestHost.CreateEditor("abcdef");
		var target = new AvalonEditTextEditTarget(editor);

		long capturedStamp = target.Version;

		editor.Document.Insert(0, "X");

		// The stamp follows every change to the current document, so the documented stale-batch workflow
		// detects direct edits as well.
		Assert.IsGreaterThan(capturedStamp, target.Version);
	}

	[TestMethod]
	public void Apply_NoOperations_LeavesTheDocumentAndItsUndoStackUntouched()
	{
		var editor = WPFTestHost.CreateEditor("abcdef");
		var target = new AvalonEditTextEditTarget(editor);

		long capturedStamp = target.Version;

		target.Apply(new PreparedTextEdits([]));

		Assert.AreEqual("abcdef", editor.Text);
		Assert.AreEqual(capturedStamp, target.Version);
		Assert.IsFalse(editor.Document.UndoStack.CanUndo);
	}

	[TestMethod]
	public void Apply_MultipleOperations_UndoAsSingleStep()
	{
		var editor = WPFTestHost.CreateEditor("abcdef");
		var target = new AvalonEditTextEditTarget(editor);

		target.Apply(new PreparedTextEdits([
			new TextEditOperation(4, 5, "E", 0),
			new TextEditOperation(1, 2, "B", 1)]));

		Assert.AreEqual("aBcdEf", editor.Text);

		editor.Undo();

		Assert.AreEqual("abcdef", editor.Text);
	}

	[TestMethod]
	public void Apply_ChangedHandlerSwapsTheEditorDocument_AppliesTheBatchToTheResolvedDocument()
	{
		var originalDocument = new TextDocument("abcdef");
		var replacementDocument = new TextDocument("original");
		var editor = new TextEditor { Document = originalDocument };
		var target = new AvalonEditTextEditTarget(editor);

		// A Changed handler swaps the editor's document after the first operation. The target resolves
		// its document once per call, so the batch still applies to the document it started with, and
		// the new document is left untouched.
		int changedCalls = 0;
		originalDocument.Changed += (_, _) =>
		{
			if (changedCalls++ == 0)
				editor.Document = replacementDocument;
		};

		// The stamp is captured first, which attaches the change subscription to the original document.
		long capturedStamp = target.Version;

		target.Apply(new PreparedTextEdits([
			new TextEditOperation(4, 5, "E", 0),
			new TextEditOperation(1, 2, "B", 1)]));

		Assert.AreEqual("aBcdEf", originalDocument.Text);
		Assert.AreEqual("original", replacementDocument.Text);
		Assert.AreEqual("original", editor.Text);

		// The stamp follows the document, and the read after the swap attaches to the new document, so it
		// has advanced in both cases.
		Assert.IsGreaterThan(capturedStamp, target.Version);
	}

	[TestMethod]
	public void Apply_MultipleOperations_RaisesChangedPerOperation_AndAggregateEventsOnce()
	{
		var editor = WPFTestHost.CreateEditor("abcdef");
		var target = new AvalonEditTextEditTarget(editor);

		int changedCalls = 0;
		int textChangedCalls = 0;

		editor.Document.Changed += (_, _) => changedCalls++;
		editor.Document.TextChanged += (_, _) => textChangedCalls++;

		target.Apply(new PreparedTextEdits([
			new TextEditOperation(4, 5, "E", 0),
			new TextEditOperation(1, 2, "B", 1)]));

		Assert.AreEqual("aBcdEf", editor.Text);

		// The document update groups the aggregate events but raises Changed per operation.
		Assert.AreEqual(2, changedCalls);
		Assert.AreEqual(1, textChangedCalls);
	}

	[TestMethod]
	public void Apply_OperationBeyondDocument_ThrowsWithoutApplying()
	{
		var editor = WPFTestHost.CreateEditor("abcdef");
		var target = new AvalonEditTextEditTarget(editor);

		long capturedStamp = target.Version;

		// The batch is internally consistent, but the operation still fails against the document.
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			target.Apply(new PreparedTextEdits([new TextEditOperation(4, 99, "X", 0)])));

		Assert.AreEqual("abcdef", editor.Text);
		Assert.AreEqual(capturedStamp, target.Version);
	}

	[TestMethod]
	public void Apply_NoOpOperationsWithOutOfRangeOffsets_AreSkipped()
	{
		var editor = WPFTestHost.CreateEditor("abcdef");
		var target = new AvalonEditTextEditTarget(editor);

		// A no-op operation cannot change the document, so it is skipped instead of being replayed - and
		// failing - against the document.
		target.Apply(new PreparedTextEdits([
			new TextEditOperation(6, 6, "Z", 0),
			new TextEditOperation(10, 10, string.Empty, 0)]));

		Assert.AreEqual("abcdefZ", editor.Text);
	}

	[TestMethod]
	public void Apply_OutOfOrderOperations_ThrowsWithoutApplying()
	{
		var editor = WPFTestHost.CreateEditor("abcdef");
		var target = new AvalonEditTextEditTarget(editor);

		Assert.ThrowsExactly<ArgumentException>(() =>
			target.Apply(new PreparedTextEdits([
				new TextEditOperation(1, 2, "B", 0),
				new TextEditOperation(4, 5, "E", 1)])));

		// The batch is validated when it is constructed, before anything is applied.
		Assert.AreEqual("abcdef", editor.Text);
		Assert.AreEqual(0, target.Version);
	}

	[TestMethod]
	public void Apply_NullOperation_ThrowsWithoutApplying()
	{
		var editor = WPFTestHost.CreateEditor("abcdef");
		var target = new AvalonEditTextEditTarget(editor);

		Assert.ThrowsExactly<ArgumentException>(() => target.Apply(new PreparedTextEdits([null!])));

		Assert.AreEqual("abcdef", editor.Text);
		Assert.AreEqual(0, target.Version);
	}

	[TestMethod]
	public void TryApply_MatchingStamp_AppliesTheBatchAndAdvancesTheStamp()
	{
		var editor = WPFTestHost.CreateEditor("abcdef");
		var target = new AvalonEditTextEditTarget(editor);

		long capturedStamp = target.Version;

		bool applied = target.TryApply(new PreparedTextEdits([new TextEditOperation(1, 4, "X", 0)]), capturedStamp);

		Assert.IsTrue(applied);
		Assert.AreEqual("aXef", editor.Text);
		Assert.IsGreaterThan(capturedStamp, target.Version);
	}

	[TestMethod]
	public void TryApply_StaleStamp_DoesNotApplyTheBatch()
	{
		var editor = WPFTestHost.CreateEditor("abcdef");
		var target = new AvalonEditTextEditTarget(editor);

		long capturedStamp = target.Version;

		// A concurrent edit invalidates the captured stamp before the batch is published.
		editor.Document.Insert(0, "X");

		bool applied = target.TryApply(new PreparedTextEdits([new TextEditOperation(1, 4, "Y", 0)]), capturedStamp);

		Assert.IsFalse(applied);
		Assert.AreEqual("Xabcdef", editor.Text);
	}

	[TestMethod]
	public void TryApply_NoOperationsAndMatchingStamp_ReportsAppliedWithoutChangingTheDocument()
	{
		var editor = WPFTestHost.CreateEditor("abcdef");
		var target = new AvalonEditTextEditTarget(editor);

		long capturedStamp = target.Version;

		bool applied = target.TryApply(new PreparedTextEdits([]), capturedStamp);

		Assert.IsTrue(applied);
		Assert.AreEqual("abcdef", editor.Text);
		Assert.IsFalse(editor.Document.UndoStack.CanUndo);
	}
}

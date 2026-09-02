using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Editing;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

[TestClass]
public sealed class TextEditorEditHelperTests
{
	[TestMethod]
	public void InsertText_InsertsAtOffsetAndPlacesCaretAfterText()
	{
		STATestHelper.RunInSTA(() =>
		{
			var editor = CreateEditor("ab");

			TextEditorEditHelper.InsertText(editor, 1, "XY");

			Assert.AreEqual("aXYb", editor.Text);
			Assert.AreEqual(3, editor.CaretOffset);
		});
	}

	[TestMethod]
	public void InsertText_WithCaretOffset_PlacesCaretAtRequestedOffset()
	{
		STATestHelper.RunInSTA(() =>
		{
			var editor = CreateEditor("ab");

			TextEditorEditHelper.InsertText(editor, 1, "X", caretOffset: 1);

			Assert.AreEqual("aXb", editor.Text);
			Assert.AreEqual(1, editor.CaretOffset);
		});
	}

	[TestMethod]
	public void ReplaceText_ReplacesRequestedRange()
	{
		STATestHelper.RunInSTA(() =>
		{
			var editor = CreateEditor("abcdef");

			TextEditorEditHelper.ReplaceText(editor, 1, 3, "X");

			Assert.AreEqual("aXef", editor.Text);
			Assert.AreEqual(2, editor.CaretOffset);
		});
	}

	[TestMethod]
	public void InsertText_WithWorkspaceTarget_UsesTargetAndDoesNotNotifyContentChanged()
	{
		STATestHelper.RunInSTA(() =>
		{
			var editor = CreateEditor("ab");
			var target = new RecordingTarget();
			int contentChangedCalls = 0;

			TextEditorEditHelper.InsertText(
				editor,
				1,
				"X",
				workspaceEditTarget: target,
				contentChanged: () => contentChangedCalls++);

			Assert.AreEqual(1, target.ApplyCalls);
			Assert.AreEqual(0, contentChangedCalls);
		});
	}

	[TestMethod]
	public void InsertText_WithoutWorkspaceTarget_NotifiesContentChanged()
	{
		STATestHelper.RunInSTA(() =>
		{
			var editor = CreateEditor("ab");
			int contentChangedCalls = 0;

			TextEditorEditHelper.InsertText(editor, 1, "X", contentChanged: () => contentChangedCalls++);

			Assert.AreEqual(1, contentChangedCalls);
			Assert.AreEqual("aXb", editor.Text);
		});
	}

	[TestMethod]
	public void InsertText_CaretOffsetBeyondDocument_ClampsToTextLength()
	{
		STATestHelper.RunInSTA(() =>
		{
			var editor = CreateEditor("ab");

			TextEditorEditHelper.InsertText(editor, 1, "X", caretOffset: 100);

			Assert.AreEqual(3, editor.CaretOffset);
		});
	}

	[TestMethod]
	public void InsertText_NullEditor_Throws()
	{
		ICSharpCode.AvalonEdit.TextEditor editor = null!;

		Assert.ThrowsExactly<ArgumentNullException>(() => TextEditorEditHelper.InsertText(editor, 0, "X"));
	}

	private static ICSharpCode.AvalonEdit.TextEditor CreateEditor(string text)
		=> new() { Document = new TextDocument(text) };

	private sealed class RecordingTarget : ITextEditTarget
	{
		public int ApplyCalls { get; private set; }

		public string Text => string.Empty;

		public void Apply(IReadOnlyList<TextEditOperation> operations)
			=> ApplyCalls++;
	}
}

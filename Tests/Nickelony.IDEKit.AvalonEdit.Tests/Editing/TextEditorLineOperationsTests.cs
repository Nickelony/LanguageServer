using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Editing;
using Nickelony.IDEKit.Core.Text;
using System.Text.RegularExpressions;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

[TestClass]
public sealed class TextEditorLineOperationsTests
{
	[TestMethod]
	public void TryReplaceFirstMatchingLine_ReplacesFirstMatchingLine()
	{
		STATestHelper.RunInSTA(() =>
		{
			var editor = CreateEditor("one\r\ntwo\r\nthree");

			bool replaced = editor.TryReplaceFirstMatchingLine(
				lineText => lineText == "two" ? "2" : null,
				scrollToLine: false);

			Assert.IsTrue(replaced);
			Assert.AreEqual("one\r\n2\r\nthree", editor.Text);
		});
	}

	[TestMethod]
	public void TryReplaceFirstMatchingLine_NoMatch_ReturnsFalseAndLeavesDocument()
	{
		STATestHelper.RunInSTA(() =>
		{
			var editor = CreateEditor("one\r\ntwo");

			bool replaced = TextEditorLineOperations.TryReplaceFirstMatchingLine(
				editor,
				_ => null,
				scrollToLine: false);

			Assert.IsFalse(replaced);
			Assert.AreEqual("one\r\ntwo", editor.Text);
		});
	}

	[TestMethod]
	public void TryRenameInFirstMatchingLine_RenamesMatchingName()
	{
		STATestHelper.RunInSTA(() =>
		{
			var editor = CreateEditor("Level = old\r\nother");
			var regex = new Regex(@"^Level\s*=\s*");

			bool replaced = editor.TryRenameInFirstMatchingLine(
				regex,
				(lineText, pattern) => pattern.Replace(lineText, string.Empty).Trim(),
				"old",
				"new",
				scrollToLine: false);

			Assert.IsTrue(replaced);
			Assert.AreEqual("Level = new\r\nother", editor.Text);
		});
	}

	[TestMethod]
	public void TryReplaceFirstMatchingLine_WithWorkspaceTarget_DelegatesToTarget()
	{
		STATestHelper.RunInSTA(() =>
		{
			var editor = CreateEditor("one\r\ntwo");
			var target = new RecordingTarget();
			int contentChangedCalls = 0;

			bool replaced = editor.TryReplaceFirstMatchingLine(
				lineText => lineText == "one" ? "1" : null,
				scrollToLine: false,
				workspaceEditTarget: target,
				onContentChanged: () => contentChangedCalls++);

			Assert.IsTrue(replaced);
			Assert.AreEqual(1, target.ApplyCalls);
			Assert.AreEqual(0, contentChangedCalls);
		});
	}

	[TestMethod]
	public void TryReplaceFirstMatchingLine_WithoutWorkspaceTarget_NotifiesContentChanged()
	{
		STATestHelper.RunInSTA(() =>
		{
			var editor = CreateEditor("one\r\ntwo");
			int contentChangedCalls = 0;

			bool replaced = editor.TryReplaceFirstMatchingLine(
				lineText => lineText == "one" ? "1" : null,
				scrollToLine: false,
				onContentChanged: () => contentChangedCalls++);

			Assert.IsTrue(replaced);
			Assert.AreEqual(1, contentChangedCalls);
			Assert.AreEqual("1\r\ntwo", editor.Text);
		});
	}

	private static TextEditor CreateEditor(string text)
		=> new() { Document = new TextDocument(text) };

	private sealed class RecordingTarget : ITextEditTarget
	{
		public int ApplyCalls { get; private set; }

		public string Text => string.Empty;

		public void Apply(IReadOnlyList<TextEditOperation> operations)
			=> ApplyCalls++;
	}
}

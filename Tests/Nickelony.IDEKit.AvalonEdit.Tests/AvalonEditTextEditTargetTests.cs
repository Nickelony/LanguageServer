using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Editing;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

[TestClass]
public sealed class AvalonEditTextEditTargetTests
{
	[TestMethod]
	public void Text_ReturnsDocumentText()
	{
		STATestHelper.RunInSTA(() =>
		{
			var target = new AvalonEditTextEditTarget(CreateEditor("abcdef"));

			Assert.AreEqual("abcdef", target.Text);
		});
	}

	[TestMethod]
	public void Apply_AppliesOperationsAndIncrementsVersion()
	{
		STATestHelper.RunInSTA(() =>
		{
			var editor = CreateEditor("abcdef");
			var target = new AvalonEditTextEditTarget(editor);

			target.Apply([new TextEditOperation(1, 4, "X", 0)]);

			Assert.AreEqual("aXef", editor.Text);
			Assert.AreEqual(1, target.Version);
		});
	}

	[TestMethod]
	public void Apply_NoOperations_LeavesDocumentAndVersionUnchanged()
	{
		STATestHelper.RunInSTA(() =>
		{
			var editor = CreateEditor("abcdef");
			var target = new AvalonEditTextEditTarget(editor);

			target.Apply([]);

			Assert.AreEqual("abcdef", editor.Text);
			Assert.AreEqual(0, target.Version);
		});
	}

	[TestMethod]
	public void Apply_MultipleOperations_UndoAsSingleStep()
	{
		STATestHelper.RunInSTA(() =>
		{
			var editor = CreateEditor("abcdef");
			var target = new AvalonEditTextEditTarget(editor);

			target.Apply([
				new TextEditOperation(4, 5, "E", 0),
				new TextEditOperation(1, 2, "B", 1)]);

			Assert.AreEqual("aBcdEf", editor.Text);

			editor.Undo();

			Assert.AreEqual("abcdef", editor.Text);
		});
	}

	[TestMethod]
	public void Apply_NullOperations_Throws()
	{
		STATestHelper.RunInSTA(() =>
		{
			var target = new AvalonEditTextEditTarget(CreateEditor("abcdef"));

			Assert.ThrowsExactly<ArgumentNullException>(() => target.Apply(null!));
		});
	}

	[TestMethod]
	public void Constructor_NullEditor_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new AvalonEditTextEditTarget(null!));
	}

	private static ICSharpCode.AvalonEdit.TextEditor CreateEditor(string text)
		=> new() { Document = new TextDocument(text) };
}

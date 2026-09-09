using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.IntelliSense.Completion;
using System.Windows;
using System.Windows.Controls;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

public sealed partial class TextCompletionItemCompletionDataTests
{
	[TestMethod]
	public void Content_PlainItem_IsTheLabel()
	{
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print"));

		Assert.AreEqual("print", data.Content);
	}

	[TestMethod]
	public void Content_DeprecatedItem_IsTheStruckThroughLabel()
	{
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print")
		{
			Tags = [TextCompletionTag.Deprecated]
		});

		var content = (TextBlock)data.Content;

		Assert.AreEqual("print", content.Text);
		Assert.AreSame(TextDecorations.Strikethrough, content.TextDecorations);

		// The same element is returned on every read, and the text used for filtering is unchanged.
		Assert.AreSame(content, data.Content);
		Assert.AreEqual("print", data.Text);
	}

	[TestMethod]
	public void Complete_DeprecatedItem_CommitsLikeAnyOtherItem()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("pr");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print")
		{
			InsertText = "print",
			Tags = [TextCompletionTag.Deprecated]
		});

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		// The tag changes the rendering only; the item stays committable.
		Assert.AreEqual("print", editor.Text);
	}

	[TestMethod]
	public void Text_ItemWithFilterText_IsTheFilterTextWhileTheContentStaysTheLabel()
	{
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print(a, b)")
		{
			FilterText = "print"
		});

		// AvalonEdit filters and ranks against Text, which follows the shared item's filter text, while the
		// displayed content stays the faithful label.
		Assert.AreEqual("print", data.Text);
		Assert.AreEqual("print(a, b)", data.Content);
	}

	[TestMethod]
	public void Text_ItemWithoutFilterText_FallsBackToTheLabel()
	{
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print"));

		Assert.AreEqual("print", data.Text);
	}
}

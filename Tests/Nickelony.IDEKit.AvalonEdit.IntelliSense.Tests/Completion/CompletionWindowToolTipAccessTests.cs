using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.IntelliSense.Completion;
using System.Windows;
using System.Windows.Controls;

namespace Nickelony.IDEKit.AvalonEdit.IntelliSense.Tests;

[TestClass]
public class CompletionWindowToolTipAccessTests
{
	[TestMethod]
	public void TryGetToolTip_ReturnsCompletionWindowToolTip()
	{
		STATestHelper.RunInSTA(() =>
		{
			var editor = new ICSharpCode.AvalonEdit.TextEditor();
			Window hostWindow = WPFTestHost.ShowInHostWindow(editor);

			try
			{
				var completionWindow = new CompletionWindow(editor.TextArea);
				completionWindow.Show();

				bool resolved = CompletionWindowToolTipAccess.TryGetToolTip(completionWindow, out ToolTip? toolTip);

				Assert.IsTrue(resolved);
				Assert.IsNotNull(toolTip);

				completionWindow.Close();
			}
			finally
			{
				hostWindow.Close();
			}
		});
	}
}

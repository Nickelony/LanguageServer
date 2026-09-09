using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using System.Windows.Controls;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

[STATestClass]
public sealed class CompletionWindowToolTipAccessTests
{
	[TestMethod]
	public void TryGetToolTip_ReturnsCompletionWindowToolTip()
	{
		var editor = new ICSharpCode.AvalonEdit.TextEditor();
		HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		using (hostWindow)
		{
			var completionWindow = new CompletionWindow(editor.TextArea);
			completionWindow.Show();

			bool resolved = CompletionWindowToolTipAccess.TryGetToolTip(completionWindow, out ToolTip? toolTip);

			Assert.IsTrue(resolved);
			Assert.IsNotNull(toolTip);

			// The reflected AvalonEdit field is what the controller depends on; losing it must fail this
			// test when the AvalonEdit version is upgraded.
			Assert.IsTrue(CompletionWindowToolTipAccess.IsFieldAvailable);

			completionWindow.Close();
		}
	}
}

using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

[TestClass]
public sealed class DiagnosticsRendererTests
{
	[TestMethod]
	public void BrushProperties_CanBeOverridden()
	{
		STATestHelper.RunInSTA(() =>
		{
			SolidColorBrush originalError = DiagnosticsRenderer.ErrorBrush;
			SolidColorBrush originalWarning = DiagnosticsRenderer.WarningBrush;
			SolidColorBrush originalInformation = DiagnosticsRenderer.InformationBrush;
			SolidColorBrush originalHint = DiagnosticsRenderer.HintBrush;

			var customError = new SolidColorBrush(Colors.Red);
			var customWarning = new SolidColorBrush(Colors.Yellow);
			var customInformation = new SolidColorBrush(Colors.Blue);
			var customHint = new SolidColorBrush(Colors.Green);

			try
			{
				DiagnosticsRenderer.ErrorBrush = customError;
				DiagnosticsRenderer.WarningBrush = customWarning;
				DiagnosticsRenderer.InformationBrush = customInformation;
				DiagnosticsRenderer.HintBrush = customHint;

				Assert.AreSame(customError, DiagnosticsRenderer.ErrorBrush);
				Assert.AreSame(customWarning, DiagnosticsRenderer.WarningBrush);
				Assert.AreSame(customInformation, DiagnosticsRenderer.InformationBrush);
				Assert.AreSame(customHint, DiagnosticsRenderer.HintBrush);
			}
			finally
			{
				DiagnosticsRenderer.ErrorBrush = originalError;
				DiagnosticsRenderer.WarningBrush = originalWarning;
				DiagnosticsRenderer.InformationBrush = originalInformation;
				DiagnosticsRenderer.HintBrush = originalHint;
			}
		});
	}

	[TestMethod]
	public void Draw_SkipsRectanglesNarrowerThanMinimumWidth()
	{
		STATestHelper.RunInSTA(() =>
		{
			var editor = new TextEditor
			{
				Document = new TextDocument("x"),
				FontSize = 1.0
			};

			Window hostWindow = WPFTestHost.ShowInHostWindow(editor);

			try
			{
				Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

				var renderer = new DiagnosticsRenderer(
					() => editor.Document,
					() => [new TextDiagnosticSegment(0, 1, TextDiagnosticSeverity.Error)]);

				var visual = new DrawingVisual();

				using (DrawingContext drawingContext = visual.RenderOpen())
					renderer.Draw(editor.TextArea.TextView, drawingContext);

				Assert.IsNull(visual.Drawing);
			}
			finally
			{
				hostWindow.Close();
			}
		});
	}
}

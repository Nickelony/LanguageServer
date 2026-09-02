using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Diagnostics;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

[TestClass]
public sealed class DiagnosticsRendererTests
{
	[TestMethod]
	public void Layer_IsCaret()
	{
		STATestHelper.RunInSTA(() =>
		{
			var renderer = CreateRenderer(new TextDocument("abcdef"));

			Assert.AreEqual(KnownLayer.Caret, renderer.Layer);
		});
	}

	[TestMethod]
	public void Draw_WithEmptySegments_DoesNotThrow()
	{
		STATestHelper.RunInSTA(() =>
		{
			var renderer = CreateRenderer(new TextDocument("abcdef"));

			renderer.Draw(null!, null!);
		});
	}

	[TestMethod]
	public void Draw_WithNullDocument_DoesNotThrow()
	{
		STATestHelper.RunInSTA(() =>
		{
			var renderer = new DiagnosticsRenderer(
				documentProvider: () => null,
				segmentsProvider: () => [new TextDiagnosticSegment(1, 3, TextDiagnosticSeverity.Error)]);

			renderer.Draw(null!, null!);
		});
	}

	[TestMethod]
	public void BrushProperties_CanBeOverridden()
	{
		STATestHelper.RunInSTA(() =>
		{
			SolidColorBrush original = DiagnosticsRenderer.ErrorBrush;
			var custom = new SolidColorBrush(Colors.Red);

			try
			{
				DiagnosticsRenderer.ErrorBrush = custom;

				Assert.AreSame(custom, DiagnosticsRenderer.ErrorBrush);
			}
			finally
			{
				DiagnosticsRenderer.ErrorBrush = original;
			}
		});
	}

	private static DiagnosticsRenderer CreateRenderer(TextDocument document)
		=> new(
			documentProvider: () => document,
			segmentsProvider: () => []);
}

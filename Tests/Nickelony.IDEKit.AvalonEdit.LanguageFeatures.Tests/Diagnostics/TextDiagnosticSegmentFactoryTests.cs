using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.Diagnostics;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Diagnostics;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

[STATestClass]
public sealed class TextDiagnosticSegmentFactoryTests
{
	[TestMethod]
	public void Create_ProjectsOffsetsAndSeverity()
	{
		var diagnostics = new List<TextDiagnostic>
		{
			new(TextDiagnosticSeverity.Error, "first", 1, 5),
			new(TextDiagnosticSeverity.Hint, "second", 7, 7),
		};

		IReadOnlyList<TextDiagnosticSegment> segments = TextDiagnosticSegmentFactory.Create(diagnostics);

		Assert.HasCount(2, segments);
		Assert.AreEqual(1, segments[0].StartOffset);
		Assert.AreEqual(5, segments[0].EndOffset);
		Assert.AreEqual(TextDiagnosticSeverity.Error, segments[0].Severity);
		Assert.AreEqual(7, segments[1].StartOffset);
		Assert.AreEqual(7, segments[1].EndOffset);
		Assert.AreEqual(TextDiagnosticSeverity.Hint, segments[1].Severity);
	}

	[TestMethod]
	public void CreateProvider_ProjectsTheCurrentDiagnosticsOnEachCall()
	{
		IReadOnlyList<TextDiagnostic> current = [new TextDiagnostic(TextDiagnosticSeverity.Warning, "first", 1, 2)];

		Func<IReadOnlyList<TextDiagnosticSegment>> provider = TextDiagnosticSegmentFactory.CreateProvider(() => current);

		Assert.HasCount(1, provider());

		current = [];

		// The provider projects on each call, so a later render pass sees the updated diagnostics.
		Assert.IsEmpty(provider());
	}

	[TestMethod]
	public void CreateRenderer_DrawsTheProjectedSegments()
	{
		var editor = new TextEditor { Text = "sample" };
		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		IReadOnlyList<TextDiagnostic> diagnostics = [new TextDiagnostic(TextDiagnosticSeverity.Error, "first", 1, 2)];

		// The renderer draws for the projected diagnostics and draws nothing when the projection is empty,
		// so the draw exercises the segment pipeline and not just the renderer's construction.
		int drawnWithDiagnostics = CountDrawnFigures(editor, TextDiagnosticSegmentFactory.CreateRenderer(() => diagnostics));
		int drawnWithoutDiagnostics = CountDrawnFigures(editor, TextDiagnosticSegmentFactory.CreateRenderer(() => []));

		Assert.IsTrue(drawnWithDiagnostics > 0, "A projected diagnostic must draw an underline.");
		Assert.AreEqual(0, drawnWithoutDiagnostics);
	}

	[TestMethod]
	public void Create_NullDiagnostics_ThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => TextDiagnosticSegmentFactory.Create(null!));
	}

	[TestMethod]
	public void CreateProvider_NullProvider_ThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => TextDiagnosticSegmentFactory.CreateProvider(null!));
	}

	private static int CountDrawnFigures(TextEditor editor, DiagnosticsRenderer renderer)
	{
		editor.TextArea.TextView.EnsureVisualLines();

		var visual = new DrawingVisual();

		using (DrawingContext drawingContext = visual.RenderOpen())
		{
			renderer.Draw(editor.TextArea.TextView, drawingContext);
		}

		return visual.Drawing is DrawingGroup group ? group.Children.Count : 0;
	}
}

using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Diagnostics;
using Nickelony.IDEKit.Core.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

[STATestClass]
public sealed class DiagnosticsRendererTests
{
	[TestMethod]
	public void DefaultPens_AreFrozen()
	{
		DiagnosticsRenderer renderer = CreateRenderer();

		Assert.IsTrue(renderer.ErrorPen.IsFrozen);
		Assert.IsTrue(renderer.WarningPen.IsFrozen);
		Assert.IsTrue(renderer.InformationPen.IsFrozen);
		Assert.IsTrue(renderer.HintPen.IsFrozen);
	}

	[TestMethod]
	public void Draw_WidensRectanglesNarrowerThanMinimumWidth()
	{
		var editor = new TextEditor
		{
			Document = new TextDocument("x"),
			FontSize = 1.0
		};

		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		// The premise: the rendered rectangle is narrower than the renderer's 2-DIP minimum.
		// Asserting it here keeps a font-metric change from silently turning this into a vacuous test.
		var segment = new TextSegment { StartOffset = 0, EndOffset = 1 };
		var rects = ICSharpCode.AvalonEdit.Rendering.BackgroundGeometryBuilder
			.GetRectsForSegment(editor.TextArea.TextView, segment, false)
			.ToList();

		Assert.IsNotEmpty(rects);
		Assert.IsTrue(
			rects.All(rect => rect.Width < 2.0),
			"The test font must produce rectangles narrower than the renderer's 2-DIP minimum.");

		var renderer = new DiagnosticsRenderer(
			() => [new TextDiagnosticSegment(0, 1, TextDiagnosticSeverity.Error)]);

		var visual = new DrawingVisual();

		using (DrawingContext drawingContext = visual.RenderOpen())
			renderer.Draw(editor.TextArea.TextView, drawingContext);

		// A zero-width diagnostic (for example an end-of-file error) renders as a widened rectangle
		// instead of being dropped.
		DrawingGroup? drawing = visual.Drawing;

		Assert.IsNotNull(drawing);
		Assert.HasCount(1, drawing.Children);

		var geometryDrawing = (GeometryDrawing)drawing.Children[0];

		Assert.IsTrue(
			geometryDrawing.Geometry.Bounds.Width >= 1.99,
			"Expected the narrow rectangle to be widened to the renderer's minimum width.");
	}

	[TestMethod]
	public void Draw_UsesThePenAndGeometryOfEachSeverity()
	{
		var editor = new TextEditor
		{
			Document = new TextDocument(new string('x', 40)),
			FontSize = 12.0
		};

		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		AssertDrawsUnderline(editor, TextDiagnosticSeverity.Error, expectedSquiggle: true);
		AssertDrawsUnderline(editor, TextDiagnosticSeverity.Warning, expectedSquiggle: true);
		AssertDrawsUnderline(editor, TextDiagnosticSeverity.Information, expectedSquiggle: true);
		AssertDrawsUnderline(editor, TextDiagnosticSeverity.Hint, expectedSquiggle: false);
	}

	[TestMethod]
	public void Draw_MultipleSegmentsWithMixedSeverities_BatchesOneGeometryPerSeverity()
	{
		var editor = new TextEditor
		{
			Document = new TextDocument(new string('x', 60)),
			FontSize = 12.0
		};

		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		var renderer = new DiagnosticsRenderer(
			() =>
			[
				new TextDiagnosticSegment(0, 5, TextDiagnosticSeverity.Error),
				new TextDiagnosticSegment(10, 15, TextDiagnosticSeverity.Error),
				new TextDiagnosticSegment(20, 25, TextDiagnosticSeverity.Warning),
				new TextDiagnosticSegment(30, 35, TextDiagnosticSeverity.Hint),
			]);

		var visual = new DrawingVisual();

		using (DrawingContext drawingContext = visual.RenderOpen())
			renderer.Draw(editor.TextArea.TextView, drawingContext);

		DrawingGroup? drawing = visual.Drawing;

		Assert.IsNotNull(drawing);

		// The two error segments share one batched geometry, so one drawing per severity is produced
		// instead of one drawing per segment.
		Assert.HasCount(3, drawing.Children);

		var errorDrawing = (GeometryDrawing)drawing.Children[0];
		var warningDrawing = (GeometryDrawing)drawing.Children[1];
		var hintDrawing = (GeometryDrawing)drawing.Children[2];

		Assert.AreSame(renderer.ErrorPen, errorDrawing.Pen);
		Assert.AreSame(renderer.WarningPen, warningDrawing.Pen);
		Assert.AreSame(renderer.HintPen, hintDrawing.Pen);

		// The batched error geometry spans both error segments: it must be wider than the geometry
		// produced for the first error segment alone.
		double singleErrorWidth = RenderGeometryWidth(
			editor,
			[new TextDiagnosticSegment(0, 5, TextDiagnosticSeverity.Error)]);

		Assert.IsGreaterThan(singleErrorWidth, errorDrawing.Geometry.Bounds.Width);
	}

	[TestMethod]
	public void Draw_EmptyRangeAtTopOfDocument_DrawsTheNormalizedSegment()
	{
		var editor = new TextEditor
		{
			Document = new TextDocument("abcdef"),
			FontSize = 12.0
		};

		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);
		Assert.AreEqual(0, editor.TextArea.TextView.VisualLines[0].FirstDocumentLine.Offset);

		// An empty range normalizes to a length-1 segment, and the top of the document is visible,
		// so the underline must be drawn even though the requested range is empty.
		double width = RenderGeometryWidth(
			editor,
			[new TextDiagnosticSegment(0, 0, TextDiagnosticSeverity.Error)]);

		Assert.IsGreaterThan(0.0, width);
	}

	[TestMethod]
	public void Draw_ReversedRangeWithinVisibleRange_DrawsTheNormalizedSegment()
	{
		var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("line", 400)));
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		// Line 300 is beyond the first viewport, so the scroll moves the first visible offset away from zero.
		editor.ScrollToLine(300);
		editor.UpdateLayout();

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);
		Assert.IsGreaterThan(1, editor.TextArea.TextView.VisualLines[0].FirstDocumentLine.LineNumber);

		int firstVisibleOffset = editor.TextArea.TextView.VisualLines[0].FirstDocumentLine.Offset;

		Assert.IsGreaterThan(0, firstVisibleOffset, "The view must be scrolled past the document start.");

		// The reversed range ends where the visible range starts, so its end offset is not strictly
		// greater than the first visible offset; its normalized form starts two characters into the
		// first visible line and must still be drawn.
		double width = RenderGeometryWidth(
			editor,
			[new TextDiagnosticSegment(firstVisibleOffset + 2, firstVisibleOffset, TextDiagnosticSeverity.Error)]);

		Assert.IsGreaterThan(0.0, width);
	}

	private static double RenderGeometryWidth(TextEditor editor, IReadOnlyList<TextDiagnosticSegment> segments)
	{
		var renderer = new DiagnosticsRenderer(() => segments);
		var visual = new DrawingVisual();

		using (DrawingContext drawingContext = visual.RenderOpen())
			renderer.Draw(editor.TextArea.TextView, drawingContext);

		DrawingGroup? drawing = visual.Drawing;

		Assert.IsNotNull(drawing, "Expected the renderer to draw the normalized segment.");
		Assert.HasCount(1, drawing.Children);

		var geometryDrawing = (GeometryDrawing)drawing.Children[0];

		Assert.AreSame(renderer.ErrorPen, geometryDrawing.Pen);
		return geometryDrawing.Geometry.Bounds.Width;
	}

	private static void AssertDrawsUnderline(TextEditor editor, TextDiagnosticSeverity severity, bool expectedSquiggle)
	{
		var renderer = new DiagnosticsRenderer(
			() => [new TextDiagnosticSegment(0, 40, severity)]);

		var visual = new DrawingVisual();

		using (DrawingContext drawingContext = visual.RenderOpen())
			renderer.Draw(editor.TextArea.TextView, drawingContext);

		DrawingGroup? drawing = visual.Drawing;

		Assert.IsNotNull(drawing, $"No drawing was produced for {severity}.");
		Assert.HasCount(1, drawing.Children);

		var geometryDrawing = (GeometryDrawing)drawing.Children[0];
		Pen expectedPen = severity switch
		{
			TextDiagnosticSeverity.Error => renderer.ErrorPen,
			TextDiagnosticSeverity.Warning => renderer.WarningPen,
			TextDiagnosticSeverity.Information => renderer.InformationPen,
			_ => renderer.HintPen
		};

		Assert.AreSame(expectedPen, geometryDrawing.Pen, $"Wrong pen used for {severity}.");

		Assert.IsTrue(
			geometryDrawing.Geometry is StreamGeometry,
			$"Wrong underline geometry for {severity}.");
		Assert.IsGreaterThan(0.0, geometryDrawing.Geometry.Bounds.Width, $"Empty underline bounds for {severity}.");

		// A squiggle spans the wave height; a hint is a straight line with flat bounds.
		if (expectedSquiggle)
			Assert.IsGreaterThan(0.5, geometryDrawing.Geometry.Bounds.Height, $"Empty squiggle amplitude for {severity}.");
		else
			Assert.AreEqual(0.0, geometryDrawing.Geometry.Bounds.Height, $"Expected flat hint underline bounds for {severity}.");
	}

	[TestMethod]
	public void Draw_AttachedToARealView_RendersTheUnderlineInTheLayer()
	{
		var editor = new TextEditor
		{
			Document = new TextDocument(new string('x', 40)),
			FontSize = 12.0
		};

		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		var brush = new SolidColorBrush(Colors.Magenta);
		brush.Freeze();

		var errorPen = new Pen(brush, 2.0);
		errorPen.Freeze();

		var renderer = new DiagnosticsRenderer(
			() => [new TextDiagnosticSegment(0, 40, TextDiagnosticSeverity.Error)])
		{
			ErrorPen = errorPen
		};

		editor.TextArea.TextView.BackgroundRenderers.Add(renderer);

		BitmapSource bitmap = TestBitmapRendering.PumpAndRender(editor.TextArea.TextView);

		// The renderer draws in the selection layer; rendering the view must draw its underline.
		Assert.IsTrue(
			TestBitmapRendering.TryGetColorColumnBounds(bitmap, int.MaxValue, Colors.Magenta, out int minColumn, out int maxColumn),
			"Expected the attached renderer to draw the error underline into the rendered view.");
		Assert.IsGreaterThan(1, maxColumn - minColumn);
	}

	[TestMethod]
	public void Draw_SkipsNoneSeveritySegments()
	{
		var editor = new TextEditor
		{
			Document = new TextDocument(new string('x', 40)),
			FontSize = 1.0
		};

		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		var renderer = new DiagnosticsRenderer(
			() => [new TextDiagnosticSegment(0, 40, TextDiagnosticSeverity.None)]);

		var visual = new DrawingVisual();

		using (DrawingContext drawingContext = visual.RenderOpen())
			renderer.Draw(editor.TextArea.TextView, drawingContext);

		Assert.IsNull(visual.Drawing);
	}

	[TestMethod]
	public void Draw_SkipsUnrecognizedSeveritySegments()
	{
		var editor = new TextEditor
		{
			Document = new TextDocument(new string('x', 40)),
			FontSize = 1.0
		};

		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		var renderer = new DiagnosticsRenderer(
			() => [new TextDiagnosticSegment(0, 40, (TextDiagnosticSeverity)42)]);

		var visual = new DrawingVisual();

		using (DrawingContext drawingContext = visual.RenderOpen())
			renderer.Draw(editor.TextArea.TextView, drawingContext);

		// An unrecognized severity is treated like None and draws nothing.
		Assert.IsNull(visual.Drawing);
	}

	[TestMethod]
	public void Draw_SegmentOutsideVisibleRange_DrawsNothing()
	{
		var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("line", 400)));
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		// Premise: the segment's line must not be among the rendered visual lines for the test to mean anything.
		Assert.IsFalse(
			editor.TextArea.TextView.VisualLines.Any(line => line.FirstDocumentLine.LineNumber >= 350),
			"Expected line 350 to be outside the visible range.");

		// Line 350 is far below the visible range of the host window.
		int startOffset = document.GetLineByNumber(350).Offset;

		var renderer = new DiagnosticsRenderer(
			() => [new TextDiagnosticSegment(startOffset, startOffset + 4, TextDiagnosticSeverity.Error)]);

		var visual = new DrawingVisual();

		using (DrawingContext drawingContext = visual.RenderOpen())
			renderer.Draw(editor.TextArea.TextView, drawingContext);

		Assert.IsNull(visual.Drawing);
	}

	[TestMethod]
	public void Draw_CustomPen_DrawsTheUnderlineWithThatPen()
	{
		var editor = new TextEditor
		{
			Document = new TextDocument(new string('x', 40)),
			FontSize = 12.0
		};

		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		var renderer = new DiagnosticsRenderer(
			() => [new TextDiagnosticSegment(0, 40, TextDiagnosticSeverity.Error)]);

		var brush = new SolidColorBrush(Colors.Magenta);
		brush.Freeze();

		var errorPen = new Pen(brush, 2.0);
		errorPen.Freeze();

		renderer.ErrorPen = errorPen;

		var visual = new DrawingVisual();

		using (DrawingContext drawingContext = visual.RenderOpen())
			renderer.Draw(editor.TextArea.TextView, drawingContext);

		DrawingGroup? drawing = visual.Drawing;

		Assert.IsNotNull(drawing);
		Assert.HasCount(1, drawing.Children);

		var geometryDrawing = (GeometryDrawing)drawing.Children[0];

		Assert.AreSame(errorPen, geometryDrawing.Pen);
		Assert.IsGreaterThan(0.0, geometryDrawing.Geometry.Bounds.Width);
	}

	[TestMethod]
	public void Layer_DefaultsToSelectionAndIsAssignable()
	{
		DiagnosticsRenderer renderer = CreateRenderer();

		// The selection layer is repainted when the view scrolls or its visual lines change, and it
		// exists for any text area; the caret layer redraws on the caret blink instead.
		Assert.AreEqual(ICSharpCode.AvalonEdit.Rendering.KnownLayer.Selection, renderer.Layer);

		renderer.Layer = ICSharpCode.AvalonEdit.Rendering.KnownLayer.Text;

		Assert.AreEqual(ICSharpCode.AvalonEdit.Rendering.KnownLayer.Text, renderer.Layer);
	}

	[TestMethod]
	public void Draw_NullProviderResults_DrawsNothing()
	{
		var editor = new TextEditor { Document = new TextDocument("x") };

		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		var renderer = new DiagnosticsRenderer(() => null!);

		var visual = new DrawingVisual();

		using (DrawingContext drawingContext = visual.RenderOpen())
			renderer.Draw(editor.TextArea.TextView, drawingContext);

		Assert.IsNull(visual.Drawing);
	}

	[TestMethod]
	public void Draw_ViewWithoutDocument_DrawsNothing()
	{
		// An unhosted editor has no document and no valid visual lines; the renderer must return before
		// touching the view's visual lines or querying the segments.
		bool segmentsRequested = false;
		var renderer = new DiagnosticsRenderer(() =>
		{
			segmentsRequested = true;
			return [new TextDiagnosticSegment(0, 1, TextDiagnosticSeverity.Error)];
		});

		var visual = new DrawingVisual();

		using (DrawingContext drawingContext = visual.RenderOpen())
			renderer.Draw(new TextEditor().TextArea.TextView, drawingContext);

		Assert.IsNull(visual.Drawing);
		Assert.IsFalse(segmentsRequested, "The segments provider must not be queried for a view that cannot be drawn.");
	}

	[TestMethod]
	public void Draw_CustomPens_DrawEachSeverityWithItsOwnPen()
	{
		var editor = new TextEditor
		{
			Document = new TextDocument(new string('x', 40)),
			FontSize = 12.0
		};

		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		var renderer = new DiagnosticsRenderer(
			() =>
			[
				new TextDiagnosticSegment(0, 5, TextDiagnosticSeverity.Error),
				new TextDiagnosticSegment(6, 10, TextDiagnosticSeverity.Warning),
				new TextDiagnosticSegment(11, 15, TextDiagnosticSeverity.Information),
				new TextDiagnosticSegment(16, 20, TextDiagnosticSeverity.Hint),
			]);

		Pen errorPen = CreateFrozenPen(Colors.Magenta);
		Pen warningPen = CreateFrozenPen(Colors.Cyan);
		Pen informationPen = CreateFrozenPen(Colors.Lime);
		Pen hintPen = CreateFrozenPen(Colors.Orange);

		renderer.ErrorPen = errorPen;
		renderer.WarningPen = warningPen;
		renderer.InformationPen = informationPen;
		renderer.HintPen = hintPen;

		var visual = new DrawingVisual();

		using (DrawingContext drawingContext = visual.RenderOpen())
			renderer.Draw(editor.TextArea.TextView, drawingContext);

		DrawingGroup? drawing = visual.Drawing;

		Assert.IsNotNull(drawing);
		Assert.HasCount(4, drawing.Children);
		Assert.AreSame(errorPen, ((GeometryDrawing)drawing.Children[0]).Pen);
		Assert.AreSame(warningPen, ((GeometryDrawing)drawing.Children[1]).Pen);
		Assert.AreSame(informationPen, ((GeometryDrawing)drawing.Children[2]).Pen);
		Assert.AreSame(hintPen, ((GeometryDrawing)drawing.Children[3]).Pen);
	}

	[TestMethod]
	public void Draw_ValidVisualLinesWithoutVisibleRange_DrawsNothing()
	{
		var editor = new TextEditor
		{
			Document = new TextDocument(new string('x', 40)),
			FontSize = 12.0,
			Width = 200.0,
			Height = 0.0
		};

		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		editor.UpdateLayout();

		// The premise: the view has a valid visual-line state but no visible line to draw against.
		Assert.IsTrue(editor.TextArea.TextView.VisualLinesValid);
		Assert.IsEmpty(editor.TextArea.TextView.VisualLines);

		bool segmentsRequested = false;
		var renderer = new DiagnosticsRenderer(() =>
		{
			segmentsRequested = true;
			return [new TextDiagnosticSegment(0, 5, TextDiagnosticSeverity.Error)];
		});

		var visual = new DrawingVisual();

		using (DrawingContext drawingContext = visual.RenderOpen())
			renderer.Draw(editor.TextArea.TextView, drawingContext);

		// The provider is queried, but the empty visible range filters every segment out.
		Assert.IsTrue(segmentsRequested);
		Assert.IsNull(visual.Drawing);
	}

	private static Pen CreateFrozenPen(Color color)
	{
		var brush = new SolidColorBrush(color);
		brush.Freeze();

		var pen = new Pen(brush, 1.5);
		pen.Freeze();
		return pen;
	}

	private static DiagnosticsRenderer CreateRenderer()
		=> new(() => []);
}

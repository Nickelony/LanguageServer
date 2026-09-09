using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Infrastructure;
using System.Windows;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.Diagnostics;

/// <summary>
/// Renders diagnostic underlines in an AvalonEdit text view.
/// </summary>
/// <remarks>
/// <para>
/// The segments provider and the pens are queried during rendering and are not monitored for changes.
/// Hosts must invalidate the text view when they change, for example with <see cref="TextView.Redraw()"/>;
/// <see cref="TextView.InvalidateLayer(KnownLayer)"/> does not invalidate only the named layer; it
/// re-measures the view instead.
/// </para>
/// <para>
/// The pens are the theming seam for the underline colors: each defaults to a sample pen, and hosts
/// should assign theme-appropriate pens; changing a pen does not redraw the view by itself.
/// </para>
/// <para>
/// A provider that returns <see langword="null"/> is treated as producing no data. Providers must not
/// throw, because an exception from a provider surfaces inside the render pass.
/// </para>
/// <para>
/// Segments are clamped against the document of the text view that is being drawn, so the rendered
/// geometry always matches the drawn document. A view without a document, or with invalid visual
/// lines, draws nothing.
/// </para>
/// <para>
/// The renderer draws in <see cref="KnownLayer.Selection"/> by default, the layer AvalonEdit's own
/// current-line renderer uses, which is repainted when the view scrolls or its visual lines change.
/// Add the instance to the text view's background renderers; assign <see cref="Layer"/> before
/// attaching it when a different layer is required.
/// </para>
/// <para>
/// Severities map to underline styles: <see cref="TextDiagnosticSeverity.Error"/>,
/// <see cref="TextDiagnosticSeverity.Warning"/>, and
/// <see cref="TextDiagnosticSeverity.Information"/> draw squiggly underlines with their
/// respective pens, <see cref="TextDiagnosticSeverity.Hint"/> draws a dashed straight
/// underline, and <see cref="TextDiagnosticSeverity.None"/> and values outside the defined
/// members draw nothing. The severity-to-shape mapping is this renderer's fixed convention; a
/// host that needs different underline shapes adds its own <see cref="IBackgroundRenderer"/>.
/// </para>
/// </remarks>
public sealed class DiagnosticsRenderer : IBackgroundRenderer
{
	private const double MinimumRenderableRectangleWidth = 2.0;

	private static readonly SolidColorBrush s_errorBrush = BrushHelpers.CreateFrozenBrush(Color.FromArgb(224, 220, 76, 60));
	private static readonly SolidColorBrush s_warningBrush = BrushHelpers.CreateFrozenBrush(Color.FromArgb(224, 226, 165, 44));
	private static readonly SolidColorBrush s_informationBrush = BrushHelpers.CreateFrozenBrush(Color.FromArgb(224, 88, 170, 255));
	private static readonly SolidColorBrush s_hintBrush = BrushHelpers.CreateFrozenBrush(Color.FromArgb(192, 166, 166, 166));

	private static readonly Pen s_errorPen = BrushHelpers.CreateFrozenPen(s_errorBrush, 1.4);
	private static readonly Pen s_warningPen = BrushHelpers.CreateFrozenPen(s_warningBrush, 1.4);
	private static readonly Pen s_informationPen = BrushHelpers.CreateFrozenPen(s_informationBrush, 1.4);
	private static readonly Pen s_hintPen = BrushHelpers.CreateFrozenDashedPen(s_hintBrush, 1.5, [1.0, 3.0]);

	private Pen _errorPen = s_errorPen;
	private Pen _warningPen = s_warningPen;
	private Pen _informationPen = s_informationPen;
	private Pen _hintPen = s_hintPen;

	/// <summary>
	/// Gets or sets the pen used to draw error underlines.
	/// </summary>
	/// <remarks>
	/// The default is a sample pen; hosts should assign a theme-appropriate pen. Changing the pen does
	/// not redraw the view by itself.
	/// </remarks>
	/// <exception cref="ArgumentNullException">The assigned pen is <see langword="null"/>.</exception>
	public Pen ErrorPen
	{
		get => _errorPen;
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			_errorPen = value;
		}
	}

	/// <summary>
	/// Gets or sets the pen used to draw warning underlines.
	/// </summary>
	/// <remarks>
	/// The default is a sample pen; see <see cref="ErrorPen"/> for the theming guidance.
	/// </remarks>
	/// <exception cref="ArgumentNullException">The assigned pen is <see langword="null"/>.</exception>
	public Pen WarningPen
	{
		get => _warningPen;
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			_warningPen = value;
		}
	}

	/// <summary>
	/// Gets or sets the pen used to draw information underlines.
	/// </summary>
	/// <remarks>
	/// The default is a sample pen; see <see cref="ErrorPen"/> for the theming guidance.
	/// </remarks>
	/// <exception cref="ArgumentNullException">The assigned pen is <see langword="null"/>.</exception>
	public Pen InformationPen
	{
		get => _informationPen;
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			_informationPen = value;
		}
	}

	/// <summary>
	/// Gets or sets the pen used to draw hint underlines.
	/// </summary>
	/// <remarks>
	/// The default is a sample pen; see <see cref="ErrorPen"/> for the theming guidance.
	/// </remarks>
	/// <exception cref="ArgumentNullException">The assigned pen is <see langword="null"/>.</exception>
	public Pen HintPen
	{
		get => _hintPen;
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			_hintPen = value;
		}
	}

	private readonly Func<IReadOnlyList<TextDiagnosticSegment>> _segmentsProvider;

	/// <summary>
	/// Initializes a new instance of the <see cref="DiagnosticsRenderer"/> class.
	/// </summary>
	/// <param name="segmentsProvider">Provides the diagnostic segments to render.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="segmentsProvider"/> is <see langword="null"/>.
	/// </exception>
	public DiagnosticsRenderer(Func<IReadOnlyList<TextDiagnosticSegment>> segmentsProvider)
	{
		ArgumentNullException.ThrowIfNull(segmentsProvider);

		_segmentsProvider = segmentsProvider;
	}

	/// <summary>
	/// Gets or sets the layer the renderer draws in.
	/// </summary>
	/// <remarks>
	/// Defaults to <see cref="KnownLayer.Selection"/>, which is repainted when the text view scrolls or
	/// its visual lines change and exists for any text area. A bare <see cref="TextView"/> without a
	/// text area has no selection or caret layer, so such a view draws nothing until the layer is
	/// assigned to <see cref="KnownLayer.Background"/> or <see cref="KnownLayer.Text"/>. Assign the
	/// layer before adding the renderer to the view's background renderers.
	/// </remarks>
	public KnownLayer Layer { get; set; } = KnownLayer.Selection;

	/// <inheritdoc/>
	/// <remarks>
	/// See the type remarks for the provider and view requirements a render pass observes.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textView"/> or <paramref name="drawingContext"/> is <see langword="null"/>.
	/// </exception>
	public void Draw(TextView textView, DrawingContext drawingContext)
	{
		ArgumentNullException.ThrowIfNull(textView);
		ArgumentNullException.ThrowIfNull(drawingContext);

		// The renderer runs inside a render pass; a view that has not completed a layout pass
		// (its visual lines are invalid) or has no document cannot be drawn against.
		if (!textView.VisualLinesValid || textView.Document is null)
			return;

		IReadOnlyList<TextDiagnosticSegment>? segments = _segmentsProvider();

		if (segments is null || segments.Count == 0)
			return;

		TextDocument document = textView.Document;

		// Rectangles are collected per severity so each pen builds one geometry per render pass
		// instead of one geometry per segment.
		List<Rect>? errorRects = null;
		List<Rect>? warningRects = null;
		List<Rect>? informationRects = null;
		List<Rect>? hintRects = null;

		foreach (TextDiagnosticSegment segment in segments)
		{
			List<Rect>? rects = segment.Severity switch
			{
				TextDiagnosticSeverity.Error => errorRects ??= [],
				TextDiagnosticSeverity.Warning => warningRects ??= [],
				TextDiagnosticSeverity.Information => informationRects ??= [],
				TextDiagnosticSeverity.Hint => hintRects ??= [],
				_ => null
			};

			if (rects is null)
				continue;

			// Normalization runs before the visibility test: an empty or reversed range becomes a
			// length-1 segment, and the test must use the normalized offsets because those are the
			// offsets the underline is drawn for.
			if (!TextRangeNormalizer.TryNormalizeRange(document, segment.StartOffset, segment.EndOffset, out int startOffset, out int endOffset))
				continue;

			// The visibility test runs before the segment is materialized: most segments of a large
			// diagnostic set are outside the visible range, and a discarded segment must not allocate.
			if (!IntersectsVisibleRange(textView, startOffset, endOffset))
				continue;

			var textSegment = new TextSegment
			{
				StartOffset = startOffset,
				EndOffset = endOffset
			};

			foreach (Rect rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, textSegment, false))
			{
				if (rect.Height <= 0.0)
					continue;

				// Rectangles narrower than the minimum (end-of-file diagnostics, empty lines, thin glyphs)
				// are widened instead of dropped so the underline stays visible.
				rects.Add(rect.Width >= MinimumRenderableRectangleWidth
					? rect
					: new Rect(rect.X, rect.Y, MinimumRenderableRectangleWidth, rect.Height));
			}
		}

		DrawSquigglyUnderline(drawingContext, errorRects, _errorPen);
		DrawSquigglyUnderline(drawingContext, warningRects, _warningPen);
		DrawSquigglyUnderline(drawingContext, informationRects, _informationPen);
		DrawStraightUnderline(drawingContext, hintRects, _hintPen);
	}

	/// <summary>
	/// Determines whether the normalized segment can intersect the document range that is currently visible.
	/// </summary>
	/// <remarks>
	/// The caller guarantees that the text view's visual lines are valid.
	/// </remarks>
	private static bool IntersectsVisibleRange(TextView textView, int startOffset, int endOffset)
	{
		var visualLines = textView.VisualLines;

		// A valid visual-line state can still be empty (for example before layout completed), and the
		// render pass must draw nothing in that case instead of indexing an empty list.
		if (visualLines.Count == 0)
			return false;

		// Segment ends are exclusive, so a segment ending where the visible range starts cannot be visible.
		return endOffset > visualLines[0].FirstDocumentLine.Offset
			&& startOffset <= visualLines[^1].LastDocumentLine.EndOffset;
	}

	/// <summary>
	/// Draws the squiggly underline for the supplied rectangles as one batched geometry.
	/// </summary>
	/// <param name="drawingContext">The drawing context of the render pass.</param>
	/// <param name="rects">The rectangles to underline; <see langword="null"/> or empty draws nothing.</param>
	/// <param name="pen">The pen that strokes the geometry.</param>
	private static void DrawSquigglyUnderline(DrawingContext drawingContext, List<Rect>? rects, Pen pen)
	{
		if (rects is null || rects.Count == 0)
			return;

		var geometry = new StreamGeometry();

		using (StreamGeometryContext context = geometry.Open())
		{
			foreach (Rect rect in rects)
				AppendSquiggleFigure(context, rect);
		}

		geometry.Freeze();
		drawingContext.DrawGeometry(null, pen, geometry);
	}

	/// <summary>
	/// Appends one zig-zag figure for a rectangle to the geometry under construction.
	/// </summary>
	/// <param name="context">The geometry context to append to.</param>
	/// <param name="rect">The line-box rectangle to underline.</param>
	private static void AppendSquiggleFigure(StreamGeometryContext context, Rect rect)
	{
		// A 1.6-DIP amplitude keeps the zig-zag legible under the 1.4-DIP error pen, and the wave is
		// offset so that neither stroke leaves the rectangle: the down stroke ends above the line box's
		// bottom edge instead of bleeding into the next line's leading.
		const double amplitude = 1.6;
		const double step = 4.0;
		const double penBleed = 0.7;

		double baseline = rect.Bottom - amplitude - penBleed;

		bool goingUp = true;
		context.BeginFigure(new Point(rect.Left, baseline), false, false);

		for (double x = rect.Left; x < rect.Right; x += step)
		{
			double nextX = Math.Min(x + (step / 2.0), rect.Right);
			double y = baseline + (goingUp ? -amplitude : amplitude);
			context.LineTo(new Point(nextX, y), true, false);

			goingUp = !goingUp;

			nextX = Math.Min(x + step, rect.Right);
			context.LineTo(new Point(nextX, baseline), true, false);
		}
	}

	/// <summary>
	/// Draws the straight underline for the supplied rectangles as one batched geometry.
	/// </summary>
	/// <param name="drawingContext">The drawing context of the render pass.</param>
	/// <param name="rects">The rectangles to underline; <see langword="null"/> or empty draws nothing.</param>
	/// <param name="pen">The pen that strokes the geometry.</param>
	private static void DrawStraightUnderline(DrawingContext drawingContext, List<Rect>? rects, Pen pen)
	{
		if (rects is null || rects.Count == 0)
			return;

		// All rectangles are drawn as one geometry, matching the batched squiggly underlines.
		var geometry = new StreamGeometry();

		using (StreamGeometryContext context = geometry.Open())
		{
			foreach (Rect rect in rects)
			{
				double y = rect.Bottom - 1.0;

				context.BeginFigure(new Point(rect.Left, y), false, false);
				context.LineTo(new Point(rect.Right, y), true, false);
			}
		}

		geometry.Freeze();
		drawingContext.DrawGeometry(null, pen, geometry);
	}
}

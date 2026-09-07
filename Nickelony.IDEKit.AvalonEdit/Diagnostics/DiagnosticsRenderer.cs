using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Documents;
using Nickelony.IDEKit.Infrastructure;
using System.Windows;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.Diagnostics;

/// <summary>
/// Renders diagnostic underlines in an AvalonEdit text view.
/// </summary>
/// <remarks>
/// The segment provider is queried during rendering and is not monitored for changes.
/// Hosts must invalidate the text view when the diagnostic segments change.
/// </remarks>
public sealed class DiagnosticsRenderer : IBackgroundRenderer
{
	private const double MinimumRenderableRectangleWidth = 2.0;

	private static SolidColorBrush s_errorBrush = BrushHelpers.CreateFrozenBrush(Color.FromArgb(224, 220, 76, 60));
	private static SolidColorBrush s_warningBrush = BrushHelpers.CreateFrozenBrush(Color.FromArgb(224, 226, 165, 44));
	private static SolidColorBrush s_informationBrush = BrushHelpers.CreateFrozenBrush(Color.FromArgb(224, 88, 170, 255));
	private static SolidColorBrush s_hintBrush = BrushHelpers.CreateFrozenBrush(Color.FromArgb(192, 166, 166, 166));

	private static Pen s_errorPen = BrushHelpers.CreateFrozenPen(s_errorBrush, 1.4);
	private static Pen s_warningPen = BrushHelpers.CreateFrozenPen(s_warningBrush, 1.4);
	private static Pen s_informationPen = BrushHelpers.CreateFrozenPen(s_informationBrush, 1.4);
	private static Pen s_hintPen = BrushHelpers.CreateFrozenDashedPen(s_hintBrush, 1.5, [1.0, 3.0]);

	/// <summary>
	/// Gets or sets the brush used to draw error underlines.
	/// The assigned brush is used by subsequent renders. Setting it rebuilds the pen used for drawing.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
	public static SolidColorBrush ErrorBrush
	{
		get => s_errorBrush;
		set
		{
			ArgumentNullException.ThrowIfNull(value);

			s_errorBrush = value;
			s_errorPen = BrushHelpers.CreateFrozenPen(value, 1.4);
		}
	}

	/// <summary>
	/// Gets or sets the brush used to draw warning underlines.
	/// The assigned brush is used by subsequent renders. Setting it rebuilds the pen used for drawing.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
	public static SolidColorBrush WarningBrush
	{
		get => s_warningBrush;
		set
		{
			ArgumentNullException.ThrowIfNull(value);

			s_warningBrush = value;
			s_warningPen = BrushHelpers.CreateFrozenPen(value, 1.4);
		}
	}

	/// <summary>
	/// Gets or sets the brush used to draw information underlines.
	/// The assigned brush is used by subsequent renders. Setting it rebuilds the pen used for drawing.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
	public static SolidColorBrush InformationBrush
	{
		get => s_informationBrush;
		set
		{
			ArgumentNullException.ThrowIfNull(value);

			s_informationBrush = value;
			s_informationPen = BrushHelpers.CreateFrozenPen(value, 1.4);
		}
	}

	/// <summary>
	/// Gets or sets the brush used to draw hint underlines.
	/// The assigned brush is used by subsequent renders. Setting it rebuilds the pen used for drawing.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
	public static SolidColorBrush HintBrush
	{
		get => s_hintBrush;
		set
		{
			ArgumentNullException.ThrowIfNull(value);

			s_hintBrush = value;
			s_hintPen = BrushHelpers.CreateFrozenDashedPen(value, 1.5, [1.0, 3.0]);
		}
	}

	private readonly Func<TextDocument> _documentProvider;
	private readonly Func<IReadOnlyList<TextDiagnosticSegment>> _segmentsProvider;

	/// <summary>
	/// Initializes a renderer with providers for the document and diagnostic segments.
	/// </summary>
	/// <param name="documentProvider">Provides the document used to clamp ranges.</param>
	/// <param name="segmentsProvider">Provides the diagnostic segments to render.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="documentProvider"/> or <paramref name="segmentsProvider"/> is <see langword="null"/>.
	/// </exception>
	public DiagnosticsRenderer(
		Func<TextDocument> documentProvider,
		Func<IReadOnlyList<TextDiagnosticSegment>> segmentsProvider)
	{
		ArgumentNullException.ThrowIfNull(documentProvider);
		ArgumentNullException.ThrowIfNull(segmentsProvider);

		_documentProvider = documentProvider;
		_segmentsProvider = segmentsProvider;
	}

	/// <inheritdoc/>
	public KnownLayer Layer => KnownLayer.Caret;

	/// <inheritdoc/>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textView"/> or <paramref name="drawingContext"/> is <see langword="null"/>,
	/// or a provider returns <see langword="null"/>.
	/// </exception>
	public void Draw(TextView textView, DrawingContext drawingContext)
	{
		ArgumentNullException.ThrowIfNull(textView);
		ArgumentNullException.ThrowIfNull(drawingContext);

		IReadOnlyList<TextDiagnosticSegment> segments = _segmentsProvider();
		ArgumentNullException.ThrowIfNull(segments);

		if (segments.Count == 0)
			return;

		TextDocument document = _documentProvider();
		ArgumentNullException.ThrowIfNull(document);

		foreach (TextDiagnosticSegment segment in segments)
		{
			if (!TextSegmentFactory.TryCreate(document, segment.StartOffset, segment.EndOffset, out TextSegment? textSegment))
				continue;

			foreach (Rect rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, textSegment, false))
			{
				if (rect.Width < MinimumRenderableRectangleWidth)
					continue; // Skip very narrow rectangles

				switch (segment.Severity)
				{
					case TextDiagnosticSeverity.Warning:
						DrawSquigglyUnderline(drawingContext, rect, s_warningPen);
						break;

					case TextDiagnosticSeverity.Information:
						DrawSquigglyUnderline(drawingContext, rect, s_informationPen);
						break;

					case TextDiagnosticSeverity.Hint:
						DrawStraightUnderline(drawingContext, rect, s_hintPen);
						break;

					default:
						DrawSquigglyUnderline(drawingContext, rect, s_errorPen);
						break;
				}
			}
		}
	}

	private static void DrawSquigglyUnderline(DrawingContext drawingContext, Rect rect, Pen pen)
	{
		const double amplitude = 1.6;
		const double step = 4.0;

		double baseline = rect.Bottom - 1.0;

		var geometry = new StreamGeometry();

		using (StreamGeometryContext context = geometry.Open())
		{
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

		geometry.Freeze();
		drawingContext.DrawGeometry(null, pen, geometry);
	}

	private static void DrawStraightUnderline(DrawingContext drawingContext, Rect rect, Pen pen)
	{
		double y = rect.Bottom - 1.0;
		drawingContext.DrawLine(pen, new Point(rect.Left, y), new Point(rect.Right, y));
	}
}

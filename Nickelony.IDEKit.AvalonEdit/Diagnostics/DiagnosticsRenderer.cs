using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Documents;
using Nickelony.IDEKit.Infrastructure;

namespace Nickelony.IDEKit.AvalonEdit.Diagnostics;

/// <summary>
/// Renders diagnostic underlines in an AvalonEdit text view.
/// </summary>
/// <remarks>
/// Segments are clamped to the current document by <see cref="TextSegmentFactory"/>. Empty or
/// reversed ranges therefore underline one character when the document is non-empty. The segment
/// provider is queried each time AvalonEdit asks the renderer to draw.
/// </remarks>
public sealed class DiagnosticsRenderer : IBackgroundRenderer
{
	private static SolidColorBrush s_errorBrush = BrushHelpers.CreateFrozenBrush(Color.FromArgb(224, 220, 76, 60));
	private static SolidColorBrush s_warningBrush = BrushHelpers.CreateFrozenBrush(Color.FromArgb(224, 226, 165, 44));
	private static SolidColorBrush s_informationBrush = BrushHelpers.CreateFrozenBrush(Color.FromArgb(224, 88, 170, 255));
	private static SolidColorBrush s_hintBrush = BrushHelpers.CreateFrozenBrush(Color.FromArgb(192, 166, 166, 166));

	private static Pen s_errorPen = BrushHelpers.CreateFrozenPen(s_errorBrush, 1.4);
	private static Pen s_warningPen = BrushHelpers.CreateFrozenPen(s_warningBrush, 1.4);
	private static Pen s_informationPen = BrushHelpers.CreateFrozenPen(s_informationBrush, 1.4);
	private static Pen s_hintPen = BrushHelpers.CreateFrozenDashedPen(s_hintBrush, 1.5, [1.0, 3.0]);

	/// <summary>
	/// Gets or sets the brush used to draw error underlines. Reassigning the brush rebuilds the
	/// underline pen so the new color applies to subsequent drawing.
	/// </summary>
	public static SolidColorBrush ErrorBrush
	{
		get => s_errorBrush;
		set
		{
			s_errorBrush = value;
			s_errorPen = BrushHelpers.CreateFrozenPen(value, 1.4);
		}
	}

	/// <summary>
	/// Gets or sets the brush used to draw warning underlines. Reassigning the brush rebuilds the
	/// underline pen so the new color applies to subsequent drawing.
	/// </summary>
	public static SolidColorBrush WarningBrush
	{
		get => s_warningBrush;
		set
		{
			s_warningBrush = value;
			s_warningPen = BrushHelpers.CreateFrozenPen(value, 1.4);
		}
	}

	/// <summary>
	/// Gets or sets the brush used to draw information underlines. Reassigning the brush rebuilds the
	/// underline pen so the new color applies to subsequent drawing.
	/// </summary>
	public static SolidColorBrush InformationBrush
	{
		get => s_informationBrush;
		set
		{
			s_informationBrush = value;
			s_informationPen = BrushHelpers.CreateFrozenPen(value, 1.4);
		}
	}

	/// <summary>
	/// Gets or sets the brush used to draw hint underlines. Reassigning the brush rebuilds the
	/// underline pen so the new color applies to subsequent drawing.
	/// </summary>
	public static SolidColorBrush HintBrush
	{
		get => s_hintBrush;
		set
		{
			s_hintBrush = value;
			s_hintPen = BrushHelpers.CreateFrozenDashedPen(value, 1.5, [1.0, 3.0]);
		}
	}

	private readonly Func<TextDocument?> _documentProvider;
	private readonly Func<IReadOnlyList<TextDiagnosticSegment>> _segmentsProvider;

	/// <summary>
	/// Initializes a new instance of the <see cref="DiagnosticsRenderer"/> class.
	/// </summary>
	/// <param name="documentProvider">Provides the document the segments are clamped against.</param>
	/// <param name="segmentsProvider">Provides the diagnostic segments to render.</param>
	public DiagnosticsRenderer(
		Func<TextDocument?> documentProvider,
		Func<IReadOnlyList<TextDiagnosticSegment>> segmentsProvider)
	{
		_documentProvider = documentProvider;
		_segmentsProvider = segmentsProvider;
	}

	/// <inheritdoc/>
	public KnownLayer Layer => KnownLayer.Caret;

	/// <inheritdoc/>
	public void Draw(TextView textView, DrawingContext drawingContext)
	{
		IReadOnlyList<TextDiagnosticSegment> segments = _segmentsProvider();

		if (segments.Count == 0)
			return;

		TextDocument? document = _documentProvider();

		foreach (TextDiagnosticSegment segment in segments)
		{
			if (!TextSegmentFactory.TryCreate(document, segment.StartOffset, segment.EndOffset, out TextSegment? textSegment))
				continue;

			foreach (Rect rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, textSegment, false))
			{
				if (rect.Width < 2.0)
					continue;

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
		double baseline = rect.Bottom - 1.0;
		double amplitude = 1.6;
		double step = 4.0;

		var geometry = new StreamGeometry();

		using (StreamGeometryContext context = geometry.Open())
		{
			bool goingUp = true;
			context.BeginFigure(new Point(rect.Left, baseline), false, false);

			for (double x = rect.Left; x < rect.Right; x += step)
			{
				double nextX = Math.Min(x + step / 2.0, rect.Right);
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

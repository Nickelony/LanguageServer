using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.Infrastructure;

namespace Nickelony.IDEKit.AvalonEdit.ChangeMarkers;

/// <summary>
/// A narrow left margin that draws a marker bar beside each visible line reported by a change marker source.
/// </summary>
/// <remarks>
/// The margin queries the source while rendering and does not subscribe to source changes. Hosts must
/// request a redraw after the source's marked lines change.
/// </remarks>
public sealed class ChangeMarkerMargin : AbstractMargin
{
	private static double s_marginWidth = 4.0;

	private static SolidColorBrush s_markerBrush = BrushHelpers.CreateFrozenBrush(Color.FromRgb(0x1E, 0x90, 0xFF));

	private readonly IChangeMarkerSource _markerSource;

	/// <summary>
	/// Gets or sets the width of the margin reserved for change markers. The value is read during
	/// measure, so assign it before the margin is shown for it to take effect.
	/// </summary>
	public static double MarginWidth
	{
		get => s_marginWidth;
		set => s_marginWidth = value;
	}

	/// <summary>
	/// Gets or sets the brush used to draw change markers. Reassigning the brush applies to subsequent drawing.
	/// </summary>
	public static SolidColorBrush MarkerBrush
	{
		get => s_markerBrush;
		set => s_markerBrush = value;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ChangeMarkerMargin"/> class.
	/// </summary>
	/// <param name="markerSource">The source whose marked lines are rendered.</param>
	public ChangeMarkerMargin(IChangeMarkerSource markerSource)
		=> _markerSource = markerSource;

	/// <inheritdoc/>
	protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
	{
		if (oldTextView is not null)
		{
			oldTextView.VisualLinesChanged -= TextView_VisualLinesChanged;
			oldTextView.ScrollOffsetChanged -= TextView_ScrollOffsetChanged;
		}

		base.OnTextViewChanged(oldTextView, newTextView);

		if (newTextView is not null)
		{
			newTextView.VisualLinesChanged += TextView_VisualLinesChanged;
			newTextView.ScrollOffsetChanged += TextView_ScrollOffsetChanged;
		}

		InvalidateVisual();
	}

	/// <inheritdoc/>
	protected override Size MeasureOverride(Size availableSize)
		=> new(MarginWidth, 0.0);

	/// <inheritdoc/>
	protected override void OnRender(DrawingContext drawingContext)
	{
		TextView? textView = TextView;

		if (textView is null || !textView.VisualLinesValid)
			return;

		HashSet<int> markedLineNumbers = CollectMarkedLineNumbers();

		if (markedLineNumbers.Count == 0)
			return;

		double markerWidth = MarginWidth;

		foreach (VisualLine line in textView.VisualLines)
		{
			if (!markedLineNumbers.Contains(line.FirstDocumentLine.LineNumber))
				continue;

			double top = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.TextTop)
				- textView.VerticalOffset;

			drawingContext.DrawRectangle(s_markerBrush, null, new Rect(0.0, top, markerWidth, line.Height));
		}
	}

	private HashSet<int> CollectMarkedLineNumbers()
	{
		var lineNumbers = new HashSet<int>();

		foreach (DocumentLine line in _markerSource.GetMarkedLines())
			lineNumbers.Add(line.LineNumber);

		return lineNumbers;
	}

	private void TextView_VisualLinesChanged(object? sender, EventArgs e)
		=> InvalidateVisual();

	private void TextView_ScrollOffsetChanged(object? sender, EventArgs e)
		=> InvalidateVisual();
}

using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.Infrastructure;
using System.Windows;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.ChangeMarkers;

/// <summary>
/// Displays a marker beside each visible document line returned by an <see cref="IChangeMarkerSource"/>.
/// </summary>
/// <remarks>
/// The source is queried during rendering and is not monitored for changes.
/// Hosts must invalidate the margin when the source's marked lines change.
/// </remarks>
public sealed class ChangeMarkerMargin : AbstractMargin
{
	private static SolidColorBrush s_markerBrush = BrushHelpers.CreateFrozenBrush(Color.FromRgb(0x1E, 0x90, 0xFF));

	private readonly IChangeMarkerSource _markerSource;

	/// <summary>
	/// Gets or sets the width reserved for change markers.
	/// The value is read during measurement. Changing it does not invalidate the margin's layout.
	/// </summary>
	public static double MarginWidth { get; set; } = 4.0;

	/// <summary>
	/// Gets or sets the brush used to draw change markers.
	/// The assigned brush is used by subsequent renders.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
	public static SolidColorBrush MarkerBrush
	{
		get => s_markerBrush;
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			s_markerBrush = value;
		}
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ChangeMarkerMargin"/> class.
	/// </summary>
	/// <param name="markerSource">The source whose marked lines are rendered.</param>
	/// <exception cref="ArgumentNullException"><paramref name="markerSource"/> is <see langword="null"/>.</exception>
	public ChangeMarkerMargin(IChangeMarkerSource markerSource)
	{
		ArgumentNullException.ThrowIfNull(markerSource);
		_markerSource = markerSource;
	}

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

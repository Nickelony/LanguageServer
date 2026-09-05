using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.Infrastructure;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.Bookmarks;

/// <summary>
/// Renders icons for visible bookmarked lines and toggles bookmarks with a left-click.
/// </summary>
/// <remarks>
/// The margin invalidates itself when its text view, visual lines, or scroll offset changes, and after
/// it toggles a bookmark. It does not subscribe to <see cref="BookmarkCoordinator"/> changes, so call
/// <see cref="UIElement.InvalidateVisual"/> after changing bookmarks programmatically.
/// </remarks>
public sealed class BookmarkMargin : AbstractMargin
{
	private const double IconWidth = 10.0;
	private const double IconHeight = 9.0;

	private static Geometry s_iconGeometry = CreateIconGeometry();
	private static SolidColorBrush s_iconBrush = BrushHelpers.CreateFrozenBrush(Color.FromRgb(0xE6, 0xA2, 0x3C));

	private readonly BookmarkCoordinator _bookmarkCoordinator;

	/// <summary>
	/// Gets or sets the width reserved for bookmark icons.
	/// The value is read during measurement. Changing it does not invalidate the margin's layout.
	/// </summary>
	public static double MarginWidth { get; set; } = 16.0;

	/// <summary>
	/// Gets or sets the brush used to draw bookmark icons.
	/// The assigned brush is used by subsequent renders.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
	public static SolidColorBrush IconBrush
	{
		get => s_iconBrush;
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			s_iconBrush = value;
		}
	}

	/// <summary>
	/// Gets or sets the geometry used to draw bookmark icons.
	/// The geometry is not scaled and is centered when its bounds start at (0,0).
	/// The assigned geometry is used by subsequent renders.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
	public static Geometry IconGeometry
	{
		get => s_iconGeometry;
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			s_iconGeometry = value;
		}
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="BookmarkMargin"/> class.
	/// </summary>
	/// <param name="bookmarkCoordinator">The coordinator used to query and toggle bookmarks.</param>
	/// <exception cref="ArgumentNullException"><paramref name="bookmarkCoordinator"/> is <see langword="null"/>.</exception>
	public BookmarkMargin(BookmarkCoordinator bookmarkCoordinator)
	{
		ArgumentNullException.ThrowIfNull(bookmarkCoordinator);
		_bookmarkCoordinator = bookmarkCoordinator;
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

		HashSet<int> bookmarkedLineNumbers = CollectBookmarkedLineNumbers();

		if (bookmarkedLineNumbers.Count == 0)
			return;

		Geometry iconGeometry = IconGeometry;

		double iconWidth = iconGeometry.Bounds.Width;
		double iconHeight = iconGeometry.Bounds.Height;
		double iconLeft = (MarginWidth - iconWidth) / 2.0;

		foreach (VisualLine line in textView.VisualLines)
		{
			if (!bookmarkedLineNumbers.Contains(line.FirstDocumentLine.LineNumber))
				continue;

			double top = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.TextTop)
				- textView.VerticalOffset
				+ ((line.Height - iconHeight) / 2.0);

			drawingContext.PushTransform(new TranslateTransform(iconLeft, top));
			drawingContext.DrawGeometry(s_iconBrush, null, iconGeometry);
			drawingContext.Pop();
		}
	}

	/// <inheritdoc/>
	protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
	{
		base.OnMouseLeftButtonDown(e);

		TextView? textView = TextView;

		if (textView is null || !textView.VisualLinesValid || e.Handled)
			return;

		Point position = e.GetPosition(this);
		VisualLine? line = textView.GetVisualLineFromVisualTop(position.Y + textView.VerticalOffset);

		if (line is null)
			return;

		_bookmarkCoordinator.ToggleBookmark(line.FirstDocumentLine.Offset);

		InvalidateVisual();
		e.Handled = true;
	}

	/// <inheritdoc/>
	protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
		=> new PointHitTestResult(this, hitTestParameters.HitPoint);

	private HashSet<int> CollectBookmarkedLineNumbers()
	{
		var lineNumbers = new HashSet<int>();

		foreach (DocumentLine line in _bookmarkCoordinator.GetBookmarkedLines())
			lineNumbers.Add(line.LineNumber);

		return lineNumbers;
	}

	private void TextView_VisualLinesChanged(object? sender, EventArgs e)
		=> InvalidateVisual();

	private void TextView_ScrollOffsetChanged(object? sender, EventArgs e)
		=> InvalidateVisual();

	private static StreamGeometry CreateIconGeometry()
	{
		var geometry = new StreamGeometry();

		using (StreamGeometryContext context = geometry.Open())
		{
			context.BeginFigure(new Point(0.0, 0.0), true, true);
			context.LineTo(new Point(IconWidth, 0.0), true, false);
			context.LineTo(new Point(IconWidth, IconHeight), true, false);
			context.LineTo(new Point(IconWidth / 2.0, 6.5), true, false);
			context.LineTo(new Point(0.0, IconHeight), true, false);
		}

		geometry.Freeze();
		return geometry;
	}
}

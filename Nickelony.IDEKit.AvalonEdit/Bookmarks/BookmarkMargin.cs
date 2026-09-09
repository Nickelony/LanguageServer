using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using Nickelony.IDEKit.Core.Notifications;
using Nickelony.IDEKit.Infrastructure;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.Bookmarks;

/// <summary>
/// Renders icons for visible bookmarked lines and toggles bookmarks when the left mouse button is pressed.
/// </summary>
/// <remarks>
/// <para>
/// The margin invalidates itself when its text view, visual lines, or scroll offset change and
/// after it toggles a bookmark. A source that implements <see cref="IChangeNotificationSource"/> is
/// followed while the margin is connected to a text view; a source without notifications requires
/// the host to invalidate the margin after its bookmarks change.
/// </para>
/// <para>
/// A left-button click on a visual line's margin row requests a toggle for that line's bookmark
/// through <see cref="OnBookmarkToggleRequested"/>; a click on a row without a visual line (for
/// example past the end of the document) does nothing. The horizontal click position is not
/// inspected.
/// </para>
/// <para>
/// The icon and the reserved width scale with the margin's font size.
/// </para>
/// <para>
/// The icon drawing is sample behavior and replaceable: assign <see cref="LineStatusIconMarginBase.IconBrush"/> and
/// <see cref="LineStatusIconMarginBase.IconGeometry"/> (the defaults are a frozen amber bookmark icon), or override
/// <see cref="LineStatusIconMarginBase.DrawMarker"/> in a derived margin. The toggle
/// decision is delegated to <see cref="OnBookmarkToggleRequested"/>, whose default toggles through
/// the source; a derived margin can override it to add modifier keys, confirmation, or a
/// context-menu route.
/// </para>
/// </remarks>
public class BookmarkMargin : LineStatusIconMarginBase
{
	private const double IconWidth = 10.0;
	private const double IconHeight = 9.0;

	private static readonly Geometry s_defaultIconGeometry = CreateIconGeometry();
	private static readonly SolidColorBrush s_defaultIconBrush = BrushHelpers.CreateFrozenBrush(Color.FromRgb(0xE6, 0xA2, 0x3C));

	static BookmarkMargin()
	{
		IconBrushProperty.OverrideMetadata(
			typeof(BookmarkMargin),
			new FrameworkPropertyMetadata(s_defaultIconBrush, FrameworkPropertyMetadataOptions.AffectsRender));
		IconGeometryProperty.OverrideMetadata(
			typeof(BookmarkMargin),
			new FrameworkPropertyMetadata(s_defaultIconGeometry, FrameworkPropertyMetadataOptions.AffectsRender));
	}

	private readonly IBookmarkSource _bookmarkSource;

	/// <summary>
	/// Initializes a new instance of the <see cref="BookmarkMargin"/> class.
	/// </summary>
	/// <param name="bookmarkSource">The source used to query and toggle bookmarks.</param>
	/// <exception cref="ArgumentNullException"><paramref name="bookmarkSource"/> is <see langword="null"/>.</exception>
	public BookmarkMargin(IBookmarkSource bookmarkSource)
	{
		ArgumentNullException.ThrowIfNull(bookmarkSource);
		_bookmarkSource = bookmarkSource;

		if (bookmarkSource is IChangeNotificationSource notifyingSource)
			SetNotifyingSource(notifyingSource);
	}

	/// <inheritdoc/>
	protected override IReadOnlyList<int> GetMarkedLineNumbers()
		=> _bookmarkSource.GetMarkedLineNumbers();

	/// <inheritdoc/>
	protected override bool OnIconClicked(VisualLine visualLine, Point position)
	{
		if (!OnBookmarkToggleRequested(visualLine.FirstDocumentLine.Offset))
			return false;

		// The toggle changed the marked lines, so the margin repaints itself.
		InvalidateVisual();
		return true;
	}

	/// <inheritdoc/>
	protected override Point ResolveClickPosition(MouseButtonEventArgs e)
		=> ClickPositionResolver is Func<MouseButtonEventArgs, Point> resolver ? resolver(e) : base.ResolveClickPosition(e);

	/// <summary>
	/// Gets or sets an optional resolver for the click position used by
	/// <see cref="LineStatusIconMarginBase.OnMouseLeftButtonDown(MouseButtonEventArgs)"/>, instead of the event's
	/// own position.
	/// </summary>
	/// <remarks>
	/// A test seam: a synthetic mouse event cannot carry a position, so a test that exercises the routed
	/// click path sets this resolver to a deterministic point.
	/// </remarks>
	internal Func<MouseButtonEventArgs, Point>? ClickPositionResolver { get; set; }

	/// <summary>
	/// Resolves the line at the specified margin-relative position and requests a bookmark toggle for it
	/// through <see cref="OnBookmarkToggleRequested"/>.
	/// </summary>
	/// <param name="position">The position in the margin's coordinate space.</param>
	/// <returns>
	/// <see langword="true"/> when a line was found and the toggle request was handled; otherwise,
	/// <see langword="false"/>.
	/// </returns>
	internal bool TryToggleBookmarkAt(Point position)
		=> TryGetVisualLineAt(position, out VisualLine? visualLine)
			&& OnBookmarkToggleRequested(visualLine.FirstDocumentLine.Offset);

	/// <summary>
	/// Requests a bookmark toggle for the document line at the supplied offset and reports whether the
	/// request was handled.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The default toggles through <see cref="IBookmarkSource.ToggleBookmark"/> and returns
	/// <see langword="true"/>, so the margin invalidates itself and marks the click handled. A derived
	/// margin can override this member to apply a different gesture policy (modifier keys,
	/// confirmation, or a context-menu route); returning <see langword="false"/> leaves the click
	/// unhandled and the margin uninvalidated.
	/// </para>
	/// <para>
	/// The member is called only for a click that resolved to a visual line, so
	/// <paramref name="lineOffset"/> is the first document line offset of the clicked line.
	/// </para>
	/// </remarks>
	/// <param name="lineOffset">The zero-based offset of the clicked line.</param>
	/// <returns><see langword="true"/> when the toggle was performed; otherwise, <see langword="false"/>.</returns>
	protected virtual bool OnBookmarkToggleRequested(int lineOffset)
	{
		_bookmarkSource.ToggleBookmark(lineOffset);
		return true;
	}

	private static StreamGeometry CreateIconGeometry()
	{
		var geometry = new StreamGeometry();

		using (StreamGeometryContext context = geometry.Open())
		{
			context.BeginFigure(new Point(0.0, 0.0), true, true);
			context.LineTo(new Point(IconWidth, 0.0), true, false);
			context.LineTo(new Point(IconWidth, IconHeight), true, false);

			// The notch vertex sits 2.5 DIP above the icon's bottom edge.
			context.LineTo(new Point(IconWidth / 2.0, 6.5), true, false);
			context.LineTo(new Point(0.0, IconHeight), true, false);
		}

		geometry.Freeze();
		return geometry;
	}
}

using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Bookmarks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

[STATestClass]
public sealed class BookmarkMarginTests
{
	[TestMethod]
	public void MeasureOverride_ReservesDefaultWidth()
	{
		var margin = CreateMargin(new TextDocument("one\r\ntwo"));

		WPFTestHost.PinDesignFontSize(margin);
		margin.Measure(new Size(100.0, 100.0));

		Assert.AreEqual(16.0, margin.DesiredSize.Width);
	}

	[TestMethod]
	public void MarginWidth_NegativeNaNOrInfinity_IsRejectedWithoutChangingMeasure()
	{
		BookmarkMargin margin = CreateMargin(new TextDocument("one\r\ntwo"));

		Assert.ThrowsExactly<ArgumentException>(() => margin.MarginWidth = -4.0);
		Assert.ThrowsExactly<ArgumentException>(() => margin.MarginWidth = double.NaN);
		Assert.ThrowsExactly<ArgumentException>(() => margin.MarginWidth = double.PositiveInfinity);
		Assert.AreEqual(16.0, margin.MarginWidth);

		WPFTestHost.PinDesignFontSize(margin);
		margin.Measure(new Size(100.0, 100.0));
		Assert.AreEqual(16.0, margin.DesiredSize.Width);
	}

	[TestMethod]
	public void IconBrushAndGeometry_NullAssignments_AreRejected()
	{
		BookmarkMargin margin = CreateMargin(new TextDocument("one\r\ntwo"));

		Assert.ThrowsExactly<ArgumentNullException>(() => margin.IconBrush = null!);
		Assert.ThrowsExactly<ArgumentNullException>(() => margin.IconGeometry = null!);

		// XAML and SetValue assignments bypass the CLR property setter and hit the DP validation callback.
		Assert.ThrowsExactly<ArgumentException>(() => margin.SetValue(BookmarkMargin.IconBrushProperty, null!));
		Assert.ThrowsExactly<ArgumentException>(() => margin.SetValue(BookmarkMargin.IconGeometryProperty, null!));
	}

	[TestMethod]
	public void MarginWidth_Change_IsHonoredByMeasureOverride()
	{
		BookmarkMargin margin = CreateMargin(new TextDocument("one\r\ntwo"));

		margin.MarginWidth = 24.0;

		WPFTestHost.PinDesignFontSize(margin);
		margin.Measure(new Size(100.0, 100.0));

		Assert.AreEqual(24.0, margin.DesiredSize.Width);
	}

	[TestMethod]
	public void MarginWidthProperty_DefaultAndMetadata()
	{
		Assert.AreEqual(16.0, BookmarkMargin.MarginWidthProperty.DefaultMetadata.DefaultValue);

		var metadata = (FrameworkPropertyMetadata)BookmarkMargin.MarginWidthProperty.GetMetadata(typeof(BookmarkMargin));

		Assert.IsTrue(metadata.AffectsMeasure);
		Assert.IsFalse(metadata.AffectsRender);
	}

	[TestMethod]
	public void IconBrushProperty_DefaultIsFrozenAmber_AffectsRender()
	{
		var metadata = (FrameworkPropertyMetadata)BookmarkMargin.IconBrushProperty.GetMetadata(typeof(BookmarkMargin));
		var defaultValue = (SolidColorBrush)metadata.DefaultValue;

		Assert.AreEqual(Color.FromRgb(0xE6, 0xA2, 0x3C), defaultValue.Color);
		Assert.IsTrue(defaultValue.IsFrozen);

		Assert.IsTrue(metadata.AffectsRender);
		Assert.IsFalse(metadata.AffectsMeasure);
	}

	[TestMethod]
	public void IconGeometryProperty_DefaultIsFrozenIcon()
	{
		var metadata = (FrameworkPropertyMetadata)BookmarkMargin.IconGeometryProperty.GetMetadata(typeof(BookmarkMargin));
		var defaultValue = (Geometry)metadata.DefaultValue;

		Assert.IsNotNull(defaultValue);
		Assert.IsTrue(defaultValue.IsFrozen);
		Assert.IsGreaterThan(0.0, defaultValue.Bounds.Width);
		Assert.IsGreaterThan(0.0, defaultValue.Bounds.Height);
	}

	[TestMethod]
	public void MarginWidth_Change_InvalidatesMeasure()
	{
		BookmarkMargin margin = CreateMargin(new TextDocument("one\r\ntwo"));

		margin.Measure(new Size(100.0, 100.0));
		Assert.IsTrue(margin.IsMeasureValid);

		margin.MarginWidth = 24.0;

		Assert.IsFalse(margin.IsMeasureValid);
	}

	[TestMethod]
	public void TryToggleBookmarkAt_ClickOnMarginRow_TogglesBookmarkForThatLine()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);
		var margin = new BookmarkMargin(coordinator);
		var editor = new TextEditor { Document = document };

		using HostWindow hostWindow = WPFTestHost.ShowEditorWithMargin(editor, margin);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		VisualLine secondLine = editor.TextArea.TextView.VisualLines
			.First(line => line.FirstDocumentLine.LineNumber == 2);

		double clickY = secondLine.GetTextLineVisualYPosition(secondLine.TextLines[0], VisualYPosition.TextTop) + 2.0;

		bool toggled = margin.TryToggleBookmarkAt(new Point(4.0, clickY));

		Assert.IsTrue(toggled);
		Assert.HasCount(1, coordinator.GetMarkedLineNumbers());
		Assert.AreEqual(2, coordinator.GetMarkedLineNumbers()[0]);

		// A second click on the same row removes the bookmark again.
		bool toggledOff = margin.TryToggleBookmarkAt(new Point(4.0, clickY));

		Assert.IsTrue(toggledOff);
		Assert.IsEmpty(coordinator.GetMarkedLineNumbers());
	}

	[TestMethod]
	public void TryToggleBookmarkAt_ClickOutsideVisualLines_TogglesNothing()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);
		var margin = new BookmarkMargin(coordinator);
		var editor = new TextEditor { Document = document };

		using HostWindow hostWindow = WPFTestHost.ShowEditorWithMargin(editor, margin);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		bool toggled = margin.TryToggleBookmarkAt(new Point(4.0, 100000.0));

		Assert.IsFalse(toggled);
		Assert.IsEmpty(coordinator.GetMarkedLineNumbers());
	}

	[TestMethod]
	public void HitTest_InsideMargin_ReturnsTheMarginAsTheHitTarget()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);
		var margin = new BookmarkMargin(coordinator);
		var editor = new TextEditor { Document = document };

		using HostWindow hostWindow = WPFTestHost.ShowEditorWithMargin(editor, margin);

		margin.UpdateLayout();

		// The margin accepts hit tests across its whole surface so a click on any row reaches it.
		IInputElement? hitElement = margin.InputHitTest(new Point(4.0, 4.0));

		Assert.AreSame(margin, hitElement);
	}

	[TestMethod]
	public void OnRender_DrawsIconInMarginForBookmarkedLine()
	{
		var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("line", 20)));
		var coordinator = new BookmarkCoordinator(() => document);
		var margin = new BookmarkMargin(coordinator);
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		coordinator.ToggleBookmark(document.GetLineByNumber(1).Offset);

		using HostWindow hostWindow = WPFTestHost.ShowEditorWithMargin(editor, margin);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		BitmapSource bitmap = TestBitmapRendering.RenderToBitmap(editor);

		Assert.IsTrue(
			TestBitmapRendering.HasColorInLeftStrip(bitmap, 16, Color.FromRgb(0xE6, 0xA2, 0x3C)),
			"Expected an amber bookmark icon in the margin strip.");
	}

	[TestMethod]
	public void OnRender_DrawsNothingWithoutBookmarks()
	{
		var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("line", 20)));
		var coordinator = new BookmarkCoordinator(() => document);
		var margin = new BookmarkMargin(coordinator);
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using HostWindow hostWindow = WPFTestHost.ShowEditorWithMargin(editor, margin);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		BitmapSource bitmap = TestBitmapRendering.RenderToBitmap(editor);

		Assert.IsFalse(TestBitmapRendering.HasColorInLeftStrip(bitmap, 16, Color.FromRgb(0xE6, 0xA2, 0x3C)),
			"Expected no amber pixels in the margin strip without bookmarks.");
	}

	[TestMethod]
	public void SourceSubscription_FollowsTextViewConnection()
	{
		var document = new TextDocument("one\r\ntwo");
		var coordinator = new BookmarkCoordinator(() => document);
		var margin = new BookmarkMargin(coordinator);
		var editor = new TextEditor { Document = document };

		editor.TextArea.LeftMargins.Add(margin);

		Assert.IsTrue(margin.IsSourceSubscribed);

		editor.TextArea.LeftMargins.Remove(margin);

		Assert.IsFalse(margin.IsSourceSubscribed);
	}

	[TestMethod]
	public void DesiredWidth_ScalesWithTextViewFontSize()
	{
		var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("line", 20)));
		var coordinator = new BookmarkCoordinator(() => document);
		var margin = new BookmarkMargin(coordinator);
		var editor = new TextEditor { Document = document, FontSize = 24.0 };

		using HostWindow hostWindow = WPFTestHost.ShowEditorWithMargin(editor, margin);

		editor.UpdateLayout();

		// The gutter is authored for the 12-DIP design font and scales with the font size.
		Assert.AreEqual(16.0 * 24.0 / 12.0, margin.DesiredSize.Width, 0.5);

		editor.FontSize = 8.0;
		editor.UpdateLayout();

		Assert.AreEqual(16.0 * 8.0 / 12.0, margin.DesiredSize.Width, 0.5);
	}

	[TestMethod]
	public void OnRender_IconBounds_ScaleWithTextViewFontSize()
	{
		(int MinColumn, int MaxColumn) RenderSingleIconBounds(double fontSize)
		{
			var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("line", 20)));
			var coordinator = new BookmarkCoordinator(() => document);
			var margin = new BookmarkMargin(coordinator);
			var editor = new TextEditor { Document = document, FontSize = fontSize };

			coordinator.ToggleBookmark(document.GetLineByNumber(1).Offset);

			using HostWindow hostWindow = WPFTestHost.ShowEditorWithMargin(editor, margin);

			Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

			BitmapSource bitmap = TestBitmapRendering.RenderToBitmap(editor);

			bool found = TestBitmapRendering.TryGetColorColumnBounds(
				bitmap,
				(int)Math.Ceiling(margin.ActualWidth),
				Color.FromRgb(0xE6, 0xA2, 0x3C),
				out int minColumn,
				out int maxColumn);

			Assert.IsTrue(found, "Expected an amber bookmark icon in the margin.");
			return (minColumn, maxColumn);
		}

		(int MinColumn, int MaxColumn) smallIcon = RenderSingleIconBounds(8.0);
		(int MinColumn, int MaxColumn) largeIcon = RenderSingleIconBounds(24.0);

		int smallIconWidth = smallIcon.MaxColumn - smallIcon.MinColumn;
		int largeIconWidth = largeIcon.MaxColumn - largeIcon.MinColumn;

		Assert.IsGreaterThan(0, smallIconWidth);
		Assert.IsGreaterThan(0, largeIconWidth);

		Assert.IsLessThan(10, smallIconWidth, "Expected a small-font icon narrower than the authored 10-DIP icon.");
		Assert.IsGreaterThan(10, largeIconWidth, "Expected a large-font icon wider than the authored 10-DIP icon.");

		Assert.IsGreaterThan(smallIconWidth, largeIconWidth);
	}

	[TestMethod]
	public void OnRender_SmallFont_AdjacentBookmarkIconsDoNotOverlap()
	{
		var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("line", 20)));
		var coordinator = new BookmarkCoordinator(() => document);
		var margin = new BookmarkMargin(coordinator);
		var editor = new TextEditor { Document = document, FontSize = 6.0 };

		coordinator.ToggleBookmark(document.GetLineByNumber(1).Offset);
		coordinator.ToggleBookmark(document.GetLineByNumber(2).Offset);

		using HostWindow hostWindow = WPFTestHost.ShowEditorWithMargin(editor, margin);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		double lineHeight = editor.TextArea.TextView.DefaultLineHeight;

		// The premise: at this font size a line is shorter than the authored 9-DIP-tall
		// icon, so an unscaled icon would overlap the neighboring line's icon.
		Assert.IsLessThan(9.0, lineHeight, "The test font must produce lines shorter than the authored icon.");

		BitmapSource bitmap = TestBitmapRendering.RenderToBitmap(editor);

		int segments = TestBitmapRendering.CountColorRowSegments(
			bitmap,
			(int)Math.Ceiling(margin.ActualWidth),
			Color.FromRgb(0xE6, 0xA2, 0x3C));

		Assert.AreEqual(2, segments, "Expected one separated icon segment per bookmarked line.");
	}

	[TestMethod]
	public void OnRender_WrappedMarkedLine_DrawsASingleIcon()
	{
		// A single document line long enough to wrap several times at the hosted width.
		var document = new TextDocument(string.Join(" ", Enumerable.Repeat("word", 300)));
		var coordinator = new BookmarkCoordinator(() => document);
		var margin = new BookmarkMargin(coordinator);
		var editor = new TextEditor { Document = document, FontSize = 12.0, WordWrap = true };

		coordinator.ToggleBookmark(document.GetLineByNumber(1).Offset);

		using HostWindow hostWindow = WPFTestHost.ShowEditorWithMargin(editor, margin);

		IReadOnlyList<VisualLine> visualLines = editor.TextArea.TextView.VisualLines;

		// The premise: word wrap keeps the document line as one visual line made of several
		// text lines, so the icon must not repeat per wrapped segment.
		Assert.HasCount(1, visualLines);
		Assert.IsGreaterThan(1, visualLines[0].TextLines.Count);

		BitmapSource bitmap = TestBitmapRendering.RenderToBitmap(editor);

		int segments = TestBitmapRendering.CountColorRowSegments(
			bitmap,
			(int)Math.Ceiling(margin.ActualWidth),
			Color.FromRgb(0xE6, 0xA2, 0x3C));

		// The icon is drawn once and centered on the wrapped line instead of repeating per segment.
		Assert.AreEqual(1, segments, "Expected a single bookmark icon for the wrapped line.");
	}

	[TestMethod]
	public void SourceChange_AfterDispatcherPump_RepaintsTheMarker()
	{
		var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("line", 20)));
		var coordinator = new BookmarkCoordinator(() => document);
		var margin = new BookmarkMargin(coordinator);
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using HostWindow hostWindow = WPFTestHost.ShowEditorWithMargin(editor, margin);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		// The bookmark is added while the margin is connected, so only the source notification
		// can invalidate the margin; the queued invalidation must run on the margin's dispatcher.
		coordinator.ToggleBookmark(document.GetLineByNumber(1).Offset);

		BitmapSource bitmap = TestBitmapRendering.PumpAndRender(editor);

		Assert.IsTrue(
			TestBitmapRendering.HasColorInLeftStrip(bitmap, 16, Color.FromRgb(0xE6, 0xA2, 0x3C)),
			"Expected the notified margin to repaint the new bookmark icon.");
	}

	[TestMethod]
	public void CustomBookmarkSource_DrivesRendering()
	{
		var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("line", 20)));
		var source = new FixedLineStatusSource(() => document, [1]);
		var margin = new BookmarkMargin(source);
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using HostWindow hostWindow = WPFTestHost.ShowEditorWithMargin(editor, margin);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		BitmapSource bitmap = TestBitmapRendering.RenderToBitmap(editor);

		Assert.IsTrue(
			TestBitmapRendering.HasColorInLeftStrip(bitmap, 16, Color.FromRgb(0xE6, 0xA2, 0x3C)),
			"Expected an amber bookmark icon in the margin strip.");
	}

	[TestMethod]
	public void TryToggleBookmarkAt_CustomSource_TogglesThroughSource()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var source = new FixedLineStatusSource(() => document, []);
		var margin = new BookmarkMargin(source);
		var editor = new TextEditor { Document = document };

		using HostWindow hostWindow = WPFTestHost.ShowEditorWithMargin(editor, margin);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		VisualLine secondLine = editor.TextArea.TextView.VisualLines
			.First(line => line.FirstDocumentLine.LineNumber == 2);

		double clickY = secondLine.GetTextLineVisualYPosition(secondLine.TextLines[0], VisualYPosition.TextTop) + 2.0;

		bool toggled = margin.TryToggleBookmarkAt(new Point(4.0, clickY));

		Assert.IsTrue(toggled);
		Assert.AreEqual(1, source.ToggleCalls);
		Assert.AreEqual(2, source.ToggledLineNumber);
	}

	[TestMethod]
	public void OnRender_CustomIconBrushAndGeometry_AreUsed()
	{
		var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("line", 20)));
		var coordinator = new BookmarkCoordinator(() => document);

		var margin = new BookmarkMargin(coordinator)
		{
			IconBrush = new SolidColorBrush(Colors.Red),
			IconGeometry = new RectangleGeometry(new Rect(0.0, 0.0, 10.0, 9.0))
		};

		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		coordinator.ToggleBookmark(document.GetLineByNumber(1).Offset);

		using HostWindow hostWindow = WPFTestHost.ShowEditorWithMargin(editor, margin);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		BitmapSource bitmap = TestBitmapRendering.RenderToBitmap(editor);

		Assert.IsTrue(
			TestBitmapRendering.HasColorInLeftStrip(bitmap, 16, Colors.Red),
			"Expected the custom red brush to paint the custom icon geometry.");

		Assert.IsFalse(
			TestBitmapRendering.HasColorInLeftStrip(bitmap, 16, Color.FromRgb(0xE6, 0xA2, 0x3C)),
			"Expected the default amber icon to be replaced.");
	}

	[TestMethod]
	public void OnMouseLeftButtonDown_UnattachedMargin_DoesNotToggle()
	{
		var document = new TextDocument("one\r\ntwo");
		var coordinator = new BookmarkCoordinator(() => document);
		var margin = new BookmarkMargin(coordinator);

		var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
		{
			RoutedEvent = UIElement.MouseLeftButtonDownEvent
		};

		margin.RaiseEvent(args);

		Assert.IsFalse(args.Handled);
		Assert.IsEmpty(coordinator.GetMarkedLineNumbers());
	}

	[TestMethod]
	public void OnMouseLeftButtonDown_WithoutValidVisualLines_DoesNotToggle()
	{
		var document = new TextDocument("one\r\ntwo");
		var coordinator = new BookmarkCoordinator(() => document);
		var margin = new BookmarkMargin(coordinator);
		var editor = new TextEditor { Document = document };

		// The editor is never hosted or laid out, so the text view has no valid visual lines yet.
		editor.TextArea.LeftMargins.Add(margin);

		var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
		{
			RoutedEvent = UIElement.MouseLeftButtonDownEvent
		};

		margin.RaiseEvent(args);

		Assert.IsFalse(args.Handled);
		Assert.IsEmpty(coordinator.GetMarkedLineNumbers());
	}

	[TestMethod]
	public void OnMouseLeftButtonDown_ClickOnMarginRow_TogglesBookmarkAndHandlesEvent()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);
		var margin = new BookmarkMargin(coordinator);
		var editor = new TextEditor { Document = document };

		using HostWindow hostWindow = WPFTestHost.ShowEditorWithMargin(editor, margin);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		VisualLine secondLine = editor.TextArea.TextView.VisualLines
			.First(line => line.FirstDocumentLine.LineNumber == 2);

		double clickY = secondLine.GetTextLineVisualYPosition(secondLine.TextLines[0], VisualYPosition.TextTop)
			+ (secondLine.Height / 2.0);

		// A synthetic mouse event cannot carry a position, so the margin's click seam supplies one
		// deterministically instead of moving the host window under the physical pointer.
		margin.ClickPositionResolver = _ => new Point(4.0, clickY);

		var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
		{
			RoutedEvent = UIElement.MouseLeftButtonDownEvent
		};

		margin.RaiseEvent(args);

		// The successful path toggles through the source, invalidates the margin, and handles the event.
		Assert.IsTrue(args.Handled);
		Assert.HasCount(1, coordinator.GetMarkedLineNumbers());
		Assert.AreEqual(2, coordinator.GetMarkedLineNumbers()[0]);
	}

	[TestMethod]
	public void DrawMarker_UnarrangedMargin_CentersTheIconInTheAuthoredWidth()
	{
		var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("line", 20)));
		var margin = new BookmarkMargin(new BookmarkCoordinator(() => document));

		WPFTestHost.PinDesignFontSize(margin);

		// The unarranged margin has no actual width, so the authored fallback width must be used.
		Assert.AreEqual(0.0, margin.ActualWidth);

		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		VisualLine firstLine = editor.TextArea.TextView.VisualLines[0];
		var visual = new DrawingVisual();

		using (DrawingContext drawingContext = visual.RenderOpen())
			margin.DrawMarker(drawingContext, firstLine, 0.0);

		DrawingGroup? drawing = visual.Drawing;

		Assert.IsNotNull(drawing);

		// The 10-DIP icon is centered in the 16-DIP authored width: (16 - 10) / 2 = 3.
		Assert.AreEqual(3.0, drawing.Bounds.X, 0.001);
		Assert.AreEqual(10.0, drawing.Bounds.Width, 0.001);
	}

	[TestMethod]
	public void OnBookmarkToggleRequested_Override_ControlsTheToggleAndTheClickHandling()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);
		var margin = new HookMargin(coordinator);
		var editor = new TextEditor { Document = document };

		using HostWindow hostWindow = WPFTestHost.ShowEditorWithMargin(editor, margin);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		VisualLine secondLine = editor.TextArea.TextView.VisualLines
			.First(line => line.FirstDocumentLine.LineNumber == 2);

		double clickY = secondLine.GetTextLineVisualYPosition(secondLine.TextLines[0], VisualYPosition.TextTop) + 2.0;

		margin.ClickPositionResolver = _ => new Point(4.0, clickY);

		// The override vetoes the toggle: the click resolves to line 2 but stays unhandled, and nothing
		// is bookmarked.
		margin.HookResult = false;

		var vetoedArgs = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
		{
			RoutedEvent = UIElement.MouseLeftButtonDownEvent
		};

		margin.RaiseEvent(vetoedArgs);

		Assert.AreEqual(secondLine.FirstDocumentLine.Offset, margin.LastRequestedOffset);
		Assert.IsFalse(vetoedArgs.Handled);
		Assert.IsEmpty(coordinator.GetMarkedLineNumbers());

		// The passthrough to the default implementation toggles through the source and reports the
		// request as handled.
		margin.HookResult = true;

		bool toggled = margin.TryToggleBookmarkAt(new Point(4.0, clickY));

		Assert.IsTrue(toggled);
		Assert.AreEqual(secondLine.FirstDocumentLine.Offset, margin.LastRequestedOffset);
		Assert.HasCount(1, coordinator.GetMarkedLineNumbers());
		Assert.AreEqual(2, coordinator.GetMarkedLineNumbers()[0]);
	}

	private static BookmarkMargin CreateMargin(TextDocument document)
		=> new(new BookmarkCoordinator(() => document));

	/// <summary>
	/// A bookmark margin whose toggle hook records the requested line offset and can veto the toggle
	/// without calling the default implementation.
	/// </summary>
	private sealed class HookMargin : BookmarkMargin
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="HookMargin"/> class.
		/// </summary>
		/// <param name="bookmarkSource">The source used to query and toggle bookmarks.</param>
		public HookMargin(IBookmarkSource bookmarkSource)
			: base(bookmarkSource)
		{ }

		/// <summary>
		/// Gets or sets a value indicating whether the override delegates to the default toggle.
		/// </summary>
		public bool HookResult { get; set; } = true;

		/// <summary>
		/// Gets the line offset of the last toggle request.
		/// </summary>
		public int? LastRequestedOffset { get; private set; }

		/// <inheritdoc/>
		protected override bool OnBookmarkToggleRequested(int lineOffset)
		{
			LastRequestedOffset = lineOffset;
			return HookResult && base.OnBookmarkToggleRequested(lineOffset);
		}
	}
}

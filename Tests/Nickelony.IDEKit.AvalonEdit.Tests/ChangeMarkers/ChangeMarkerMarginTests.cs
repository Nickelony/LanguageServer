using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.ChangeMarkers;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using Nickelony.IDEKit.Core.LineStatus;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

[STATestClass]
public sealed class ChangeMarkerMarginTests
{
	[TestMethod]
	public void MeasureOverride_ReservesMarkerDefaultWidth()
	{
		ChangeMarkerMargin margin = CreateMargin();

		WPFTestHost.PinDesignFontSize(margin);
		margin.Measure(new Size(100.0, 100.0));

		Assert.AreEqual(4.0, margin.DesiredSize.Width);
	}

	[TestMethod]
	public void MarkerBrush_NullAssignments_AreRejected()
	{
		ChangeMarkerMargin margin = CreateMargin();

		Assert.ThrowsExactly<ArgumentNullException>(() => margin.MarkerBrush = null!);

		// XAML and SetValue assignments bypass the CLR property setter and hit the DP validation callback.
		Assert.ThrowsExactly<ArgumentException>(() => margin.SetValue(ChangeMarkerMargin.MarkerBrushProperty, null!));
	}

	[TestMethod]
	public void MarginWidth_InvalidValues_AreRejectedWithoutChangingMeasure()
	{
		ChangeMarkerMargin margin = CreateMargin();

		Assert.ThrowsExactly<ArgumentException>(() => margin.MarginWidth = -4.0);
		Assert.ThrowsExactly<ArgumentException>(() => margin.MarginWidth = double.NaN);
		Assert.ThrowsExactly<ArgumentException>(() => margin.MarginWidth = double.PositiveInfinity);
		Assert.AreEqual(4.0, margin.MarginWidth);

		WPFTestHost.PinDesignFontSize(margin);
		margin.Measure(new Size(100.0, 100.0));
		Assert.AreEqual(4.0, margin.DesiredSize.Width);
	}

	[TestMethod]
	public void MarginWidth_Change_IsHonoredByMarkerMeasureOverride()
	{
		ChangeMarkerMargin margin = CreateMargin();

		margin.MarginWidth = 6.0;

		WPFTestHost.PinDesignFontSize(margin);
		margin.Measure(new Size(100.0, 100.0));

		Assert.AreEqual(6.0, margin.DesiredSize.Width);
	}

	[TestMethod]
	public void MarginWidthProperty_EffectiveDefaultIsFourDip_BaseRegistrationIsSixteenDip()
	{
		// The property is registered on LineStatusMarginBase; ChangeMarkerMargin overrides its default value.
		Assert.AreEqual(16.0, LineStatusMarginBase.MarginWidthProperty.DefaultMetadata.DefaultValue);

		var metadata = (FrameworkPropertyMetadata)ChangeMarkerMargin.MarginWidthProperty.GetMetadata(typeof(ChangeMarkerMargin));

		Assert.AreEqual(4.0, metadata.DefaultValue);
		Assert.IsTrue(metadata.AffectsMeasure);
		Assert.IsFalse(metadata.AffectsRender);

		// The effective default on a margin instance is the overridden value.
		Assert.AreEqual(4.0, CreateMargin().MarginWidth);
	}

	[TestMethod]
	public void MarkerBrushProperty_DefaultIsFrozenBlue_AffectsRender()
	{
		var defaultValue = (SolidColorBrush)ChangeMarkerMargin.MarkerBrushProperty.DefaultMetadata.DefaultValue;

		Assert.AreEqual(Color.FromRgb(0x1E, 0x90, 0xFF), defaultValue.Color);
		Assert.IsTrue(defaultValue.IsFrozen);

		var metadata = (FrameworkPropertyMetadata)ChangeMarkerMargin.MarkerBrushProperty.GetMetadata(typeof(ChangeMarkerMargin));

		Assert.IsTrue(metadata.AffectsRender);
		Assert.IsFalse(metadata.AffectsMeasure);
	}

	[TestMethod]
	public void MarginWidth_Change_InvalidatesMarkerMeasure()
	{
		ChangeMarkerMargin margin = CreateMargin();

		margin.Measure(new Size(100.0, 100.0));
		Assert.IsTrue(margin.IsMeasureValid);

		margin.MarginWidth = 6.0;

		Assert.IsFalse(margin.IsMeasureValid);
	}

	[TestMethod]
	public void OnRender_DrawsMarkerInMarginForMarkedLine()
	{
		var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("line", 20)));
		var margin = new ChangeMarkerMargin(new FixedLineStatusSource(() => document, [1], notifiesChanges: false));
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using HostWindow hostWindow = WPFTestHost.ShowEditorWithMargin(editor, margin);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		BitmapSource bitmap = TestBitmapRendering.RenderToBitmap(editor);

		Assert.IsTrue(
			TestBitmapRendering.HasColorInLeftStrip(bitmap, 4, Color.FromRgb(0x1E, 0x90, 0xFF)),
			"Expected a blue change marker in the margin strip.");
	}

	[TestMethod]
	public void OnRender_DrawsNothingWithoutMarkedLines()
	{
		var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("line", 20)));
		var margin = new ChangeMarkerMargin(new EmptyMarkerSource());
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using HostWindow hostWindow = WPFTestHost.ShowEditorWithMargin(editor, margin);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		BitmapSource bitmap = TestBitmapRendering.RenderToBitmap(editor);

		Assert.IsFalse(
			TestBitmapRendering.HasColorInLeftStrip(bitmap, 4, Color.FromRgb(0x1E, 0x90, 0xFF)),
			"Expected no blue pixels in the margin strip without marked lines.");
	}

	[TestMethod]
	public void OnRender_WrappedMarkedLine_BarSpansTheWholeWrappedLine()
	{
		// A single document line long enough to wrap several times at the hosted width.
		var document = new TextDocument(string.Join(" ", Enumerable.Repeat("word", 300)));
		var tracker = new UnsavedChangesTracker(() => document);
		var margin = new ChangeMarkerMargin(tracker);
		var editor = new TextEditor { Document = document, FontSize = 12.0, WordWrap = true };

		using HostWindow hostWindow = WPFTestHost.ShowEditorWithMargin(editor, margin);

		IReadOnlyList<VisualLine> visualLines = editor.TextArea.TextView.VisualLines;

		// The premise: word wrap keeps the document line as one visual line made of several
		// text lines; the empty baseline marks the single line.
		Assert.HasCount(1, visualLines);
		Assert.IsGreaterThan(1, visualLines[0].TextLines.Count);

		BitmapSource bitmap = TestBitmapRendering.RenderToBitmap(editor);

		bool found = TestBitmapRendering.TryGetColorRowBounds(
			bitmap,
			(int)Math.Ceiling(margin.ActualWidth),
			Color.FromRgb(0x1E, 0x90, 0xFF),
			out int minRow,
			out int maxRow);

		Assert.IsTrue(found, "Expected a blue change marker in the margin strip.");

		// The bar covers the full wrapped height instead of a single text line's height.
		Assert.IsGreaterThan(editor.TextArea.TextView.DefaultLineHeight, maxRow - minRow + 1);

		int segments = TestBitmapRendering.CountColorRowSegments(
			bitmap,
			(int)Math.Ceiling(margin.ActualWidth),
			Color.FromRgb(0x1E, 0x90, 0xFF));

		Assert.AreEqual(1, segments, "Expected one continuous change bar for the wrapped line.");
	}

	[TestMethod]
	public void IsSourceSubscribed_FollowsTextViewConnection()
	{
		var document = new TextDocument("one\r\ntwo");
		var tracker = new UnsavedChangesTracker(() => document);
		var margin = new ChangeMarkerMargin(tracker);
		var editor = new TextEditor { Document = document };

		editor.TextArea.LeftMargins.Add(margin);

		Assert.IsTrue(margin.IsSourceSubscribed);

		editor.TextArea.LeftMargins.Remove(margin);

		Assert.IsFalse(margin.IsSourceSubscribed);
	}

	[TestMethod]
	public void IsSourceSubscribed_NonNotifyingSource_IsFalse()
	{
		var document = new TextDocument("one\r\ntwo");
		var margin = new ChangeMarkerMargin(new EmptyMarkerSource());
		var editor = new TextEditor { Document = document };

		editor.TextArea.LeftMargins.Add(margin);

		Assert.IsFalse(margin.IsSourceSubscribed);
	}

	[TestMethod]
	public void NotifyingSource_BackgroundChange_IsMarshaledToMarginDispatcher()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var tracker = new UnsavedChangesTracker(() => document);
		var margin = new ChangeMarkerMargin(tracker);
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		// The baseline matches the document, so nothing is marked yet.
		tracker.SetBaseline(document.Text);

		using HostWindow hostWindow = WPFTestHost.ShowEditorWithMargin(editor, margin);

		// Changing the baseline on a background thread notifies the margin, which must
		// marshal its repaint instead of touching the visual from the wrong thread.
		Task.Run(() => tracker.SetBaseline("one\r\nTWO\r\nthree")).GetAwaiter().GetResult();

		BitmapSource bitmap = TestBitmapRendering.PumpAndRender(editor);

		Assert.IsTrue(
			TestBitmapRendering.HasColorInLeftStrip(bitmap, 4, Color.FromRgb(0x1E, 0x90, 0xFF)),
			"Expected the margin to mark the line that changed on the background thread.");
	}

	[TestMethod]
	public void NotifyingSource_ChangeAfterTextViewDetached_PumpIsSafeAndMarginStaysDetached()
	{
		var document = new TextDocument("one\r\ntwo");
		var tracker = new UnsavedChangesTracker(() => document);
		var margin = new ChangeMarkerMargin(tracker);
		var editor = new TextEditor { Document = document };

		editor.TextArea.LeftMargins.Add(margin);

		Assert.IsTrue(margin.IsSourceSubscribed);

		// Queue a repaint while the margin is still connected to the text view...
		tracker.SetBaseline("one\r\nTWO");

		// ...then detach it before the queued callback runs. The queued invalidation observes the
		// detached text view: pumping it must neither throw nor re-subscribe the margin to the source.
		editor.TextArea.LeftMargins.Remove(margin);
		WPFTestHost.PumpDispatcher(editor.Dispatcher, DispatcherPriority.Render);

		Assert.IsFalse(margin.IsSourceSubscribed);
	}

	[TestMethod]
	public void OnRender_AfterTrackerDisposal_DrawsWithoutThrowing()
	{
		var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("line", 20)));
		var tracker = new UnsavedChangesTracker(() => document);
		var margin = new ChangeMarkerMargin(tracker);
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using HostWindow hostWindow = WPFTestHost.ShowEditorWithMargin(editor, margin);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		// The margin reads its source during rendering, so disposing the tracker while the margin is
		// still attached must not turn the next repaint into a dispatcher exception.
		tracker.Dispose();
		margin.InvalidateVisual();

		BitmapSource bitmap = TestBitmapRendering.PumpAndRender(editor);

		Assert.IsFalse(
			TestBitmapRendering.HasColorInLeftStrip(bitmap, 4, Color.FromRgb(0x1E, 0x90, 0xFF)),
			"Expected no marker after the tracker was disposed.");
	}

	[TestMethod]
	public void DesiredWidth_ScalesWithTextViewFontSize()
	{
		var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("line", 20)));
		var margin = new ChangeMarkerMargin(new EmptyMarkerSource());
		var editor = new TextEditor { Document = document, FontSize = 24.0 };

		using HostWindow hostWindow = WPFTestHost.ShowEditorWithMargin(editor, margin);

		editor.UpdateLayout();

		// The gutter is authored for the 12-DIP design font and scales with the font size.
		Assert.AreEqual(4.0 * 24.0 / 12.0, margin.DesiredSize.Width, 0.5);

		editor.FontSize = 8.0;
		editor.UpdateLayout();

		Assert.AreEqual(4.0 * 8.0 / 12.0, margin.DesiredSize.Width, 0.5);
	}

	[TestMethod]
	public void OnRender_MarkerBounds_ScaleWithTextViewFontSize()
	{
		(int MinColumn, int MaxColumn) RenderMarkerBounds(double fontSize)
		{
			var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("line", 20)));
			var margin = new ChangeMarkerMargin(new FixedLineStatusSource(() => document, [1], notifiesChanges: false));
			var editor = new TextEditor { Document = document, FontSize = fontSize };

			using HostWindow hostWindow = WPFTestHost.ShowEditorWithMargin(editor, margin);

			Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

			BitmapSource bitmap = TestBitmapRendering.RenderToBitmap(editor);

			bool found = TestBitmapRendering.TryGetColorColumnBounds(
				bitmap,
				(int)Math.Ceiling(margin.ActualWidth),
				Color.FromRgb(0x1E, 0x90, 0xFF),
				out int minColumn,
				out int maxColumn);

			Assert.IsTrue(found, "Expected a blue change marker in the margin.");
			return (minColumn, maxColumn);
		}

		(int MinColumn, int MaxColumn) smallMarker = RenderMarkerBounds(8.0);
		(int MinColumn, int MaxColumn) largeMarker = RenderMarkerBounds(24.0);

		int smallMarkerWidth = smallMarker.MaxColumn - smallMarker.MinColumn;
		int largeMarkerWidth = largeMarker.MaxColumn - largeMarker.MinColumn;

		Assert.IsGreaterThan(0, smallMarkerWidth);
		Assert.IsGreaterThan(smallMarkerWidth, largeMarkerWidth);
	}

	[TestMethod]
	public void OnRender_CustomMarkerBrush_IsUsed()
	{
		var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("line", 20)));

		var margin = new ChangeMarkerMargin(new FixedLineStatusSource(() => document, [1], notifiesChanges: false))
		{
			MarkerBrush = new SolidColorBrush(Colors.Red)
		};

		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using HostWindow hostWindow = WPFTestHost.ShowEditorWithMargin(editor, margin);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		BitmapSource bitmap = TestBitmapRendering.RenderToBitmap(editor);

		Assert.IsTrue(
			TestBitmapRendering.HasColorInLeftStrip(bitmap, 4, Colors.Red),
			"Expected the custom red marker brush to be used.");

		Assert.IsFalse(
			TestBitmapRendering.HasColorInLeftStrip(bitmap, 4, Color.FromRgb(0x1E, 0x90, 0xFF)),
			"Expected the default blue marker to be replaced.");
	}

	[TestMethod]
	public void DrawMarker_UnarrangedMargin_DrawsTheMarkerAtTheAuthoredWidth()
	{
		var document = new TextDocument(string.Join("\r\n", Enumerable.Repeat("line", 20)));
		var margin = new ChangeMarkerMargin(new FixedLineStatusSource(() => document, [1]));

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

		// The default 4-DIP bar spans the authored width when the margin is not arranged.
		Assert.AreEqual(0.0, drawing.Bounds.X, 0.001);
		Assert.AreEqual(4.0, drawing.Bounds.Width, 0.001);
	}

	private static ChangeMarkerMargin CreateMargin()
		=> new(new EmptyMarkerSource());

	// A source without the IChangeNotificationSource interface, so the margin exercises the
	// no-subscription branch; FixedLineStatusSource implements the interface even when it never
	// raises, so it cannot stand in for this double.
	private sealed class EmptyMarkerSource : ILineStatusSource
	{
		public IReadOnlyList<int> GetMarkedLineNumbers() => [];
	}
}

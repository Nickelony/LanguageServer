using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using Nickelony.IDEKit.Core.Notifications;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

[STATestClass]
public sealed class LineStatusMarginBaseTests
{
	[TestMethod]
	public void MeasureOverride_ZeroMarginWidth_ReservesNoWidth()
	{
		var margin = new RecordingMargin { MarginWidth = 0.0 };

		WPFTestHost.PinDesignFontSize(margin);
		margin.Measure(new Size(100.0, 100.0));

		Assert.AreEqual(0.0, margin.DesiredSize.Width);
	}

	[TestMethod]
	public void MeasureOverride_ScalesReservedWidthWithFontSize()
	{
		var margin = new RecordingMargin { MarginWidth = 10.0 };

		WPFTestHost.PinDesignFontSize(margin, 24.0);
		margin.Measure(new Size(100.0, 100.0));

		// The reserved width is authored for the 12-DIP design font.
		Assert.AreEqual(20.0, margin.DesiredSize.Width);
	}

	[TestMethod]
	public void OnRender_DrawsMarkerForMarkedLine()
	{
		var document = CreateDocument(40);
		var margin = new RecordingMargin();
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using HostWindow hostWindow = WPFTestHost.ShowEditorWithMargin(editor, margin);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		margin.SetMarkedLines([3]);
		margin.InvalidateVisual();

		TestBitmapRendering.RenderToBitmap(editor);

		Assert.HasCount(1, margin.Draws);
		Assert.AreEqual(3, margin.Draws[0].LineNumber);
		Assert.IsGreaterThan(0.0, margin.Draws[0].VisualTop);
	}

	[TestMethod]
	public void OnRender_MarkedLinesBeforeViewport_AreSkippedByTheForwardWalk()
	{
		var document = CreateDocument(400);
		var margin = new RecordingMargin();
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using HostWindow hostWindow = WPFTestHost.ShowEditorWithMargin(editor, margin);

		editor.ScrollToLine(299);
		editor.UpdateLayout();

		IReadOnlyList<VisualLine> visualLines = editor.TextArea.TextView.VisualLines;

		Assert.IsNotEmpty(visualLines);

		int firstVisibleLineNumber = visualLines[0].FirstDocumentLine.LineNumber;

		// The premise: the viewport starts below the document start, so the marked line above
		// it must be skipped by the forward-only walk while the first visible line is drawn.
		Assert.IsGreaterThan(1, firstVisibleLineNumber, "The scroll position must hide the first document line.");

		margin.SetMarkedLines([firstVisibleLineNumber - 1, firstVisibleLineNumber]);
		margin.InvalidateVisual();

		TestBitmapRendering.RenderToBitmap(editor);

		Assert.HasCount(1, margin.Draws);
		Assert.AreEqual(firstVisibleLineNumber, margin.Draws[0].LineNumber);
	}

	[TestMethod]
	public void OnRender_MarkedLinesBelowViewport_DrawNothing()
	{
		var document = CreateDocument(400);
		var margin = new RecordingMargin();
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using HostWindow hostWindow = WPFTestHost.ShowEditorWithMargin(editor, margin);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		// Line 390 is far below the visible range.
		margin.SetMarkedLines([390]);
		margin.InvalidateVisual();

		TestBitmapRendering.RenderToBitmap(editor);

		Assert.IsEmpty(margin.Draws);
	}

	[TestMethod]
	public void OnRender_WrappedMarkedLine_DrawsOnceForTheWholeLine()
	{
		// A single document line long enough to wrap several times at the hosted width.
		string wrappedText = string.Join(" ", Enumerable.Repeat("word", 300));
		var document = new TextDocument(wrappedText);
		var margin = new RecordingMargin();
		var editor = new TextEditor { Document = document, FontSize = 12.0, WordWrap = true };

		using HostWindow hostWindow = WPFTestHost.ShowEditorWithMargin(editor, margin);

		IReadOnlyList<VisualLine> visualLines = editor.TextArea.TextView.VisualLines;

		// The premise: word wrap keeps the document line as one visual line made of several
		// text lines, so the marker must not be duplicated per wrapped segment.
		Assert.HasCount(1, visualLines);
		Assert.IsGreaterThan(1, visualLines[0].TextLines.Count);

		VisualLine wrappedLine = visualLines[0];
		double visualTop = wrappedLine.VisualTop - editor.TextArea.TextView.VerticalOffset;

		margin.SetMarkedLines([1]);
		margin.InvalidateVisual();

		TestBitmapRendering.RenderToBitmap(editor);

		// One draw for the whole wrapped line, anchored at its line box top; the marker
		// covers the remaining segments through VisualLine.Height.
		Assert.HasCount(1, margin.Draws);
		Assert.AreEqual(1, margin.Draws[0].LineNumber);
		Assert.AreEqual(visualTop, margin.Draws[0].VisualTop, 0.5);
		Assert.IsGreaterThan(editor.TextArea.TextView.DefaultLineHeight, wrappedLine.Height);
	}

	[TestMethod]
	public void OnTextViewChanged_MarginAttachedToSecondEditor_RendersForTheNewEditor()
	{
		var margin = new RecordingMargin();
		var firstDocument = CreateDocument(40);
		var firstEditor = new TextEditor { Document = firstDocument, FontSize = 12.0 };

		using (HostWindow firstHost = WPFTestHost.ShowEditorWithMargin(firstEditor, margin))
		{
			Assert.IsNotEmpty(firstEditor.TextArea.TextView.VisualLines);

			margin.SetMarkedLines([3]);
			margin.InvalidateVisual();

			TestBitmapRendering.RenderToBitmap(firstEditor);

			Assert.HasCount(1, margin.Draws);
		}

		// Move the same margin to a second editor after it was connected once before.
		firstEditor.TextArea.LeftMargins.Remove(margin);

		var secondDocument = CreateDocument(40);
		var secondEditor = new TextEditor { Document = secondDocument, FontSize = 12.0 };

		using HostWindow secondHost = WPFTestHost.ShowEditorWithMargin(secondEditor, margin);

		Assert.IsNotEmpty(secondEditor.TextArea.TextView.VisualLines);

		margin.Draws.Clear();
		margin.SetMarkedLines([7]);
		margin.InvalidateVisual();

		TestBitmapRendering.RenderToBitmap(secondEditor);

		// The margin repaints for the new editor and uses the new document's lines.
		Assert.HasCount(1, margin.Draws);
		Assert.AreEqual(7, margin.Draws[0].LineNumber);
	}

	[TestMethod]
	public void SetNotifyingSource_SecondCall_ThrowsInvalidOperationException()
	{
		var margin = new DoubleAssignMargin();
		var firstSource = new FixedLineStatusSource(() => new TextDocument(), []);
		var secondSource = new FixedLineStatusSource(() => new TextDocument(), []);

		Assert.ThrowsExactly<InvalidOperationException>(() => margin.AssignTwoSources(firstSource, secondSource));
	}

	[TestMethod]
	public void SetNotifyingSource_AfterConnecting_SubscribesImmediately()
	{
		var document = CreateDocument(40);
		var margin = new LateAssignMargin();
		var source = new FixedLineStatusSource(() => document, []);
		var editor = new TextEditor { Document = document, FontSize = 12.0 };

		using HostWindow hostWindow = WPFTestHost.ShowEditorWithMargin(editor, margin);

		// The margin is connected before the handler is registered, so the registration must subscribe
		// right away instead of silently never subscribing.
		margin.AssignSource(source);

		Assert.IsTrue(margin.IsSubscribed);
	}

	[TestMethod]
	public void QueueVisualInvalidation_CoalescesWhileQueuedAndReleasesTheGateWhenItRuns()
	{
		var margin = new RecordingMargin();

		margin.QueueInvalidation();
		margin.QueueInvalidation();

		// Notifications that arrive while an invalidation is queued coalesce into that one invalidation.
		Assert.IsTrue(margin.IsInvalidationQueued);

		WPFTestHost.PumpDispatcher(Dispatcher.CurrentDispatcher, DispatcherPriority.Render);

		// The gate is released when the queued action runs, so a later notification queues again.
		Assert.IsFalse(margin.IsInvalidationQueued);

		margin.QueueInvalidation();

		Assert.IsTrue(margin.IsInvalidationQueued);
	}

	private static TextDocument CreateDocument(int lineCount)
		=> new(string.Join("\r\n", Enumerable.Repeat("line", lineCount)));

	private sealed class LateAssignMargin : LineStatusMarginBase
	{
		public bool IsSubscribed => IsSourceSubscribed;

		public void AssignSource(IChangeNotificationSource source)
			=> SetNotifyingSource(source);

		protected override IReadOnlyList<int> GetMarkedLineNumbers() => [];

		protected internal override void DrawMarker(DrawingContext drawingContext, VisualLine visualLine, double visualTop)
		{ }
	}

	private sealed class DoubleAssignMargin : LineStatusMarginBase
	{
		public void AssignTwoSources(IChangeNotificationSource first, IChangeNotificationSource second)
		{
			SetNotifyingSource(first);
			SetNotifyingSource(second);
		}

		protected override IReadOnlyList<int> GetMarkedLineNumbers() => [];

		protected internal override void DrawMarker(DrawingContext drawingContext, VisualLine visualLine, double visualTop)
		{ }
	}

	private sealed class RecordingMargin : LineStatusMarginBase
	{
		private IReadOnlyList<int> _markedLines = [];

		public List<(int LineNumber, double VisualTop)> Draws { get; } = [];

		public void SetMarkedLines(IReadOnlyList<int> lineNumbers)
			=> _markedLines = [.. lineNumbers];

		public void QueueInvalidation()
			=> QueueVisualInvalidation();

		protected override IReadOnlyList<int> GetMarkedLineNumbers()
			=> _markedLines;

		protected internal override void DrawMarker(DrawingContext drawingContext, VisualLine visualLine, double visualTop)
			=> Draws.Add((visualLine.FirstDocumentLine.LineNumber, visualTop));
	}
}

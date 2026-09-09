using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using AvalonTextEditor = ICSharpCode.AvalonEdit.TextEditor;

namespace Nickelony.IDEKit.AvalonEdit.Markdown;

/// <summary>
/// Routes mouse-wheel events between a tooltip's scroll viewers and a nested scrollable code block.
/// </summary>
/// <remarks>
/// <para>
/// WPF consumes a wheel event at the innermost scroll viewer and never chains the scroll to an
/// enclosing viewer, so the outer preview handler decides which viewer may consume the event: a nested
/// scrollable element scrolls first, and the outer viewer takes over once the nested element reaches
/// its scrolling boundary. This helper isolates that workaround so it can be removed if WPF or
/// AvalonEdit ever chains the wheel itself.
/// </para>
/// <para>
/// The nested element is recognized as a code-block editor (or the bordered element that hosts one),
/// which is the only nested scrollable the renderer produces; other scrollable content hosted in a
/// tooltip is not chained. When the outer viewer cannot move further in the wheel's direction, the
/// event is left unhandled so an enclosing host scroller can take it.
/// </para>
/// </remarks>
internal static class ToolTipScrollChaining
{
	/// <summary>
	/// Attaches wheel routing to an element that participates in tooltip scrolling.
	/// </summary>
	/// <param name="element">
	/// A tooltip viewer, a code-block editor, or a plain-text fallback viewer.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="element"/> is <see langword="null"/>.
	/// </exception>
	public static void Attach(UIElement element)
	{
		ArgumentNullException.ThrowIfNull(element);
		element.PreviewMouseWheel += ScrollHost_PreviewMouseWheel;
	}

	private static void ScrollHost_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
	{
		if (e.Delta == 0 || sender is not DependencyObject senderObject)
			return;

		// The handler is attached to the tooltip viewer and to each code-block editor. When a wheel event
		// over the viewer comes from a nested code-block editor that can still consume it, the event is
		// left alone so the nested element scrolls first; the viewer scrolls only once that element reaches
		// its scrolling boundary. WPF tunnels the outer preview handler first, so without this check the
		// nested element would never receive the wheel event.
		if (sender is FlowDocumentScrollViewer
			&& FindNestedEditor(e.OriginalSource, senderObject) is AvalonTextEditor nestedEditor
			&& CanConsumeWheel(nestedEditor, e.Delta))
		{
			return;
		}

		// A code block is scrolled through the text area's scroll surface, which is the surface that
		// actually drives the rendered text.
		if (sender is AvalonTextEditor editor)
		{
			double previousEditorOffset = GetEditorOffset(editor);

			ScrollEditorByWheel(editor, e.Delta);
			e.Handled = GetEditorOffset(editor) != previousEditorOffset;
			return;
		}

		var scrollViewer = sender as ScrollViewer ?? senderObject.FindVisualDescendant<ScrollViewer>();

		if (scrollViewer is null || scrollViewer.ScrollableHeight <= 0.0)
			return;

		double previousOffset = scrollViewer.VerticalOffset;

		ScrollByWheel(scrollViewer, e.Delta);

		// Only a wheel that actually moved the view counts as handled; at the scrolling boundary the event
		// is left for an enclosing host scroller.
		e.Handled = scrollViewer.VerticalOffset != previousOffset;
	}

	/// <summary>
	/// Scrolls a code-block editor by one native wheel step per notch: three text lines by default, or
	/// one page when the operating system is configured to page-scroll with the wheel.
	/// </summary>
	/// <param name="editor">The editor to scroll.</param>
	/// <param name="delta">The wheel delta of the event.</param>
	private static void ScrollEditorByWheel(AvalonTextEditor editor, int delta)
	{
		if (editor.TextArea is not IScrollInfo scrollInfo)
			return;

		int wheelScrollLines = SystemParameters.WheelScrollLines;

		if (wheelScrollLines == 0)
			return;

		int notchCount = Math.Max(1, Math.Abs(delta) / Mouse.MouseWheelDeltaForOneLine);
		double scrollableHeight = Math.Max(0.0, scrollInfo.ExtentHeight - scrollInfo.ViewportHeight);
		double stepHeight = wheelScrollLines < 0
			? notchCount * Math.Max(1.0, scrollInfo.ViewportHeight)
			: notchCount * wheelScrollLines * Math.Max(1.0, editor.TextArea.TextView.DefaultLineHeight);
		double targetOffset = delta > 0
			? scrollInfo.VerticalOffset - stepHeight
			: scrollInfo.VerticalOffset + stepHeight;
		double clampedOffset = Math.Clamp(targetOffset, 0.0, scrollableHeight);

		if (clampedOffset != scrollInfo.VerticalOffset)
			scrollInfo.SetVerticalOffset(clampedOffset);
	}

	/// <summary>
	/// Scrolls the viewer by one native wheel step per notch: three text lines by default, or one page
	/// when the operating system is configured to page-scroll with the wheel.
	/// </summary>
	/// <param name="scrollViewer">The viewer to scroll.</param>
	/// <param name="delta">The wheel delta of the event.</param>
	private static void ScrollByWheel(ScrollViewer scrollViewer, int delta)
	{
		int wheelScrollLines = SystemParameters.WheelScrollLines;

		if (wheelScrollLines == 0)
			return;

		int notchCount = Math.Max(1, Math.Abs(delta) / Mouse.MouseWheelDeltaForOneLine);
		bool scrollUp = delta > 0;

		if (wheelScrollLines < 0)
		{
			for (int i = 0; i < notchCount; i++)
			{
				if (scrollUp)
					scrollViewer.PageUp();
				else
					scrollViewer.PageDown();
			}

			return;
		}

		int stepCount = notchCount * wheelScrollLines;

		for (int i = 0; i < stepCount; i++)
		{
			if (scrollUp)
				scrollViewer.LineUp();
			else
				scrollViewer.LineDown();
		}
	}

	/// <summary>
	/// Finds the code-block editor under a wheel event's original source: either the editor itself or
	/// the bordered element that hosts it, whose padding area also routes the wheel to its editor.
	/// </summary>
	/// <param name="originalSource">The event's original source.</param>
	/// <param name="sender">The element the preview handler is attached to.</param>
	/// <returns>The nested editor, or <see langword="null"/> when the event is not over a code block.</returns>
	private static AvalonTextEditor? FindNestedEditor(object? originalSource, DependencyObject sender)
	{
		DependencyObject? current = originalSource as DependencyObject;

		while (current is not null && !ReferenceEquals(current, sender))
		{
			if (current is AvalonTextEditor editor)
				return editor;

			if (current is Border { Child: AvalonTextEditor hostedEditor })
				return hostedEditor;

			current = current is Visual or Visual3D
				? VisualTreeHelper.GetParent(current)
				: LogicalTreeHelper.GetParent(current);
		}

		return null;
	}

	private static bool CanConsumeWheel(AvalonTextEditor editor, int delta)
	{
		if (editor.TextArea is not IScrollInfo scrollInfo)
			return false;

		double scrollableHeight = Math.Max(0.0, scrollInfo.ExtentHeight - scrollInfo.ViewportHeight);

		if (scrollableHeight <= 0.0)
			return false;

		return delta > 0
			? scrollInfo.VerticalOffset > 0.0
			: scrollInfo.VerticalOffset < scrollableHeight;
	}

	private static double GetEditorOffset(AvalonTextEditor editor)
		=> editor.TextArea is IScrollInfo scrollInfo ? scrollInfo.VerticalOffset : 0.0;

	private static T? FindVisualDescendant<T>(this DependencyObject current) where T : DependencyObject
	{
		int count = VisualTreeHelper.GetChildrenCount(current);

		for (int i = 0; i < count; i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(current, i);

			if (child is T match)
				return match;

			T? result = FindVisualDescendant<T>(child);

			if (result is not null)
				return result;
		}

		return null;
	}
}

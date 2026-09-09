using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.Core.Notifications;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Nickelony.IDEKit.AvalonEdit.Rendering;

/// <summary>
/// Provides the shared AvalonEdit text-view wiring for margins that paint a marker beside
/// every visible line that the source reports as marked.
/// </summary>
/// <remarks>
/// <para>
/// The margin invalidates itself when its text view, visual lines, or scroll offset change.
/// </para>
/// <para>
/// A subclass whose source notifies changes registers the source through <see cref="SetNotifyingSource"/>;
/// the margin then invalidates itself when the source raises <see cref="IChangeNotificationSource.Changed"/>
/// while it is connected to a text view, subscribing and unsubscribing with the connection. Registering the
/// source after the margin is already connected subscribes right away.
/// </para>
/// <para>
/// Subclasses supply the marked line numbers through <see cref="GetMarkedLineNumbers"/> and paint their
/// marker through <see cref="DrawMarker"/>.
/// </para>
/// <para>
/// The reserved width is owned by this class through <see cref="MarginWidth"/> and scales with
/// <see cref="GetFontScale"/>; concrete margins that need a different authored default override
/// the property's metadata.
/// </para>
/// </remarks>
public abstract class LineStatusMarginBase : AbstractMargin
{
	/// <summary>
	/// The font size, in device-independent pixels, for which a concrete margin's default marker
	/// geometry and reserved width are authored.
	/// </summary>
	/// <remarks>
	/// Margins scale their marker and reserved width by the ratio of their own font size to this value,
	/// so the margin width stays proportional to the text when the editor font changes.
	/// </remarks>
	protected const double DesignFontSize = 12.0;

	/// <summary>
	/// Identifies the <see cref="MarginWidth"/> dependency property.
	/// </summary>
	public static readonly DependencyProperty MarginWidthProperty = DependencyProperty.Register(
		nameof(MarginWidth),
		typeof(double),
		typeof(LineStatusMarginBase),
		new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure),
		ValidateMarginWidth);

	private IChangeNotificationSource? _source;

	private int _textViewConnectionVersion;
	private bool _sourceSubscribed;
	private int _invalidationQueued;

	/// <summary>
	/// Gets a value indicating whether the margin is subscribed to the source's changed event while it has a text view.
	/// </summary>
	internal bool IsSourceSubscribed => _sourceSubscribed;

	/// <summary>
	/// Gets a value indicating whether a coalesced visual invalidation is currently queued.
	/// </summary>
	internal bool IsInvalidationQueued => Volatile.Read(ref _invalidationQueued) != 0;

	/// <summary>
	/// Assigns the notifying source that follows the margin's text-view connection and invalidates the
	/// margin when its marked lines may have changed.
	/// </summary>
	/// <remarks>
	/// Call this once from the constructor of a margin whose source notifies changes. The margin subscribes
	/// while it is connected to a text view and unsubscribes when it is detached; a notification that arrives
	/// while connected is coalesced into one queued invalidation. Calling this method a second time is
	/// rejected because the live subscription cannot be transferred to another source. A subclass that needs a
	/// different reaction to source notifications can subscribe to the source's
	/// <see cref="IChangeNotificationSource.Changed"/> event itself instead of registering it here.
	/// </remarks>
	/// <param name="source">The source that raises <see cref="IChangeNotificationSource.Changed"/>.</param>
	/// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">A notifying source is already assigned.</exception>
	protected void SetNotifyingSource(IChangeNotificationSource source)
	{
		ArgumentNullException.ThrowIfNull(source);

		if (_source is not null)
			throw new InvalidOperationException("A notifying source is already assigned.");

		_source = source;

		// A margin that is already connected when the source is registered subscribes right away;
		// without this the registration would silently never subscribe.
		UpdateSourceSubscription(TextView is not null);
	}

	/// <summary>
	/// Gets or sets the width reserved for line-status markers, authored at the design font size.
	/// </summary>
	/// <remarks>
	/// Changing the value invalidates the margin's measure. The effective width scales with the
	/// margin's font size (see <see cref="DesignFontSize"/>).
	/// </remarks>
	/// <exception cref="ArgumentException">
	/// The assigned value is negative, <see cref="double.NaN"/>, or infinite, so the property's
	/// validation callback rejects it and the setter throws.
	/// </exception>
	public double MarginWidth
	{
		get => (double)GetValue(MarginWidthProperty);
		set => SetValue(MarginWidthProperty, value);
	}

	/// <summary>
	/// Queues a visual invalidation on the margin's dispatcher while it remains connected to its current text view.
	/// </summary>
	/// <remarks>
	/// The invalidation runs at <see cref="DispatcherPriority.Render"/>. Multiple notifications that arrive
	/// while an invalidation is queued coalesce into one; the queue gate is released when the queued action
	/// runs, so a later notification queues again.
	/// </remarks>
	protected void QueueVisualInvalidation()
	{
		if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
			return;

		if (Interlocked.Exchange(ref _invalidationQueued, 1) != 0)
			return;

		int connectionVersion = Volatile.Read(ref _textViewConnectionVersion);

		Dispatcher.BeginInvoke(
			DispatcherPriority.Render,
			new Action(() =>
			{
				Volatile.Write(ref _invalidationQueued, 0);

				if (connectionVersion == Volatile.Read(ref _textViewConnectionVersion) && TextView is not null)
					InvalidateVisual();
			}));
	}

	/// <summary>
	/// Gets the scale factor applied to the margin's marker and reserved width,
	/// derived from the margin's inherited font size relative to <see cref="DesignFontSize"/>.
	/// </summary>
	/// <remarks>
	/// WPF's font-size validation rejects non-positive and non-finite values, so the <c>1.0</c> fallback
	/// guards the scaling math instead of handling a reachable configuration.
	/// </remarks>
	/// <returns>
	/// The font-size scale factor, or <c>1.0</c> when the margin's font size is not a positive finite number.
	/// </returns>
	protected double GetFontScale()
	{
		// AbstractMargin derives from FrameworkElement, which has no FontSize property;
		// the inherited TextBlock.FontSize attached property supplies the editor font size.
		double fontSize = (double)GetValue(TextBlock.FontSizeProperty);
		return fontSize > 0.0 && double.IsFinite(fontSize) ? fontSize / DesignFontSize : 1.0;
	}

	/// <summary>
	/// Gets the width available to the marker: the margin's arranged width when it has one, otherwise the
	/// scaled reserved width, which is the value used before the first arrange pass.
	/// </summary>
	/// <returns>The width available to the marker, in device-independent pixels.</returns>
	protected double GetEffectiveMarginWidth()
		=> ActualWidth > 0.0 ? ActualWidth : MarginWidth * GetFontScale();

	/// <inheritdoc/>
	protected override Size MeasureOverride(Size availableSize)
		=> new(MarginWidth * GetFontScale(), 0.0);

	/// <summary>
	/// Gets the one-based document line numbers that currently have a marker, in ascending order.
	/// Line numbers outside the document are ignored by the render pass.
	/// </summary>
	protected abstract IReadOnlyList<int> GetMarkedLineNumbers();

	/// <summary>
	/// Draws the marker for one marked visual line.
	/// </summary>
	/// <param name="drawingContext">The drawing context of the margin.</param>
	/// <param name="visualLine">The visual line to draw the marker for.</param>
	/// <param name="visualTop">
	/// The y-position of the top of <paramref name="visualLine"/>'s line box, in margin coordinates.
	/// The line box spans <see cref="VisualLine.Height"/> from this position; the marker should stay
	/// inside those bounds.
	/// </param>
	protected internal abstract void DrawMarker(DrawingContext drawingContext, VisualLine visualLine, double visualTop);

	/// <inheritdoc/>
	protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
	{
		Interlocked.Increment(ref _textViewConnectionVersion);

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

		UpdateSourceSubscription(newTextView is not null);
		InvalidateVisual();
	}

	/// <inheritdoc/>
	protected override void OnRender(DrawingContext drawingContext)
	{
		TextView? textView = TextView;

		if (textView is null || !textView.VisualLinesValid)
			return;

		IReadOnlyList<int>? markedLineNumbers = GetMarkedLineNumbers();

		// The contract requires a non-null result, but the read runs in the render pass, so a misbehaving
		// source must not turn into a render exception.
		if (markedLineNumbers is null || markedLineNumbers.Count == 0)
			return;

		int markedLineIndex = 0;

		foreach (VisualLine line in textView.VisualLines)
		{
			int lineNumber = line.FirstDocumentLine.LineNumber;

			// Marked lines are ascending, so the index only moves forward as the visual lines advance.
			// A word-wrapped document line stays a single visual line with several text lines, so it is
			// drawn once; DrawMarker receives the line box's top and subclasses cover the full wrapped
			// height through VisualLine.Height.
			while (markedLineIndex < markedLineNumbers.Count && markedLineNumbers[markedLineIndex] < lineNumber)
				markedLineIndex++;

			if (markedLineIndex >= markedLineNumbers.Count)
				break;

			if (markedLineNumbers[markedLineIndex] != lineNumber)
				continue;

			double visualTop = line.VisualTop - textView.VerticalOffset;

			DrawMarker(drawingContext, line, visualTop);
		}
	}

	private static bool ValidateMarginWidth(object value)
		=> value is double width && double.IsFinite(width) && width >= 0.0;

	private void UpdateSourceSubscription(bool connected)
	{
		if (_source is null)
			return;

		if (connected == _sourceSubscribed)
			return;

		_sourceSubscribed = connected;

		if (connected)
			_source.Changed += Source_Changed;
		else
			_source.Changed -= Source_Changed;
	}

	private void Source_Changed(object? sender, EventArgs e)
		=> QueueVisualInvalidation();

	private void TextView_VisualLinesChanged(object? sender, EventArgs e)
		=> InvalidateVisual();

	private void TextView_ScrollOffsetChanged(object? sender, EventArgs e)
		=> InvalidateVisual();
}

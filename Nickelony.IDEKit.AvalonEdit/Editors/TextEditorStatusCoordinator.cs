using ICSharpCode.AvalonEdit.Editing;

namespace Nickelony.IDEKit.AvalonEdit.Editors;

/// <summary>
/// Coordinates caret and selection notifications and maintains zoom state for an AvalonEdit <see cref="TextArea"/>.
/// </summary>
public sealed class TextEditorStatusCoordinator : IDisposable
{
	private readonly Action _raiseStatusChanged;
	private readonly Action _raiseZoomChanged;

	private readonly TextArea _textArea;

	private bool _attached;
	private bool _disposed;

	/// <summary>
	/// Associates a text area with callbacks for status and zoom changes.
	/// </summary>
	/// <param name="textArea">The text area to observe after <see cref="Attach"/> is called.</param>
	/// <param name="raiseStatusChanged">The callback invoked when the observed caret position or selection changes.</param>
	/// <param name="raiseZoomChanged">The callback invoked after a zoom change is applied.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textArea"/>, <paramref name="raiseStatusChanged"/>, or <paramref name="raiseZoomChanged"/> is <see langword="null"/>.
	/// </exception>
	public TextEditorStatusCoordinator(TextArea textArea, Action raiseStatusChanged, Action raiseZoomChanged)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentNullException.ThrowIfNull(raiseStatusChanged);
		ArgumentNullException.ThrowIfNull(raiseZoomChanged);

		_textArea = textArea;
		_raiseStatusChanged = raiseStatusChanged;
		_raiseZoomChanged = raiseZoomChanged;
	}

	/// <summary>
	/// Gets or sets the stored zoom percentage.
	/// </summary>
	/// <remarks>
	/// Direct assignment stores the value without validation. It does not apply a font size or invoke the zoom callback.
	/// </remarks>
	public int Zoom { get; set; } = 100;

	/// <summary>
	/// Subscribes to the text area's caret and selection change events.
	/// </summary>
	/// <remarks>Repeated calls before disposal have no effect.</remarks>
	/// <exception cref="ObjectDisposedException">The coordinator has been disposed.</exception>
	public void Attach()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		if (_attached)
			return;

		_attached = true;

		_textArea.Caret.PositionChanged += TextArea_PositionChanged;
		_textArea.SelectionChanged += TextArea_SelectionChanged;
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;

		if (!_attached)
			return;

		_attached = false;

		_textArea.Caret.PositionChanged -= TextArea_PositionChanged;
		_textArea.SelectionChanged -= TextArea_SelectionChanged;
	}

	/// <summary>
	/// Tries to adjust the stored zoom by one step toward the relevant supplied bound and applies the corresponding font size.
	/// </summary>
	/// <remarks>
	/// Positive deltas are capped at <paramref name="maxZoom"/> and negative deltas at <paramref name="minZoom"/>.
	/// Direct assignments to <see cref="Zoom"/> outside those bounds are not normalized before a step is applied.
	/// </remarks>
	/// <param name="delta">
	/// The direction of the step: positive to zoom in, negative to zoom out,
	/// or <c>0</c> to make no change. Only the sign is used.
	/// </param>
	/// <param name="minZoom">The lower bound used when zooming out.</param>
	/// <param name="maxZoom">The upper bound used when zooming in.</param>
	/// <param name="zoomStepSize">The size (in percentage points) of one zoom step.</param>
	/// <param name="defaultFontSize">The font size at <c>100</c>% zoom.</param>
	/// <param name="applyFontSize">The callback that receives the scaled font size.</param>
	/// <returns>
	/// <see langword="true"/> when the zoom changes and the font-size and zoom-change callbacks are invoked;
	/// otherwise, <see langword="false"/>.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="applyFontSize"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="minZoom"/> is greater than <paramref name="maxZoom"/>,
	/// <paramref name="zoomStepSize"/> is not positive, or
	/// <paramref name="defaultFontSize"/> is not a finite positive number.
	/// </exception>
	public bool TryHandleZoom(
		int delta,
		int minZoom,
		int maxZoom,
		int zoomStepSize,
		double defaultFontSize,
		Action<double> applyFontSize)
	{
		ArgumentNullException.ThrowIfNull(applyFontSize);

		ArgumentOutOfRangeException.ThrowIfGreaterThan(minZoom, maxZoom, nameof(minZoom));
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(zoomStepSize);

		if (!double.IsFinite(defaultFontSize) || defaultFontSize <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(defaultFontSize),
				defaultFontSize,
				"Default font size must be a finite positive number.");
		}

		int nextZoom;

		if (delta > 0)
		{
			if (Zoom >= maxZoom)
				return false;

			nextZoom = Math.Min(maxZoom, Zoom + zoomStepSize);
		}
		else if (delta < 0)
		{
			if (Zoom <= minZoom)
				return false;

			nextZoom = Math.Max(minZoom, Zoom - zoomStepSize);
		}
		else
		{
			return false;
		}

		Zoom = nextZoom;

		applyFontSize(defaultFontSize * Zoom / 100);

		_raiseZoomChanged();
		return true;
	}

	private void TextArea_PositionChanged(object? sender, EventArgs e)
		=> _raiseStatusChanged();

	private void TextArea_SelectionChanged(object? sender, EventArgs e)
		=> _raiseStatusChanged();
}

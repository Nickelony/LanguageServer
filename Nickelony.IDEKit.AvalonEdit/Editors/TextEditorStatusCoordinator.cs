using ICSharpCode.AvalonEdit.Editing;

namespace Nickelony.IDEKit.AvalonEdit.Editors;

/// <summary>
/// Tracks caret position and zoom state for an AvalonEdit text area.
/// </summary>
public sealed class TextEditorStatusCoordinator : IDisposable
{
	private readonly Action _raiseStatusChanged;
	private readonly Action _raiseZoomChanged;
	private readonly TextArea _textArea;

	private bool _attached;
	private bool _disposed;
	private int _zoom = 100;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextEditorStatusCoordinator"/> class.
	/// </summary>
	/// <param name="textArea">The text area whose caret and selection changes raise status updates.</param>
	/// <param name="raiseStatusChanged">The callback invoked when the caret or selection changes.</param>
	/// <param name="raiseZoomChanged">The callback invoked when the zoom changes.</param>
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
	/// Gets or sets the current zoom percentage.
	/// </summary>
	/// <remarks>Setting this property directly does not apply a font size or invoke the zoom callback.</remarks>
	public int Zoom
	{
		get => _zoom;
		set => _zoom = value;
	}

	/// <summary>
	/// Attaches to the text area's caret and selection change events.
	/// </summary>
	/// <remarks>
	/// Repeated calls are no-ops. Calling this method after disposal throws
	/// <see cref="ObjectDisposedException"/>.
	/// </remarks>
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
	/// Tries to apply a zoom delta within the configured range.
	/// </summary>
	/// <param name="delta">A positive value to zoom in, a negative value to zoom out, or <c>0</c> to do nothing.</param>
	/// <param name="minZoom">The minimum allowed zoom percentage.</param>
	/// <param name="maxZoom">The maximum allowed zoom percentage.</param>
	/// <param name="zoomStepSize">The step size for each zoom change.</param>
	/// <param name="defaultFontSize">The font size at 100% zoom.</param>
	/// <param name="applyFontSize">The callback used to apply the scaled font size.</param>
	/// <returns>
	/// <see langword="true"/> when the zoom changed and the callbacks were invoked; otherwise, <see langword="false"/>.
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
			throw new ArgumentOutOfRangeException(
				nameof(defaultFontSize),
				defaultFontSize,
				"Default font size must be a finite positive number.");

		int nextZoom;

		if (delta > 0)
		{
			if (_zoom >= maxZoom)
				return false;

			nextZoom = Math.Min(maxZoom, _zoom + zoomStepSize);
		}
		else if (delta < 0)
		{
			if (_zoom <= minZoom)
				return false;

			nextZoom = Math.Max(minZoom, _zoom - zoomStepSize);
		}
		else
		{
			return false;
		}

		_zoom = nextZoom;
		applyFontSize(defaultFontSize * _zoom / 100);
		_raiseZoomChanged();
		return true;
	}

	private void TextArea_PositionChanged(object? sender, EventArgs e)
		=> _raiseStatusChanged();

	private void TextArea_SelectionChanged(object? sender, EventArgs e)
		=> _raiseStatusChanged();
}

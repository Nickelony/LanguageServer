using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Editing;

namespace Nickelony.IDEKit.AvalonEdit.IntelliSense.Completion;

/// <summary>
/// Owns the completion window lifecycle for a single text area and tracks at most one window.
/// Creating a new window force-closes any previously tracked window, so the host reflects only the
/// latest completion window.
/// </summary>
public sealed class CompletionWindowHost
{
	private readonly TextArea _textArea;
	private EventHandler? _closedHandler;
	private Action? _trackedClosedAction;
	private CompletionWindow? _trackedWindow;

	/// <summary>
	/// Initializes a new instance of the <see cref="CompletionWindowHost"/> class.
	/// </summary>
	/// <param name="textArea">The text area completion windows are created for.</param>
	public CompletionWindowHost(TextArea textArea) => _textArea = textArea;

	/// <summary>
	/// Creates a new completion window. Any previously tracked window is closed first, so only the
	/// most recently created window remains tracked.
	/// </summary>
	/// <param name="width">The window width.</param>
	/// <param name="height">The window height.</param>
	/// <param name="borderBrush">The window border brush.</param>
	/// <param name="background">The window background brush.</param>
	/// <param name="foreground">The window foreground brush.</param>
	/// <returns>The new completion window.</returns>
	public CompletionWindow Create(double width, double height, Brush borderBrush, Brush background, Brush foreground)
	{
		CloseTrackedWindow();

		return new(_textArea)
		{
			WindowStyle = WindowStyle.None,
			ResizeMode = ResizeMode.NoResize,
			BorderThickness = new Thickness(1.0),
			Background = background,
			Foreground = foreground,
			BorderBrush = borderBrush,
			Width = width,
			Height = height
		};
	}

	/// <summary>
	/// Shows the given completion window and tracks it until it closes. The callback is invoked when
	/// the window closes.
	/// </summary>
	/// <param name="completionWindow">The window to show.</param>
	/// <param name="onClosed">The callback invoked when the window closes.</param>
	public void Show(CompletionWindow completionWindow, Action onClosed)
	{
		TrackWindow(completionWindow, onClosed);

		completionWindow.Show();
	}

	/// <summary>
	/// Closes the given completion window, releasing its tracking first when it is the currently tracked
	/// window, and then invokes the callback.
	/// </summary>
	/// <param name="completionWindow">The window to close, or <see langword="null"/> to do nothing.</param>
	/// <param name="onClosed">The callback invoked when the window closes.</param>
	public void Close(CompletionWindow? completionWindow, Action onClosed)
	{
		if (completionWindow is null)
			return;

		UntrackWindow(completionWindow);
		completionWindow.Close();
		onClosed();
	}

	private void TrackWindow(CompletionWindow completionWindow, Action onClosed)
	{
		UntrackWindow(_trackedWindow);

		_trackedWindow = completionWindow;
		_trackedClosedAction = onClosed;
		_closedHandler = (sender, e) =>
		{
			UntrackWindow(completionWindow);
			onClosed();
		};

		completionWindow.Closed += _closedHandler;
	}

	private void UntrackWindow(CompletionWindow? completionWindow)
	{
		if (completionWindow is null || _closedHandler is null || !ReferenceEquals(completionWindow, _trackedWindow))
			return;

		completionWindow.Closed -= _closedHandler;
		_closedHandler = null;
		_trackedClosedAction = null;
		_trackedWindow = null;
	}

	private void CloseTrackedWindow()
	{
		if (_trackedWindow is null)
			return;

		CompletionWindow trackedWindow = _trackedWindow;
		Action? onClosed = _trackedClosedAction;

		UntrackWindow(trackedWindow);
		trackedWindow.Close();
		onClosed?.Invoke();
	}
}

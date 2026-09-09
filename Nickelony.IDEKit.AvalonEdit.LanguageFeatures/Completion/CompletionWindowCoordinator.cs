using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.Infrastructure;
using System.Windows;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

/// <summary>
/// Manages the completion window lifecycle for a single text area: creating windows with the default skin,
/// showing, closing, and tracking at most one window at a time. Creating a new window closes the previously
/// created or tracked window, and a window is tracked while it is shown.
/// </summary>
/// <remarks>
/// The coordinator is owned by the <see cref="TextCompletionController"/> of its text area, which creates and
/// shows windows through it, so every shown window went through the controller's configuration pipeline
/// (sizing, tooltip skin, window hook, and non-activation policy) and the controller's reported presentation
/// state stays truthful. A host observes the managed window through <see cref="ActiveWindow"/> and
/// <see cref="IsWindowOpen"/>, learns about closures through <see cref="WindowClosed"/>, and can close the
/// window with <see cref="Close"/>; window creation and showing are not part of the public surface.
/// The coordinator must be created and used on the thread that owns <see cref="TextArea"/>, because it creates
/// and shows WPF windows and subscribes to their events directly. It holds no resources of its own:
/// <see cref="Close"/> is safe to call, and the coordinator remains usable, before and after the
/// window closes.
/// </remarks>
public sealed class CompletionWindowCoordinator
{
	private readonly TextArea _textArea;
	private readonly CompletionWindowSkin _defaultSkin;
	private EventHandler? _closedHandler;
	private CompletionWindow? _trackedWindow;
	private CompletionWindow? _createdWindow;

	/// <summary>
	/// Initializes a new instance of the <see cref="CompletionWindowCoordinator"/> class.
	/// </summary>
	/// <param name="textArea">The text area completion windows are created for.</param>
	/// <param name="defaultSkin">The default skin applied to created windows.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textArea"/>, <paramref name="defaultSkin"/>, or one of the skin's brushes is
	/// <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The skin's <see cref="CompletionWindowSkin.BorderThickness"/> is negative or not finite.
	/// </exception>
	public CompletionWindowCoordinator(TextArea textArea, CompletionWindowSkin defaultSkin)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentNullException.ThrowIfNull(defaultSkin);
		ArgumentNullException.ThrowIfNull(defaultSkin.BorderBrush);
		ArgumentNullException.ThrowIfNull(defaultSkin.Background);
		ArgumentNullException.ThrowIfNull(defaultSkin.Foreground);

		if (!NumericValidation.IsFiniteNonNegative(defaultSkin.BorderThickness))
			throw new ArgumentOutOfRangeException(nameof(defaultSkin), defaultSkin.BorderThickness, "The skin border thickness must be finite and non-negative.");

		_textArea = textArea;
		_defaultSkin = defaultSkin;
	}

	/// <summary>
	/// Occurs when the shown window closes, whatever the cause - the window itself, a replacement, or
	/// <see cref="Close"/> - after the window is no longer tracked, exactly once per shown window.
	/// </summary>
	/// <remarks>
	/// A window that was created but never shown does not raise the event. A throwing subscriber propagates
	/// to whoever closed the window, exactly like a throwing host callback on the close path.
	/// </remarks>
	public event EventHandler? WindowClosed;

	/// <summary>
	/// Gets the currently tracked completion window, or <see langword="null"/> when none is tracked.
	/// </summary>
	public CompletionWindow? ActiveWindow => _trackedWindow;

	/// <summary>
	/// Gets a value indicating whether a completion window is currently tracked. Only a window that has
	/// been shown successfully is tracked.
	/// </summary>
	public bool IsWindowOpen => _trackedWindow is not null;

	/// <summary>
	/// Creates a new completion window with the given maximum height, closing the previously created or
	/// tracked window.
	/// </summary>
	/// <remarks>
	/// A previously tracked window is closed by the replacement and <see cref="WindowClosed"/> is raised for it.
	/// The created window is not tracked until it is shown through <see cref="Show"/>; its style is left at
	/// AvalonEdit's <see cref="WindowStyle.None"/> metadata and its initial width is AvalonEdit's default, so
	/// the caller sizes the window before showing it.
	/// </remarks>
	/// <param name="maxHeight">
	/// The maximum window height; the window sizes to its content up to this value.
	/// Defaults to the shared default completion window height cap (<c>300</c>).
	/// </param>
	/// <returns>The created window, which becomes the tracked <see cref="ActiveWindow"/> once it is shown.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="maxHeight"/> is negative or not finite.
	/// </exception>
	internal CompletionWindow Create(double maxHeight = CompletionWindowDefaults.DefaultWindowMaxHeight)
	{
		if (!NumericValidation.IsFiniteNonNegative(maxHeight))
			throw new ArgumentOutOfRangeException(nameof(maxHeight), maxHeight, "The maximum window height must be finite and non-negative.");

		// A window that was created but never shown is not tracked, so it must be closed here; otherwise a
		// repeated Create without Show would leave the first window untracked and never closed.
		Close();

		return _createdWindow = new(_textArea)
		{
			ResizeMode = ResizeMode.NoResize,
			BorderThickness = new Thickness(_defaultSkin.BorderThickness),
			Background = _defaultSkin.Background,
			Foreground = _defaultSkin.Foreground,
			BorderBrush = _defaultSkin.BorderBrush,
			MaxHeight = maxHeight
		};
	}

	/// <summary>
	/// Shows the completion window created by <see cref="Create"/>, if one exists, and tracks it until it
	/// closes. A show failure leaves the window untracked; <see cref="Close"/> can still close it.
	/// </summary>
	/// <remarks>
	/// Only the window created by the most recent <see cref="Create"/> call is shown, and no window can be
	/// tracked when this method runs: <see cref="Create"/> closes the previously created or tracked window
	/// before it creates its own.
	/// </remarks>
	internal void Show()
	{
		if (_createdWindow is not CompletionWindow window)
			return;

		TrackWindow(window);

		try
		{
			window.Show();
			_createdWindow = null;
		}
		catch
		{
			// A failed show must not leave the window tracked: the event would never be raised for a window
			// that never became visible.
			UntrackWindow(window);
			throw;
		}
	}

	/// <summary>
	/// Closes the currently tracked completion window, or the window created by <see cref="Create"/> when it
	/// has not been shown.
	/// </summary>
	public void Close()
	{
		CompletionWindow? window = _trackedWindow ?? _createdWindow;

		if (window is null)
			return;

		bool wasTracked = ReferenceEquals(window, _trackedWindow);

		UntrackWindow(window);
		_createdWindow = null;
		window.Close();

		if (wasTracked)
			WindowClosed?.Invoke(this, EventArgs.Empty);
	}

	private void TrackWindow(CompletionWindow completionWindow)
	{
		_trackedWindow = completionWindow;
		_closedHandler = (sender, e) =>
		{
			UntrackWindow(completionWindow);
			WindowClosed?.Invoke(this, EventArgs.Empty);
		};

		completionWindow.Closed += _closedHandler;
	}

	private void UntrackWindow(CompletionWindow? completionWindow)
	{
		if (completionWindow is null || _closedHandler is null || !ReferenceEquals(completionWindow, _trackedWindow))
			return;

		completionWindow.Closed -= _closedHandler;
		_closedHandler = null;
		_trackedWindow = null;
	}
}

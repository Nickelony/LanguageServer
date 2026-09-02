using System.Windows.Media;
using ICSharpCode.AvalonEdit.CodeCompletion;

namespace Nickelony.IDEKit.AvalonEdit.IntelliSense.Completion;

/// <summary>
/// Coordinates creation, display, and closure of the completion window for a single text area.
/// </summary>
public sealed class CompletionWindowCoordinator
{
	private readonly CompletionWindowHost _host;
	private readonly Brush _defaultBorderBrush;
	private readonly Brush _defaultBackground;
	private readonly Brush _defaultForeground;
	private CompletionWindow? _window;

	/// <summary>
	/// Initializes a new instance of the <see cref="CompletionWindowCoordinator"/> class.
	/// </summary>
	/// <param name="host">The completion window host that owns the window lifecycle.</param>
	/// <param name="defaultBorderBrush">The default window border brush.</param>
	/// <param name="defaultBackground">The default window background brush.</param>
	/// <param name="defaultForeground">The default window foreground brush.</param>
	public CompletionWindowCoordinator(
		CompletionWindowHost host,
		Brush defaultBorderBrush,
		Brush defaultBackground,
		Brush defaultForeground)
	{
		ArgumentNullException.ThrowIfNull(host);
		_host = host;
		ArgumentNullException.ThrowIfNull(defaultBorderBrush);
		_defaultBorderBrush = defaultBorderBrush;
		ArgumentNullException.ThrowIfNull(defaultBackground);
		_defaultBackground = defaultBackground;
		ArgumentNullException.ThrowIfNull(defaultForeground);
		_defaultForeground = defaultForeground;
	}

	/// <summary>
	/// Gets the currently tracked completion window, or <see langword="null"/> when none is tracked.
	/// </summary>
	public CompletionWindow? ActiveWindow => _window;

	/// <summary>
	/// Gets a value indicating whether a completion window is currently tracked.
	/// </summary>
	public bool IsWindowOpen => _window is not null;

	/// <summary>
	/// Creates a new completion window with the given size, replacing any tracked window.
	/// </summary>
	/// <param name="width">The window width.</param>
	/// <param name="height">The window height.</param>
	public void Initialize(int width = 300, int height = 300)
		=> _window = _host.Create(width, height, _defaultBorderBrush, _defaultBackground, _defaultForeground);

	/// <summary>
	/// Shows the currently tracked completion window, if one has been created.
	/// </summary>
	public void Show()
	{
		if (_window is null)
			return;

		_host.Show(_window, () => _window = null);
	}

	/// <summary>
	/// Closes the currently tracked completion window, if one exists.
	/// </summary>
	public void Close()
		=> _host.Close(_window, () => _window = null);

	/// <summary>
	/// Closes the currently tracked completion window, if one exists.
	/// </summary>
	public void Dispose()
		=> Close();
}

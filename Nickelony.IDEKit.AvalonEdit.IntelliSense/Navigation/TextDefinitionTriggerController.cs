using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nickelony.IDEKit.AvalonEdit.IntelliSense.Navigation;

/// <summary>
/// Handles shared editor-side definition navigation triggers such as F12 and Ctrl+Click.
/// Disposal signals cancellation to in-flight navigation and prevents further navigation; a
/// disposed controller must not be reused.
/// </summary>
public sealed class TextDefinitionTriggerController : IDisposable
{
	private static readonly Action<ILogger, Exception?> s_logDefinitionNavigationFailed = LoggerMessage.Define(
		LogLevel.Warning,
		new EventId(0),
		"Definition navigation failed.");

	private readonly ILogger _logger;
	private readonly FrameworkElement _owner;
	private readonly Func<Point, int> _getOffsetFromPoint;
	private readonly Func<int, CancellationToken, Task<bool>> _tryNavigateAsync;
	private readonly CancellationTokenSource _disposalCancellation = new();
	private bool _isDisposed;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextDefinitionTriggerController"/> class.
	/// </summary>
	/// <param name="owner">The element pointer positions are resolved against.</param>
	/// <param name="getOffsetFromPoint">Resolves the zero-based document offset for a point in the owner.</param>
	/// <param name="tryNavigateAsync">Attempts to resolve and navigate to a definition asynchronously.</param>
	/// <param name="logger">An optional logger for navigation failures.</param>
	public TextDefinitionTriggerController(
		FrameworkElement owner,
		Func<Point, int> getOffsetFromPoint,
		Func<int, CancellationToken, Task<bool>> tryNavigateAsync,
		ILogger? logger = null)
	{
		ArgumentNullException.ThrowIfNull(owner);
		ArgumentNullException.ThrowIfNull(getOffsetFromPoint);
		ArgumentNullException.ThrowIfNull(tryNavigateAsync);

		_logger = logger ?? NullLogger.Instance;
		_owner = owner;
		_getOffsetFromPoint = getOffsetFromPoint;
		_tryNavigateAsync = tryNavigateAsync;
	}

	/// <summary>
	/// Handles an F12 key press for definition navigation.
	/// </summary>
	/// <param name="e">The key event to inspect.</param>
	/// <param name="caretOffset">The current zero-based document caret offset.</param>
	/// <param name="cancellationToken">An optional caller cancellation token.</param>
	/// <returns><see langword="true"/> when the input was handled as a navigation; otherwise, <see langword="false"/>.</returns>
	public async Task<bool> TryHandleKeyDownAsync(KeyEventArgs e, int caretOffset, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(e);

		if (_isDisposed || e.Key != Key.F12)
			return false;

		using CancellationTokenSource linkedCancellation =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposalCancellation.Token);

		if (!await TryNavigateAsync(caretOffset, linkedCancellation.Token).ConfigureAwait(true))
			return false;

		if (_isDisposed)
			return false;

		e.Handled = true;
		return true;
	}

	/// <summary>
	/// Handles a Ctrl+LeftClick definition navigation attempt.
	/// </summary>
	/// <param name="e">The mouse event to inspect.</param>
	/// <param name="cancellationToken">An optional caller cancellation token.</param>
	/// <returns><see langword="true"/> when the input was handled as a navigation; otherwise, <see langword="false"/>.</returns>
	public async Task<bool> TryHandlePointerNavigationAsync(MouseButtonEventArgs e, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(e);

		if (_isDisposed || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || e.ChangedButton != MouseButton.Left)
			return false;

		int hoveredOffset = _getOffsetFromPoint(e.GetPosition(_owner));

		if (hoveredOffset == -1)
			return false;

		using CancellationTokenSource linkedCancellation =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposalCancellation.Token);

		if (!await TryNavigateAsync(hoveredOffset, linkedCancellation.Token).ConfigureAwait(true))
			return false;

		if (_isDisposed)
			return false;

		e.Handled = true;
		return true;
	}

	/// <summary>
	/// Signals cancellation to any in-flight navigation and prevents further navigation. Disposal is
	/// idempotent; a disposed controller must not be reused.
	/// </summary>
	public void Dispose()
	{
		if (_isDisposed)
			return;

		_isDisposed = true;
		_disposalCancellation.Cancel();
		_disposalCancellation.Dispose();
	}

	// Definition navigation is user-triggered, so provider failures are converted to a logged
	// unsuccessful result at the event boundary.
	private async Task<bool> TryNavigateAsync(int offset, CancellationToken cancellationToken)
	{
		try
		{
			return await _tryNavigateAsync(offset, cancellationToken).ConfigureAwait(true);
		}
		catch (OperationCanceledException)
		{
			return false;
		}
		catch (Exception exception)
		{
			s_logDefinitionNavigationFailed(_logger, exception);
			return false;
		}
	}
}

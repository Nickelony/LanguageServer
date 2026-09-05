using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.IDEKit.AvalonEdit.IntelliSense.Presentation;
using Nickelony.IDEKit.Core.Infrastructure;
using Nickelony.IDEKit.IntelliSense.Signatures;

namespace Nickelony.IDEKit.AvalonEdit.IntelliSense.Signatures;

/// <summary>
/// Coordinates shared signature help request state, refresh scheduling, and optional presentation updates.
/// Results from invalidated requests are ignored, so a superseded request cannot replace newer state.
/// </summary>
public sealed class TextSignatureHelpController : IDisposable
{
	private static readonly Action<ILogger, Exception?> s_logSignatureHelpRequestFailed = LoggerMessage.Define(
		LogLevel.Warning,
		new EventId(0),
		"Signature help request failed.");

	private readonly ILogger _logger;
	private readonly Func<int> _getCurrentCaretOffset;
	private readonly Func<int, int, Task<TextSignatureHelpInfo?>> _requestSignatureHelpAsync;
	private readonly Action<TextSignatureHelpInfo> _showSignatureHelp;
	private readonly Action _dismissSignatureHelp;
	private readonly Action<TextSignatureHelpPresentationState>? _applySignatureState;
	private readonly Action? _cancelInFlightRequest;
	private readonly DispatcherTimer _refreshTimer = new();
	private readonly RequestTokenSource _signatureRequestTokens = new();
	private int _pendingSignatureHelpOffset = -1;
	private bool _signatureRefreshPending;
	private bool _signatureRequestInFlight;
	private bool _isVisible;
	private bool _isDisposed;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextSignatureHelpController"/> class.
	/// </summary>
	/// <param name="getCurrentCaretOffset">Gets the current zero-based document caret offset.</param>
	/// <param name="requestSignatureHelpAsync">Requests signature help asynchronously for a zero-based document offset and request token.</param>
	/// <param name="showSignatureHelp">Shows the given signature help information.</param>
	/// <param name="dismissSignatureHelp">Dismisses the current signature help presentation.</param>
	/// <param name="applySignatureState">An optional callback invoked whenever the signature help presentation state changes.</param>
	/// <param name="cancelInFlightRequest">An optional callback used to cancel the active provider request when it is superseded.</param>
	/// <param name="refreshDebounceDelay">The debounce delay for scheduled refreshes.</param>
	/// <param name="logger">An optional logger for request failures.</param>
	public TextSignatureHelpController(
		Func<int> getCurrentCaretOffset,
		Func<int, int, Task<TextSignatureHelpInfo?>> requestSignatureHelpAsync,
		Action<TextSignatureHelpInfo> showSignatureHelp,
		Action dismissSignatureHelp,
		Action<TextSignatureHelpPresentationState>? applySignatureState = null,
		Action? cancelInFlightRequest = null,
		TimeSpan? refreshDebounceDelay = null,
		ILogger? logger = null)
	{
		ArgumentNullException.ThrowIfNull(getCurrentCaretOffset);
		ArgumentNullException.ThrowIfNull(requestSignatureHelpAsync);
		ArgumentNullException.ThrowIfNull(showSignatureHelp);
		ArgumentNullException.ThrowIfNull(dismissSignatureHelp);

		_logger = logger ?? NullLogger.Instance;
		_getCurrentCaretOffset = getCurrentCaretOffset;
		_requestSignatureHelpAsync = requestSignatureHelpAsync;
		_showSignatureHelp = showSignatureHelp;
		_dismissSignatureHelp = dismissSignatureHelp;
		_applySignatureState = applySignatureState;
		_cancelInFlightRequest = cancelInFlightRequest;

		_refreshTimer.Interval = refreshDebounceDelay ?? TimeSpan.FromMilliseconds(50.0);
		_refreshTimer.Tick += RefreshTimer_Tick;
	}

	/// <summary>
	/// Gets the currently displayed signature help state, if any.
	/// </summary>
	public TextSignatureHelpInfo? CurrentSignatureHelp { get; private set; }

	/// <summary>
	/// Gets the current shared signature help presentation state.
	/// </summary>
	public TextSignatureHelpPresentationState CurrentPresentation { get; private set; } = TextSignatureHelpPresentationState.Empty;

	/// <summary>
	/// Gets a value indicating whether signature help is currently visible.
	/// </summary>
	public bool IsVisible => _isVisible;

	/// <summary>
	/// Gets a value indicating whether signature help is visible or any request or refresh is pending.
	/// </summary>
	public bool IsActiveOrPending => _isVisible || _signatureRequestInFlight || _signatureRefreshPending;

	/// <summary>
	/// Dismisses the current signature help presentation and invalidates pending work.
	/// </summary>
	public void Dismiss()
	{
		if (_isDisposed)
			return;

		DismissCore();
	}

	private void DismissCore()
	{
		CancelPendingRefreshCore();
		InvalidateRequestsCore();
		DismissPresentation();
	}

	/// <summary>
	/// Requests signature help at the specified offset. When another request is in flight, the
	/// request is queued for a debounced refresh after the active request finishes.
	/// </summary>
	/// <param name="offset">The zero-based document offset to request signature help for.</param>
	/// <returns>A task that completes after an immediate request finishes or a superseding request has been queued for refresh.</returns>
	public Task RequestAsync(int offset)
	{
		if (_isDisposed)
			return Task.CompletedTask;

		return RequestAsyncCore(offset);
	}

	/// <summary>
	/// Schedules a debounced refresh at the current caret offset.
	/// </summary>
	public void ScheduleRefresh()
	{
		if (_isDisposed)
			return;

		_pendingSignatureHelpOffset = _getCurrentCaretOffset();
		_signatureRefreshPending = true;
		_refreshTimer.Stop();
		_refreshTimer.Start();
		ApplyPresentationState();
	}

	/// <summary>
	/// Cancels any pending debounced refresh.
	/// </summary>
	public void CancelPendingRefresh()
	{
		if (_isDisposed)
			return;

		CancelPendingRefreshCore();
	}

	private void CancelPendingRefreshCore()
	{
		_refreshTimer.Stop();
		_signatureRefreshPending = false;
		_pendingSignatureHelpOffset = -1;
		ApplyPresentationState();
	}

	/// <summary>
	/// Marks outstanding requests as stale so completed results are ignored.
	/// </summary>
	public void InvalidateRequests()
	{
		if (_isDisposed)
			return;

		InvalidateRequestsCore();
	}

	private void InvalidateRequestsCore()
	{
		_signatureRequestTokens.Invalidate();
		_signatureRequestInFlight = false;
		ApplyPresentationState();
	}

	/// <summary>
	/// Dismisses signature help, cancels any pending refresh, and invalidates any in-flight request.
	/// </summary>
	public void Dispose()
	{
		if (_isDisposed)
			return;

		_isDisposed = true;
		_refreshTimer.Stop();
		_refreshTimer.Tick -= RefreshTimer_Tick;
		DismissCore();
	}

	private void ShowSignatureHelp(TextSignatureHelpInfo signatureInfo)
	{
		CurrentSignatureHelp = signatureInfo;
		_showSignatureHelp(signatureInfo);
		_isVisible = true;
		ApplyPresentationState();
	}

	private void DismissPresentation()
	{
		CurrentSignatureHelp = null;
		_dismissSignatureHelp();
		_isVisible = false;
		ApplyPresentationState();
	}

	private void ApplyPresentationState()
	{
		CurrentPresentation = new TextSignatureHelpPresentationState(
			CurrentSignatureHelp,
			_isVisible,
			_signatureRequestInFlight,
			_signatureRefreshPending);
		_applySignatureState?.Invoke(CurrentPresentation);
	}

	private async Task RequestAsyncCore(int offset)
	{
		if (_signatureRequestInFlight)
		{
			// Let the host stop the active provider call when possible, then reject its result as stale.
			_cancelInFlightRequest?.Invoke();
			_signatureRequestTokens.Invalidate();
			_pendingSignatureHelpOffset = offset;
			_signatureRefreshPending = true;
			return;
		}

		_signatureRequestInFlight = true;
		bool wasVisibleAtRequestStart = _isVisible;
		int requestToken = _signatureRequestTokens.Begin();
		ApplyPresentationState();

		try
		{
			TextSignatureHelpInfo? signatureInfo = await _requestSignatureHelpAsync(offset, requestToken).ConfigureAwait(true);

			if (_isDisposed || !_signatureRequestTokens.IsCurrent(requestToken))
				return;

			if (signatureInfo is null)
			{
				if (!wasVisibleAtRequestStart)
					DismissPresentation();

				return;
			}

			ShowSignatureHelp(signatureInfo);
		}
		catch (OperationCanceledException)
		{ }
		catch (Exception exception)
		{
			if (_isDisposed)
				return;

			s_logSignatureHelpRequestFailed(_logger, exception);
		}
		finally
		{
			_signatureRequestInFlight = false;

			if (!_isDisposed)
			{
				ApplyPresentationState();

				if (_signatureRefreshPending && _pendingSignatureHelpOffset >= 0)
				{
					_refreshTimer.Stop();
					_refreshTimer.Start();
				}
			}
		}
	}

	private async void RefreshTimer_Tick(object? sender, EventArgs e)
	{
		_refreshTimer.Stop();

		if (_isDisposed || !_signatureRefreshPending || _pendingSignatureHelpOffset < 0)
			return;

		if (_signatureRequestInFlight)
			return;

		int offset = _pendingSignatureHelpOffset;
		_signatureRefreshPending = false;
		await RequestAsyncCore(offset).ConfigureAwait(true);
	}
}

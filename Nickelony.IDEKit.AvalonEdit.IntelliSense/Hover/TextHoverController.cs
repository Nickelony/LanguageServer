using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.IDEKit.AvalonEdit.IntelliSense.Presentation;
using Nickelony.IDEKit.Core.Infrastructure;
using Nickelony.IDEKit.IntelliSense.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Hover;

namespace Nickelony.IDEKit.AvalonEdit.IntelliSense.Hover;

/// <summary>
/// Coordinates hover requests, request invalidation, and hover-versus-diagnostic tooltip display.
/// Completed results are displayed only when they still correspond to the current pointer position
/// and session generation.
/// </summary>
public sealed class TextHoverController : IDisposable
{
	private static readonly Action<ILogger, Exception?> s_logHoverRequestFailed = LoggerMessage.Define(
		LogLevel.Warning,
		new EventId(0),
		"Hover request failed.");

	private readonly ILogger _logger;
	private readonly FrameworkElement _owner;
	private readonly Func<Point, int> _getOffsetFromPoint;
	private readonly Func<int, TextHoverRequestState> _buildRequestState;
	private readonly Func<int, CancellationToken, Task<TextHoverInfo?>> _requestHoverAsync;
	private readonly Func<int, int?> _getCurrentRequestOffset;
	private readonly Action<TextEditorDiagnostic> _showDiagnosticToolTip;
	private readonly Action<TextHoverInfo> _showHoverToolTip;
	private readonly Action<TextHoverInfo, TextEditorDiagnostic> _showCombinedToolTip;
	private readonly Action<TextHoverPresentationState>? _applyHoverState;
	private readonly Func<int>? _sessionGenerationProvider;

	private CancellationTokenSource? _hoverCancellationTokenSource;
	private readonly RequestTokenSource _hoverRequestTokens = new();
	private bool _isDisposed;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextHoverController"/> class.
	/// </summary>
	/// <param name="owner">The element hover positions are resolved against.</param>
	/// <param name="getOffsetFromPoint">Resolves the zero-based document offset for a point in the owner.</param>
	/// <param name="buildRequestState">Builds the hover request state for a hovered offset.</param>
	/// <param name="requestHoverAsync">Resolves hover information asynchronously for an offset.</param>
	/// <param name="getCurrentRequestOffset">Resolves the current request offset for a hovered offset.</param>
	/// <param name="showDiagnosticToolTip">Shows a diagnostic tooltip.</param>
	/// <param name="showHoverToolTip">Shows a hover tooltip.</param>
	/// <param name="showCombinedToolTip">Shows a combined hover and diagnostic tooltip.</param>
	/// <param name="applyHoverState">An optional callback invoked whenever the hover presentation state changes.</param>
	/// <param name="sessionGenerationProvider">An optional provider of the current session generation.</param>
	/// <param name="logger">An optional logger for request failures.</param>
	public TextHoverController(
		FrameworkElement owner,
		Func<Point, int> getOffsetFromPoint,
		Func<int, TextHoverRequestState> buildRequestState,
		Func<int, CancellationToken, Task<TextHoverInfo?>> requestHoverAsync,
		Func<int, int?> getCurrentRequestOffset,
		Action<TextEditorDiagnostic> showDiagnosticToolTip,
		Action<TextHoverInfo> showHoverToolTip,
		Action<TextHoverInfo, TextEditorDiagnostic> showCombinedToolTip,
		Action<TextHoverPresentationState>? applyHoverState = null,
		Func<int>? sessionGenerationProvider = null,
		ILogger? logger = null)
	{
		ArgumentNullException.ThrowIfNull(owner);
		ArgumentNullException.ThrowIfNull(getOffsetFromPoint);
		ArgumentNullException.ThrowIfNull(buildRequestState);
		ArgumentNullException.ThrowIfNull(requestHoverAsync);
		ArgumentNullException.ThrowIfNull(getCurrentRequestOffset);
		ArgumentNullException.ThrowIfNull(showDiagnosticToolTip);
		ArgumentNullException.ThrowIfNull(showHoverToolTip);
		ArgumentNullException.ThrowIfNull(showCombinedToolTip);

		_logger = logger ?? NullLogger.Instance;
		_owner = owner;
		_getOffsetFromPoint = getOffsetFromPoint;
		_buildRequestState = buildRequestState;
		_requestHoverAsync = requestHoverAsync;
		_getCurrentRequestOffset = getCurrentRequestOffset;
		_showDiagnosticToolTip = showDiagnosticToolTip;
		_showHoverToolTip = showHoverToolTip;
		_showCombinedToolTip = showCombinedToolTip;
		_applyHoverState = applyHoverState;
		_sessionGenerationProvider = sessionGenerationProvider;
	}

	/// <summary>
	/// Gets the current shared hover presentation state.
	/// </summary>
	public TextHoverPresentationState CurrentPresentation { get; private set; } = TextHoverPresentationState.Empty();

	/// <summary>
	/// Handles a mouse-hover event and shows hover or diagnostic content when appropriate.
	/// </summary>
	/// <param name="e">The mouse event to handle.</param>
	public async Task HandleMouseHoverAsync(MouseEventArgs e)
	{
		ArgumentNullException.ThrowIfNull(e);

		if (_isDisposed)
			return;

		int hoveredOffset = -1;
		TextHoverRequestState requestState = default;

		try
		{
			hoveredOffset = _getOffsetFromPoint(e.GetPosition(_owner));

			if (hoveredOffset == -1)
			{
				ApplyHoverState(TextHoverPresentationState.Empty(hoveredOffset));
				return;
			}

			requestState = _buildRequestState(hoveredOffset);

			if (!requestState.ShouldRequestHover)
			{
				ApplyHoverState(CreatePresentationState(hoveredOffset, requestState, null));
				ShowDiagnosticToolTipIfAvailable(requestState);
				return;
			}

			CancellationToken cancellationToken = ResetCancellationTokenSource();
			int hoverRequestToken = _hoverRequestTokens.Begin();
			int sessionGeneration = _sessionGenerationProvider?.Invoke() ?? 0;

			TextHoverInfo? hoverInfo = await _requestHoverAsync(requestState.RequestOffset, cancellationToken).ConfigureAwait(true);

			if (_isDisposed || cancellationToken.IsCancellationRequested || !_hoverRequestTokens.IsCurrent(hoverRequestToken) || !IsSessionGenerationCurrent(sessionGeneration))
				return;

			int currentHoveredOffset = _getOffsetFromPoint(Mouse.GetPosition(_owner));

			if (currentHoveredOffset == -1)
				return;

			int? currentRequestOffset = _getCurrentRequestOffset(currentHoveredOffset);

			if (!currentRequestOffset.HasValue || currentRequestOffset.Value != requestState.RequestOffset)
				return;

			TextHoverRequestState displayState = _buildRequestState(currentHoveredOffset);
			ShowBestToolTip(hoverInfo, displayState);
			ApplyHoverState(CreatePresentationState(currentHoveredOffset, displayState, GetDisplayableHoverInfo(hoverInfo)));
		}
		catch (OperationCanceledException)
		{ }
		catch (Exception exception)
		{
			if (_isDisposed)
				return;

			s_logHoverRequestFailed(_logger, exception);

			if (hoveredOffset >= 0)
			{
				ShowDiagnosticToolTipIfAvailable(requestState);
				ApplyHoverState(CreatePresentationState(hoveredOffset, requestState, null));
			}
		}
	}

	/// <summary>
	/// Cancels the current hover request, if one is still in flight.
	/// </summary>
	public void CancelPendingRequest()
	{
		if (_isDisposed)
			return;

		CancelPendingRequestCore();
	}

	private void CancelPendingRequestCore()
	{
		_hoverCancellationTokenSource?.Cancel();
		_hoverCancellationTokenSource?.Dispose();
		_hoverCancellationTokenSource = null;
	}

	/// <summary>
	/// Marks outstanding hover requests as stale so completed results are ignored.
	/// </summary>
	public void InvalidateRequests()
	{
		if (_isDisposed)
			return;

		_hoverRequestTokens.Invalidate();
	}

	/// <summary>
	/// Cancels the in-flight hover request and marks outstanding work as stale.
	/// </summary>
	public void Dispose()
	{
		if (_isDisposed)
			return;

		_isDisposed = true;
		CancelPendingRequestCore();
		_hoverRequestTokens.Invalidate();
	}

	private bool IsSessionGenerationCurrent(int sessionGeneration)
		=> _sessionGenerationProvider is null || sessionGeneration == _sessionGenerationProvider();

	private void ApplyHoverState(TextHoverPresentationState state)
	{
		CurrentPresentation = state;
		_applyHoverState?.Invoke(state);
	}

	private static TextHoverPresentationState CreatePresentationState(int hoveredOffset, TextHoverRequestState requestState, TextHoverInfo? hoverInfo)
	{
		return new(
			HoveredOffset: hoveredOffset,
			RequestOffset: requestState.ShouldRequestHover ? requestState.RequestOffset : -1,
			HoverInfo: hoverInfo,
			DiagnosticInfo: requestState.DiagnosticInfo,
			CanShowToolTip: requestState.CanShowToolTip,
			CanShowDiagnosticFallback: requestState.CanShowDiagnosticFallback);
	}

	private static TextHoverInfo? GetDisplayableHoverInfo(TextHoverInfo? hoverInfo)
	{
		if (hoverInfo is null || string.IsNullOrWhiteSpace(hoverInfo.Content))
			return null;

		return hoverInfo;
	}

	private CancellationToken ResetCancellationTokenSource()
	{
		_hoverCancellationTokenSource?.Cancel();
		_hoverCancellationTokenSource?.Dispose();
		_hoverCancellationTokenSource = new CancellationTokenSource();
		return _hoverCancellationTokenSource.Token;
	}

	private void ShowDiagnosticToolTipIfAvailable(TextHoverRequestState requestState)
	{
		if (!requestState.CanShowDiagnosticFallback)
			return;

		TextEditorDiagnostic? diagnosticInfo = GetDisplayableDiagnosticInfo(requestState.DiagnosticInfo);

		if (diagnosticInfo is not null)
			_showDiagnosticToolTip(diagnosticInfo);
	}

	private void ShowBestToolTip(TextHoverInfo? hoverInfo, TextHoverRequestState requestState)
	{
		if (!requestState.CanShowToolTip)
			return;

		TextHoverInfo? displayableHoverInfo = GetDisplayableHoverInfo(hoverInfo);
		TextEditorDiagnostic? displayableDiagnosticInfo = GetDisplayableDiagnosticInfo(requestState.DiagnosticInfo);

		if (displayableHoverInfo is not null && displayableDiagnosticInfo is not null)
			_showCombinedToolTip(displayableHoverInfo, displayableDiagnosticInfo);
		else if (displayableHoverInfo is not null)
			_showHoverToolTip(displayableHoverInfo);
		else if (displayableDiagnosticInfo is not null)
			_showDiagnosticToolTip(displayableDiagnosticInfo);
	}

	private static TextEditorDiagnostic? GetDisplayableDiagnosticInfo(TextEditorDiagnostic? diagnosticInfo)
	{
		if (diagnosticInfo is null || string.IsNullOrWhiteSpace(diagnosticInfo.Message))
			return null;

		return diagnosticInfo;
	}
}

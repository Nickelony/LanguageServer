using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.CodeCompletion;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.IDEKit.AvalonEdit.Documents;
using Nickelony.IDEKit.AvalonEdit.IntelliSense.Presentation;
using Nickelony.IDEKit.Core.Infrastructure;
using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.IDEKit.AvalonEdit.IntelliSense.Completion;

/// <summary>
/// Carries the sizing and timing options used by the <see cref="TextCompletionController"/>.
/// </summary>
/// <param name="RequestDebounceDelay">The debounce delay before a scheduled completion request runs.</param>
/// <param name="ToolTipResolveDelay">The delay before a completion tooltip update is resolved.</param>
/// <param name="WindowMinWidth">The minimum width of the completion window.</param>
/// <param name="WindowMaxWidth">The maximum width of the completion window.</param>
/// <param name="WindowHeight">The height of the completion window.</param>
/// <param name="WidthMeasurementSampleCount">The number of samples used to measure the completion item width.</param>
/// <param name="ToolTipHorizontalOffset">The horizontal offset of the completion tooltip.</param>
/// <param name="WindowHorizontalChrome">The horizontal chrome width of the completion window.</param>
/// <param name="ItemIconWidth">The width reserved for the completion item icon.</param>
/// <param name="ItemDetailSpacing">The spacing between completion item details.</param>
public sealed record TextCompletionControllerOptions(
	TimeSpan RequestDebounceDelay,
	TimeSpan ToolTipResolveDelay,
	int WindowMinWidth,
	int WindowMaxWidth,
	int WindowHeight,
	int WidthMeasurementSampleCount,
	double ToolTipHorizontalOffset,
	double WindowHorizontalChrome,
	double ItemIconWidth,
	double ItemDetailSpacing)
{
	/// <summary>
	/// Gets the default completion controller options.
	/// </summary>
	public static TextCompletionControllerOptions Default { get; } = new(
		TimeSpan.FromMilliseconds(120.0),
		TimeSpan.FromMilliseconds(120.0),
		420,
		920,
		320,
		80,
		10.0,
		52.0,
		24.0,
		12.0);
}

/// <summary>
/// Coordinates shared completion popup lifecycle, tooltip ownership, sizing, and request scheduling.
/// The popup is owned through a <see cref="CompletionWindowCoordinator"/>, which tracks at most one
/// window and replaces the previous window when a new one is opened.
/// </summary>
public sealed class TextCompletionController : IDisposable
{
	private static readonly Action<ILogger, Exception?> s_logCompletionRequestFailed = LoggerMessage.Define(
		LogLevel.Warning,
		new EventId(0),
		"Completion request failed.");

	private static readonly Action<ILogger, Exception?> s_logCompletionTooltipResolutionFailed = LoggerMessage.Define(
		LogLevel.Warning,
		new EventId(0),
		"Failed to resolve completion tooltip content.");

	private readonly ILogger _logger;
	private readonly ICSharpCode.AvalonEdit.TextEditor _editor;
	private readonly CompletionWindowCoordinator _windowCoordinator;
	private readonly TextCompletionControllerOptions _options;
	private readonly Action<TextCompletionPresentationState>? _applyPresentationState;
	private readonly Action<CompletionWindow>? _configureWindow;
	private readonly Func<TextCompletionItem, ICompletionData>? _completionItemFactory;
	private readonly Func<ICompletionData, Task<object?>?>? _resolveDescriptionAsync;
	private readonly Func<ICompletionData, (string Text, string? Detail)>? _getDisplayInfo;
	private readonly Brush? _toolTipBackground;
	private readonly Brush? _toolTipBorder;
	private readonly DispatcherTimer _requestTimer = new();
	private readonly DispatcherTimer _toolTipUpdateTimer = new();
	private Func<Task>? _scheduledRequestAsync;
	private ToolTip? _pendingCompletionToolTip;
	private readonly RequestTokenSource _requestTokens = new();
	private CancellationTokenSource? _requestCancellation;
	private int _toolTipUpdateToken;
	private bool _isDisposed;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextCompletionController"/> class.
	/// </summary>
	/// <param name="editor">The editor the controller serves.</param>
	/// <param name="windowCoordinator">The completion window coordinator that owns the window lifecycle.</param>
	/// <param name="options">The controller options, or <see langword="null"/> to use the defaults.</param>
	/// <param name="applyPresentationState">An optional callback that applies the completion presentation state.</param>
	/// <param name="configureWindow">An optional callback that configures a completion window before it is shown.</param>
	/// <param name="completionItemFactory">An optional factory that maps provider items to completion data.</param>
	/// <param name="resolveDescriptionAsync">An optional callback that resolves an item's description asynchronously.</param>
	/// <param name="getDisplayInfo">An optional callback that resolves the display text and detail used for width measurement.</param>
	/// <param name="toolTipBackground">The optional completion tooltip background brush.</param>
	/// <param name="toolTipBorder">The optional completion tooltip border brush.</param>
	/// <param name="logger">An optional logger for request failures.</param>
	public TextCompletionController(
		ICSharpCode.AvalonEdit.TextEditor editor,
		CompletionWindowCoordinator windowCoordinator,
		TextCompletionControllerOptions? options = null,
		Action<TextCompletionPresentationState>? applyPresentationState = null,
		Action<CompletionWindow>? configureWindow = null,
		Func<TextCompletionItem, ICompletionData>? completionItemFactory = null,
		Func<ICompletionData, Task<object?>?>? resolveDescriptionAsync = null,
		Func<ICompletionData, (string Text, string? Detail)>? getDisplayInfo = null,
		Brush? toolTipBackground = null,
		Brush? toolTipBorder = null,
		ILogger? logger = null)
	{
		ArgumentNullException.ThrowIfNull(editor);
		ArgumentNullException.ThrowIfNull(windowCoordinator);

		_logger = logger ?? NullLogger.Instance;
		_editor = editor;
		_windowCoordinator = windowCoordinator;
		_options = options ?? TextCompletionControllerOptions.Default;
		_applyPresentationState = applyPresentationState;
		_configureWindow = configureWindow;
		_completionItemFactory = completionItemFactory;
		_resolveDescriptionAsync = resolveDescriptionAsync;
		_getDisplayInfo = getDisplayInfo;
		_toolTipBackground = toolTipBackground;
		_toolTipBorder = toolTipBorder;
	}

	/// <summary>
	/// Gets the current shared completion presentation state.
	/// </summary>
	public TextCompletionPresentationState CurrentPresentation { get; private set; } = TextCompletionPresentationState.Empty;

	/// <summary>
	/// Initializes request scheduling with the callback used to run a scheduled request.
	/// This method also configures the debounce timers used by request and tooltip scheduling.
	/// </summary>
	/// <param name="scheduledRequestAsync">The callback that runs a scheduled completion request.</param>
	public void InitializeScheduling(Func<Task> scheduledRequestAsync)
	{
		ArgumentNullException.ThrowIfNull(scheduledRequestAsync);

		if (_isDisposed)
			return;

		_scheduledRequestAsync = scheduledRequestAsync;
		_requestTimer.Interval = _options.RequestDebounceDelay;
		_requestTimer.Tick -= RequestTimer_Tick;
		_requestTimer.Tick += RequestTimer_Tick;

		_toolTipUpdateTimer.Interval = _options.ToolTipResolveDelay;
		_toolTipUpdateTimer.Tick -= ToolTipUpdateTimer_Tick;
		_toolTipUpdateTimer.Tick += ToolTipUpdateTimer_Tick;
	}

	/// <summary>
	/// Gets the currently tracked completion window, or <see langword="null"/> when none is tracked or
	/// the controller has been disposed.
	/// </summary>
	public CompletionWindow? ActiveWindow => _isDisposed ? null : _windowCoordinator.ActiveWindow;

	/// <summary>
	/// Gets the cancellation token for the current request, or <see cref="CancellationToken.None"/>
	/// when no current request exists. The token is cancelled when requests are invalidated, when a
	/// newer request begins, or when the controller is disposed.
	/// </summary>
	public CancellationToken CurrentRequestCancellationToken
		=> _requestCancellation?.Token ?? CancellationToken.None;

	/// <summary>
	/// Begins a new request and returns its token.
	/// </summary>
	/// <returns>The token of the new request, or <c>-1</c> when the controller is disposed.</returns>
	public int BeginRequest()
	{
		if (_isDisposed)
			return -1;

		CancelInFlightRequest();
		_requestCancellation = new CancellationTokenSource();
		return _requestTokens.Begin();
	}

	/// <summary>
	/// Determines whether the given request token is still the current request.
	/// </summary>
	/// <param name="requestToken">The request token to check.</param>
	/// <returns><see langword="true"/> when the token is current; otherwise, <see langword="false"/>.</returns>
	public bool IsRequestCurrent(int requestToken)
		=> !_isDisposed && _requestTokens.IsCurrent(requestToken);

	/// <summary>
	/// Invalidates all in-flight completion requests.
	/// </summary>
	public void InvalidateRequests()
	{
		if (_isDisposed)
			return;

		_requestTokens.Invalidate();
		CancelInFlightRequest();
	}

	private void CancelInFlightRequest()
	{
		if (_requestCancellation is null)
			return;

		_requestCancellation.Cancel();
		_requestCancellation.Dispose();
		_requestCancellation = null;
	}

	/// <summary>
	/// Schedules a completion request to run after the configured debounce delay, when scheduling has
	/// been initialized.
	/// </summary>
	public void ScheduleRequest()
	{
		if (_isDisposed || _scheduledRequestAsync is null)
			return;

		_requestTimer.Stop();
		_requestTimer.Start();
		SetRequestScheduled(true);
	}

	/// <summary>
	/// Cancels any pending scheduled completion request.
	/// </summary>
	public void CancelPendingRequest()
	{
		if (_isDisposed)
			return;

		CancelPendingRequestCore();
	}

	private void CancelPendingRequestCore()
	{
		_requestTimer.Stop();
		SetRequestScheduled(false);
	}

	/// <summary>
	/// Closes the active completion window and any completion tooltip.
	/// </summary>
	public void CloseWindow()
	{
		if (_isDisposed)
			return;

		CloseWindowCore();
	}

	private void CloseWindowCore()
	{
		CancelPendingRequestCore();
		CancelTooltipUpdateCore();

		if (_windowCoordinator.ActiveWindow is CompletionWindow completionWindow
			&& CompletionWindowToolTipAccess.TryGetToolTip(completionWindow, out ToolTip? tooltip))
		{
			tooltip.IsOpen = false;
		}

		_windowCoordinator.Close();
		SetWindowState(false);
		SetTooltipState(null, false);
	}

	/// <summary>
	/// Opens a completion window with the given items, replacing any currently tracked window.
	/// The replacement range uses zero-based document offsets with an exclusive end offset.
	/// </summary>
	/// <param name="items">The completion items to show.</param>
	/// <param name="startOffset">The optional zero-based start offset of the replacement range.</param>
	/// <param name="endOffset">The optional exclusive end offset of the replacement range.</param>
	/// <returns><see langword="true"/> when the window was opened; otherwise, <see langword="false"/>.</returns>
	public bool OpenOrRefresh(IEnumerable<ICompletionData> items, int? startOffset = null, int? endOffset = null)
	{
		ArgumentNullException.ThrowIfNull(items);

		if (_isDisposed)
			return false;

		ICompletionData[] completionItems = items.ToArray();

		if (completionItems.Length == 0)
			return false;

		CloseWindow();
		_windowCoordinator.Initialize(_options.WindowMinWidth, _options.WindowHeight);

		if (_windowCoordinator.ActiveWindow is not CompletionWindow completionWindow)
			return false;

		_configureWindow?.Invoke(completionWindow);
		StyleTooltip(completionWindow);
		MakeWindowNonActivatable(completionWindow);
		ResizeWindow(completionWindow, completionItems);

		if (startOffset.HasValue)
			completionWindow.StartOffset = startOffset.Value;

		if (endOffset.HasValue)
			completionWindow.EndOffset = endOffset.Value;

		foreach (ICompletionData item in completionItems)
			completionWindow.CompletionList.CompletionData.Add(item);

		_windowCoordinator.Show();
		SetWindowState(true);
		ScheduleInitialSelection();
		return true;
	}

	/// <summary>
	/// Applies a completion session decision to the completion window.
	/// </summary>
	/// <param name="decision">The decision to apply.</param>
	/// <param name="mapItem">An optional mapper from provider items to completion data.</param>
	/// <returns><see langword="true"/> when a completion window was opened; otherwise, <see langword="false"/>.</returns>
	public bool ApplyDecision(TextCompletionSessionDecision decision, Func<TextCompletionItem, ICompletionData>? mapItem = null)
	{
		if (_isDisposed)
			return false;

		if (decision.CloseWindow)
			CloseWindow();

		if (decision.Items is null || !decision.StartOffset.HasValue || !decision.EndOffset.HasValue)
			return false;

		mapItem ??= _completionItemFactory;

		if (mapItem is null)
			return false;

		ICompletionData[] items = decision.Items.Select(mapItem).ToArray();
		return OpenOrRefresh(items, decision.StartOffset.Value, decision.EndOffset.Value);
	}

	/// <summary>
	/// Schedules the completion window to close if it is empty.
	/// </summary>
	public void ScheduleCloseIfEmpty()
	{
		if (_isDisposed)
			return;

		_editor.Dispatcher.BeginInvoke(new Action(() => CloseWindowIfEmpty()), DispatcherPriority.Background);
	}

	/// <summary>
	/// Cancels any pending completion tooltip update.
	/// </summary>
	public void CancelTooltipUpdate()
	{
		if (_isDisposed)
			return;

		CancelTooltipUpdateCore();
	}

	private void CancelTooltipUpdateCore()
	{
		_toolTipUpdateToken++;
		_pendingCompletionToolTip = null;
		_toolTipUpdateTimer.Stop();
	}

	/// <summary>
	/// Stops completion scheduling and closes any open completion window or tooltip.
	/// </summary>
	public void Dispose()
	{
		if (_isDisposed)
			return;

		_isDisposed = true;
		_requestTimer.Stop();
		_requestTimer.Tick -= RequestTimer_Tick;
		_toolTipUpdateTimer.Stop();
		_toolTipUpdateTimer.Tick -= ToolTipUpdateTimer_Tick;
		_requestTokens.Invalidate();
		CancelInFlightRequest();
		CloseWindowCore();
	}

	private async void RequestTimer_Tick(object? sender, EventArgs e)
	{
		_requestTimer.Stop();
		SetRequestScheduled(false);

		if (_scheduledRequestAsync is null)
			return;

		try
		{
			await _scheduledRequestAsync().ConfigureAwait(true);
		}
		catch (Exception exception)
		{
			if (_isDisposed)
				return;

			s_logCompletionRequestFailed(_logger, exception);
		}
	}

	private async void ToolTipUpdateTimer_Tick(object? sender, EventArgs e)
	{
		_toolTipUpdateTimer.Stop();

		if (_pendingCompletionToolTip is not ToolTip tooltip)
			return;

		await UpdateTooltipAsync(tooltip, _toolTipUpdateToken).ConfigureAwait(true);
	}

	private void StyleTooltip(CompletionWindow completionWindow)
	{
		if (completionWindow.CompletionList.ListBox is not ListBox listBox)
			return;

		if (!CompletionWindowToolTipAccess.TryGetToolTip(completionWindow, out ToolTip? tooltip))
			return;

		if (_toolTipBackground is not null)
			tooltip.Background = _toolTipBackground;

		if (_toolTipBorder is not null)
			tooltip.BorderBrush = _toolTipBorder;

		tooltip.BorderThickness = new Thickness(0.0);
		tooltip.Padding = new Thickness(0.0);
		tooltip.PlacementTarget = listBox;
		tooltip.Placement = PlacementMode.Right;
		tooltip.HorizontalOffset = _options.ToolTipHorizontalOffset;
		tooltip.StaysOpen = true;

		listBox.SelectionChanged += (s, e) => ScheduleTooltipUpdate(tooltip);
		listBox.PreviewMouseLeftButtonUp += (s, e) => HandleCompletionListClick(listBox, tooltip, e);
	}

	private void HandleCompletionListClick(ListBox listBox, ToolTip tooltip, MouseButtonEventArgs e)
	{
		ListBoxItem? listBoxItem = FindVisualAncestorOrSelf<ListBoxItem>(e.OriginalSource as DependencyObject);

		if (listBoxItem is null)
			return;

		if (!ReferenceEquals(listBox.SelectedItem, listBoxItem.DataContext))
			listBox.SelectedItem = listBoxItem.DataContext;

		listBox.ScrollIntoView(listBoxItem.DataContext);
		ScheduleTooltipUpdate(tooltip);
	}

	private static T? FindVisualAncestorOrSelf<T>(DependencyObject? element) where T : DependencyObject
	{
		while (element is not null)
		{
			if (element is T match)
				return match;

			element = VisualTreeHelper.GetParent(element);
		}

		return null;
	}

	private void ScheduleTooltipUpdate(ToolTip tooltip)
	{
		_pendingCompletionToolTip = tooltip;
		_toolTipUpdateToken++;
		_toolTipUpdateTimer.Stop();
		_toolTipUpdateTimer.Start();
	}

	private async Task UpdateTooltipAsync(ToolTip tooltip, int updateToken)
	{
		if (_isDisposed || ActiveWindow?.CompletionList.ListBox is not ListBox listBox)
			return;

		if (listBox.SelectedItem is not ICompletionData item)
		{
			tooltip.IsOpen = false;
			SetTooltipState(null, false);
			return;
		}

		try
		{
			object? description = item.Description;

			if (description is not null)
				ApplyTooltipContent(tooltip, description);
			else
			{
				tooltip.IsOpen = false;
				SetTooltipState(null, false);
			}

			if (_resolveDescriptionAsync is not null)
			{
				if (updateToken != _toolTipUpdateToken)
					return;

				Task<object?>? resolveTask = _resolveDescriptionAsync(item);

				if (resolveTask is null)
					return;

				object? resolvedDescription = await resolveTask.ConfigureAwait(true);

				if (_isDisposed || updateToken != _toolTipUpdateToken)
					return;

				if (ActiveWindow?.CompletionList.ListBox is not ListBox currentListBox
					|| !ReferenceEquals(currentListBox, listBox)
					|| !ReferenceEquals(currentListBox.SelectedItem, item))
				{
					return;
				}

				if (resolvedDescription is not null)
					ApplyTooltipContent(tooltip, resolvedDescription);
				else
				{
					tooltip.IsOpen = false;
					SetTooltipState(null, false);
				}
			}
		}
		catch (Exception exception)
		{
			if (_isDisposed)
				return;

			tooltip.IsOpen = false;
			SetTooltipState(null, false);

			s_logCompletionTooltipResolutionFailed(_logger, exception);
		}
	}

	private void SetRequestScheduled(bool isRequestScheduled)
		=> ApplyPresentationState(CurrentPresentation with { IsRequestScheduled = isRequestScheduled });

	private void SetWindowState(bool hasOpenWindow)
		=> ApplyPresentationState(CurrentPresentation with { HasOpenWindow = hasOpenWindow });

	private void SetTooltipState(object? toolTipContent, bool isToolTipVisible)
		=> ApplyPresentationState(CurrentPresentation with { ToolTipContent = toolTipContent, IsToolTipVisible = isToolTipVisible });

	private void ApplyPresentationState(TextCompletionPresentationState state)
	{
		CurrentPresentation = state;
		_applyPresentationState?.Invoke(state);
	}

	private void ResizeWindow(CompletionWindow completionWindow, ICompletionData[] items)
	{
		double requiredWidth = _options.WindowMinWidth;
		int measurementCount = Math.Min(items.Length, _options.WidthMeasurementSampleCount);
		var textWidthCache = new Dictionary<string, double>(StringComparer.Ordinal);

		for (int i = 0; i < measurementCount; i++)
			requiredWidth = Math.Max(requiredWidth, MeasureItemWidth(items[i], textWidthCache));

		completionWindow.Width = Math.Max(
			_options.WindowMinWidth,
			Math.Min(_options.WindowMaxWidth, requiredWidth + _options.WindowHorizontalChrome));
	}

	private double MeasureItemWidth(ICompletionData completionData, IDictionary<string, double> textWidthCache)
	{
		(string text, string? detail) = _getDisplayInfo is not null
			? _getDisplayInfo(completionData)
			: (completionData.Text, null);

		double width = _options.ItemIconWidth + MeasureTextWidth(text, textWidthCache);

		if (!string.IsNullOrWhiteSpace(detail))
			width += _options.ItemDetailSpacing + MeasureTextWidth(detail, textWidthCache);

		return width;
	}

	private double MeasureTextWidth(string text, IDictionary<string, double> textWidthCache)
	{
		if (string.IsNullOrWhiteSpace(text))
			return 0.0;

		if (textWidthCache.TryGetValue(text, out double cachedWidth))
			return cachedWidth;

		double pixelsPerDip = VisualTreeHelper.GetDpi(_editor).PixelsPerDip;

		var formattedText = new FormattedText(
			text,
			CultureInfo.CurrentUICulture,
			FlowDirection.LeftToRight,
			new Typeface(_editor.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
			_editor.FontSize,
			_editor.Foreground,
			pixelsPerDip);

		double width = formattedText.WidthIncludingTrailingWhitespace;
		textWidthCache[text] = width;
		return width;
	}

	private void ApplyTooltipContent(ToolTip tooltip, object content)
	{
		tooltip.Content = content;
		SetTooltipState(content, true);

		if (!tooltip.IsOpen)
		{
			tooltip.IsOpen = true;
		}
		else
		{
			tooltip.InvalidateMeasure();
			tooltip.InvalidateVisual();
		}
	}

	private void ScheduleInitialSelection()
		=> _editor.Dispatcher.BeginInvoke(new Action(SelectInitialItem), DispatcherPriority.ContextIdle);

	private void SelectInitialItem()
	{
		if (ActiveWindow is not CompletionWindow completionWindow)
			return;

		completionWindow.CompletionList.SelectItem(GetCompletionWindowQuery(completionWindow));
		CloseWindowIfEmpty();
	}

	private static void MakeWindowNonActivatable(CompletionWindow completionWindow)
	{
		completionWindow.SourceInitialized += (s, e) =>
		{
			if (s is Window window && PresentationSource.FromVisual(window) is HwndSource source)
				source.AddHook(CompletionWindowWndProc);
		};
	}

	private static IntPtr CompletionWindowWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
	{
		const int MouseActivateNoActivate = 3;
		const int WindowMessageMouseActivate = 0x0021;

		if (msg == WindowMessageMouseActivate)
		{
			handled = true;
			return new IntPtr(MouseActivateNoActivate);
		}

		return IntPtr.Zero;
	}

	private bool CloseWindowIfEmpty()
	{
		if (ActiveWindow is not CompletionWindow completionWindow)
			return false;

		ListBox listBox = completionWindow.CompletionList.ListBox;

		if (listBox?.HasItems != false)
			return false;

		CloseWindow();
		return true;
	}

	private string GetCompletionWindowQuery(CompletionWindow completionWindow)
	{
		if (_editor.Document is null)
			return string.Empty;

		int startOffset = _editor.Document.ClampOffset(completionWindow.StartOffset);
		int endOffset = Math.Max(startOffset, Math.Min(completionWindow.EndOffset, _editor.Document.TextLength));

		return endOffset > startOffset
			? _editor.Document.GetText(startOffset, endOffset - startOffset)
			: string.Empty;
	}
}

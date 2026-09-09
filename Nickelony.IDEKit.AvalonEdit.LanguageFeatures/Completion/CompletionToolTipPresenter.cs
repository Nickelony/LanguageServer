using ICSharpCode.AvalonEdit.CodeCompletion;
using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.Core.Requests;
using Nickelony.IDEKit.Infrastructure;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

/// <summary>
/// Owns the completion tooltip pipeline: tooltip access and chrome styling, the debounced description
/// resolution, and the tooltip reporting that feeds the completion presentation state.
/// </summary>
/// <remarks>
/// <para>
/// The presenter is created and used on the editor thread, because its resolve timer runs on the creating
/// thread's dispatcher and the tooltip it styles belongs to a WPF completion window.
/// </para>
/// <para>
/// The AvalonEdit completion window does not expose its tooltip publicly, so the tooltip is resolved through
/// <see cref="CompletionWindowToolTipAccess"/>, which reflects the private AvalonEdit field. When the field is
/// unavailable, tooltip styling and description resolution are disabled, and the first failure is logged once
/// so an AvalonEdit upgrade becomes visible to the host.
/// </para>
/// <para>
/// The tooltip is decorated rather than replaced: AvalonEdit's stock selection-changed handler writes the same
/// tooltip instance for every item description and cannot be disabled without touching the private field, so a
/// host-owned second tooltip would either render next to the stock one or still depend on that field. Keeping
/// one tooltip means the stock writer and this presenter must render strings identically; that parity is a
/// deliberate, permanent constraint of sharing an engine-owned surface, kept pinned by a rendering-parity
/// test so an AvalonEdit rendering change fails loudly. Replacing the decoration with a host-owned details
/// surface (or an upstream public tooltip) is the only alternative.
/// </para>
/// <para>
/// The presenter does not own the completion presentation state; it reports tooltip visibility and content
/// through the state sink callback supplied by the completion controller, and it reads the tracked window
/// through the controller's active-window accessor. Both callbacks keep the presenter independent of the
/// controller instance.
/// </para>
/// </remarks>
internal sealed class CompletionToolTipPresenter : IDisposable
{
	// The completion tooltip uses package log event ids 1001 (resolve failed) and 1002 (unsupported
	// access); the README carries the canonical id table.
	private static readonly Action<ILogger, Exception?> s_logToolTipResolutionFailed = LoggerMessage.Define(
		LogLevel.Warning,
		new EventId(1001, "ToolTipResolutionFailed"),
		"Failed to resolve completion tooltip content.");

	private static readonly Action<ILogger, Exception?> s_logToolTipAccessUnsupported = LoggerMessage.Define(
		LogLevel.Warning,
		new EventId(1002, "ToolTipAccessUnsupported"),
		"The AvalonEdit completion window tooltip field was not found, so completion tooltip styling and "
		+ "description resolution are disabled. This usually means the AvalonEdit version is newer than the one "
		+ "this package was built against.");

	private readonly ILogger _logger;
	private readonly Dispatcher _dispatcher;
	private readonly CompletionToolTipSkin _skin;
	private readonly Func<CompletionWindow?> _getActiveWindow;
	private readonly Action<object?, bool> _setToolTipState;
	private readonly Func<ICompletionData, CancellationToken, Task<object?>>? _resolveDescriptionAsync;
	private readonly Action<ToolTip>? _configureToolTip;
	private readonly DispatcherDebouncer _updateDebouncer;
	private readonly RequestTokenSource _updateTokens = new();
	private CancellationTokenSource? _resolveCancellation;
	private bool _accessWarningLogged;
	private bool _isDisposed;

	/// <summary>
	/// Initializes a new instance of the <see cref="CompletionToolTipPresenter"/> class.
	/// </summary>
	/// <param name="options">The controller options the tooltip timing is read from.</param>
	/// <param name="skin">The tooltip chrome applied to each window's tooltip.</param>
	/// <param name="dispatcher">The editor dispatcher the tooltip pipeline runs on.</param>
	/// <param name="getActiveWindow">
	/// The accessor for the currently tracked completion window, or <see langword="null"/> when none is tracked.
	/// </param>
	/// <param name="setToolTipState">
	/// The sink that reports tooltip visibility and content to the completion presentation state.
	/// </param>
	/// <param name="hooks">
	/// The controller hooks the description resolver and the tooltip configuration hook are read from, or
	/// <see langword="null"/> when the host supplied none.
	/// </param>
	/// <param name="logger">The logger for tooltip failures.</param>
	internal CompletionToolTipPresenter(
		TextCompletionControllerOptions options,
		CompletionToolTipSkin skin,
		Dispatcher dispatcher,
		Func<CompletionWindow?> getActiveWindow,
		Action<object?, bool> setToolTipState,
		TextCompletionControllerHooks? hooks,
		ILogger logger)
	{
		_logger = logger;
		_dispatcher = dispatcher;
		_skin = skin;
		_getActiveWindow = getActiveWindow;
		_setToolTipState = setToolTipState;
		_resolveDescriptionAsync = hooks?.ResolveDescriptionAsync;
		_configureToolTip = hooks?.ConfigureToolTip;

		// The resolve debouncer and its timer are created on construction, and each selection change arms the
		// timer, so debounced description updates work even when the host schedules no requests. The debouncer
		// is bound to the editor dispatcher explicitly, even when the controller was created on a thread whose
		// dispatcher is not the editor's.
		_updateDebouncer = new DispatcherDebouncer(options.ToolTipResolveDelay, dispatcher);
	}

	/// <summary>
	/// Gets a value indicating whether a debounced tooltip update is waiting for its delay.
	/// </summary>
	internal bool IsUpdatePending => _updateDebouncer.IsPending;

	/// <summary>
	/// Applies the tooltip skin to a newly created completion window and wires the list selection changes
	/// that schedule debounced tooltip updates.
	/// </summary>
	/// <remarks>
	/// The configuration hook supplied to the constructor runs after the skin, so a host can override any of
	/// the skin's values; a throwing hook propagates to the completion controller's window rollback scope
	/// together with the window hook. When the AvalonEdit tooltip field is unavailable, the skin and the hook
	/// are skipped and the one-time warning has been logged.
	/// </remarks>
	/// <param name="completionWindow">The completion window to style.</param>
	internal void Style(CompletionWindow completionWindow)
	{
		ListBox listBox = completionWindow.CompletionList.ListBox;

		if (!CompletionWindowToolTipAccess.TryGetToolTip(completionWindow, out ToolTip? tooltip))
		{
			LogAccessUnsupportedOnce();
			return;
		}

		if (_skin.Background is not null)
			tooltip.Background = _skin.Background;

		if (_skin.BorderBrush is not null)
			tooltip.BorderBrush = _skin.BorderBrush;

		tooltip.BorderThickness = _skin.BorderThickness;
		tooltip.Padding = _skin.Padding;
		tooltip.PlacementTarget = listBox;
		tooltip.Placement = _skin.Placement;
		tooltip.HorizontalOffset = _skin.HorizontalOffset;

		// The hook runs after the skin so a host can override placement, offsets, and padding.
		_configureToolTip?.Invoke(tooltip);

		// AvalonEdit's stock selection-changed handler already opens the tooltip for any non-null description
		// (wrapping only strings in a TextBlock) and fires immediately. This subscription adds a debounced
		// pass that can await ResolveDescriptionAsync and report the tooltip content through the completion
		// presentation state.
		listBox.SelectionChanged += (s, e) => ScheduleUpdate(tooltip);
	}

	/// <summary>
	/// Closes the tooltip of the given completion window when the tooltip is accessible.
	/// </summary>
	/// <remarks>
	/// AvalonEdit's own window close also closes the tooltip; closing it here keeps the tracked tooltip state
	/// consistent before the window is closed and untracked. The method is static because it needs no
	/// presenter state; the reflected field is cached, so the per-call cost is a field read.
	/// </remarks>
	/// <param name="completionWindow">The completion window whose tooltip is closed.</param>
	internal static void CloseToolTip(CompletionWindow completionWindow)
	{
		if (CompletionWindowToolTipAccess.TryGetToolTip(completionWindow, out ToolTip? tooltip))
			tooltip.IsOpen = false;
	}

	/// <summary>
	/// Cancels a pending or in-flight tooltip update.
	/// </summary>
	/// <remarks>
	/// A running resolver observes the cancellation through the token passed to it; a result it produces after
	/// the cancellation is discarded by the update token.
	/// </remarks>
	internal void CancelUpdate()
	{
		_updateTokens.Invalidate();
		_updateDebouncer.Cancel();
		CancelResolveCancellation();
	}

	/// <summary>
	/// Stops the tooltip update timer and cancels pending tooltip work.
	/// </summary>
	public void Dispose()
	{
		if (_isDisposed)
			return;

		_isDisposed = true;
		_updateDebouncer.Dispose();
		CancelUpdate();
	}

	// A dispatcher debounce callback that runs the asynchronous update; like a timer handler it is an async
	// void dispatcher method, so the update itself contains every failure.
	private async void RunUpdate(ToolTip tooltip, long updateToken)
	{
		var resolveCancellation = new CancellationTokenSource();
		_resolveCancellation = resolveCancellation;

		await UpdateAsync(tooltip, updateToken, resolveCancellation).ConfigureAwait(true);
	}

	// A selection change schedules one debounced update; starting a new update invalidates the previous
	// token and cancels a resolver that is still running.
	private void ScheduleUpdate(ToolTip tooltip)
	{
		long updateToken = _updateTokens.BeginRequest();
		CancelResolveCancellation();
		_updateDebouncer.Arm(() => RunUpdate(tooltip, updateToken));
	}

	private async Task UpdateAsync(ToolTip tooltip, long updateToken, CancellationTokenSource resolveCancellation)
	{
		try
		{
			CancellationToken cancellationToken = resolveCancellation.Token;
			CompletionWindow? activeWindow = _getActiveWindow();

			if (_isDisposed || cancellationToken.IsCancellationRequested || activeWindow is null)
				return;

			// The tooltip belongs to the window that was styled when this update was scheduled, while the list
			// state is read from the currently active window; ApplyResolvedDescription re-matches window, list,
			// and selected item after the await before any content is written, so a window swap during the
			// resolve cannot publish stale content into the old tooltip.
			ListBox listBox = activeWindow.CompletionList.ListBox;

			if (listBox.SelectedItem is not ICompletionData item)
			{
				tooltip.IsOpen = false;
				_setToolTipState(null, false);
				return;
			}

			object? description = item.Description;

			if (description is not null)
				ApplyContent(tooltip, description);
			else
			{
				tooltip.IsOpen = false;
				_setToolTipState(null, false);
			}

			if (_resolveDescriptionAsync is not null)
			{
				if (!_updateTokens.IsCurrent(updateToken))
					return;

				object? resolvedDescription = await _resolveDescriptionAsync(item, cancellationToken).ConfigureAwait(true);

				// The resolver's continuation resumes on the captured context when the creating thread has one.
				// Without a context (a manually pumped dispatcher, tooling, tests) it resumes on a thread-pool
				// thread, so the tooltip-affine remainder is marshalled to the editor thread explicitly.
				await DispatcherInvocation.RunAsync(
					_dispatcher,
					() => ApplyResolvedDescription(tooltip, updateToken, listBox, item, resolvedDescription, cancellationToken));
			}
		}
		catch (OperationCanceledException)
		{
			// A superseded or canceled resolve does not report a failure.
		}
		catch (Exception exception)
		{
			if (_isDisposed)
				return;

			tooltip.IsOpen = false;
			_setToolTipState(null, false);

			s_logToolTipResolutionFailed(_logger, exception);
		}
		finally
		{
			// The update owns the token, so disposing its source here is safe even when the resolve was
			// canceled while it ran.
			resolveCancellation.Dispose();

			if (ReferenceEquals(_resolveCancellation, resolveCancellation))
				_resolveCancellation = null;
		}
	}

	// Applies or drops a resolved description after the resolver's token and the update token were checked.
	// Runs on the editor thread so the tooltip access and the presentation state sink stay thread-affine.
	private void ApplyResolvedDescription(
		ToolTip tooltip,
		long updateToken,
		ListBox listBox,
		ICompletionData item,
		object? resolvedDescription,
		CancellationToken cancellationToken)
	{
		if (_isDisposed || cancellationToken.IsCancellationRequested || !_updateTokens.IsCurrent(updateToken))
			return;

		CompletionWindow? currentWindow = _getActiveWindow();

		if (currentWindow is null
			|| !ReferenceEquals(currentWindow.CompletionList.ListBox, listBox)
			|| !ReferenceEquals(currentWindow.CompletionList.ListBox.SelectedItem, item))
		{
			return;
		}

		if (resolvedDescription is not null)
			ApplyContent(tooltip, resolvedDescription);
		else
		{
			tooltip.IsOpen = false;
			_setToolTipState(null, false);
		}
	}

	private void ApplyContent(ToolTip tooltip, object content)
	{
		// Two writers update this tooltip: AvalonEdit's stock selection-changed handler (which renders strings in
		// a wrapping TextBlock) and this debounced path, which additionally awaits the description resolver and
		// updates the presentation state. Reproduce the stock wrapping so both writers render identically.
		object toolTipContent = content is string text
			? new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }
			: content;

		tooltip.Content = toolTipContent;
		_setToolTipState(content, true);

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

	// The AvalonEdit tooltip field is reflected; when it disappears the tooltip paths degrade silently, so the
	// first failure per presenter is logged to make an AvalonEdit upgrade visible to the host.
	private void LogAccessUnsupportedOnce()
	{
		if (_accessWarningLogged || CompletionWindowToolTipAccess.IsFieldAvailable)
			return;

		_accessWarningLogged = true;
		s_logToolTipAccessUnsupported(_logger, null);
	}

	// The update that owns the token disposes its source when it returns; canceling only keeps the token
	// safely observable for a resolver that is still running.
	private void CancelResolveCancellation()
	{
		CancellationTokenSource? resolveCancellation = _resolveCancellation;
		_resolveCancellation = null;
		resolveCancellation?.Cancel();
	}
}

using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.IDEKit.AvalonEdit.Documents;
using Nickelony.IDEKit.Core.Notifications;
using Nickelony.IDEKit.Core.Requests;
using Nickelony.IDEKit.Infrastructure;
using Nickelony.IDEKit.IntelliSense.CodeActions;
using System.Windows;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;

/// <summary>
/// Coordinates code-action requests for a text area: it watches the caret, selection, and document,
/// debounces refresh requests through the host hooks, publishes the available actions to the margin
/// indicator (<see cref="Margin"/>), and presents them in a skinned drop-down menu.
/// </summary>
/// <remarks>
/// <para>
/// The controller owns the request policy; the host owns the language-server mapping. For every
/// settled context the controller builds a <see cref="TextCodeActionContext"/> and asks the host's
/// request-state builder which range (if any) to ask about; a <see langword="null"/> state vetoes the
/// request for that context. The context carries a snapshot of the document text, so every settled
/// request copies the document once; a host with very large documents can raise
/// <see cref="TextCodeActionControllerOptions.RequestDebounceDelay"/> to reduce how often that happens.
/// A completed result is published only when no newer context change invalidated it, and the indicator
/// is rendered on the line the caret is on when the result was published.
/// </para>
/// <para>
/// The controller must be created and used on the thread that owns the text area, because it touches
/// the text area's document and dispatcher and hosts the menu directly. Hook continuations are
/// marshalled back to that thread.
/// </para>
/// <para>
/// The host calls <see cref="RefreshAsync"/> when state the request depends on changed outside the
/// editor - for example after the provider reported new diagnostics, because diagnostic-based quick
/// fixes can become available without a caret move. Editor context changes (caret, selection, and
/// document) schedule a debounced request on their own.
/// </para>
/// <para>
/// Actions are snapshots: the user may invoke an action after the document changed. A host must apply
/// actions through its version-validated edit pipeline, so a stale action is rejected instead of
/// corrupting the document.
/// </para>
/// </remarks>
public sealed class TextCodeActionController : IDisposable, IChangeNotificationSource
{
	// Code actions use package log event ids 1030 and 1031 (see the README for the canonical table).
	private static readonly Action<ILogger, Exception?> s_logRequestFailed = LoggerMessage.Define(
		LogLevel.Warning,
		new EventId(1030, "CodeActionRequestFailed"),
		"The code-action request failed.");

	private static readonly Action<ILogger, Exception?> s_logHostCallbackFailed = LoggerMessage.Define(
		LogLevel.Warning,
		new EventId(1031, "CodeActionHostCallbackFailed"),
		"A code-action host callback failed.");

	private readonly TextArea _textArea;
	private readonly Func<TextCodeActionContext, TextCodeActionRequestState?> _buildRequestState;
	private readonly Func<TextCodeActionRequestState, CancellationToken, Task<IReadOnlyList<TextCodeActionItem>>> _requestCodeActionsAsync;
	private readonly Func<TextCodeActionItem, Task> _executeActionAsync;
	private readonly ILogger _logger;
	private readonly DispatcherDebouncer _debouncer;
	private readonly LatestRequestCoordinator _requests = new();
	private readonly TextCodeActionMenuOptions _menuOptions;
	private readonly TextCodeActionMenuPresenter _menu;
	private readonly List<int> _indicatorLines = [];

	private TextDocument? _document;
	private IReadOnlyList<TextCodeActionItem> _actions = [];
	private int _indicatorLineNumber;
	private bool _hasActions;
	private bool _isDisposed;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextCodeActionController"/> class.
	/// </summary>
	/// <param name="textArea">The text area the controller serves.</param>
	/// <param name="skin">The skin applied to created actions menus.</param>
	/// <param name="hooks">The host callbacks; the hook group documents which members are required.</param>
	/// <param name="options">The controller options, or <see langword="null"/> to use the defaults.</param>
	/// <param name="menuOptions">The menu presentation options, or <see langword="null"/> to use the defaults.</param>
	/// <param name="logger">An optional logger for request failures.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textArea"/> is <see langword="null" />, <paramref name="skin"/> (or one of its
	/// brushes), <paramref name="hooks"/>, or a required hook is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The caller is not on the thread that owns <paramref name="textArea"/>.
	/// </exception>
	public TextCodeActionController(
		TextArea textArea,
		TextCodeActionMenuSkin skin,
		TextCodeActionControllerHooks hooks,
		TextCodeActionControllerOptions? options = null,
		TextCodeActionMenuOptions? menuOptions = null,
		ILogger? logger = null)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentNullException.ThrowIfNull(skin);

		// The menu skin mirrors the completion window skin, whose brushes are required as well: the menu
		// applies the skin's brushes directly, so a null brush is a caller error instead of a meaningful
		// "theme default".
		ArgumentNullException.ThrowIfNull(skin.Background);
		ArgumentNullException.ThrowIfNull(skin.Foreground);
		ArgumentNullException.ThrowIfNull(skin.BorderBrush);
		ArgumentNullException.ThrowIfNull(hooks);
		ArgumentNullException.ThrowIfNull(hooks.BuildRequestState);
		ArgumentNullException.ThrowIfNull(hooks.RequestCodeActionsAsync);
		ArgumentNullException.ThrowIfNull(hooks.ExecuteActionAsync);

		// The controller touches the text area's document and its dispatcher and hosts the menu directly,
		// so creating it off the text area's thread is a caller error.
		textArea.Dispatcher.VerifyAccess();

		TextCodeActionControllerOptions resolvedOptions = options ?? TextCodeActionControllerOptions.Default;
		TextCodeActionMenuOptions resolvedMenuOptions = menuOptions ?? TextCodeActionMenuOptions.Default;

		_textArea = textArea;
		_buildRequestState = hooks.BuildRequestState;
		_requestCodeActionsAsync = hooks.RequestCodeActionsAsync;
		_executeActionAsync = hooks.ExecuteActionAsync;
		_logger = logger ?? NullLogger.Instance;

		_menuOptions = resolvedMenuOptions;
		_debouncer = new DispatcherDebouncer(resolvedOptions.RequestDebounceDelay, textArea.Dispatcher);
		_menu = new TextCodeActionMenuPresenter(
			textArea,
			skin,
			resolvedMenuOptions.MaxHeight,
			hooks.ConfigureMenu,
			hooks.ConfigureMenuItem);
		Margin = new TextCodeActionMargin(this);

		textArea.Caret.PositionChanged += HandleContextChangedEvent;
		textArea.SelectionChanged += HandleContextChangedEvent;
		textArea.DocumentChanged += HandleDocumentChanged;
		AttachDocument(textArea.Document);
	}

	/// <summary>
	/// Gets the margin that renders the indicator for the current context.
	/// </summary>
	/// <remarks>
	/// Add the margin to the text area's left margins to show the indicator; the margin is driven by
	/// this controller and repaints when actions become available or disappear. Disposing the
	/// controller clears the indicator but does not remove the margin from the text area - remove it
	/// yourself when the editor is torn down.
	/// </remarks>
	public TextCodeActionMargin Margin { get; }

	/// <summary>
	/// Gets the actions published for the current context, in provider order, or an empty list when none are
	/// available.
	/// </summary>
	/// <remarks>
	/// The returned list is the controller's snapshot and must not be mutated. It is replaced wholesale whenever
	/// a request publishes a new result, so read it after <see cref="Changed"/> (or immediately before opening
	/// the menu) instead of caching it across context changes. Actions are snapshots: apply them through a
	/// version-validated edit pipeline, like the execute hook receives them.
	/// </remarks>
	public IReadOnlyList<TextCodeActionItem> Actions => _actions;

	/// <summary>
	/// Gets a value indicating whether actions are available for the current context.
	/// </summary>
	public bool HasActions => _hasActions;

	/// <summary>
	/// Gets a value indicating whether the actions menu is open.
	/// </summary>
	public bool IsActionsOpen => _menu.IsOpen;

	/// <inheritdoc/>
	public event EventHandler? Changed;

	/// <summary>
	/// Requests code actions for the current context immediately, replacing any scheduled request.
	/// </summary>
	/// <returns>
	/// A task that completes after the request concluded. Failures are contained: they are logged and
	/// clear the indicator instead of faulting the task.
	/// </returns>
	public async Task RefreshAsync()
	{
		if (_isDisposed)
			return;

		_debouncer.Cancel();
		await RequestActionsAsync().ConfigureAwait(true);
	}

	/// <summary>
	/// Opens the actions menu for the current context, anchored at the caret, when actions are available.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> when the menu opened; otherwise, <see langword="false"/> because no
	/// actions are available or the editor is not connected to a presentation source.
	/// </returns>
	public bool TryOpenActions()
		=> TryOpenActions(_indicatorLineNumber, GetCaretAnchor());

	/// <summary>
	/// Opens the actions menu for the specified document line, anchored at the specified position, when the
	/// current context's actions are available for that line.
	/// </summary>
	/// <remarks>
	/// This is the anchoring seam for a host-owned indicator: a host that draws its own code-action indicator
	/// - or handles its own trigger gesture with a known anchor - opens the library menu at its own click
	/// position instead of the caret by passing the line the indicator belongs to and a position in the text
	/// area's coordinate space. The menu itself stays library-owned: its chrome and item styling are customized
	/// through <see cref="TextCodeActionControllerHooks.ConfigureMenu"/> and
	/// <see cref="TextCodeActionControllerHooks.ConfigureMenuItem"/>, while its anchoring, focus handling,
	/// and lifetime stay with the controller so the presentation state remains consistent.
	/// </remarks>
	/// <param name="lineNumber">The one-based document line the action context belongs to.</param>
	/// <param name="anchor">The menu's top-left position in the text area's coordinate space.</param>
	/// <returns>
	/// <see langword="true"/> when the menu opened; otherwise, <see langword="false"/> because no actions are
	/// available for the line or the editor is not connected to a presentation source.
	/// </returns>
	public bool TryOpenActions(int lineNumber, Point anchor)
	{
		if (_isDisposed || !_hasActions || lineNumber != _indicatorLineNumber || !CanOpenMenu())
			return false;

		_menu.Open(_actions, _textArea, anchor, InvokeAction);
		return true;
	}

	/// <summary>
	/// Closes the actions menu when it is open.
	/// </summary>
	public void CloseActions()
	{
		if (_isDisposed)
			return;

		_menu.Close();
	}

	/// <summary>
	/// Cancels the scheduled debounced request, if any, so it never runs.
	/// </summary>
	public void CancelScheduledRequest()
	{
		if (_isDisposed)
			return;

		_debouncer.Cancel();
	}

	/// <summary>
	/// Cancels the in-flight request, if any, so a cooperative host can stop early. The canceled
	/// request's result is rejected because its cancellation token is canceled; a result that a newer
	/// request replaced is rejected as well.
	/// </summary>
	public void CancelInFlightRequest()
	{
		if (_isDisposed)
			return;

		_requests.CancelPendingRequest();
	}

	/// <summary>
	/// Marks outstanding requests as stale so their completed results are discarded without canceling
	/// the work.
	/// </summary>
	public void InvalidateRequests()
	{
		if (_isDisposed)
			return;

		_requests.Invalidate();
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		if (_isDisposed)
			return;

		_isDisposed = true;

		_textArea.Caret.PositionChanged -= HandleContextChangedEvent;
		_textArea.SelectionChanged -= HandleContextChangedEvent;
		_textArea.DocumentChanged -= HandleDocumentChanged;
		AttachDocument(null);

		_debouncer.Dispose();
		_requests.CancelPendingRequest();
		_requests.Invalidate();
		_menu.Dispose();

		_actions = [];
		_hasActions = false;
		_indicatorLines.Clear();

		// A margin that is still attached repaints without the indicator; a detached margin has
		// already unsubscribed from the event.
		Changed?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>
	/// Gets the one-based document lines the margin must mark: the indicator's line while actions are
	/// available, otherwise nothing.
	/// </summary>
	/// <returns>The cached indicator lines; the returned list is reused and must not be mutated.</returns>
	internal IReadOnlyList<int> GetIndicatorLineNumbers() => _indicatorLines;

	/// <summary>
	/// Opens the actions menu for a margin click on the indicator's line.
	/// </summary>
	/// <param name="lineNumber">The one-based document line the margin click resolved to.</param>
	/// <param name="marginPoint">The click position in the margin's coordinate space.</param>
	/// <returns><see langword="true"/> when the menu opened; otherwise, <see langword="false"/>.</returns>
	internal bool TryOpenActionsFromMargin(int lineNumber, Point marginPoint)
	{
		Point areaPoint = Margin.TranslatePoint(marginPoint, _textArea);

		return TryOpenActions(
			lineNumber,
			new Point(GetTextViewOrigin().X + _menuOptions.AnchorXOffset, areaPoint.Y + _menuOptions.MarginAnchorYOffset));
	}

	private void HandleContextChangedEvent(object? sender, EventArgs e) => HandleContextChanged();

	private void HandleDocumentChanged(object? sender, EventArgs e)
	{
		if (_isDisposed)
			return;

		AttachDocument(_textArea.Document);
		ClearState();
		HandleContextChanged();
	}

	private void Document_Changed(object? sender, DocumentChangeEventArgs e)
	{
		if (_isDisposed)
			return;

		HandleContextChanged();
	}

	private void HandleContextChanged()
	{
		if (_isDisposed)
			return;

		// Pending work belongs to the previous context: a cooperative host is asked to stop early and
		// the completed result cannot be published after this point.
		_requests.CancelPendingRequest();
		_requests.Invalidate();

		// A published indicator whose line no longer matches the caret line is stale immediately;
		// movement inside the same line keeps it until the refreshed request replaces or clears it.
		if (_hasActions && GetCaretLineNumber() != _indicatorLineNumber)
			ClearState();

		ScheduleRequest();
	}

	private void ScheduleRequest()
	{
		_debouncer.Arm(() => _ = RequestActionsAsync());
	}

	private void AttachDocument(TextDocument? document)
	{
		if (ReferenceEquals(_document, document))
			return;

		if (_document is not null)
			_document.Changed -= Document_Changed;

		_document = document;

		if (_document is not null)
			_document.Changed += Document_Changed;
	}

	private async Task RequestActionsAsync()
	{
		if (_isDisposed)
			return;

		TextDocument? document = _textArea.Document;

		if (document is null)
		{
			ClearState();
			return;
		}

		string documentText = document.Text;
		int caretOffset = document.ClampOffset(_textArea.Caret.Offset);
		(int selectionStart, int selectionEnd) = GetSelectionOffsets(caretOffset);
		var context = new TextCodeActionContext(documentText, caretOffset, selectionStart, selectionEnd);

		TextCodeActionRequestState? state;

		try
		{
			state = _buildRequestState(context);
		}
		catch (Exception exception)
		{
			s_logHostCallbackFailed(_logger, exception);
			ClearState();
			return;
		}

		if (state is not TextCodeActionRequestState requestState)
		{
			ClearState();
			return;
		}

		try
		{
			// The coordinator cancels the previous request when a newer one starts and discards results
			// that are no longer current. Its publication callbacks run on the caller's captured
			// synchronization context only when one exists, so the current-state check and the publish
			// path marshal to the text area's dispatcher explicitly: hosts that pump a dispatcher
			// manually and tests get the same guarantee as application hosts.
			await _requests.RunAsync(
				state: (State: requestState, Document: document),
				computeAsync: (request, cancellationToken) => _requestCodeActionsAsync(request.State, cancellationToken),
				canApply: (request, _) => DispatcherInvocation.Run(
					_textArea.Dispatcher,
					() => !_isDisposed && ReferenceEquals(_textArea.Document, request.Document)),
				apply: actions => DispatcherInvocation.Run(
					_textArea.Dispatcher,
					() => PublishActions(actions))).ConfigureAwait(true);
		}
		catch (OperationCanceledException)
		{
			// A superseded request reports cancellation, not a failure; the superseding context has
			// already scheduled its own refresh.
		}
		catch (Exception exception)
		{
			if (_isDisposed)
				return;

			s_logRequestFailed(_logger, exception);
			DispatcherInvocation.Run(_textArea.Dispatcher, ClearState);
		}
	}

	private void PublishActions(IReadOnlyList<TextCodeActionItem> actions)
	{
		if (_isDisposed)
			return;

		// A misbehaving host delegate must not corrupt the published state or crash the dispatch pass.
		if (actions is null)
		{
			ClearState();
			return;
		}

		List<TextCodeActionItem> usable = [];

		for (int index = 0; index < actions.Count; index++)
		{
			TextCodeActionItem? action = actions[index];

			if (action is not null)
				usable.Add(action);
		}

		if (usable.Count == 0)
		{
			ClearState();
			return;
		}

		_actions = usable;
		_hasActions = true;
		_indicatorLineNumber = GetCaretLineNumber();

		_indicatorLines.Clear();

		if (_indicatorLineNumber > 0)
			_indicatorLines.Add(_indicatorLineNumber);

		Changed?.Invoke(this, EventArgs.Empty);
	}

	private void ClearState()
	{
		bool raisedChange = _hasActions || _indicatorLines.Count > 0;

		_actions = [];
		_hasActions = false;
		_indicatorLines.Clear();

		if (raisedChange)
			Changed?.Invoke(this, EventArgs.Empty);
	}

	private int GetCaretLineNumber()
	{
		TextDocument? document = _textArea.Document;

		if (document is null)
			return 0;

		int offset = document.ClampOffset(_textArea.Caret.Offset);
		return document.GetLineByOffset(offset).LineNumber;
	}

	private (int Start, int End) GetSelectionOffsets(int caretOffset)
	{
		Selection? selection = _textArea.Selection;

		if (selection is null || selection.IsEmpty)
			return (caretOffset, caretOffset);

		ISegment? segment = selection.SurroundingSegment;

		if (segment is null)
			return (caretOffset, caretOffset);

		// The offsets are normalized for the host regardless of the selection direction; Selection is an
		// extensible contract, so the segment's ordering is not relied on.
		return (Math.Min(segment.Offset, segment.EndOffset), Math.Max(segment.Offset, segment.EndOffset));
	}

	private bool CanOpenMenu()
		=> PresentationSource.FromVisual(_textArea) is not null;

	private Point GetCaretAnchor()
	{
		Point origin = GetTextViewOrigin();
		double y = origin.Y;
		TextView textView = _textArea.TextView;

		if (_indicatorLineNumber > 0 && textView.VisualLinesValid
			&& textView.GetVisualLine(_indicatorLineNumber) is VisualLine line)
		{
			y += line.VisualTop - textView.VerticalOffset;
		}

		return new Point(origin.X + _menuOptions.AnchorXOffset, y + _menuOptions.CaretAnchorYOffset);
	}

	private Point GetTextViewOrigin()
		=> _textArea.TextView.TranslatePoint(new Point(0.0, 0.0), _textArea);

	private void InvokeAction(TextCodeActionItem item) => _ = ExecuteActionAsync(item);

	private async Task ExecuteActionAsync(TextCodeActionItem item)
	{
		if (_isDisposed)
			return;

		try
		{
			await _executeActionAsync(item).ConfigureAwait(true);
		}
		catch (Exception exception)
		{
			s_logHostCallbackFailed(_logger, exception);
		}
	}
}

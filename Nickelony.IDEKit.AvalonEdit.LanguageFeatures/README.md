# Nickelony.IDEKit.AvalonEdit.LanguageFeatures

The language-features UI layer of the Nickelony.IDEKit package family. It brings
the hover, signature help, definition-navigation, completion-window, code-action,
diagnostic-segment projection, and semantic-token colorizing controllers to
AvalonEdit editors, built on the dependency-free
`Nickelony.IDEKit.Core` and `Nickelony.IDEKit.IntelliSense` contracts and
host-state records plus the `Nickelony.IDEKit.AvalonEdit` document/editing
helpers.

### Requirements

Windows Desktop (WPF) and AvalonEdit 6.3.x; the package builds on the
`Nickelony.IDEKit.Core`, `Nickelony.IDEKit.IntelliSense`, and
`Nickelony.IDEKit.AvalonEdit` packages and carries
`Microsoft.Extensions.Logging.Abstractions` for the controllers' optional
loggers.

The package targets `net8.0-windows`, uses WPF, and is organized into vertical
feature slices, each with its own namespace:

- `Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Hover` - the hover request
  controller (`TextHoverController`) that coordinates hover-versus-diagnostic
  tooltip display: a completed request combines the hover content and the
  host-supplied diagnostic (either may be absent), and the diagnostic is shown
  alone only when the request was not made or failed and the host state allows
  that fallback. The `TextHoverEvaluationState`
  host-state record (`Nickelony.IDEKit.IntelliSense.Hover`) a host builds for
  each hovered offset carries those decisions.
- `Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Signatures` - the signature
  help controller (`TextSignatureHelpController`) with debounced refresh,
  supersession, and overload navigation of the visible payload
  (`SelectNextSignature`/`SelectPreviousSignature`), which wraps around by
  default and dismisses at an edge when `TextSignatureHelpControllerOptions.Cycle`
  is disabled. Its options record
  (`TextSignatureHelpControllerOptions`) lives in
  `Nickelony.IDEKit.IntelliSense.Signatures`.
- `Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Navigation` - `TextDefinitionNavigation`,
  which applies a resolved `TextDefinitionLocation` to the text area (a host with a
  `TextEditor` passes `editor.TextArea`) and reports
  locations in other files through a host callback. Definition resolution comes
  in two forms: a hover-first symbol lookup (`TryGoToDefinition`,
  `TryGoToSymbol`) for name-catalog providers, and an offset-first resolver
  (`TryGoToDefinitionAtOffset`/`TryGoToDefinitionAtOffsetAsync`) for
  position-based providers such as an LSP-backed resolver. Both forms come in
  synchronous (in-memory) and asynchronous (resolver delegates with cancellation)
  variants and evaluate a single document-text snapshot per navigation.

  Definition-navigation triggers (F12, Ctrl+Click) are host policy: the host
  decides when to call `TextDefinitionNavigation`, for example through
  `Nickelony.IDEKit.KeyBindings`. The package contains only the navigation
  helpers.
- `Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion` - the completion
  window lifecycle (`CompletionWindowCoordinator` with `CompletionWindowSkin`,
  created and exposed by the controller), the completion window controller
  (`TextCompletionController`) with request scheduling, tooltip presentation,
  sizing, and the commit-character input policy, the tooltip chrome record
  (`CompletionToolTipSkin`), the completion-data opt-in contracts
  (`ICommitCharacterCompletionData` and `IPreselectedCompletionData`), and the
  default `ICompletionData` bridge (`TextCompletionItemCompletionData`). The bridge and the request-tracking
  session (`TextCompletionRequestSession` from
  `Nickelony.IDEKit.IntelliSense.Completion`, exposed as
  `TextCompletionController.Requests`) round out the slice. The request lifetime
  (tokens and cancellation) runs on the shared core `LatestRequestCoordinator`,
  while the debounced scheduling and the tooltip pipeline live in internal
  collaborators (`CompletionRequestScheduler` and `CompletionToolTipPresenter`),
  so the controller's public surface stays window- and decision-oriented. The
  the controller's options record deliberately stays with this binding, because its
  defaults describe the AvalonEdit completion window's presentation (the engine's
  height cap, the window chrome, and the item sizing), while the sibling
  controllers' framework-neutral timing and policy records live in
  `Nickelony.IDEKit.IntelliSense`.
- The completion and signature help controllers expose their current state through a
  `CurrentPresentation` property; the hover controller deliberately does not, because
  the host owns the hover surface and learns every display decision through its single
  display callback. The state records (`TextCompletionPresentationState` in
  `Nickelony.IDEKit.IntelliSense.Completion`, `TextSignatureHelpPresentationState` in
  `Nickelony.IDEKit.IntelliSense.Signatures`) carry the derived visibility/request
  conveniences: completion's `IsPresentationVisibleOrRequestScheduled` is a visible list,
  a visible detail popup, or a scheduled request, and an in-flight request is observed
  separately through `Requests.CurrentRequestCancellationToken` and `Requests.IsCurrent`;
  signature help's `IsPresentationVisibleOrRequestPending` is a visible popup, an
  in-flight request, or a pending refresh. The two unions differ by design and keep the
  controllers free of pass-through properties.
- Internal plumbing is compiled in from `Shared/Infrastructure` so every package
  shares one implementation: the dispatcher debouncer
  (`DispatcherDebouncer.cs`) and the dispatcher marshalling helper
  (`DispatcherInvocation.cs`), plus the numeric validation
  (`NumericValidation.cs`, also linked into `Nickelony.IDEKit.IntelliSense`) and
  the brush helpers (`BrushHelpers.cs`, also linked into the editor packages).
  Nothing they expose is part of the public API.
- `Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Highlighting` - the generic
  `SemanticTokensColorizer` rendering kernel with an injectable
  `ISemanticTokenStyleResolver`; hosts map token types to their own theme.
  The resolved `TextRunStyle` supports a foreground brush, bold,
  italic, and text decorations. The style implements the shared
  `Nickelony.IDEKit.AvalonEdit.Rendering.ITextRunStyle` contract and is applied
  to visual line elements through the shared `TextRunStyleApplier` helper, so
  semantic tokens and TextMate runs use the same paint-time application path.
  `SetTokens`/`Rebuild`/`ClearTokens` return
  whether they applied a change, redraw the text view when they did, and
  must run on the UI thread; a push that still maps to the same line and
  character anchors is a no-op, an edit that moved an anchor re-applies, and
  `Rebuild` is the refresh path after resolver configuration changes. Tokens
  are clipped to the line containing their
  start offset, so a multi-line token is rendered only on its start line,
  and the host is responsible for re-pushing tokens after document edits.
- `Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Diagnostics` -
  `TextDiagnosticSegmentFactory` projects IntelliSense diagnostics into the
  AvalonEdit diagnostic renderer's segment model, so hosts do not hand-map
  them: `Create` is the caching path (project once and invalidate the view
  when diagnostics change), `CreateProvider` re-projects on every render pass,
  and `CreateRenderer` returns a wired renderer.
- `Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions` - the quick-fix
  surface: `TextCodeActionController` (debounced requests over host hooks,
  indicator state, menu lifecycle, explicit dispatcher marshaling of published
  results), `TextCodeActionMargin` (the light-bulb indicator margin driven by
  the controller as its notifying source and built on the shared
  `LineStatusIconMarginBase` from `Nickelony.IDEKit.AvalonEdit.Rendering`; a left
  press on the indicator's line opens the menu unless the host turns that gesture
  off), and the menu records
  (`TextCodeActionMenuSkin`,
  `TextCodeActionMenuOptions`, `TextCodeActionControllerHooks`). The host records
  `TextCodeActionContext`
  (editor state), `TextCodeActionRequestState` (the host's chosen request range;
  `null` vetoes the context), `TextCodeActionItem` (title, kind, preferred,
  opaque host `Payload` returned to the execute hook), and
  `TextCodeActionControllerOptions` (request timing) live in
  `Nickelony.IDEKit.IntelliSense.CodeActions`; the menu sizing and anchor policy
  (`TextCodeActionMenuOptions`, a 320-unit default height cap) lives with this
  binding because it is host presentation.

### Logging

Controllers accept an optional `Microsoft.Extensions.Logging.ILogger` and
default to `NullLogger` when none is supplied, so hosts control where request
failures are logged. Event ids are stable, unique within this package, and drawn
from the package's own `1000` block, so they never collide with the smaller id
ranges sibling packages use today: completion request failures use
`1000`, the completion tooltip presenter uses `1001` (description resolve
failed) and `1002` (tooltip access unsupported), hover uses `1010`-`1012`
(request failed, host callback failed, offset mismatch), signature help uses
`1020` (request failed) and `1021` (host callback failed), and code actions use
`1030` (request failed) and `1031` (host callback failed).

### Host integration seams

The controllers are hook-driven: they take hooks for offset resolution,
request execution, and presentation, so a host wires them to its own editor
state and skins. Host hooks are grouped in dedicated hook types
(`TextCompletionControllerHooks`, `TextHoverControllerHooks`,
`TextSignatureHelpControllerHooks`, and `TextCodeActionControllerHooks`), so wiring stays declarative and new hooks
can be added without growing the constructors. `TextCompletionController` takes
the text area, its `CompletionWindowSkin`, the options record, and the hooks; it
creates the `CompletionWindowCoordinator` for that text area itself and
exposes it as `controller.WindowCoordinator`, so hosts observe window closures
through `WindowCoordinator.WindowClosed` instead of composing the coordinator
themselves; window creation and showing stay with the controller, so every
shown window went through the controller's configuration pipeline and the
reported presentation state stays truthful. Hosts keep their own completion-item
and window styling (for example a `CompletionData` type and window-style skin)
and can supply item factories and display-text selectors. The scheduled request
callback is a hook (`TextCompletionControllerHooks.ScheduledRequestAsync`): the
controller is fully configured when it is constructed, and a host that never
schedules requests simply omits the callback.

Trigger and popup-precedence policy stays in the host: helpers such as
"Ctrl+Space input", "F12 or Ctrl+Click for go-to-definition", or "do not show
hover while another popup is open" are a few lines of host code and are
deliberately not part of this package.

The controllers bind to the editor differently by design: the completion
controller takes the text area (it owns its window coordinator), the hover
controller takes the element hover positions resolve against plus its hooks,
and the signature help controller is host-state driven through hooks only. Each
shape matches what the controller actually touches.

The completion window is shown non-activatable by default, so clicks in the list
never activate the window or steal focus from the editor. A host that wants the
window to activate normally sets `NonActivatingWindow = false`; the option
remarks describe both modes. A window whose filtered list becomes empty is
closed instead of showing AvalonEdit's empty template; setting
`CloseWhenEmpty = false` keeps the empty template instead.

The controller also installs the commit-character input policy while it is
alive: when the selected item's completion data declares commit characters
(`ICommitCharacterCompletionData`, which the default bridge implements from the
shared item), a typed character from that set accepts the item and is still
typed, so one keystroke both commits and inserts the character. The policy runs
in the text-input preview stage, before text-composition services such as an
auto-closing service see the character, so the commit composes with them
regardless of subscription order; `AcceptOnCommitCharacters = false` turns the
policy off.

The default bridge likewise feeds item facts into behavior: the item's
additional text edits are applied together with the insertion as one undo unit
(malformed, stale, or overlapping entries are skipped individually), a
deprecated item's label renders struck through without changing selection or
committing, the item's commit characters drive the input policy above, and the
item's `FilterText` (falling back to the label) is exposed as AvalonEdit's
filter key while the label stays the displayed content, so list filtering and
the shared completion-session kernel match on the same text.

### Wiring example

The following sketch wires the completion and hover controllers for an editor;
production hosts add lifetime management (`Dispose` on editor teardown) and
their own item and tooltip presentation. `RequestCompletionAsync`,
`RefreshCompletionState`, `BuildHoverRequestState`, `RequestHoverAsync`, and
`ShowHoverToolTip` stand for host-defined methods used by the sketch.

```csharp
var options = TextCompletionControllerOptions.Default with
{
    RequestDebounceDelay = TimeSpan.FromMilliseconds(50.0)
};

// The scheduled-request hook runs the standard pipeline through the controller field, so the
// field is assigned after construction from the callback's point of view.
TextCompletionController? completion = null;

completion = new TextCompletionController(
    editor.TextArea,
    new CompletionWindowSkin(Brushes.Gray, Brushes.Black, Brushes.White),
    options,
    new TextCompletionControllerHooks
    {
        ConfigureWindow = window => window.FontSize = editor.FontSize,
        GetDisplayInfo = item => (item.Text, null),
        ResolveDescriptionAsync = (item, cancellationToken) => Task.FromResult<object?>(item.Documentation),
        ToolTipSkin = CompletionToolTipSkin.Default with
        {
            Background = Brushes.Black,
            BorderBrush = Brushes.Gray
        },

        // The debounced request produces the decision; its items go through the default
        // TextCompletionItemCompletionData adapter, so no item mapper is needed here.
        ScheduledRequestAsync = () => completion!.RequestAsync(RequestCompletionAsync)
    });

// Observe every closure of a shown window, including host-forced closes.
completion.WindowCoordinator.WindowClosed += (_, _) => RefreshCompletionState();

var hover = new TextHoverController(
    editor,
    new TextHoverControllerHooks
    {
        GetOffsetFromPoint = point => editor.GetPositionFromPoint(point) is { } position
            ? editor.Document.GetOffset(position.Location)
            : null,
        BuildRequestState = BuildHoverRequestState,
        RequestHoverAsync = RequestHoverAsync,

        // One sink for display and hiding: null for both arguments means hide.
        ShowToolTip = ShowHoverToolTip
    });
```

### Code actions

`TextCodeActionController` is host-driven: the host builds a request state from the editor
context (returning `null` vetoes the context), requests actions, and applies the invoked action
through its version-validated edit pipeline. Add the indicator margin to the editor's left
margins; editor context changes schedule requests on their own, `RefreshAsync` covers provider
state that changed without a caret move (for example new diagnostics), and the host key gesture
(for example Ctrl+.) calls `TryOpenActions`. The published snapshot is readable through `Actions`,
and a host with its own indicator opens the same menu at its own click point with
`TryOpenActions(line, anchor)` (a point in the text area's coordinate space); the menu surface
itself stays library-owned, and its chrome is customizable through `ConfigureMenu` while each
created item is customizable through `ConfigureMenuItem`. The margin's left-press gesture is the
library default: a host that owns the gesture sets `TextCodeActionMargin.OpenOnLeftPress` to
`false` and opens the menu itself through `TryOpenActions`. Actions are
snapshots - the host's execute hook must reject stale actions (apply through the same
version-checked pipeline as other workspace edits).

```csharp
var codeActions = new TextCodeActionController(
    editor.TextArea,
    new TextCodeActionMenuSkin(Brushes.Gray, Brushes.Black, Brushes.White),
    hooks: new TextCodeActionControllerHooks
    {
        BuildRequestState = BuildCodeActionState,
        RequestCodeActionsAsync = RequestCodeActionsAsync,
        ExecuteActionAsync = ApplyCodeActionAsync
    });

// The margin renders the light bulb and opens the menu on a left press on its line; set
// OpenOnLeftPress = false to own the gesture and call TryOpenActions yourself.
editor.TextArea.LeftMargins.Add(codeActions.Margin);

// Refresh whenever provider state changed outside the editor (for example diagnostics).
await codeActions.RefreshAsync();
```

`BuildCodeActionState`, `RequestCodeActionsAsync` (which maps the host's provider actions into
`TextCodeActionItem` values, keeping the provider action in `Payload` to apply on execute), and
`ApplyCodeActionAsync` stand for host-defined methods used by the sketch. Dispose the controller
on editor teardown and remove the margin with the editor.

### Request coordination semantics

The XML documentation of `TextHoverController`, `TextCompletionController`, and
`TextSignatureHelpController` is the normative contract for request coordination;
this section is an orientation, not the contract.

- `TextHoverController` - `CancelInFlightRequest` cancels the in-flight request,
  `InvalidateRequests` rejects outstanding results without canceling it, and the
  optional `ContextVersionProvider` value is captured before a request.
- `TextCompletionController` - the request lifetime runs on the shared core
  `LatestRequestCoordinator` behind the `Requests` session: `Requests.BeginRequest`,
  `Requests.IsCurrent`, and `Requests.CurrentRequestCancellationToken` expose the
  manual pipeline seam, `Requests.CancelInFlightRequest` cancels the pending
  token without invalidating the request, `Requests.InvalidateRequests` rejects
  results without canceling, and `CancelScheduledRequest` cancels only the
  debounced scheduled request. A superseded tooltip description resolve is
  canceled through the token passed to `ResolveDescriptionAsync`.
- `TextSignatureHelpController` - the latest request wins: starting a
  request cancels the in-flight one through the shared `LatestRequestCoordinator`,
  and a superseded response is never applied. `CancelScheduledRefresh` clears the
  debounced refresh, `CancelInFlightRequest` cancels the provider token,
  `InvalidateRequests` rejects without canceling, and `Dismiss` does all three;
  navigation and timing are configured through `TextSignatureHelpControllerOptions`.

All three controllers share the core `LatestRequestCoordinator`. The hover and
signature help controllers await a single result and decide admission in a
`canApply` predicate; the completion controller additionally exposes the
coordinator to hosts that drive the pipeline themselves, which is why its request
identifiers and cancellation token are public through the `Requests` session. The
coordinator cancels the superseded request's token and rejects its result in both
flows, so a provider that ignores cancellation cannot publish a canceled
decision, and a manually driven request and a standard request supersede each
other.

Provider failures follow one convention across the controllers: a failure of a
provider call or a host hook inside a controller-owned pipeline is contained -
logged through the controller's logger - and the operation reports its
not-applied result (`RequestAsync` reports `false`, hover resolves to a hide or
diagnostic decision, signature help stays on its previous presentation), so
timer, dispatcher, and event-handler callers never observe provider exceptions.
Only argument validation still throws.

### Completion window sizing and tooltips

The completion window sizes to its content: the measured content width plus
`WindowHorizontalChrome` is clamped by `WindowMaxWidth`, and `WindowMaxHeight`
caps the content-driven height (default `300`, AvalonEdit's own cap).
`WindowMinContentWidth` is the minimum *content* width measured before the
chrome is added, so the window floor is `WindowMinContentWidth` +
`WindowHorizontalChrome` (`472` with the defaults), capped by `WindowMaxWidth`;
the controller rejects an options record whose maximum cannot hold that floor.
The default measurement sizes the display text and adds an optional icon column
(`ItemIconWidth`, only for items that supply an image) and an optional detail gap
(`ItemDetailSpacing`, only when detail text is present). Items are measured until the
window's maximum content width is reached (identical display strings only once
per pass), so the widest items size the window. A host with a different template
supplies `MeasureItemWidth` (or overrides the numeric options). All numeric
options and the replacement offsets passed to `OpenOrRefresh` are validated, and
offsets beyond the document end are clamped (a raw end offset before the raw
start offset is rejected before clamping). The window chrome - border thickness
and colors - comes from the `CompletionWindowSkin`; `ConfigureWindow` can still
override any window property after the controller sized the window. The tooltip resolve
debouncer is created when the controller is constructed (each selection change
arms its timer), so `ResolveDescriptionAsync`
descriptions work even when a host's hooks carry no scheduled-request callback. String
descriptions render in a wrapping
`TextBlock`, matching AvalonEdit's stock tooltip, and null descriptions keep the
tooltip hidden. When `ResolveDescriptionAsync` returns null content, the tooltip
is hidden as well. The tooltip chrome (placement, offset, border, padding, and
optional brushes) comes from the `ToolTipSkin` hook: `CompletionToolTipSkin.Default`
is the starting point; a null skin is replaced by that default, whose placement,
offset, border, and padding are applied unconditionally, and only the two nullable
brushes (`Background` and `BorderBrush`) leave the WPF theme's tooltip colors in
place. `ConfigureToolTip` still runs afterwards for last-word tweaking.
`ConfigureWindow` runs after the controller sized the window - on every open and
on every in-place refresh - so a hook can pin any window property, including the
width. After an open, the best-matching item is selected; when the provider marked
an item as preselected (LSP's `preselect`) and it survived the filtering, that item wins
and is scrolled into view, because AvalonEdit's selected-item setter does not scroll
(the flag is read through `IPreselectedCompletionData`, which the default adapter implements).
`WindowClosed` on the controller's `WindowCoordinator` is raised exactly
once per shown window, whether it closes through `Close`, `Dispose`, or being
replaced by a window for a new range. The tooltip is decorated rather than
replaced: AvalonEdit owns and opens the single tooltip instance, so the
presenter must render string descriptions exactly like the stock writer.
Replacing the decoration with a host-owned details surface is the alternative
if AvalonEdit ever exposes its tooltip publicly.

### Completion requests and decisions

`RequestAsync` is the convenience entry point for the standard scenario: it
starts the request on the shared core request coordinator behind `Requests`,
invokes the host callback (which computes the
`TextCompletionSessionDecision` from the shared completion-session kernel or its
own provider logic), discards a superseded or canceled result, and applies the
decision on the editor thread (marshalled explicitly when the creating thread
captured no synchronization context). Cancellation is reported as `false`, not
thrown, and a provider or callback failure is contained and logged (event id
`1000`) and reports `false` as well. A host that needs to run its own request steps (for
example extra provider round trips) starts the request with
`Requests.BeginRequest`, observes `Requests.CurrentRequestCancellationToken`,
checks `Requests.IsCurrent` before applying a decision, and rejects a canceled
token the same way `RequestAsync` does.
`ApplyDecision` maps the decision's items through the `CompletionItemFactory`
hook when it is set, and through the built-in `TextCompletionItemCompletionData`
adapter otherwise, so plain label/description/insert-text items need no host
mapper; the same mapping applies when `RequestAsync` applies its decision. A
host that already holds shared `TextCompletionItem` instances wraps them with
`TextCompletionSessionDecision.Open(items, startOffset, endOffset)` and calls
`ApplyDecision` - that is the standard path for showing a plain item list
without adapting every item to `ICompletionData` by hand. A factory that returns
null for an item throws `InvalidOperationException` - the item is neither
skippable nor representable - so hosts should only map shapes they can actually
present. The decision's item set is the shared session kernel's answer for the
typed word, while the engine's list re-filters and ranks the same collection for
display; the default adapter exposes the item's `FilterText` as AvalonEdit's
filter key, so both matchers read the same text. The split is deliberate - the
kernel owns session decisions and the engine owns list presentation - and is why
the two filters must agree on the same key.

### Refreshing an open window

`OpenOrRefresh(items, startOffset, endOffset)` requires both replacement offsets;
the shared `TextCompletionSessionDecision` type enforces the same requirement on
every decision (a decision that carries items carries a valid, ordered replacement
range), so `ApplyDecision` never receives a partially populated decision. When a
window is open and the requested start offset matches
the open window's start offset, the window is refreshed in place: its end offset
and item list are updated, the list is re-filtered for the new query (even when
the query did not change, including the empty query), and the initial selection
is re-established - no close/reopen flash and no second `WindowClosed` report. A
refreshed window whose filtered list is empty closes itself when `CloseWhenEmpty`
is enabled (the default); the call still reports `true`, because the refresh
itself was applied. A different start offset starts a new session: the old window
closes (reporting the closure) and a new one opens for the new range. Offsets
beyond the document end are clamped; a raw end offset before a raw start offset
is rejected.

### Tooltip availability

AvalonEdit's completion window keeps its tooltip in a private field that it does
not expose publicly, so the controller resolves it by reflection and can only
decorate an existing tooltip.
`TextCompletionController.IsToolTipSupported` reports whether the running
AvalonEdit version exposes that field; when it does not,
`ResolveDescriptionAsync` tooltips are skipped (logged once with event id `1002`)
while everything else keeps working. The controller decorates the window's
existing tooltip instead of owning a second one: replacing the field would make
the same content render twice and lose AvalonEdit's own show/hide bookkeeping.

### Thread affinity

The controllers are created and used on the thread that owns the state they
touch - for the editor-bound controllers that is the editor thread: they touch
editor and popup state directly, and their debounce timers run on that thread's
dispatcher. Provider requests stay asynchronous and their continuations are
marshalled back to the owning thread - explicitly when the creating thread
captured no synchronization context (a manually pumped dispatcher, tooling,
tests), so a provider that completes on a thread-pool thread never touches
dispatcher-bound state from there. The signature help controller takes an
optional `Dispatcher` for hosts whose owner thread has no dispatcher yet when the
controller is created; the completion controller uses the editor's dispatcher and
the hover controller the owner's dispatcher.

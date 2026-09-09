# Editor-binding guide

This guide is for a developer adding a new editor binding to the Nickelony IDEKit
family - for example `Nickelony.IDEKit.AvaloniaEdit` on AvaloniaEdit. The editor-neutral
logic a binding needs (the text model, the edit kernel, planners, request coordination,
feature contracts, payloads, and host-state records) already lives in
`Nickelony.IDEKit.Core` and `Nickelony.IDEKit.IntelliSense`. A binding reuses those
packages and writes only the framework-typed surface; it never re-implements a planner
or kernel that already exists.

The `Nickelony.IDEKit.AvalonEdit` and `Nickelony.IDEKit.AvalonEdit.LanguageFeatures`
packages are the verified reference implementation. Every piece named in section 3 has
a working AvalonEdit counterpart there, and the packaged tests pin its behavior; port
that shape rather than inventing a new one. The seam itself was produced by the
2026-09-18 AvaloniaEdit-readiness extraction program (phase records archived in
`docs/archived - irrelevant - to be deleted/__180918_avaloniaedit-readiness-extraction-plan.md`).

## 1. The tier model and dependency direction

```
Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures   (-> AvaloniaEdit, IntelliSense, base)
        |
        v
Nickelony.IDEKit.AvaloniaEdit                    (-> AvaloniaEdit, Core)
        |
        v
Nickelony.IDEKit.IntelliSense   ------------>    Nickelony.IDEKit.Core
        (-> Core only)                           (dependency-free leaf)

Language-server packages sit on the same contracts:
Nickelony.LanguageServer.Abstractions  ->  Nickelony.IDEKit.IntelliSense
```

The dependency direction is fixed:

- A binding references `Nickelony.IDEKit.Core`; a binding that hosts the IntelliSense
  controllers also references `Nickelony.IDEKit.IntelliSense`; its language-features
  package references its own base package. Nothing references a binding.
- The neutral packages are never pushed upward: `Core` and `IntelliSense` must not know
  about a toolkit, a binding, or a concrete editor control. A type that needs a toolkit
  type lives in the binding; the neutral half is the contract, planner, or record the
  binding consumes.
- `Nickelony.LanguageServer.Abstractions` references `Nickelony.IDEKit.IntelliSense`, so
  a binding hosts a language provider directly - see the
  [consumer integration guide](ConsumerIntegration.md) for construction, event
  marshaling, and cancellation behavior.
- A binding ships as its own packages in the repository's "Editor adapters" tier (root
  README) and mirrors the AvalonEdit split: a base package (documents, editing, margins,
  renderers) plus a language-features package (IntelliSense controllers, windows, skins).

## 2. What a binding reuses

The neutral packages are the reusable half of every feature. A binding consumes them
as-is; nothing below needs a re-extraction for a second toolkit.

### `Nickelony.IDEKit.Core` (dependency-free)

- **Text model** (`Nickelony.IDEKit.Core.Text`): `ITextSnapshot`, `ITextLine`,
  `StringTextSnapshot`, `TextRange`, `TextPosition`, `TextPositionRange`,
  `TextEditOperation`, `TextLineMap`, `TextLineSplitter`, `BacktickFenceTextNormalizer`.
- **Edit kernel and edit boundary** (`Nickelony.IDEKit.Core.Editing`): `TextEditKernel`,
  `TextEditInput`, `TextEditPreparationResult`, `TextEditPreparationDiagnostic`,
  `PreparedTextEdits`, `ITextEditTarget`, `ITextEditTargetVersion`,
  `TextIncrementalEditCalculator`, `TextIncrementalEdit`,
  `TextRangeOffsetResolver`, `IncrementalLineStateCache`.
- **Auto-closing** (`Nickelony.IDEKit.Core.AutoClosing`): `TextAutoClosingResolver` and
  its model - `TextAutoClosingOptions`, `TextAutoClosingPair`, `TextAutoClosingPairKind`,
  `TextAutoClosingAction`, `TextAutoClosingActionKind`, `TextAutoClosingProvenance`,
  `TextAutoClosingResult`.
- **Line comments** (`Nickelony.IDEKit.Core.Comments`): `TextLineCommentPlanner` (the
  edit computation) with `TextLineCommentEdit` and `TextLineCommentAction`, plus the
  comment-scanning models (`CommentOperations`, `CommentSyntax`, `CommentSpan`,
  `CommentKind`, `CommentSpanEnumerator`, `StringLiteralStyle`, `ContinuationOperations`).
- **Diagnostics vocabulary** (`Nickelony.IDEKit.Core.Diagnostics`):
  `TextDiagnosticSegment`, `TextDiagnosticSeverity`.
- **Request coordination** (`Nickelony.IDEKit.Core.Requests`): `LatestRequestCoordinator`,
  `RequestTokenSource`, `RequestOutcome`.
- **Navigation identity** (`Nickelony.IDEKit.Core.Navigation`): `NavigationLocation`.
- **Line-status contracts** (`Nickelony.IDEKit.Core.LineStatus`): `ILineStatusSource`;
  change notifications (`Nickelony.IDEKit.Core.Notifications`): `IChangeNotificationSource`.
- **Formatting** (`Nickelony.IDEKit.Core.Formatting`): `ITextDocumentFormatter`,
  `TrimTrailingWhitespaceFormatter`, `WhitespaceConverter`.
- **Indentation** (`Nickelony.IDEKit.Core.Indentation`): `IIndentationPolicy`,
  `IndentationContext`, `IndentationOperations`, `IndentationTextLine`.
- **Identifiers** (`Nickelony.IDEKit.Core.Identifiers`): `IdentifierOperations`,
  `IdentifierCharacterPolicy`, `IdentifierSpanMode`.
- **Supporting primitives**: `LineDiffer` (`Core.Diffing`), `LocalPathComparisonPolicy`
  (`Core.Pathing`), `FindReplaceText`/`TextSearchQuery` (`Core.FindReplace`),
  `SidecarLineFile` (`Core.Persistence`), `ThemeCatalog<TTheme>` (`Core.Themes`).

### `Nickelony.IDEKit.IntelliSense` (Core-only)

- **Completion** (`...IntelliSense.Completion`): `ITextCompletionProvider`,
  `TextCompletionRequest`, `TextCompletionTrigger`, `TextCompletionItem`,
  `TextCompletionItemKind`, `TextCompletionTextEdit`, `TextCompletionInsertTextFormat`,
  `TextCompletionFilter`, `TextCompletionItemFilter`, `TextCompletionSessionDecision`,
  `TextCompletionWordSpan`, `TextCompletionWordLocator`, the shared
  `TextCompletionSessionKernel`, `TextSnippetExpander` with `TextSnippetExpansion` and
  `TextSnippetPlaceholder`, and the host-state records `TextCompletionRequestSession`
  and `TextCompletionPresentationState`.
- **Hover** (`...IntelliSense.Hover`): `ITextHoverProvider`, `TextHoverRequest`,
  `TextHoverInfo`, `TextHoverEvaluationState`; `TextMarkupKind` in the root namespace.
- **Signature help** (`...IntelliSense.Signatures`): `ITextSignatureHelpProvider`,
  `TextSignatureHelpRequest`, `TextSignatureHelpContext`,
  `TextSignatureHelpTriggerKind`, `TextSignatureHelp`, `TextSignatureInformation`,
  `TextSignatureParameterInfo`, `TextSignatureHelpControllerOptions`,
  `TextSignatureHelpPresentationState`.
- **Definition navigation** (`...IntelliSense.Navigation`): `ITextDefinitionProvider`,
  `TextDefinitionRequest`, `TextDefinitionLocation`, `TextDefinitionDiscriminator`.
- **Diagnostics** (`...IntelliSense.Diagnostics`): `ITextDiagnosticsProvider`,
  `TextDiagnosticsRequest`, `TextDiagnostic`, `DiagnosticHitTester`.
- **Code actions** (`...IntelliSense.CodeActions`): `TextCodeActionContext`,
  `TextCodeActionRequestState`, `TextCodeActionItem`, `TextCodeActionControllerOptions`.
- **Document symbols** (`...IntelliSense.DocumentSymbols`): `TextDocumentSymbol`,
  `TextDocumentSymbolKind`, `ITextDocumentSymbolProvider`, `TextDocumentSymbolRequest`,
  `DocumentSymbolOutlineBuilder`, `DocumentSymbolProjection<TItem>`,
  `TextDocumentSymbolKindConversion`.
- **Semantic tokens** (`...IntelliSense.SemanticTokens`): `TextSemanticToken`,
  `TextSemanticTokenTypes`, `TextSemanticTokenModifiers`.

## 3. What every binding writes (AvalonEdit reference)

Each row names the piece a binding must ship itself, its AvalonEdit counterpart, and
the neutral types it composes. Presenters and schedulers are internal collaborators in
the reference implementation; a binding is free to arrange them the same way.

| Piece | AvalonEdit reference | Composes |
|---|---|---|
| Snapshot and document adapters | `TextDocumentSnapshot`, `TextDocumentExtensions`, `DocumentLineStateCache`, `DocumentVersionCache`, `TextRangeNormalizer` (base) | `ITextSnapshot`, `ITextLine` (`Core.Text`); `LineDiffer` for cache work |
| Edit-target adapter | `AvalonEditTextEditTarget` (base) | `ITextEditTarget`, `ITextEditTargetVersion` (`Core.Editing`) |
| Editing and line operations | `TextEditorEditOperations`, `TextEditorLineOperations`, `TextEditorFirstMatchingLineOperations` (base) | `Core.Text`/`Core.Editing` primitives as the operation needs them |
| Auto-closing input service | `TextAutoClosingService` with `ITextAutoClosingService` (base) | `TextAutoClosingResolver` and its model (`Core.AutoClosing`) |
| Line-comment service | `TextLineCommentService` with `ITextLineCommentService` (base) | `TextLineCommentPlanner`, `TextLineCommentEdit`, `TextLineCommentAction` (`Core.Comments`) |
| Formatting service and indentation bridge | `TextDocumentFormattingService` with `ITextDocumentFormattingService`, `PolicyIndentationStrategy` (base) | `ITextDocumentFormatter`, `TrimTrailingWhitespaceFormatter`, `WhitespaceConverter`, `IIndentationPolicy` |
| Navigation helpers | `TextAreaNavigationOperations`, `TextEditorMouseNavigation` (base) | `NavigationLocation` (`Core.Navigation`) |
| Definition-navigation glue | `TextDefinitionNavigation` (language features) | `TextDefinitionLocation` and the provider contracts (`IntelliSense.Navigation`) |
| Line-status margins and markers | `LineStatusMarginBase`, `ChangeMarkerMargin`, `UnsavedChangesTracker`, `BookmarkCoordinator`, `BookmarkMargin`, `IBookmarkSource` (base) | `ILineStatusSource` (`Core.LineStatus`), `IChangeNotificationSource` (`Core.Notifications`); `LineDiffer` for the tracker |
| Bookmark persistence | `IBookmarkStore`, `BookmarkSidecarStore`, `BookmarkStoreExtensions` (base) | `SidecarLineFile` (`Core.Persistence`) |
| Diagnostics renderer | `DiagnosticsRenderer` (base) | `TextDiagnosticSegment`, `TextDiagnosticSeverity` (`Core.Diagnostics`) |
| Diagnostic projection (glue) | `TextDiagnosticSegmentFactory` (language features) | `TextDiagnostic` (`IntelliSense.Diagnostics`) projected onto `TextDiagnosticSegment` |
| Text-run styling | `ITextRunStyle`, `TextRunStyle`, `TextRunStyleApplier` (base) | none - toolkit styling seam shared by the colorizers |
| Regex highlighting | `RegexHighlightingDefinition`, `RegexHighlightingRule`, `RegexHighlightingSpan`, `RegexHighlightingStyle` (base) | none |
| Dispatcher utilities (internal) | `DispatcherDebouncer`, `DispatcherInvocation` (language-features infrastructure) | none - one pair per toolkit dispatcher |
| Completion controller family | `TextCompletionController`, `TextCompletionControllerHooks`, `TextCompletionControllerOptions`, `CompletionWindowCoordinator`, `CompletionWindowSkin`, `CompletionToolTipSkin`, `TextCompletionItemCompletionData`, `ICommitCharacterCompletionData` (language features) | `TextCompletionSessionKernel`, `TextCompletionItemFilter`, `TextCompletionWordLocator`, `TextSnippetExpander`, `TextCompletionRequestSession`, `TextCompletionPresentationState`, `LatestRequestCoordinator` |
| Hover controller | `TextHoverController`, `TextHoverControllerHooks` (language features) | `TextHoverEvaluationState`, `TextHoverRequest`, `TextHoverInfo` |
| Signature-help controller | `TextSignatureHelpController`, `TextSignatureHelpControllerHooks` (language features) | `TextSignatureHelpControllerOptions`, `TextSignatureHelpPresentationState`, the payload family |
| Code-action controller and menu | `TextCodeActionController`, `TextCodeActionControllerHooks`, `TextCodeActionMargin`, `TextCodeActionMenuSkin`, `TextCodeActionMenuOptions` (language features) | `TextCodeActionContext`, `TextCodeActionRequestState`, `TextCodeActionItem`, `TextCodeActionControllerOptions` |
| Semantic-token colorizer | `SemanticTokensColorizer`, `ISemanticTokenStyleResolver` (language features) | `TextSemanticToken` payloads; `TextRunStyle` for painting |

## 4. Intentional per-binding pieces

These exist in every binding by design; port the shape, not the code:

- **Framework-typed hooks** carry toolkit values (`Point`, `ContextMenu`, windows) -
  they are the host's own editor state exposed to a controller.
- **Skins and style resolution** bind colors, brushes, and metrics to one toolkit's
  rendering model (`CompletionWindowSkin`, `CompletionToolTipSkin`,
  `TextCodeActionMenuSkin`, and the colorizer's style resolver).
- **Presenters and window/popup mechanics** (`CompletionWindow*`, the tooltip pipeline,
  the code-action menu) are toolkit windowing, not language logic.
- **Dispatcher utilities** (`DispatcherDebouncer`, `DispatcherInvocation`) wrap one
  toolkit's dispatcher and thread-affinity rules.
- **Margins and renderers** (`LineStatusMarginBase`-derived margins,
  `DiagnosticsRenderer`) draw into one toolkit's visual layers (`IBackgroundRenderer`).
- **Input services** bind to one toolkit's event shapes (text-entering, preview key,
  mouse) - `TextAutoClosingService` is the reference.
- **Projection glue** such as `TextDiagnosticSegmentFactory` maps a neutral payload onto
  the shared renderer input; the mapping is trivial but toolkit-adjacent.
- **Text-area glue** such as `TextDefinitionNavigation` and `TextEditorMouseNavigation`
  applies neutral results to a concrete editor surface.
- **`PolicyIndentationStrategy`** adapts the neutral `IIndentationPolicy` onto one
  toolkit's indentation seam.
- **`TextCompletionControllerOptions`** mixes request timing with window sizing; the
  sizing half is per-window, so the record stays with the binding (recorded decision).
  The signature-help counterpart (`TextSignatureHelpControllerOptions`) has no
  window-typed members and lives in `IntelliSense`; the code-action pair follows
  the same rule: `TextCodeActionControllerOptions` (request timing only) stays in
  `IntelliSense`, while the menu sizing record (`TextCodeActionMenuOptions`) lives
  with the binding.

## 5. Adapter patterns to copy

### 5.1 Snapshot over the editor document

`TextDocumentSnapshot` implements the Core `ITextSnapshot`: it captures an immutable
document snapshot on the document's owner thread and stays readable from any thread.
Line metadata is materialized lazily from a full text copy on first line access - so
keystroke-rate code reads only `TextLength`/`GetCharAt` (the auto-closing resolver does
exactly that; the line-comment planner accepts the materialization because toggling is a
user-rate command). Copy both the shape and the cost discipline.

### 5.2 Edit target over the editor document

`AvalonEditTextEditTarget` implements `ITextEditTarget` and its version contract. The
edit-target contract documented on `TextEditorEditOperations` is the pattern: a supplied
target must hold the same content as the editor document when the call is made and must
update the editor document before returning, because requested caret and selection
offsets are applied to the document clamped to its current length. When no target is
supplied, apply through the binding's own target over the document directly.

### 5.3 The resolve/apply split

Every per-keystroke or per-command transform follows the same shape: capture a snapshot,
call the neutral planner or resolver (`TextLineCommentPlanner`, `TextAutoClosingResolver`,
`ITextDocumentFormatter`), then apply the result through the edit target. The binding
keeps only input glue, read-only policy, document edits, and provenance tracking
(`TextLineCommentService` and `TextAutoClosingService` are the references). If a new
feature needs text-only logic that does not exist yet, add the planner to Core with this
shape instead of keeping logic in the binding.

### 5.4 Margin over a line-status source

A margin takes a Core `ILineStatusSource`, draws a marker for each one-based marked line
(scaling with the editor's font metrics), and subscribes to `IChangeNotificationSource`
when the source implements it to invalidate itself. `UnsavedChangesTracker` is the
reference source for unsaved-change lines (built on `LineDiffer`); the bookmark family
shows the same pattern with persistence through the Core `SidecarLineFile` writer (the
host supplies the sidecar extension).

### 5.5 Request lifecycle and dispatcher marshaling

Controllers run their request lifecycle on `LatestRequestCoordinator`: `RunAsync` awaits
work and publishes through a current-state check, or `BeginRequest`/`IsCurrent` admit
and validate a request the controller completes itself. Debounce input in the binding
(`DispatcherDebouncer` pattern), marshal results to the UI thread explicitly
(`DispatcherInvocation` pattern), and expose state through the neutral host-state records
(`TextCompletionPresentationState`, `TextSignatureHelpPresentationState`); the hover
controller deliberately exposes no state and reports every decision through its single
display callback instead.

### 5.6 Signature parameter highlight

Signature help delivers pre-resolved parameter labels (`TextSignatureParameterInfo`), and
the provider contract only guarantees that a label is a plain-text fragment of
`TextSignatureInformation.Label`. A binding highlights the active parameter by locating
the parameter's label text inside the signature label. When a label cannot be located
(for example because the server omitted parameter text from the signature label, or
because the provider left the label blank), present the signature without an
active-parameter highlight instead of guessing a range. An empty label is never
locatable, so treat it as unresolved rather than matching it at position zero.

## 6. Suggested porting order and test layering

Dependency-respecting order (each step builds on the previous):

1. Snapshot and document adapters (section 5.1) - the foundation everything else reads.
2. Edit-target adapter and the editing/line operations (section 5.2).
3. Services that compose neutral planners: auto-closing, line comments, formatting,
   indentation bridge.
4. Navigation helpers over `NavigationLocation`, plus the mouse helpers.
5. Line-status margins, the unsaved-changes tracker, and the bookmark family with
   sidecar persistence.
6. Diagnostics renderer and the diagnostic projection glue.
7. Language-features controllers: completion first (window coordinator, skins, item
   bridge, hooks), then hover, signature help, code actions, definition navigation, and
   the semantic-token colorizer.
8. Host wiring: triggers, gestures, and popup precedence stay host policy - the
   repository's `Nickelony.IDEKit.KeyBindings` package or the host's own command code.

Test layering:

- The neutral logic is already covered in `Tests/Nickelony.IDEKit.Core.Tests`
  (`AutoClosing`, `Comments`, `Requests`, ...) and `Tests/Nickelony.IDEKit.IntelliSense.Tests`.
  A new binding does not re-test planners or kernels and never duplicates those suites.
- Binding suites test only the adapters and controllers: document application, edit
  targets, selection restoration, margin invalidation, and controller decisions.
  `Tests/Nickelony.IDEKit.AvalonEdit.Tests` and
  `Tests/Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests` are the references.
- Pin the toolkit UI thread: the AvalonEdit suites use MSTest `[STATestClass]` because
  WPF controls need STA; an Avalonia binding pins its UI thread the same way.
- Tests live in flat per-package namespaces; filter by type-name prefix, for example
  `--filter "FullyQualifiedName~TextLineComment"`.

## 7. Do not port

- The `Nickelony.IDEKit.AvalonEdit.Markdown` and `Nickelony.IDEKit.AvalonEdit.TextMate`
  packages stay untouched (owner decision, 2026-09-18); they are optional integrations
  that depend on their own third-party libraries.
- Host-owned behavior: trigger gestures, popup precedence, workspace-edit application,
  and undo grouping are host code, not binding surface.
- The workspace family (`Nickelony.IDEKit.Workspace`, `Nickelony.IDEKit.Workspace.Views`)
  is independent of editor bindings; combine it with a binding in host code.
- Never fork neutral logic into a binding. Extend `Core`/`IntelliSense` with the
  resolve/apply split (section 5.3) so every binding benefits.

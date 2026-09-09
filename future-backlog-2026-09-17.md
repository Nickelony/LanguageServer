# Future backlog - live list (2026-09-17 baseline, verified and trimmed 2026-09-18)

> Purpose: the live list of the repository's remaining open work. Executed history is not carried
> here. Every entry was re-verified against the tree on 2026-09-18: forced rebuild 0 warnings /
> 0 errors; 15 suites, **3024 passed / 6 skipped / 0 failed** (refreshed at the AvaloniaEdit
> extraction close-out); shared feed at `preview.38` with 15 packages; Tomb cold verify green
> (TombLib.Tests 932/932, TombEditor.Tests 367/367). Evidence:
> `artifacts/verify-final-summary.txt`.
>
> Where the closed work lives: the docs archive (`docs/archived - irrelevant - to be deleted/`) -
> the settled-decision archive (`future-backlog-archive-2026-09-16.md`, which also carries the
> 2026-09-18 trim registry), the two 2026-09 program report sets (`__160916_*`), the retired plans
> (`__170917_*`, `__180918_provider-framework-execution-plan.md`,
> `__180918_avaloniaedit-readiness-extraction-plan.md`), and the kept-items dossier
> (`__160916_backlog-kept-items-dossier.md`). The previous working backlog (`future-backlog.md`)
> is archived there as provenance.
>
> Programs (2026-09-18): the AvaloniaEdit readiness extraction program is **complete** (phases
> 1-7; plan archived) - its set was flushed by the owner (`39fda58`) and the feed refresh is
> pending; the consumer migration register is kept below (section 2). The AvaloniaEdit port itself
> remains trigger-gated. One program has live remainders: the F6 plan
> (`__180918_f6-and-language-readiness-execution-plan.md`, repository root) closed out phases 1-5
> and both phase-6 library slices; the phase-6 host adoption stays trigger-gated (section 6).

## How this file works

- Work ships only as a complete vertical slice: **producer + host consumer + test story**. No
  model-only surface. An external adopter verified against its current source counts as the host
  consumer: check external adopters (Tomb Editor today) before deleting surface for lacking an
  in-repo consumer (rule precedence recorded 2026-09-19, `__180923_` IntelliSense review).
  Outcome of that check: none of the IntelliSense surface flagged as "unproduced" was deleted -
  Tomb Editor consumes the kernels, provider contracts, helpers, and item stamps in production;
  only `TextCompletionItemKindJsonConverter` remains deleted for being unproduced. The 2026-09-19
  round-3 review re-confirmed this outcome for `ITextDiagnosticsProvider`/`TextDiagnosticsRequest`
  (Tomb's `ErrorDetector` implements the provider) and recorded that `DiagnosticHitTester` has no
  direct caller in either repo while staying an advertised helper (keep-or-retire decision open).
- Breaking changes are allowed and expected; no compatibility shims; delete legacy leftovers.
- "Publish" means refreshing the local directory feed (`artifacts/packages`, consumed as
  `LanguageServerLocal`); nuget.org is out of scope. Tomb Editor is the alpha validation platform,
  not a publish gate.
- Priorities: **P1** = next feature wave; **P2** = after it; **P3** = opportunistic.
- When an item ships, remove it here, record the outcome in `CHANGELOG.md`, and add its record to
  the archive file (`docs/archived - irrelevant - to be deleted/future-backlog-archive-2026-09-16.md`).
  When a decision item is answered, record the answer here (and archive it when it closes work).

## 1. Owner decisions pending (hardening phase-6 remainder)

- **TOOL-1 - PublicAPI analyzer + baselines - one repo-wide pass.** Recommendation: **UNPARK** -
  enable `Microsoft.CodeAnalysis.PublicApiAnalyzers` and generate `PublicAPI.*.txt` baselines for
  all library projects in one coordinated change (RS0016/RS0017 become live; per-project
  enablement fails on undeclared symbols). Document the workflow in `CODE_STYLE_GUIDELINES.md` and
  `AGENTS.md`. Re-verified 2026-09-18: no analyzer reference and no `PublicAPI.*.txt` anywhere;
  the pass should cover the 15 library projects (sequencing note in the F6 plan). The baselines must
  capture the final `preview.38` surface, including `TextAutoClosingRequest` and the auto-closing
  preset constants, `LocalPathComparisonPolicy`, `PreferredDocumentLine`, `TextHoverEvaluationState`,
  `ILuaLanguageServerIntelliSenseProvider`, the moved dispatcher/forwarder surface in Provider, and
  the third-wave renames (`IdentifierSpanMode`, `IsBlankOrStartsWithLineComment`, `ApplyEdit`,
  `Superseded`, `IsWrappingSelection`/`DidWrapSelection`, `Default*AutoCloseBefore`).
- **TOOL-3 - `.runsettings` + desktop/GPU test categories + reproducible coverage.**
  Recommendation: **KEEP PARKED** - no CI consumer today. Re-verified 2026-09-18: no
  `.runsettings` in the tree.
- **Doc tracking.** Recommendation: **TRACK** `AGENTS.md`, `CODE_STYLE_GUIDELINES.md`, and the
  working backlog file (now this one); keep dated review/plan/report documents untracked or
  archived (current convention: `__*` files stay untracked). Re-verified 2026-09-19: `AGENTS.md`
  and `CODE_STYLE_GUIDELINES.md` remain untracked (both need a commit in the next flush); the
  backlog file itself is tracked and rides the wave index.

## 2. Owner actions - publication close-out

- **Flush + refresh.** The review-wave set is flushed by the owner (`0900e93`, 414 files; the
  previous extraction flush is `39fda58`). Remaining: refresh the local feed (`artifacts/packages`;
  currently `preview.38` with 15 packages) and run the visual spot-check pass against the packed
  feed (publish is owner-gated). The refresh can land whenever the owner runs it and is followed by
  the Tomb consumer pass (register below; that pass has since run green - see the close-out). The
  refresh would also replace the shared feed's 2026-09-18 snapshots, which still predate the
  IntelliSense and third Core waves. A follow-up set is staged for the next flush: the
  2026-09-19 audit closures (the M15 decoder modifier freeze with its CHANGELOG entry, the
  `TextDefinitionNavigation` terminology unification, and the Lua README/store documentation
  corrections). The third-pass Lua review wave (section 13) adds its documentation corrections to
  the same set.
- **Feed trap reminder:** source order is NOT a reliable guard - a same-version copy in the shared
  feed can win a restore (the `.38` false-result runs in the register below; third Core wave gate
  2026-09-19, `artifacts\r5-tomb-gate2.log`). Refresh the shared feed or pack a novel version (and
  prefer the private `RestorePackagesPath` pattern of `artifacts/tomb-wave4.ps1`) before any ad-hoc
  consumer restore.

### Consumer migration register - AvaloniaEdit extraction (compile-time using changes only)

| Type | From (namespace) | To (namespace) | Phase |
|------|------------------|----------------|-------|
| `TextCompletionRequestSession` | `Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion` | `Nickelony.IDEKit.IntelliSense.Completion` | 2 |
| `TextCompletionPresentationState` | `Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Presentation` | `Nickelony.IDEKit.IntelliSense.Completion` | 2 |
| `TextSignatureHelpPresentationState` | `Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Presentation` | `Nickelony.IDEKit.IntelliSense.Signatures` | 2 |
| `TextHoverRequestState` | `Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Hover` | `Nickelony.IDEKit.IntelliSense.Hover` (renamed `TextHoverEvaluationState`, 2026-09-19) | 2 |
| `TextSignatureHelpControllerOptions` | `Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Signatures` | `Nickelony.IDEKit.IntelliSense.Signatures` | 2 |
| `TextCodeActionContext` / `TextCodeActionRequestState` / `TextCodeActionItem` / `TextCodeActionControllerOptions` | `Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions` | `Nickelony.IDEKit.IntelliSense.CodeActions` | 2 |
| `NavigationLocation` | `Nickelony.IDEKit.AvalonEdit.Navigation` | `Nickelony.IDEKit.Core.Navigation` | 3 |
| `IChangeNotificationSource` / `ILineStatusSource` | `Nickelony.IDEKit.AvalonEdit.Rendering` | `Nickelony.IDEKit.Core.LineStatus` | 3 |
| `TextDiagnosticSegment` | `Nickelony.IDEKit.AvalonEdit.Diagnostics` | `Nickelony.IDEKit.Core.Diagnostics` | 3 |
| `TextAutoClosingOptions`, `TextAutoClosingPair`, `TextAutoClosingPairKind`, `TextAutoClosingAction`, `TextAutoClosingActionKind`, `TextAutoClosingProvenance`, `TextAutoClosingResult` | `Nickelony.IDEKit.AvalonEdit.Editing` | `Nickelony.IDEKit.Core.AutoClosing` | 4 |
| `TextLineCommentEdit`, `TextLineCommentAction` | `Nickelony.IDEKit.AvalonEdit.Comments` | `Nickelony.IDEKit.Core.Comments` | 5 |

New types (no migration needed): `TextAutoClosingResolver` (phase 4), `TextLineCommentPlanner`
(phase 5). Finalized 2026-09-18 against the staged diff (22 staged renames plus the two
`Core.LineStatus` moves staged as delete+add because their doc rewrites dropped rename
similarity). The Tomb consumer pass (`artifacts/verify-tomb-migration.ps1 -RunTests`) is
owner-timed after the feed refresh. **Status 2026-09-19:** the Lua second-pass host re-verify ran
the gate; it is blocked exactly on this register (4 errors: the `LanguageFeatures.Presentation`
namespace plus the `TextAutoClosingOptions`/`TextLineCommentAction` Core moves, all in files the
extraction workstream still owns as `MM`; zero Lua-related errors). The Lua slice was instead
verified by a standalone probe replicating Tomb's provider usage, which compiles 0w/0e against the
packed preview.38 Lua package (`artifacts/lua-surface-probe.ps1` + log). Re-run the gate after the
consumer pass lands.

**Status 2026-09-19 (IntelliSense resolution wave):** the host gate was re-run with the wave's
private packages after discovering that a stale same-version global-cache entry had produced false
"missing type" results (see the cache-trap note in this file). With fresh packages
(`1.0.0-preview.40`, feed rebuilt by `artifacts/is5-tomb-tests6.ps1`): all five
IntelliSense-consuming Tomb projects build clean and `TombLib.Tests` passes **932/932** - including
a genuine host-visible catch: `TextDefinitionRequest` must accept a blank symbol name as the
no-symbol-at-position state (the constructor now rejects only `null`; the CHANGELOG records the
final contract). `TombEditor.Tests` remains gated on this register - its dependency
`TombIDE.ScriptingStudio` still needs the phase-3 `NavigationLocation` move, the
`IWorkspaceDocumentEditView`/`IWorkspaceDocumentDeleteGuardView` imports, and the Workspace-wave
`IWorkspaceFileSystem.ReplaceFileAsync` implementation in `RecycleBinWorkspaceFileSystem`; none of
these are IntelliSense surface. Four Tomb test fakes also needed additive `GetCodeActionsAsync`
stubs for the Abstractions provider contract (added 2026-09-19, test-fake-only changes).
Re-verified 2026-09-19 15:10 on the settled tree: full solution build green; IntelliSense 399/399,
LanguageFeatures 360/360, Lua 250 (+4 skipped), Client 473 (+3 skipped). A second full-solution gate
later the same day (polish-pass close-out: rebuild 0 warnings/0 errors) confirmed all 15 suites green
with one intermittent Client handshake failure that re-passed in isolation and on full re-run
(now tracked as CL-14 in section 12; evidence `artifacts/full-test-summary.txt` and
`artifacts/full-test-Nickelony.LanguageServer.Client.Tests.log`). The `.40` gate evidence is
logged in `artifacts/is5-tombtests6-summary.txt`; the shared `review-feed` was afterwards rebuilt
by the extraction workstream's `preview.38` pass, so repack before any further ad-hoc consumer
restore. The `0900e93` flush carries the 2026-09-19 review waves,
including: `TextHoverEvaluationState` (renamed from `TextHoverRequestState`;
the binding hook member is `BuildEvaluationState`), `TextCompletionItemKindConversion.FromLspKind`
(moved off `TextCompletionItemKind`), the completion commit path honoring `TextEdit.NewText` and
the edit range, and the host-side `TextCodeActionMenuOptions`; Tomb source is already migrated.
Late re-check 2026-09-19: the consumer pass was still mid-flight at that moment (active staged and
unstaged edits across `Tests/TombEditor.Tests/ScriptingStudio` in `Tomb-Editor`); it landed later the
same day and the host gate is now complete - see the close-out below.

**Status 2026-09-19 (post-wave close-out): completed and green.** The consumer pass landed in
`Tomb-Editor` and the gate was re-verified with full feed isolation: the gate now packs a distinct
wave version (`1.0.0-preview.98`) and restores into a private `RestorePackagesPath` folder
(`artifacts/tomb-wave4.ps1`). Two earlier same-version `.38` runs produced false results because
NuGet mixed the shared feed's stale packages with the fresh private feed (old AvalonEdit + new Core
surfaced as CS0104 "ambiguous type" for `TextAutoClosingOptions`/`TextDiagnosticSegment`/
`TextLineCommentAction`; the stale IntelliSense package surfaced as a missing
`TextHoverEvaluationState`). Consumer changes in Tomb (uncommitted, per convention):
`NavigationLocation` imports to `Core.Navigation` (`SearchResultLocation.cs`,
`SearchResultLocationResolver.cs`, `DocumentDiagnosticsPaneProvider.cs`,
`SearchResultsPaneProvider.cs` + the resolver tests; the AvalonEdit namespace stays where
`TextAreaNavigationOperations` is used), `RecycleBinWorkspaceFileSystem` and its two test doubles
migrated `ReplaceAsync` -> `ReplaceFileAsync`, `CreateCaretLocationAt` -> `CreateCaretLocation`,
`WorkspaceDocumentReloadRequest` dropped the expected-stamp argument, `StudioSilentActionService`
bridges `IWorkspaceDocumentManager.ReplaceAsync(...).StoreResult` (the old synchronous `Replace` is
gone), the two view test doubles read text through `IWorkspaceDocumentEditView`, and
`ScriptingPhaseCCompositionTests` asserts the Lua wave's new missing-executable message text.
Result: **six targets 0 warnings/0 errors; `TombLib.Tests` 932/932; `TombEditor.Tests` 367/367**
(`artifacts/tomb-wave4-summary.txt`).

**Status 2026-09-19 (close-out re-gate after the fourth wave landed): confirmed green end to end.**
The `ILanguageServerIntelliSenseProvider` `IAsyncDisposable` migration landed host-side: four Tomb test
doubles (`FakeLuaIntellisenseProvider`, `TrackingIntelliSenseProvider`, `FakeLuaCompletionProvider`,
`FakeIntelliSenseProvider`) gained additive `ValueTask DisposeAsync()` members, and
`ScriptingPhase0CompositionTests` verifies `DisposeAsync` once (the scope disposes the provider
asynchronously) with the disposal order `provider, bridge, lifecycle` unchanged. Full host gate v6
(`artifacts/tomb-wave6.ps1`, 13 packages at `1.0.0-preview.100`, private restore folder):
**six targets 0 warnings/0 errors; `TombLib.Tests` 932/932; `TombEditor.Tests` 367/367**
(`artifacts/tomb-wave6-summary.txt`; the pre-migration run that caught the fallout is preserved as
`tomb-wave6-summary-before-regate.txt`).

## 3. Trigger-gated - start only when the trigger fires (re-verified 2026-09-18: no trigger has fired)

- **A1 - FileSystemWatcher reload adapter + async reload callbacks - P3.** Trigger: a second host
  appears, or the Tomb loop outgrows its shape. Path if revisited: async prompt overloads on
  `FileReloadCoordinator` first; optional Workspace-side watcher adapter second; optional doc-only
  "host watch policy" note with the next Workspace-touching wave. Do not move the Client watcher;
  no new package before a second consumer (ADR 0001).
- **CLI-1 - Client IDEKit boundary (protocol-only or bridge package) - P3.** Trigger: a non-IDEKit
  consumer, or de-IDEKit-ing the provider contracts. State (re-verified 2026-09-18): the
  `Interop/` slice already isolates every IDEKit-typed public member.
- **AP-05 - custom launcher/transport seam - P3.** Trigger: a host genuinely needs to replace
  process launch or transport; add the seam together with its adopter. The language-#2 kickoff
  with the chosen server in hand is this item's go/no-go moment (recorded 2026-09-18): the
  server process working directory is pinned to the executable's folder and there is no
  host-settable working directory, so a server that must launch from another directory needs
  this seam.
- **A2 - recycle-bin/trash delete decorator - P3.** Trigger: a host adopts the library without its
  own decorator.
- **A3 - `*.rename` crash-sweep helper - P3.** Trigger: the first reported orphaned rename file.
- **Parked API ideas (trigger-based).**
  - P1 - `IHighlightingDefinitionResolver` seam (TextMate). Trigger: two isolated editor
    configurations needing different highlighting registries in one process.
  - P3 - Core `ReadOnlySpan<char>` identifier overload (IDN-03). Trigger: the host expansion loop
    grows or profiling justifies the overload.
  - P4 - TextMate selector operators (exclusion, direct-child, wildcard, priority, grouping).
    Trigger: a real `.tmTheme` needs them.
  - P5 - `ITextMateStyleResolver` seam (TextMate). Trigger: computed-style overlays or a second
    resolver implementation.

## 4. Watch items - evidence-gated (all premises re-verified 2026-09-18)

- **Core documented trade-offs (TXT-8 residual) - P3.** The backtick-fence pre-join copy and the
  identifier-boundary span fast path stay until the package moves past net8; the eager
  line-text table stays as documented; WS-1 tracks the lazy-snapshot variant.
- **WS-1 - Core primitives consolidation - P3.** Remaining: the lazy line-text snapshot
  (`DeferredTextSnapshot` vs AvalonEdit's `TextDocumentSnapshot`). Path comparison done;
  fingerprint sharing declined (different contracts - revisit if a third consumer appears).
- **WS-3 - delete-guard protocol simplification - P3.** Trigger: a second view-providing host, or
  an `IWorkspaceDocumentView` redesign.
- **IS-43 - Client decode path.** `SemanticToken`/`TextSemanticToken` overlap and the double
  line-map build - revisit when the decode path is reshaped.
- **IS-06 - open-vocabulary plumbing.** Monitor for a third instance.
- **IS-09 - per-decision materialization.** Revisit only if a snapshot-carrying request becomes
  viable.
- **Client residuals - P3.** Platform-matrix: remaining `C:\Workspace` literals and unmarked
  Windows assumptions in a few tests (`TextWorkspaceEditTests`, `WorkspaceSnapshotTrackerTests`);
  the construction-site mass wrap landed with F6 (a platform-matrix decision, not a
  language-flavour one). Internal-doc verbosity/terminology polish (convention-wide). Dispatcher
  timeout-hysteresis stays provider policy (`LanguageServerProviderOptions`); raw `int[]`
  completion handoffs stay with the delta-state design (revisit with the completion wave).
- **Client performance items - recorded, no promotion.** Measured 2026-09-17 (phase 2b): line map
  ~51 us @ 42 KB; `NormalizeLocalPath` ~112 ns/call; snapshot capture ~19 ms for 300 files with
  ~62% in read+hash (the eager fingerprint is the kept design). Re-open only if the numbers or the
  design change; details in the hardening phase-2b report (archive).
- **Capability-flag surface (2026-09-19 wave) - watch.** `ILanguageServerClient` gained
  `SupportsHover`/`SupportsDefinition`/`SupportsSignatureHelp` (the Provider/Lua fakes were
  updated in-wave). Watch: if `ILanguageServerIntelliSenseProvider` (Abstractions) or the Lua
  provider surface gains matching members, external implementers break at the next pack - Tomb's
  `Tests/TombLib.Tests/Lua/LuaDocumentSymbolsProviderTests.cs` `FakeIntelliSenseProvider` is the
  known external implementer; add it to the consumer migration register then, and re-check the
  Tomb capability consumers (`LuaEditor` hover/definition gates) against the new flags.
- **AE - GameFlow multiline `/* */` comment spans.** Migrate onto
  `RegexHighlightingDefinition.Create(…)`/`BuildSpans()` when the highlighting files are touched
  anyway; Tomb keeps its private fork until then.

### Provider review residuals (2026-09-19, from `__180923_provider-review-findings-and-resolution-plan.md`)

- **PLC-01 - move provider machinery out of `Client` - P3.** `LanguageServerRequestDispatcher`,
  `DocumentOperationScheduler`, `TrackedDocumentStore`, `WorkspaceFileWatcher`,
  `WorkspaceSnapshotTracker`, `WorkspaceFileChangeForwarder`, and `WorkspaceWatchSpecification`
  are consumed only by Provider/Lua while living in `Client`. Trigger: the next breaking window
  (bundle with the Client package-boundary work - the Client review tracks the same item as
  HOST-4, so do not start two competing moves).
  **Partial delivery (2026-09-19, `__180923_` Client review):**
  `LanguageServerRequestDispatcher` and `WorkspaceFileChangeForwarder` (with their tests) moved to
  `Nickelony.LanguageServer.Provider`; `WorkspaceChangeAccumulator` became public in `Client` so the
  moved forwarder can buffer deferred changes. The remaining types stay trigger-gated: the watcher
  and its specification are one unit, and the scheduler/store are shared neutral utilities whose
  move would also ripple through the Lua store.
- **DUP-03/DUP-04 - cross-family helper consolidation - P3.** Event-raising policy exists in
  Provider (`RaiseSubscribers`) and Client (`SerializedSignalSubscriberSet`); background-task
  observation exists in Provider (`BackgroundTaskObserver`) plus two Client locals. Trigger: a
  fourth consumer appears in either family.
- **DUP-06 - `IsPathWithinRoot` promotion - P3.** Trigger: a second path-containment need; then
  promote the coordinator helper into `LanguageServerPaths`.
- **PRF-01 - per-request fast-path work - recorded, no promotion.** `EnsureStartedAsync` runs the
  watcher-ensure/replay checks on every request (and now also once before the capability gate).
  Measured cost is negligible; re-open only if profiling shows it. No watcher-ensured flag without
  measurement.
- **Document-identity redesign - trigger-gated (HST-03/RSK-04 residual).** Document identity is
  the normalized absolute local path plus the platform case policy (`LanguageServerPaths`,
  AppContext overrides); virtual/untitled/remote documents and case-sensitive volumes on
  case-insensitive hosts are unrepresentable. Trigger: the first host that needs one of those
  identities, or a case-policy incident. Provider-side docs already state the constraint; the
  redesign is family-level (Client + Provider + language packages).
- **TST-14 - shared `FakeLanguageServerClient` - deferred.** The Provider fake is minimal, the Lua
  fake is rich and Lua-owned; the repo convention for shared test code is linked files under
  `Tests/TestSupport/`. Tracked together with the Lua review's N27 (linked-file consolidation);
  do not add a test-support package before a decision there.
- **TST-15 - Lua reflection into Provider state - concluded.** Provider `InternalsVisibleTo`
  grants only its own test assembly; exposing `_disposeCts` to Lua.Tests would either widen IVT
  to a foreign test project or add test-only production surface. The behavioral invariant is
  covered by the Provider lifetime tests and the Lua in-flight-disposal test; the reflection
  accessor stays as the Lua review's D17 (documented seam).
- **TST-18 - transport-failure reopen timing - hardened (residual race recorded).** The Lua
  regression family that injects a `didChange` transport failure and expects the next operation to
  re-establish the document was flaky across full-suite runs (alternating runs timed out on the
  reopened-document wait in two different tests, while the wave's own back-to-back runs were
  green; the third family member, `GetHoverAsync_ReopensDocumentAfterIncrementalChangeTransportFailure`,
  had been repaired during the wave itself). Cause: the failed send's recovery bookkeeping
  (`InvalidateTrackedServerSynchronization` plus `MarkStartupTransportUnavailable`) completes
  asynchronously relative to the recorded notification, so a single follow-up operation can
  precede it and observe a still-synchronized document; any later operation recovers (the tracked
  content is preserved). The two observed tests now retrigger the recovery operation through
  `TestPolling.WaitForAsync` instead of pinning one interleaving, and their fixed 2-second budgets
  moved to the shared `TestPolling.DefaultTimeout`. Re-open as a product item only if a host
  observes a dropped first edit after a transport failure (current reading: eventual recovery,
  no content loss).
- **API-08 - failure-text hook defaults - declined (decision recorded).** `CreateStartupFailure`
  and `CreateMissingClientFailure` stay abstract: the copy is language/host-facing text
  (installation wording, product names) that the framework cannot phrase neutrally without
  losing meaning; the one-line implementations are the intended cost. The Lua diagnostics message
  fallback (`Unknown Lua diagnostic.`) and the omitted/unknown-severity fallback are the same class
  of language-facing copy and stay in the language package. The startup-failure copy no longer
  references a host log (second-pass review, 2026-09-19).
- **API-01 - request-helper parameter shape - decided.** `SendDocumentRequestAsync` /
  `SendDocumentPositionRequestAsync` keep their explicit parameter lists (method, capability gate,
  parameter builder, response parser, fallback) instead of a descriptor bundle: each parameter
  is a distinct piece of the request definition, call sites already use named arguments, and the
  contract is documented on the helper. Re-open only if a request shape appears that needs
  composition.
- **PRF-02 - startup-path decomposition - deferred (no behavior change).** `EnsureStartedAsync`
  (`Provider/LanguageServerIntelliSenseProviderBase.Lifecycle.cs`) is the provider's deepest single
  control-flow statement: hard-failure latch, fast-path reuse, restart shielding, lock re-entry
  re-check, reopen preparation, transport start, and the post-start settle all live in one method.
  Decomposition into a startup-outcome object was considered during the 2026-09-19 wave and
  deliberately not applied: the method is fenced by generation checks, lock re-entry, and disposal,
  so splitting it without a driver risks subtle ordering changes. Trigger: the next change that
  touches restart semantics; split along the existing phases (admit, fast path, lock, restart
  preparation, start, settle) and document the state-fence invariant per phase.
- **API-10 - synchronization parameter descriptor - deferred.** `SynchronizeDocumentAsync` ->
  `SynchronizeDocumentInSlotAsync` -> `SynchronizeDocumentCoreAsync` thread the same growing
  parameter list (path, content, open-reference flag, request-reference flag, refresh flag, the
  request-reference binding, token). Explicit lists stay per the API-01 precedent; re-open if a
  fourth synchronization shape appears that needs composition.
- **API-11 - forwarder constructor bundling - deferred.** `WorkspaceFileChangeForwarder` takes six
  constructor parameters (two capability accessors, the ensure-started delegate, the
  transport-marking action, the optional failure logger, the buffering flag). Named arguments keep
  call sites readable today; bundle into a seam record only when a seventh parameter appears or a
  second consumer needs the same bundle.
- **Path-containment separator probe - concluded (no change).** The 2026-09-19 review asked whether
  `WorkspaceChangeCoordinator.IsPathWithinRoot` must accept `Path.AltDirectorySeparatorChar`.
  Verified: `LanguageServerPaths.NormalizeLocalPath` rewrites alternative separators to the platform
  separator, `GetFullPath` canonicalizes, and `TrimEndingDirectorySeparator` strips trailing
  separators (drive roots keep theirs), so both operands use `Path.DirectorySeparatorChar`
  exclusively; accepting alternative separators here would be unreachable code.
- **TST-16 - provider test-fixture cosmetics - declined.** Per-class static fixture paths, inline
  temp-root cleanup blocks, and `Path.GetTempPath()` directory naming differ between Provider test
  classes. Unifying them is churn without behavioral value; fixtures stay per class.

#### Second fresh review residuals (2026-09-19)

- **Watcher reconciliation replay coverage - deferred.** The watcher-recovery test stops at "a
  replacement watcher was created"; a deterministic end-to-end assertion of
  `ReconcileWorkspaceSnapshotAsync` (missed changes replayed as `didChangeWatchedFiles`) needs a
  snapshot seam or a controlled file-system interleaving.
- **Coordinator buffered replay end-to-end - deferred.** `ReplayDeferredWorkspaceFileChangesAsync`
  is exercised at the forwarder level only; a provider-level test that buffers changes during a
  startup gap and replays them on recovery is still open. Resolved in the third pass:
  `WorkspaceSendFailure_MarksTheTransportUnavailableAndReplaysTheBufferedChanges` buffers a
  watched-files batch through a failed send and replays it after the next restart end to end.
- **Queued-update cancellation coverage - deferred.** Canceling a queued-not-started update and
  `CancelAllQueuedUpdates` at disposal are untested at the provider level.
- **Client fake fidelity - deferred.** The Provider fake's `StartAsync` always advances the
  generation (the real client returns `true` with the current session when already ready) and
  never resets `TextDocumentSyncKind` on transport loss; align when a test needs either shape.
  Resolved in the third pass: `StartAsyncHandler` supports gated, faulted, and failed starts (the
  shield regression uses it); the synchronization mode is deliberately kept across a simulated
  transport loss and that divergence is documented on the property.
- **Store-hook fault containment - design decision open.** An unexpected exception thrown by a
  language store hook during `Synchronize` still escapes the request pipeline as an exception
  (the identity-bound reference is now released on that path). Decide whether the framework
  should contain such faults like its own hooks; do not change without a policy decision.
- **Trimmed-close duplicate tail - recorded (benign).** In a trim/recreate/close race a late
  trimmed close can emit a duplicate `didClose` for an already-closed document; LSP tolerates
  the notification, and exactness would need per-path tombstones in the store.

#### Third fresh review residuals (2026-09-19, second execution)

Resolved in-wave: the restart replay no longer runs inside a document scheduler slot (update- or
open-triggered restarts previously threw `InvalidOperationException` or deadlocked against the startup
lock), the restart shield keys off a completed-startup history, hook containment covers the
startup/missing-client failure hooks and the rename path, a session without a negotiated synchronization
mode skips changes with a warning instead of failing the operation, the forwarder keeps failed batches ahead of
concurrently buffered changes, server rejections break the timeout streak, `DisposeAsync` shares the
provider teardown, and the fake gained a faultable/gateable `StartAsync` handler.

- **Deferred-close ownership - P3, next Provider breaking window.** The deferred-close/rename-follow
  machine (`_pendingDocumentCloses`, the open-cancels-close rule, the bounded follow loop) compensates for
  the store returning `BusyWithRequests` from `TryClose`; moving "close pending until references drain"
  into `TrackedDocumentStore` removes the provider-side state, the follow limit, and the rename migration.
  Deferred: a store-semantics change with wide test impact; the provider-side flow is correct as shipped.
- **Settings payload single owner - P3, with the next Client API wave.** Lua wires the same factory into
  `LanguageServerClientOptions` (configuration responses) and `CreateSettingsPayload` (change
  notifications); a client operation that refreshes its snapshot and sends the notification would collapse
  the hook. Both wiring points must move together - do not change piecemeal.
- **Watcher substitution abstraction - P3, trigger: a host requirement.** `CreateWorkspaceFileWatcher` can
  only construct the sealed default watcher (the documented seam covers construction, not a different
  watcher source). Introduce a watcher abstraction in Client when a host needs one; Tomb Editor does not
  override the seam today.
- **Watcher-failure message hook - optional.** The framework now raises host-neutral failure text; a hook
  mirroring `CreateStartupFailure` stays available if a host wants to localize or brand the message.
- **Remaining ensure/send classification ladders - fold on the next touch.** The ensure-start ladder is now
  shared (`TryEnsureStartedForOperationAsync`); the dispatcher retry and per-document catch ladders stay
  duplicated by design (different try bodies). Consolidate if a third ensure-style call site appears.
- **Value-type `TResponse` in `SendDocumentRequestAsync` - latent.** The helper's `response is null` check
  never fires for a struct response; constrain `TResponse` or document the requirement before a provider
  adopts one.
- **Follow-close residual - recorded (bounded).** A pending close beyond the rename-follow limit completes
  only on a later release/close/trim for the path; add a durable drain only if that interleaving becomes
  reachable in practice.
- **Test strengthening - P3 test pass.** `TrimSend_WhenTransportIsDisposed_KeepsTheDocumentedFallback` still
  cannot distinguish a swallowed exception from a no-op; the triple missing-client coverage can be
  consolidated when the suites are next touched.

### Lua review residuals (2026-09-19, from `__180923_lua-review-and-resolution-plan.md`)

Recorded after the audit found the plan's pending items were not yet ticketed here. M20 (nested-root
forwarding) and M15 (per-token modifier copies) are fixed; the items below remain open.

- **M9 - LSP 3.17 field gaps - P3.** Diagnostics `tags`/`relatedInformation`/`codeDescription`, symbol
  `deprecated`, action `disabled`, and completion `labelDetails` stay unmodeled; decide model-vs-document
  when a consumer needs them (LuaLS is the only supported server today).
- **M19 - bare-string documentation format - watch item.** `MarkupContentReader.ExtractContent` treats a
  bare string payload as Markdown everywhere (LuaLS sends `MarkupContent`, so nothing is produced in
  practice; the behavior is documented on the method). Re-open with a call-site default-kind overload if
  a server with string-form documentation is adopted.
- **Test-harness wave (N11-N13, N16-N18, N20-N24, N26-N27) - P3.** Test naming/sealing/AAA and
  `[DynamicData]` conventions, fake-behavior notes, temp-directory teardown, integration-archive
  extraction and process-tree cleanup, test-file splitting, and fake consolidation (N27 pairs with the
  Provider review's TST-14); fold into the next dedicated test pass - none affect production behavior.
  Resolved in the 2026-09-19 polish pass: both Lua test classes are sealed and their parts agree,
  `LuaLanguageServerIntelliSenseProviderTests.Coverage.cs` was split into feature partials (`Rename`,
  `CompletionResolve`, `References`; the sync/diagnostics tests moved to `DocumentSynchronization`),
  and the 1 s wait budgets were replaced by the shared `TestPolling.DefaultTimeout` (5 s).
  Second-pass additions (2026-09-19 fresh review): fake fidelity gaps (injected request failures do
  not raise `TransportUnavailable`, synchronous in-order event delivery vs the real thread-pooled
  per-URI coalescing, the silent empty-`JsonElement` default, the `DisposeAsync` no-op asymmetry, one
  unlocked response-queue read); timing robustness (bare 50-250 ms negative-assertion windows, no
  per-test timeouts - the session budget was raised to 5 minutes); and live integration coverage for
  formatting, code actions, and signature help.
- **Host-neutrality fixtures (host items 1-2, N28) - P3.** Windows-shaped literals and case-policy
  assertions in the Lua test project; neutralize when the files are next touched.
- **A-1 - semantic-token surface promotion - language-#2 kickoff.** Promote the Lua semantic-token
  surface (event, cached read, refresh controller, generic token cache) into Provider/Abstractions when
  the second language package starts (decision recorded in the Lua plan section 14.1.2). Second-pass
  scope extension (2026-09-19 fresh review): `VersionFencedPayloadCache<TItem>`,
  `LuaDocumentVersionPolicy`, `WorkspaceEditConversion`, and the language-neutral parser partials are the
  same kind of promotion candidate; target Provider (the package bridging Abstractions and Client).
- **A-3 / A-7 - provider-framework API shape - next Provider breaking window.** Public client injection
  needs the internal watcher seam promoted; the family-wide `IAsyncDisposable` question belongs to the
  shared provider contract. Both wait for the framework's next breaking window.

### Lua second-pass review residuals (2026-09-19, fresh review)

- **Completion dedupe/merge + derived `Priority` - P3 decision.** The duplicate-identity and merge
  layer (and the numeric priority hint derived from the `sortText` ranking) has no counterpart in
  established LuaLS clients (VS Code passes the server list through). Keep the layer only with the
  motivating LuaLS duplicate observed and recorded; otherwise simplify the parser to a pass-through
  and let consumers order by the protocol `sortText`/`preselect` fields.
- **LuaLS interactive client flags - P3 watch.** The initialization options advertise
  `changeConfiguration` and `viewDocument`, but the client implements neither flow LuaLS gates on
  them (`$/command` `lua.config` notifications and `window/showDocument` requests are ignored); the
  flags stay for parity with established clients because `checkThirdParty = Disable` keeps the
  interactive prompts from firing in practice. Re-open if a host wants server-side configuration
  suggestions or library-file navigation.

## 5. Language-#2 kickoff - compatibility checklist (recorded 2026-09-18, F6 program phase 5)

Full authoring detail: `docs/ProviderAuthoring.md`.

- **Launch shape.** The server process working directory is pinned to the executable's folder;
  there is no host-settable working directory. A server that must run from, or resolve paths
  against, another directory is the AP-05 go/no-go at kickoff (with the chosen server in hand).
- **Positions are fixed UTF-16.** Zero-based line/character pairs in UTF-16 code units; the
  client never negotiates another encoding. Servers that assume UTF-8 columns need a mapping
  decision.
- **Push-only diagnostics.** The client consumes `textDocument/publishDiagnostics` and does not
  implement pull diagnostics (`textDocument/diagnostic`); a pull-only server needs a design
  decision at kickoff.
- **Dynamic registration is unsupported by design.** The client advertises
  `dynamicRegistration = false` and logs and ignores `client/registerCapability` /
  `client/unregisterCapability`; a server that depends on dynamic registration does not fit.
- **Multi-root interplay (F6).** All roots are advertised in `initialize` (first root as
  `rootUri`) and answered for `workspace/workspaceFolders`; each root gets its own watch scope;
  workspace folders are fixed at construction (`didChangeWorkspaceFolders` is not sent).

## 6. Conditional program - F6 phase 6 (code actions) [host adoption only]

- **F6 phase 6 - code actions (quick fixes).** The F6 program's phases 1-5 are complete, and
  the library side is complete in two slices: the **contract slice** (2026-09-18,
  owner-scheduled) - the `TextCodeAction`/`TextCodeActionRequest` contract, the
  `Protocol/CodeActions/` payload family with `SupportsCodeActions`, and the Lua mapping
  (context diagnostics from the cached range-overlapping diagnostics; command-only actions
  omitted) - and the **host-UI slice** (2026-09-18, owner-directed) - `TextCodeActionController`,
  the light-bulb `TextCodeActionMargin`, and the skinned drop-down menu in
  `Nickelony.IDEKit.AvalonEdit.LanguageFeatures` (35 tests; standard WPF menu items over skin
  chrome). What remains is the host adoption: wire the provider adapter hooks, add the margin to
  the editor's left margins, bind the quick-fix key gesture, and apply invoked actions through
  the existing workspace-edit applier chain in one undo unit. It starts on a confirmed appetite,
  then promotes to its own program and is re-planned per the plan's phase-6 section. Plan and
  phase records: `__180918_f6-and-language-readiness-execution-plan.md` (repository root,
  phase-6 section).
- **Deferred refinement (F6-D3) - runtime workspace-folder changes.** Roots are fixed at construction
  and `workspace/didChangeWorkspaceFolders` is not sent (see the language-#2 checklist in section 5);
  the F6 plan records the decision as D3. Trigger: the first host that needs runtime folder mutation;
  adding it requires a send path plus watch-scope add/remove, because watch scopes and snapshots are
  created per construction-time root.

## 7. Workspace review residuals (ticketed 2026-09-19)

Residual, non-blocking work from `__180923_workspace-review-resolution-plan.md` (all defect and
risk items are fixed; these are refactors and extra permutations). Sources and decisions are in
that file's resolution register.

- **MW-02 - manager per-view bookkeeping consolidation (P3).** Collapse `_views`/`_viewIds`/
  `_viewVersions` into a binding record with `_viewsByViewId`/`_viewsByDocument` kept as indexes.
  No correctness payoff today; the maps document their lookups.
- **TEST-04/06/07/08 remainders (P3).** Additional status-family permutations (scripted
  save-as `DestinationBusy`, conflict-resolution blocking corners), the remaining manager
  teardown branch ideas, and extra disposal/held-operation permutations. The defect regressions
  and the reachable gaps are already covered.
- **TEST-12/13 (P3).** Split the oversized multi-concern tests and share a base between the two
  store file-system doubles (`FakeFileSystem` scripts results; `ControllableFileSystem` holds
  rendezvous open). Explicit per-test knobs were kept deliberately.
- **Tomb consumer compile verification (P2, next pack wave).** Tomb Editor
  (`Nickelony/Lua-Intellisense`) source is migrated to the settled Workspace API (async dispatch
  seam, `ProcessQueuedFilesAsync`, `ReplaceAsync`/`DiscardAsync`, directory requests without the
  batch, view capability interfaces, manager fakes). Compile-verify and run the scripting suites
  with the next local-feed pack, per the publication hold.

### Workspace fresh-review residuals (2026-09-19, resolved wave)

The 2026-09-19 fresh review of `Nickelony.IDEKit.Workspace` is resolved in the same wave, except
for the items deliberately deferred here:

- **WS-4 - status and result vocabulary consolidation (P3).** 18 public status enums and seven
  near-identical `...Result` records repeat `StaleDocument`/`StaleDocumentInstance`/
  `DocumentNotFound`/`OperationInProgress`/`Canceled`. A shared result core or a common status
  subset would shrink the surface, but every consumer switches on the per-operation enums, so the
  redesign needs a consumer inventory (Views, Tomb) first. The wave instead removed the dead reload
  stamp parameter, renamed `IWorkspaceFileSystem.ReplaceAsync` to `ReplaceFileAsync`, and
  deduplicated the factory documentation.
- **WS-5 - lightweight file-stamp mode (P3).** `LocalWorkspaceFileSystem` hashes the whole file
  (SHA-256) on every capture. A built-in size+timestamp mode with documented weaker conflict
  detection would spare hosts a decorator; it needs an answer for `ReplacementStateUnknown`
  classification when the content hash is absent before it can land.
- **WS-6 - per-document gate retention (P3).** `_documentGates` keeps one `SemaphoreSlim` per
  document ever opened until disposal; prune a deleted document's gate once no active operation
  references it.
- **WS-7 - opaque temporary-file descriptor (P3).** `WorkspaceTemporaryFile.ContentHash` and
  `Length` bake SHA-256 hex semantics into the `IWorkspaceFileSystem` contract; consider an opaque
  descriptor so custom file systems do not have to reproduce the hash.
- **WS-8 - residual open-vs-bulk-operation window (P3, documented).** A directory rename now
  rebuilds tracking atomically (the moved instance wins a destination-id collision) and the delete
  path documents that an open completing after its final pass is equivalent to an open issued after
  the operation. Revisit only if a host reports duplicate-instance symptoms.
- **WS-9 - save-as validation order (P3).** `SaveAsAsync` captures and hashes the source (and the
  destination for a second path) before `WriteReplacementAsync` encodes the content, so unencodable
  content pays two whole-file hashes before `InvalidEncoding`. Reorder only together with an
  encode-once refactor of the write path; the current order is pinned by
  `SaveAs_UnencodableContentReportsInvalidEncoding`.
- **WS-10 - edit-applier path comparison policy (P3, resolved 2026-09-19 in wave 3).**
  `WorkspaceEditApplier` now takes a `LocalPathComparisonPolicy` for target-id de-duplication, so
  hosts reuse the store's policy instead of guessing `StringComparer.OrdinalIgnoreCase`; Tomb's
  two construction sites were migrated with the wave-3 host gate.
- **Tomb migration for the 2026-09-19 API changes (P2, completed 2026-09-19).**
  `WorkspaceDocumentReloadRequest` no longer takes `ExpectedOnDiskStamp`
  (`EditorDocumentController.Workspace.cs` passes `snapshot.OnDiskStamp` today); the
  `IWorkspaceFileSystem.ReplaceFileAsync` rename is already listed in the consumer migration
  register status above. Tomb's `SaveFileAs` already routes same-path saves through `SaveFile`
  before `SaveAsAsync`, so the in-place save behavior matches the host; the library now reports
  `SavedAs` instead of `DestinationInUse` for that case. Applied and verified in the host gates
  (see the wave-3 section below).
- **Tomb migration for the 2026-09-19 second wave (P2, completed 2026-09-19).**
  `FileReloadCallbacks.PromptReload` now receives the reload result instead of the path string;
  `EditorDocumentController.Workspace.cs` builds the delegate as
  `(string filePath, CancellationToken _) => Task.FromResult(ShowFileReloadPrompt(filePath))`, so the
  migration passes the result (or `result.Snapshot?.DisplayPath`) to `ShowFileReloadPrompt`. A clean
  reload that cannot decode the disk bytes now reports the `InvalidEncoding` code instead of
  `ReadFailed` (Tomb branches on statuses rather than codes in its reload handling). `AccessDenied`
  is additive and needs no migration. Applied and verified in the host gates (see the wave-3 section
  below).

### Workspace second fresh-review residuals (2026-09-19, wave 2)

The follow-up review wave is resolved in place except for the items below; the wave-1 residuals
WS-4 to WS-9 above remain open as described (WS-10 was resolved in wave 3). `PreparedOperationCount`
was checked and stays:
Tomb's `TextWorkspaceEditApplier` plumbs the value through its factory calls.

- **WS-11 - pluggable text codec (P3).** `WorkspaceTextCodec` is still a static class with the
  closed `TextEncodingKind`; a real codec seam needs a format model that can carry encodings beyond
  the four kinds, so the redesign waits for a consumer that needs one (Shift-JIS, GBK, custom
  fallbacks).
- **WS-12 - file-system seam collapse (P2, breaking).** Fold `WriteTemporaryAsync`,
  `ReplaceFileAsync`, and `DeleteTemporaryAsync` into one conditional atomic write owned by the
  implementation, and make `WorkspaceTemporaryFile.ContentHash` optional (absorbs WS-7). Keep the
  conditional-replacement semantics and the post-failure classification inside
  `LocalWorkspaceFileSystem`; coordinate with the Tomb compile-verification wave.
- **WS-13 - shared path-comparison composition (P3).** The policy is supplied independently to the
  store, the file system, and the reload coordinator; a single options value that all three consume
  would remove the silent-drift risk documented in the README.
- **WS-14 - reload coordinator drain semantics (P3).** `ProcessQueuedFilesAsync` silently no-ops
  while a pass runs; a path queued during the run waits for the next host call. Consider coalescing
  one follow-up pass or exposing a drain hint so hosts can loop deterministically.
- **WS-15 - store-level document defaults (P3).** An options value carrying the default open
  options and new-file format would spare editor hosts from threading encoding and newline policy
  through every call; per-call values remain overrides.
- **WS-16 - directory-operation interface slice (P3).** Directory rename and delete carry their own
  result vocabulary on the main store interface; split them into an interface slice only if the
  default adoption surface should stay small.
- **WS-17 - view failure codes placement (P4).** The view-manager codes remain in the neutral base
  package as deliberate shared vocabulary (documented on the type); move them to `Workspace.Views`
  only if the package boundary needs tightening.

### Workspace third fresh-review residuals (2026-09-19, wave 3)

The third fresh review (five independent audits plus in-place verification) had its whole
P1/P2/P3 plan resolved in place, including breaking changes: the failure vocabularies gained
`IsDirectory`/`MoveStateUnknown`/`TargetSkipped`/`TargetOutcomeUnknown`, the conflict result and
change records were reshaped, `WorkspaceDocumentKey` became a readonly record struct, the
reload/save-as/rename paths got uniform token and identity handling, and the format model moved to
`BeforeFileFormat`/`AfterFileFormat` (change-record formats optional). WS-4 to WS-9 and WS-11 to
WS-17 stay open below; WS-10 is resolved above. The Tomb migration register items for all three
waves were applied before the wave-3 host gate (the reload prompt already passed the result; the
wave-3 edits switched both Tomb files to `LocalPathComparisonPolicy`, the preparation to
`BeforeFileFormat`/`AfterFileFormat`, two view call sites to the struct key's `.Value`, and one
service plus four applier tests to `ChangeSet.HasChanges`), and `artifacts/tomb-workspace-wave7.ps1`
verifies the whole register at `1.0.0-preview.101` (summary:
`artifacts/tomb-workspace-wave7-summary.txt`): all six Tomb targets build with zero errors,
`TombLib.Tests` 932/932, and `TombEditor.Tests` 367/367 at the final package set. The earlier
wave-6 run had caught the concurrent `IAsyncDisposable` provider migration fallout (366/367 plus a
TombLib.Tests compile block); that stream migrated the host (four provider doubles gained
`DisposeAsync`, the composition test verifies it) and the wave-7 re-gate confirms the whole register
green. Leftovers that stay open:

- **WS-18 - transient replace/move retry policy (P3, documented).** A transient sharing violation
  that survives the few in-call retry attempts surfaces as `WriteFailed`/`MoveFailed`; the store
  performs no backoff loop. Hosts with aggressive external writers (build watchers, sync clients)
  would benefit from a bounded retry-with-backoff option; revisit at the first host report.
- **WS-19 - optional file-watcher seam (P3).** The reload pipeline is host-pull (queued paths); an
  optional `IWorkspaceFileWatcher` adapter would let hosts forward OS notifications directly and
  retire their own watcher glue. The neutral package stays free of `FileSystemWatcher` until a
  host needs the adapter.
- **WS-20 - internally synchronized reload coordinator (P3).** `QueueFile` and
  `ProcessQueuedFilesAsync` document a host-serialized contract (single UI thread today); internal
  synchronization would remove the contract if a host ever calls from multiple threads.
- **WS-21 - LSP `WorkspaceEdit` mapping (P3).** The workspace-edit store outcomes now map cleanly
  onto the LSP `WorkspaceEdit`/`DocumentChanges` vocabulary, and the projection table lives in the
  package README; an actual `WorkspaceEdit` adapter waits for the first LanguageServer-side
  consumer.
- **WS-22 - LocalFS rollback-failure fault injection (P4).** The
  `MoveStateUnknown`/`TargetOutcomeUnknown` classifications are covered through the seam; a
  fault-injecting file system test exercising a rollback failure inside `LocalWorkspaceFileSystem`
  would pin the classification end to end. Add with the next test-infrastructure pass.
- **WS-23 - merge near-duplicate UseDisk rejection tests (P4).** Two fixture-heavy tests pin the
  same rejection paths through different setup sources; merging them would cut setup duplication
  but loses the per-source regression signal. Merge only if the fixtures are refactored anyway.

### Workspace fourth fresh-review resolution (2026-09-19, wave 4)

The fourth fresh review (full source and test read plus three independent specialist sweeps) had
its whole P1-P3 plan resolved in place, documentation and naming only: the commit result's
observed-stamp contract now matches the re-capture behavior, the edit applier documents the
`ArgumentOutOfRangeException` for undefined encodings, the local file system lists
`DeleteTemporaryAsync` in its `UnauthorizedAccessException` surface, the README stamp-decoration
guidance now requires one stamp computation across every stamp-producing member (the previous
single-member advice could mix stamp vocabularies), the open vocabulary and the failure record
moved into `WorkspaceDocumentOpenContracts.cs` and `WorkspaceOperationFailure.cs`, the factory
comparison parameter is named `pathComparison` like every other policy-taking member (breaking
for named-argument callers only), prose uses `<see langword="null"/>` and "logical"/"source
stamp" consistently, and a directory operation that stops on a changed descendant now names that
descendant in its failure message. Tests: method `[Timeout]` guards on the rendezvous tests that
must not block on a held gate (a regression now fails instead of hanging), file-level
`OperationInProgress` coverage for rename/save-as/delete, initial stale-version coverage for
reload and conflict resolution, cancellation during the `UseLogical` write, live-store null/blank
directory snapshots, the canceled result matrix, the applier constructor guard, the decorator's
default delete forwarding, and a missing-path stamp capture (261 to 272 tests; Views 57/57).
Verification: lint clean; solution build 0 warnings / 0 errors; Tomb host gate
`artifacts/tomb-ws5-gate.ps1` at `1.0.0-ws5.1` (summary `artifacts/tomb-ws5-summary.txt`): 13
packs, six targets 0 errors, `TombLib.Tests` 932/932, `TombEditor.Tests` 367/367. Leftovers:

- **WS-24 - derive `PreparedOperationCount` from the target results (P3).** The
  `WorkspaceEditApplicationResult` factories accept the count separately, so a caller can
  contradict the per-target results; deriving it in the constructor removes the last member that
  can disagree. Deferred because the migration touches two factory calls in
  `TombLib.Scripting.UI/Editing/TextWorkspaceEditApplier.cs` while that file carries in-flight
  consumer edits (its `LocalPathComparisonPolicy` migration); do it with the next host migration
  wave.
- **WS-25 - `IWorkspaceFileSystem.CreateDirectoryAsync` seam (P3).** The commit path requires the
  destination directory to exist (host responsibility, documented); a create-directory member
  would let a headless non-local file system provision roots through the seam. Add when the first
  such consumer appears.
- **WS-26 - optional `PromptReload` when no resolver is supplied (P4).** The coordinator requires a
  prompt callback even when `ResolveConflict` is absent and the prompt would never be invoked;
  make it optional with conditional validation if the friction is reported.
- **WS-27 - test-name separator unification (P4, cosmetic).** The `Documents` test family uses
  merged `Member_ScenarioExpectation`-style names while the `Editing` family uses strict
  `Member_Scenario_Expectation`, and the codec tests are type-prefixed. Harmonize in one
  mechanical rename wave if the guideline is to be enforced strictly. The near-duplicate test
  pairs flagged by the review are deliberately kept: each pins its scenario through a different
  file-system double dialect (scripted results vs rendezvous), so the pairs are coverage, not
  duplication (same rationale as WS-23).

## 8. LanguageFeatures review residuals (ticketed 2026-09-19)

Residual, cross-package items from the 2026-09-19 fresh review of
`Nickelony.IDEKit.AvalonEdit.LanguageFeatures`. Every defect and risk finding was resolved in the
same wave (host-neutrality seams, event-id block, docs, tests); the open items below stay open
because they touch sibling packages or need a measured trigger, and completed items are kept for
their evidence. The review was repeated fresh on the same day against the settled code and its
entire action plan was resolved in a second wave (LF-TMB-02 records the verification); the
remaining optional items of that pass are ticketed at the end of the list. A third fresh review on
the same day (five independent audits plus in-place verification) resolved its P1/P2 plan in a
third wave - the documentation cluster, the tooltip/filtering policy documentation, the scheduler
collapse, the signature-help `Cycle` option, and the missing test branches (387/387) - and
LF-TMB-03 records its host verification; its trigger-gated leftovers join the list:

- **LF-EVT-01 - repo-wide event-id block allocation (P3).** The wave moved this package's log ids
  into its own `1000` block (completion `1000`-`1002`, hover `1010`-`1012`, signature help
  `1020`-`1021`, code actions `1030`-`1031`). The sibling emitter packages
  (`Nickelony.IDEKit.AvalonEdit.Markdown` ids `1`-`3`, `Nickelony.IDEKit.AvalonEdit.TextMate` ids
  `1`-`3`, `Nickelony.IDEKit.KeyBindings` ids `1`-`5`) still share the small range, so a logger
  shared between those packages still cannot demultiplex by id. Allocate and document a block per
  package in `CODE_STYLE_GUIDELINES.md` when each is next touched.
- **LF-CA-01 - code-action document snapshot (P2).** `TextCodeActionController` materializes the
  whole document text for every settled context because `TextCodeActionContext.DocumentText`
  (IntelliSense) carries a string. For multi-megabyte documents that copies the document on the
  debounce cadence; the controller remarks now document the cost and the
  `RequestDebounceDelay` mitigation. A cross-package change (snapshot/version or lazy text access)
  lands with the next IntelliSense breaking window or the first host report of large-file typing
  latency.
- **LF-TMB-01 - Tomb compile verification for this wave (P2, completed 2026-09-19).**
  `artifacts/lf-tomb-verify.ps1` repacks only `Nickelony.IDEKit.AvalonEdit.LanguageFeatures` into
  the private review feed (siblings stay at the last full wave), clears the stale same-version
  cache, then restores/builds the six Tomb targets and (with `-RunTests`) runs the two Tomb test
  suites. First run 2026-09-19: all six targets fail before LanguageFeatures binding, on the
  pending AvaloniaEdit-extraction migration in Tomb - `LanguageFeatures.Presentation` imports in
  `TombLib.Scripting.UI\Bases\TextEditorBase.Navigation.cs` and `Hover\HoverControllerFactory.cs`,
  and `AvalonEdit.Comments`/`AvalonEdit.Editing` imports for `TextAutoClosingOptions` and
  `TextLineCommentAction` in `Bases\TextEditorBase.Editing.cs` (both types now live in
  `Core.AutoClosing`/`Core.Comments`). Second run 2026-09-19 (migration partially landed): five of
  the six targets build against the repacked package (`TombLib.Scripting.UI` with its 8
  pre-existing warnings, the other four clean). `TombIDE.ScriptingStudio` still fails on migration
  leftovers unattached to this wave - three CS0246 for `NavigationLocation`
  (`FindAndReplace\SearchResultLocation.cs`, `Workbench\DocumentDiagnosticsPaneProvider.cs`,
  `Workbench\LuaReferencesPaneProvider.cs`; the type now lives in `Core.Navigation` while every
  ScriptingStudio file still imports the old `AvalonEdit.Navigation` namespace) and one CS0535 for
  `RecycleBinWorkspaceFileSystem` not implementing the new `IWorkspaceFileSystem.ReplaceFileAsync`
  (Workspace wave; that workstream's `RecycleBinWorkspaceFileSystemTests.cs` is staged in Tomb).
  The two Tomb test suites were not reached. Evidence for this wave: the usage audit found zero
  Tomb references to every surface this wave changed (code actions, preselection, menu options),
  and every member Tomb consumes still exists in the built package (the 360-test suite compiles
  against them). **Completed later the same day:** the ScriptingStudio namespace/API migration and
  the `ReplaceFileAsync` implementation landed in the `Tomb-Editor` working tree (see the
  consumer-register status in section 2) and the full `.98` isolation gate ran green - six targets
  0 warnings/0 errors, `TombLib.Tests` 932/932, `TombEditor.Tests` 367/367
  (`artifacts/tomb-wave4-summary.txt`). Independently reproduced by the LanguageFeatures package
  gate (`artifacts/lf-tomb-verify-98.ps1`, status `artifacts/lf-tomb-verify-status.txt`) with the
  identical counts; its earlier `preview.38`-pinned run had produced false failures because the
  same-version restore mixed the shared feed's stale Core/IntelliSense packages with the fresh
  private feed (the cache trap recorded in section 2).
- **LF-TMB-02 - Tomb verification for the second-pass wave (P2, completed 2026-09-19).** The
  closure-wide `preview.99` regime collided with concurrent gates in the shared NuGet cache (a stale
  same-version `LanguageFeatures` extraction kept poisoning restores with the first, invalid pack),
  so the gate packs the package alone at a unique `preview.101` with its dependency closure pinned
  to the shared `.98` regime (`artifacts/lf-pack-101.nuspec`, packed from existing bin outputs with
  `-p:IncludeSymbols=false`), then builds the six Tomb targets and runs both suites:
  `TombLib.Scripting.UI` keeps its 8 pre-existing warnings, the other five targets build
  0 warnings/0 errors; `TombLib.Tests` 932/932; `TombEditor.Tests` 367/367
  (`artifacts/lf-tomb-verify-101.ps1`, status `artifacts/lf-tomb-verify-101-status.txt`, logs
  `artifacts/lf101-tomb-*.log`).
- **LF-TMB-03 - Tomb verification for the third-pass wave (P2, completed 2026-09-19).** Two
  attempts packed the package alone at the unique `preview.102` (`artifacts/lf-pack-102.nuspec`,
  `artifacts/lf-tomb-verify-102.ps1`). The first restore found the shared closure already moved from
  `.98` to the concurrent `.99` regime (the `.98` copies had been replaced in the review feed), and
  every target failure belonged to FOREIGN migration leftovers - `TextHoverEvaluationState`
  (IntelliSense wave), `CommentOperations`/`IdentifierSpanMode`/`TextLineMap` (Core wave) - plus an
  `fxc.path` file lock from a concurrent Tomb build; no failure referenced LanguageFeatures. The
  second attempt hit the concurrent full-family `.38` repack wiping the feed mid-restore (NuGet saw
  zero versions). The per-member usage audit (`artifacts/lf3-usage-audit.txt`) found ZERO Tomb
  references to every surface this wave changed. Follow-up `artifacts/lf-tomb-verify-38.ps1`
  (no packing; pre-checks feed completeness) reran the six targets and both suites against the
  completed `.38` family repack: all six targets stop on FOREIGN consumer migration still pending
  in the Tomb tree - `TextWorkspaceEditApplier.cs` against the Workspace/Core
  `LocalPathComparisonPolicy` and the required `WorkspaceDocumentChange.BeforeFileFormat`, plus
  `TombLib.WPF.dll`/`fxc.path` file locks from concurrent builds - and again no failure referenced
  LanguageFeatures. **Resolved 2026-09-19:** the consumer migrations landed and the six-target gate
  runs green with the full family (see the §2 register close-out; reconfirmed at `preview.39`,
  `artifacts\r5-tomb-gate2.log`). The third-pass closeout regime was independently reproduced at
  `preview.100` by `artifacts/lf-tomb-verify-100.ps1` (feed-completeness pre-check, no packing):
  six targets 0 errors, `TombLib.Tests` 932/932, `TombEditor.Tests` 367/367, matching the canonical
  summary `artifacts/tomb-wave6-summary.txt`.
- **LF-TST-01 - shared hosted-editor factory in the test project (P3).** The
  "create editor + show host window" pattern is still re-implemented in several test classes
  (`CompletionWindowCoordinatorTests`, `CompletionWindowSizingTests`, `SemanticTokensColorizerTests`,
  `TextDefinitionNavigationTests`, plus inline copies), although `WPFTestHost.ShowEditorWithMargin`
  already exists. Collapse into one shared helper when those files are next touched; no behavioral
  gap today.
- **LF-FLT-01 - completion sizing template-profile review (P3).** The default measurement describes
  the library's template profile (optional icon and detail columns; the icon column is reserved
  only for items that supply an image since wave 4) and re-measures on every open and in-place
  refresh. This is deliberate policy
  with host replacement seams and was reviewed as an acceptable divergence, but it is the package's
  largest home-grown subsystem; revisit its shape only if a minimal-footprint or realized-measure
  policy becomes a product goal, or when an AvalonEdit template change forces it. Trigger-gated; no
  defect today.
- **LF-SNP-01 - snippet tabstop navigation reuse (P3).** The commit path expands snippet text to plain
  text and places the caret after the final tabstop; navigating the remaining placeholders stays host
  behavior. If a host wants tab navigation, build AvalonEdit `Snippet`/`InsertionContext` elements from
  the shared expander's parsed placeholders instead of extending the expander. Trigger-gated on a host
  request.
- **LF-DOC-01 - README reference split (P4).** The package README is ~27 KB and doubles as the user
  guide and the normative reference; split it into a shorter README plus a reference document when the
  documentation is next touched. Wording and accuracy are current; this is a shape item only.
- **LF-NAV-01 - definition-navigation surface consolidation (P3, product decision).** Six public
  entry points (hover-first/symbol/offset x sync/async) cover one user gesture, and the same-document
  landing places a caret without selecting the target range. Collapse the async offset-first form as
  the canonical API with thin adapters for the other forms, and decide whether landing selects
  `SelectionRange` (fallback `TargetRange`) like established editors. Coordinate with the AvalonEdit
  navigation-helper wave (the range-application helper area is concurrently edited).
- **LF-HLT-01 - semantic-token model revision (P3).** `SemanticTokensColorizer` freezes line/char
  anchors at push time, requires the host to re-push after edits, clips multi-line tokens to their
  start line, and needs `Rebuild()` after theme changes. Versioned decorations with paint-time style
  resolution would remove the rebuild path and the drift class; adopt when a multi-line-token or
  theme-switch complaint arrives, or when an AvalonEdit decoration API makes versioned spans cheap.
- **LF-TOL-01 - completion tooltip single ownership (P3, engine decision).** The tooltip decoration
  is documented as a permanent constraint (parity pinned by a canary test). Replace it with a
  single-owner design (presenter owns content, `Description` nulled while a resolver is configured)
  only if AvalonEdit exposes its tooltip publicly, or upstream a public details seam.
- **LF-EDIT-01 - shared range-fits helper (P3).** The "range fits text length" predicate exists in
  the commit applier (twice), the Core edit kernel (negated), and the Core offset resolver clamp.
  Add a small `IsWithin(int textLength)` helper (or an internal Core predicate) when a third
  consumer appears; the applier and kernel must keep their different failure semantics either way.
- **LF-TST-02 - colorizer perf smoke opt-in (P3).**
  `LargeSemanticTokenSet_SmokeTest_IsProcessedWithinAllocationAndTimeBounds` asserts allocation
  budgets and a 30-second wall-clock bound; isolate it behind an explicit opt-in category if CI
  variance ever flags it, instead of loosening the bounds.
- **LF-TST-03 - one-shot order-sensitive flake in the Tomb F12 navigation test (P3, watch).**
  `TombLib.Tests.ClassicScriptDefinitionNavigationTests.TryHandleKeyDownAsync_F12OnSectionHeader_MovesToDefinition`
  failed once inside a full 932-test gate run (2026-09-19, `Assert.IsTrue` on the handled flag in 39 ms)
  while passing 2/2 in isolation and in four subsequent full-suite runs
  (`artifacts/r4-tombgate-fail.txt`, `artifacts/r4-f12-single.txt`, `artifacts/r4-tomb-suite-x3.txt`,
  `artifacts/r4lua-tomb-summary.txt`). Treat as load/order sensitivity in the WPF/STA trigger path
  (the controller's synchronous wait over the dispatcher); harden with an explicit pump/wait seam if
  it recurs, not by loosening the assertion.

### LanguageFeatures fourth fresh-review resolution (2026-09-19, wave 4)

The fourth fresh review (full source and test read plus three independent specialist sweeps)
resolved its whole action plan in a fourth wave: the default completion measurement became
icon-aware, the dispatcher helpers moved to the shared linked infrastructure, the icon-margin
machinery was extracted into `LineStatusIconMarginBase` (slimming `TextCodeActionMargin` and
`BookmarkMargin`), the naming/documentation nits and internal cleanups landed, and the named test
gaps were closed (LanguageFeatures 400/400, AvalonEdit 434/434). A same-day follow-up (after the
IntelliSense wave settled) moved the hover-fallback and definition-navigation rules onto the shared
records (`TextHoverEvaluationState.DiagnosticFallback`, `TextDefinitionLocation.NavigationStart`)
and fixed the IntelliSense record-split wording (IntelliSense 466/466). Decisions recorded: the completion
options record stays with the binding because its defaults describe the AvalonEdit completion
window's presentation (the misleading README rationale was fixed instead), and the
debounce/schedule call sites were consciously not consolidated (the shared `DispatcherDebouncer` is
already the single collaborator; each site's wrapper carries distinct logging and state sinks).
Leftovers:

- **LF-TMB-04 - Tomb verification for the fourth wave (P2, completed 2026-09-19).** The gate used a
  private regime so it could not race the concurrent waves: family pack `artifacts/lf4-pack.ps1` at
  `1.0.0-preview.104` into `artifacts/review-feed-lf4`, restore config
  `artifacts/tomb-restore-nuget-lf4.config`, private package cache `artifacts/lf4-packages`, then
  `artifacts/lf4-tomb-gate.ps1`: feed complete (13 packages), all six consumer targets exit 0,
  `TombLib.Tests` 932/932, `TombEditor.Tests` 367/367 (`artifacts/lf4-tomb-gate-summary.txt`,
  status `artifacts/lf4-tomb-gate-status.txt`). The shared `review-feed` `.100` regime was not
  touched. **Re-verified 2026-09-19 (follow-up wave):** the family was repacked at
  `1.0.0-preview.105` after the shared-helper follow-up and the gate re-ran green (six targets
  exit 0, `TombLib.Tests` 932/932, `TombEditor.Tests` 367/367; the summary file now records the
  `.105` rerun).
- **LF-EDIT-02 - unify the completion commit conflict policy with the Core text-edit kernel (P3,
  design).** `CompletionCommitEditApplier` mirrors the kernel's touching-boundary and
  replacement-before-insertion conventions but deliberately skips malformed/conflicting entries per
  entry instead of rejecting the batch; the class docs now cross-reference the kernel and the
  difference. Routing commits through `TextEditKernel`/`PreparedTextEdits`/`ITextEditTarget` needs a
  Core mode that reports skippable entries plus a behavior migration of the applier's pinned tests.
  Do it in a dedicated edit-pipeline wave, not incidentally.
- **LF-INT-01 - relocate AvalonEdit engine interop to the bridge package (P3, public-API
  decision).** `CompletionWindowToolTipAccess` and `CompletionWindowInterop` reference no
  IntelliSense or feature types and are pure AvalonEdit/WPF engine plumbing consumed only by the
  completion presenter. Moving them to `Nickelony.IDEKit.AvalonEdit` requires a visibility decision
  for the reflected tooltip probe (public API or IVT); relocate when the bridge package next grows,
  keeping the version-compat probes and their pinned tests together.
- **LF-TST-04 - remaining test additions and hygiene (P3).** Deliberately deferred from wave 4:
  tooltip presenter branches (null active window at update, window-swap guard during an in-flight
  resolve), the default-path sizing cap early exit and the `GetDisplayText` rich-content fallback,
  the hover-first `TryGoToDefinition` null-hover result, folding the near-duplicate token tests in
  `TextCompletionControllerRequestTokenTests`, folding `CommitCharacterTestData` into
  `TestCompletionData`, and the `Controller_*` -> `Constructor_*` naming normalization in
  `TextCodeActionValidationTests`.
- **LF-TST-05 - deterministic negative waits (P3, test infrastructure).** About 24 fixed-duration
  `PumpFrames` sites assert "must not run" behavior (debounce cancels, disposal drops); under load
  they can false-pass (never false-fail). Replace with completion-signal or stable-counter pumps
  where the cost is justified; keep fixed pumps only as documented last resorts.
- **LF-DOC-02 - IntelliSense options-record remark wording (P4, completed 2026-09-19).**
  `TextCodeActionControllerOptions`'s remarks now state the real split rule (editor-presentation
  sizing such as the menu geometry lives with the editor binding; timing policy stays
  framework-neutral), matching the IntelliSense README's corrected wording. The same follow-up
  (after the IntelliSense wave settled) moved the hover/definition decision rules onto the shared
  records (`TextHoverEvaluationState.DiagnosticFallback`, `TextDefinitionLocation.NavigationStart`)
  and dropped the LanguageFeatures blank re-checks.

### LanguageFeatures whole-solution review resolution (2026-09-19, `__190919_`)

The whole-solution fresh review's LanguageFeatures section carried five open items; all were
resolved in this wave. `TextCompletionController` now tolerates a detached document
(`OpenOrRefresh` reports no-op, the window query falls back to the empty query), and
`TextSignatureHelpController.ScheduleRefresh` contains a throwing caret-offset getter (logged with
host-callback event id `1021`, pending refresh canceled, matching the no-caret case).
`SemanticTokensColorizer` clamps the render end index with a subtraction (an overflowing
character-plus-length sum can no longer hide a token) and enforces its documented UI-thread
affinity on `SetTokens`/`Rebuild`/`ClearTokens`. The README's duplicated XML-remark blocks
(non-activatable window policy, tooltip-decoration constraint, display-text measurement) were
reduced to host-facing summaries, with each rationale kept on its type. Pinned by four new tests
(LanguageFeatures 404/404, IntelliSense 466/466). Re-verified after the wave: full solution rebuild
0 warnings / 0 errors and all 15 suites green (3724 passed / 7 skipped / 0 failed,
`artifacts/wsr-final-summary.txt`); Tomb gate at `1.0.0-preview.106` with the wave-4 private
regime (feed complete, six targets exit 0, `TombLib.Tests` 932/932, `TombEditor.Tests` 367/367,
`artifacts/lf4-tomb-gate-summary.txt`). The shared `.100` regime was not touched.

## 9. AvalonEdit review residuals (ticketed 2026-09-19)

Residual items from the 2026-09-19 fresh reviews of `Nickelony.IDEKit.AvalonEdit`. Every defect and
risk finding was resolved in the same wave (the `Clear` binding, the regex publication race, the
dead `TryCreate` member, the doc-accuracy set, the margin API consolidation, the test additions and
extractions), and the second fresh review on the same day resolved its whole action plan in a
follow-up wave: auto-closing gained the optional `ITextEditTarget` routing (pair as one batch),
`BookmarkMargin` gained the protected `OnBookmarkToggleRequested` hook and is unsealed,
`BookmarkCoordinator.IsBoundToCurrentDocument` guards the save window, and the documentary and test
gaps were closed (416 tests). A third fresh review on the same day resolved its whole action plan in
another wave: the recursion guard now covers the cache-version callback, the README and the package
doc wording were fixed, and the test gaps were closed (434 tests). A fourth fresh review on the same
day resolved its whole action plan in a follow-up wave: the auto-close tracking-anchor defect
(typed-inside-then-closer), the bookmark `TryLoad` contract, the stated no-document policy,
`ChangeMarkerMargin` unsealing, the comment-parameter order, the `TextEditorNavigationOperations`
rename, and the doc, README, and test gaps were closed (452 tests). The items below stay open
because they are trigger-gated, need a different test technique, or are product decisions:

- **AE-RENDER-01 - self-invalidation integration test (P3).** `LineStatusMarginBase` repaints when
  its text view's visual lines or scroll offset change; the queued-invalidation coalescing gate is
  now pinned deterministically (`IsInvalidationQueued`), but the self-invalidation wiring itself and
  the dispatcher-shutdown guard still need a hosted render-loop observation (or an internal seam) to
  test without flakiness. Both handlers are one-liners; revisit with the first render-loop test
  infrastructure in the suite.
- **AE-DOC-01 - long remark trim (P3).** Resolved in the 2026-09-19 fresh-review-#3 wave:
  `TextAutoClosingService`, `RegexHighlightingDefinition`, and `BookmarkCoordinator` remarks were
  split, and `DiagnosticsRenderer` no longer repeats its type guidance in `Draw`.
- **AE-API-01 - shared icon-margin base (P3, product decision).** Resolved 2026-09-19 (fourth
  wave): `LineStatusIconMarginBase` now hosts the icon brush/geometry properties, the font-scaled
  centered drawing, and the click handling; `BookmarkMargin` and the LanguageFeatures
  `TextCodeActionMargin` register their defaults through property-metadata overrides instead of
  repeating the implementation. The fresh-review-#4 wave closed the remaining asymmetry by
  unsealing `ChangeMarkerMargin` with `DrawMarker` as its documented customization point.
- **AE-ADOPT-01 - per-editor composition facade (P3, product decision).** Adoption currently
  requires composing the auto-closing service, tracker, margins, and bookmark coordinator by hand
  (the README shows the wiring). A small `Install(editor, options)` facade could cut the ceremony;
  decide once a second host adopts the package.
- **AE-HOST-01 - Tomb migration for `CreateCaretLocation` (P2).** Resolved in the host working tree:
  `LuaWorkbenchEventCoordinator.cs:122` now calls `CreateCaretLocation`, and the fresh-review-#3
  probe compiles that call shape against the packed `1.0.0-preview.100` package. No other Tomb
  reference to a renamed, removed, or behavior-changed member was found.
- **AE-HOST-02 - Tomb compile gate for the AvalonEdit review waves (P2, blocked on host-side
  migration and concurrent builds).** Fresh-review-#3 state (2026-09-19): the extended gate
  (`artifacts/ae-fresh3-tomb-gate.ps1`, five packages at `1.0.0-preview.100`) cannot pack because
  `Nickelony.IDEKit.Workspace` is mid-edit in the shared tree (shape errors in
  `WorkspaceDocumentResultFactory.cs` and `WorkspaceDocumentStore.Persistence.cs`); the fallback gate
  (`artifacts/ae-fresh3b-tomb-gate.ps1`, Core + IntelliSense + AvalonEdit at the wave version)
  packs and then fails on host-side states only: `TombLib.Scripting.UI` hit a concurrent-build race
  (wpftmp `CS2001` missing generated `.g.cs`, then duplicate `CommunityToolkit` generated types),
  `TombLib.Scripting.Lua` failed as the resulting `MSB3030` cascade, and `TombIDE.ScriptingStudio`
  plus `TombEditor.Tests` fail on the host's own half-migrated Core/IntelliSense usage
  (`CommentOperations.IsBlankOrLineComment` vs the renamed `IsBlankOrStartsWithLineComment` across
  TRX/ClassicScript/GameFlowScript, `IdentifierAffinity`, `TextHoverControllerHooks.BuildEvaluationState`
  and the required `BuildRequestState`, `TextLineMap`). No failure names an AvalonEdit member.
  Substitute evidence: `artifacts/ae-fresh3-probe` compiles the Tomb call shapes against the packed
  packages and runs the reviewed regex-guard and live-cache-version checks (`FRESH3-PROBE-OK`,
  `PROBE-EXIT=0`). **Settled 2026-09-19:** the host migration landed; the six Tomb targets and both
  suites run green with the full family (§2 register close-out; `artifacts\r5-tomb-gate2.log`).
  Gate note: the scoped `ae-fresh3`/`ae-fresh3b` gates resolve versions inconsistently (the WPF
  temporary-project restore falls back to the shared feed's repacked `.38` packages even when the
  CLI forces the wave version), so a scoped red is expected; the full-family gate is the reference.
  Fresh-review-#4 state (2026-09-19): the host tree was migrated for the only consumer-side rename
  (`TextEditorBase.GetOffsetFromPoint` now calls `TextEditorNavigationOperations`), and the wave's
  own full-family gate ran green from the same tree (`artifacts/fv4-gate-summary.txt`, 13 packages at
  `1.0.0-preview.105` in `artifacts/review-feed`, six targets build-exit=0, no failed projects, and
  both suites green: `TombLib.Tests` 932/932, `TombEditor.Tests` 367/367). The same day's concurrent
  family gates also ran green at `preview.104`, confirming the result is reproducible. A gate attempt
  can still fail on foreign timing (project-asset files deleted mid-build and `TombLib.dll` file
  locks while another family gate runs on the same Tomb tree); retry with the gates serialized.
- **AE-TEST-01 - `TryMoveCaretToMousePosition` success path (P3).** Resolved in the 2026-09-19
  fresh-review-#3 wave: the internal `TextEditorMouseNavigation.PointerPositionResolver` test seam
  drives the success path and the resolver-reported no-pointer case, and the recorded-debt comment
  is gone. The fresh-review-#4 wave replaced the process-global static resolver property with an
  internal resolver overload on the renamed `TextEditorNavigationOperations` (the tests call it
  directly), so no mutable global state remains.
- **AE-TEST-02 - margin test scaffolding consolidation (P3).** `BookmarkMarginTests` and
  `ChangeMarkerMarginTests` repeat ~150 lines of `LineStatusMarginBase` scenario tests
  (measure/width/scaling/subscription shapes) and the render-and-scan pattern; a shared abstract
  fixture plus a render-strip helper would remove the duplication without losing specificity.
- **AE-TEST-03 - unicode and BOM edge tests (P3).** No BOM test exists for
  `UnsavedChangesTracker.SetBaseline`, and `TextLineCommentService` is not exercised over a
  surrogate-pair selection; add both when the suites are next extended.
- **AE-RENDER-02 - diagnostics renderer non-default layer test (P3).** `DiagnosticsRenderer.Layer`
  is only tested as a property round-trip; pin the "assign before attaching" contract with an
  attached `KnownLayer.Background` render pass on a bare `TextView`.
- **AE-PERF-01 - indexed tracked-closing-text lookup (P4, watch).** `TextAutoClosingService`
  records one anchor per inserted closing text and scans them newest-first on each resolution (the
  list is pruned only for deleted anchors). Revisit with an offset index or aged pruning only if a
  document with tens of thousands of tracked pairs ever shows keystroke latency.
- **AE-WATCH-01 - AvalonEdit version-object assumption (watch).** `DocumentVersionCache` compares
  `TextDocument.Version` references; the behavior is now pinned by
  `AvalonEditVersion_IsReplacedOnEveryChange`, which fails loudly if a future AvalonEdit update
  mutates version objects. If it ever fires, replace the reference comparison with a
  change-notification counter.
- **AE-DEC-01 - `RegexHighlightingDefinition` single creation path (P4, considered and kept).** The
  fresh-review-#4 audit suggested collapsing the subclass path and the `Create` factories into one
  sealed provider-based shape. Kept both: the host subclasses the definition in three projects and
  one uses a live `cacheVersion` rule catalog, while the factories serve the inline case; the type
  remarks now state that both shapes feed the same lazily built, version-aware rule set.
- **AE-PERF-02 - icon-margin draw transform allocation (P4, watch).**
  `LineStatusIconMarginBase.DrawMarker` allocates and freezes a `MatrixTransform` per drawn marker
  per render pass; cache the transform against the font scale, margin width, and icon bounds if a
  profiled scenario shows it matters.
- **AE-PERF-03 - diagnostics renderer per-pass provider scan (P4, watch).** `DiagnosticsRenderer`
  scans the whole segment list on every render pass before filtering to the visible range; with
  tens of thousands of diagnostics this runs per scroll frame. Revisit with an offset-sorted or
  version-stamped provider contract if a host reports large sets.

## 10. IntelliSense review residuals (ticketed 2026-09-19)

Residual items from the 2026-09-19 clean-before-publish review of `Nickelony.IDEKit.IntelliSense`.
The defects, validation gaps, protocol-boundary placement, snippet-parser fixes, README overhaul,
and the highest-value test additions were resolved in the same wave (see `CHANGELOG.md`). A second
fresh review (round 2, `__180923_intellisense-fresh-review-round2.md`) resolved its P1/P2 plan in
the same day's follow-up wave; its leftovers are IS-10 through IS-17 below (IS-14 was resolved in
that wave through the Tomb consumer check, which exposed one real reader: the member was deleted
and the converter migrated to `Children.Count > 0`). The items below stay
open because they are trigger-gated, declined as unproduced surface, or need a measurement:

- **IS-01 - snippet-expander pluggability (P3, product decision).** `TextSnippetExpander` implements
  the VS Code/LSP snippet dialect as a static, opt-in helper; a host with another placeholder syntax
  simply does not call it, so no seam was added (an interface with no consumer would be unproduced
  surface). Add an interface/delegate seam when a second dialect or an adopter asks for one.
- **IS-02 - `DocumentSymbolOutlineBuilder` factory simplification (P3).** The bundle now offers
  optional `RangeSelector`/`SelectionRangeSelector`/`DetailSelector` (added by the round-2 wave), so
  the remaining gap is a `Func<TItem, TextDocumentSymbol>` factory overload for deeper shaping
  (nesting beyond the one grouping level) and the delegate-reference equality of the bundle;
  introduce it when a host needs that shaping.
- **IS-03 - `TextHoverEvaluationState` positional shape (P3).** The five positional parameters (two
  adjacent permission booleans) are constructed positionally by three Tomb call sites and kept for
  record fidelity. Convert to init-only properties during an API-shape wave that touches the
  bindings.
- **IS-04 - unset-state API for `InsertText`/`FilterText` (P3).** The getters keep the label
  fallback and the merge tracks "never set" internally; exposing `HasInsertText`/`HasFilterText` was
  declined as unproduced surface. Add them when a consumer needs to distinguish unset from a label
  fallback.
- **IS-05 - open signature-help trigger vocabulary (P3, product decision).**
  `TextSignatureHelpTriggerKind` stays a closed protocol-mirroring enum (its docs now state the
  deliberate asymmetry with the open `TextCompletionTrigger`). Revisit if a host needs custom
  signature triggers.
- **IS-06 - payload collection allocation policy (P3, needs measurement).** Payload snapshots keep
  the defensive `Array.AsReadOnly([..])` wrapper (two allocations per populated collection) while
  helpers return caller-owned lists. Revisit with a measurement on the semantic-token hot path.
- **IS-07 - request record equality (P3).** Request records compare the document text in
  equality/hash; the README documents them as call data rather than dictionary keys. Exclude the
  text from equality if profiling shows dictionary use.
- **IS-08 - remaining test gaps (P2/P3).** Equality tests that do not vary every component
  (`TextCodeActionItem.Payload`, both `TextCodeActionContext` selection offsets,
  `TextCodeActionRequestState.StartOffset`, `TextSignatureHelpRequest.DocumentText`); pins for the
  documented non-validation of ranges (`SelectionRange` outside `Range` acceptance); session no-op
  arms and the session-to-coordinator supersession direction; presentation-state unions with a
  payload but no flags; kind operators with null operands; non-ASCII ordinal folding in the filter;
  the `${VAR:default}` literal claim. Round-2 additions covered document-text inequality,
  hit-tester empty/reversed/negative guards, definition-request equality, hover guard rejections,
  and code-action offset validation; the remaining narrow gaps (options zero boundary, signature
  presentation payload storage, markup serialized names, over-stated equality test names, per-type
  test-file splits) stay open. Round-4 additions: grouped-mode outline-builder edges
  (selection-range selector, one-level nesting cap, child order), complete record-equality probes
  for the code-action records and the request `DocumentText`/`DocumentId` components, and signature
  navigation where the selected signature specifies its own index.

- **IS-10 - README structural pass (P4).** Accuracy is current after the round-2 updates, but the
  introduction remains a long wall before the first section heading; split it into sub-sections
  when the README is next touched (shape item only).
- **IS-11 - `TextSemanticToken` modifier allocation (P3, needs measurement).** `CaptureModifiers`
  builds a `List<string>` and wraps it in a `ReadOnlyCollection<string>` per modifiered token;
  revisit with a semantic-token hot-path measurement (the read-only contract must stay intact).
- **IS-12 - position-unit consolidation in Core (P3, cross-package).** `TextDefinitionLocation`
  (and the Client/Abstractions siblings `TextReferenceLocation`, `TextEdit`) repeat the
  line/character unit paragraph; move the normative statement onto Core
  `TextPosition`/`TextPositionRange` and reference it. Deferred because the Core tree was under
  active concurrent edit during the round-2 wave.
- **IS-13 - `DocumentSymbolProjection<TItem>` default-instance validation (P3).** A `default`
  instance is an all-null selector bundle rejected only at builder use; a validating class or
  factory would remove the representable invalid state.
- **IS-15 - code-action vocabulary naming coherence (P3).** `Nickelony.LanguageServer.Abstractions`
  ships `TextCodeAction`/`TextCodeActionRequest` (line/character, workspace-edit) next to the
  offset-based `TextCodeActionItem` family; rename one family or document the pairing.
- **IS-16 - `MarkupContentReader` markup-kind consolidation (P3).** The reader returns a
  `bool IsMarkdown` that Lua folds into `TextMarkupKind`; return the shared kind from the reader
  (Client already references IntelliSense).
- **IS-17 - snapshot-addressed completion request and parser linearization (P3).** Extends IS-09:
  the kernel still materializes the document text per call, and `TextSnippetExpander` retains a
  quadratic worst case for adversarial inputs with sparse same-range close braces (the round-2 fix
  made close-brace-free suffixes linear). Fold both into a profile-driven API/parser reshape.
- **IS-18 - `TextCompletionSessionDecision` shape cleanup (P3, product decision).** The decision
  carries `ShouldClose`, `Items`, `StartOffset`, and `EndOffset` as correlated nullables (the
  constructor enforces items-with-both-offsets) plus one `init`-settable member; a nullable
  `TextRange` replacement pair would make the impossible states unrepresentable. Deferred because
  the current shape is documented, tested, and wired into host code; revisit with the
  presentation-state records in one shape wave (IS-03).
- **IS-19 - signature payload equality model (P3).** `TextSignatureHelp`, `TextSignatureInformation`,
  and `TextSignatureParameterInfo` are immutable classes with reference equality while sibling
  payloads are records; aligning them (or documenting reference equality on each type) awaits a
  consumer that actually compares payloads.
- **IS-20 - outline builder child snapshot ownership (P3).** Grouped outlines materialize group
  children twice (the builder fills a `List` and `TextDocumentSymbol` copies it into a read-only
  snapshot); an internal ownership-taking path would remove one array copy per group. Fold into
  IS-02's factory work when a host needs deeper shaping.

**Whole-solution review re-check (2026-09-19, `__190919_fresh-review.md` IntelliSense section).**

The review's confirmed items were re-checked against the current tree: the camelCase constructor
parameters (V-8), the `<paramref name="value"/>` exception wording, and the snippet expander's
three copied escape blocks were already resolved by the round-4 wave (`CHANGELOG.md`); the
presentation-state union remarks were reconciled in this wave (each record states its own union;
the deliberate contrast lives in the LanguageFeatures README). Remaining review micro-items to
re-check when the files are next touched (all low/nit; reported line references are from the
review's baseline and may have drifted):

- `TextCompletionItem` `DocumentationKind` merge asymmetry (`:295-299`, `:603-612`).
- `<exception>` tag ordering in several records.
- `IsVisible` versus `IsListVisible` naming drift across the two presentation records (decide or
  document the deliberate difference).
- Redundant merge-rule test block (`TextCompletionItemTests.cs:381-534`).
- Protocol trivia in `TextCompletionItem` docs (`:320-366`).
- `TextDiagnostic.Range` computing a fresh `TextRange` per access (measure or cache; a struct
  allocation, currently trivial).

## 11. Core review residuals (ticketed 2026-09-19)

Residual items from the 2026-09-19 fresh review of `Nickelony.IDEKit.Core`; the shipped half is
recorded in `CHANGELOG.md` (Core, "Fresh review resolution (2026-09-19)"). The confirmed defect
(`GetCodeEnd` single-character block-comment closers), the nullable-contract fix, the enum-value
and documentation set, the two safe performance fixes, and the test additions landed. The items
below stay open because they are cross-package, need a measurement, or are deliberate keeps.

**Host verification record (Tomb Editor, 2026-09-19).**

- Usage scan (`artifacts/cr-tomb-usage.txt`): Tomb production consumes `TextEditKernel`/
  `TextEditInput`/`TextEditPreparation*` (`TextWorkspaceEditApplier` + `TextEditInputAdapter`),
  `ThemeCatalog` (`LuaThemeRepository`), `FindReplaceText` (13 call sites), `WhitespaceConverter`
  (`TextEditorBase.Editing`), `ContinuationOperations` (`ClassicScript` `ErrorDetector`),
  `BacktickFenceTextNormalizer` (3 sites), `CommentOperations` transforms
  (`ClassicScript`/`GameFlow`/`TRX` line services + `ScriptReplacer`), `RequestTokenSource`
  (`ErrorDetectionWorker`), and `IndentationOperations` including `SplitLines`/`IndentationTextLine`
  (`LuaIndentationStrategy`). Consequence: the review's "unproduced surface" deletion candidates
  are produced by the external adopter - none were deleted (per the consumer rule in "How this
  file works").
- Gate `artifacts/verify-tomb-migration.ps1 -RunTests` (log `artifacts/cr-tomb-verify.txt`): the
  wave packed 13 packages into the private review feed, and the gate failed on the known
  consumer-migration register only (same blockers as LF-TMB-01) - `LanguageFeatures.Presentation`
  imports in `TombLib.Scripting.UI\Bases\TextEditorBase.Navigation.cs` and
  `Hover\HoverControllerFactory.cs`, and missing Core usings for
  `TextDiagnosticSegment`/`TextAutoClosingOptions`/`TextLineCommentAction` in
  `Bases\TextEditorBase.Diagnostics.cs` and `Bases\TextEditorBase.Editing.cs`; those files are
  staged/modified by the extraction workstream. Zero errors reference this wave's changes. Re-run
  after the register's phases land.
- Substitute host evidence: the packed `Nickelony.IDEKit.Core` `.38` package was exercised from a
  standalone host-style probe (`PROBE-OK`): `GetCodeEnd("x { c }", { })` = 1, continuation-marker
  detection with the same syntax, pair deletion through the nullable out parameter, no-op instance
  identity for `WhitespaceConverter`, and the enum pins. Incidental find: the probe's first restore
  compiled against a stale same-version global-cache entry while the review-feed package was
  correct - clear `~/.nuget/packages/nickelony.idekit.core` before any ad-hoc consumer restore (the
  gate script clears the cache at start; a probe with its own feed config does not). Refined
  2026-09-19 (IntelliSense gate): `dotnet restore --force` does NOT replace an already-present
  same-version folder, and cache deletion can silently fail on files locked by other running
  builds/agents - when that happens, pack a NEW version (`-p:Version=...`, e.g. `preview.39`) and
  override the consumer's `Nickelony*Version` properties to it instead of fighting the cache; a
  stale `.38` entry caused two false "missing type" CS0246 failures in the Tomb test projects that
  the fresh `.39` packages resolved. Recurrence 2026-09-19 (third Core wave gate): the same trap
  also works at the FEED level - the `artifacts\packages` shared feed still holds 2026-09-18
  preview.38 copies of the current wave packages, and a restore that sees `.38` in both the private
  review feed and the shared feed can resolve the stale shared copy (observed as false
  `TextHoverEvaluationState` CS0246 in the six Tomb targets). `verify-tomb-migration.ps1` now packs
  and overrides at a novel `preview.39`; keep gate versions ahead of every feed copy. The private
  `RestorePackagesPath` pattern (`artifacts\tomb-wave4.ps1`) is the stronger isolation when
  concurrent gates share the NuGet cache.

**Open items**

- **CR-01 - `SourceIndex` naming and the `TextEditPreparationDiagnostic` type name (P3, rename
  wave).** `TextEditOperation.SourceIndex`, `TextEditPreparationDiagnostic.SourceIndex`, and
  `RelatedSourceIndex` should become `InputIndex`/`RelatedInputIndex` (the kernel's vocabulary is
  "input"; `TextEditInput` is the source batch element), and `TextEditPreparationDiagnostic` should
  lose the "diagnostic" word (it collides with `TextDiagnostic*`); `TextEditPreparationIssue` is the
  recorded candidate. Scope: Core + Core.Tests + two named-argument sites in `Workspace.Tests`; no
  production consumer reads the property in-repo.
- **CR-02 - replacement payload consolidation (P3, API-shape wave).** `TextEditInput`,
  `TextIncrementalEdit`, `TextEditOperation`, and `TextLineCommentEdit` are four range+text shapes
  with different roles and validation; a shared minimal `TextReplacement(TextRange, string)` was
  proposed by the review. Deferred because the role split is documented and each shape carries
  distinct null semantics; revisit in an API-shape window (bundle with CR-01).
- **CR-03 - slice placement (P3, move-only).** `TextLineMap` and `TextRangeOffsetResolver` are
  position/offset text tooling that currently live in `Editing`; `IncrementalLineStateCache` is a
  parser cache in `Editing`; `ContinuationOperations` sits in `Comments` while its docs say it is
  about statements. Namespace-breaking and mechanical; bundle into one wave. **Partially resolved
  by the third review wave: `TextLineMap` moved to `Core.Text`; `TextRangeOffsetResolver` stays in
  `Editing` (its fallback semantics are edit-scoped). See §15.**
- **CR-04 - comment-span absorption simplification (P3, product decision).** The absorption rule
  (comment-only line absorbs its indentation and the preceding terminator) serves
  `RemoveComments`/`MaskComments`, which Tomb consumes; simplifying the span model toward the
  line-based planner would change documented behavior and needs the consumer inventory first.
- **CR-05 - comment-spacing policy (P3, trigger).** `TextLineCommentPlanner` fixes the
  "+1 space / remove one space" convention. Add an `insertSpace`-style option (VS Code parity) only
  when a host needs non-default spacing; a model-only knob would be unproduced surface today.
- **CR-06 - snapshot span access + resolver typing path (P3, needs measurement).** Adding a span
  accessor to `ITextSnapshot` and threading the clamped caret through the auto-closing resolver were
  the review's hot-path items. `TextDocumentSnapshot` cannot answer a span without materializing the
  whole document text, so the change needs a profile (and a per-implementation strategy) before
  promotion; it would otherwise regress first-keystroke cost on large files.
- **CR-07 - auto-closing flag interactions (P3, decide).** Pair deletion ignores
  `Options.EscapeCharacter` while the skip path honors it (asymmetric, untested); symmetric
  bracket-kind pairs can never suppress doubling (`|`/`|`); wrapping-selection on the closing-token
  path is unspecified. Decide behavior (document vs change) with the next auto-closing touch.
- **CR-08 - test-hardening remainders (P3).** Zero-width regex matches in the find helpers;
  `RegexCache` concurrent-miss semantics (the benign race is documented; a test can only pin the
  usable-instance contract); `CommentOperationsLineCommentTests` DataRow folds; the Windows-gated
  sidecar paths (injectable failure seam); the wall-clock ceilings in the scanner/snapshot guards.

### From the 2026-09-18 handoff review - recorded declines and keeps

Moved here per that review's Definition of done ("declines belong in `future-backlog-2026-09-17.md`");
the full resolution record stays in `__180923_idekit-core-fresh-review-and-remediation.md`.

- **API-04 - `IChangeNotificationSource` stays in `Core.LineStatus` (declined).** A namespace move
  for one interface costs cross-package churn (four implementers, five consumers) without a second
  generic notifier; the docs are consumer-neutral now. Revisit if another generic notifier appears.
- **API-05 - two request primitives stay (recorded decision).** `RequestTokenSource` (token-only)
  and `LatestRequestCoordinator` (run + host-driven surface) both remain; the exposed-token lifetime
  is uniform and the XML states the two levels. Consolidation remains an option, not a defect - fold
  into a future Requests-slice wave if a host is confused by the choice.
- **TEST-04 items 5 and 12 - declined (test-owned fakes only).** The `LineStatus` consumer fake and
  the formatter-declines case would exercise only test-owned fakes; add when a production
  implementation lands in-repo.
- **TEST-06 decisions - recorded.** Core.Tests keeps the flat namespace and the 15-entry
  `GlobalUsings.cs` (repo-wide test convention; the AGENTS.md folder-namespace rule targets
  production projects). Comment-helper duplication and one-line wrappers stay (churn without
  coverage gain).
- **RSK-06 - refuted; do not redo.** A one-tick positive regex timeout is accepted on .NET 8;
  `Regex.MinimumMatchTimeout` is not public API. The existing timeout docs are accurate.
- **Handoff open follow-ups.** (a) Solution-wide verification must be re-run once the concurrent
  workstreams (Workspace tests, hover) land - the same condition as this section's host gates.
  (b) The 2026-09-19 renames (`PreferredDocumentLine`, `LocalPathComparisonPolicy`) compile against
  Tomb's next repack; ride the same gate as section 2 / LF-TMB-01.

## 12. LanguageServer Client review residuals (ticketed 2026-09-19)

Residual items from the 2026-09-19 fresh review of `Nickelony.LanguageServer.Client`; the shipped
half is recorded in `CHANGELOG.md` (Client, "Client review resolution - second pass"). The
scheduler deadlock (reproduced, now fixed with a regression test), the converter and validation
defects, the transport/document/workspace hardening, the capability surface completion, the
real-process test harness, and the documentation/naming corrections landed. The items below stay
open because they are cross-package, feature-sized, or need a measurement or trigger. A fourth-pass
wave (2026-09-19) is recorded in the same CHANGELOG section; its resolved items are marked inline
below, and the fourth-pass deferrals start at CL-32. A fifth-pass wave (2026-09-19) re-reviewed the
Client fresh and resolved the URI-shape, snapshot-clone, session-teardown, code-action retention,
settings-casing, cancel-reachability, capability-fallback, and documentation-consistency findings;
its residual items are appended at the end of this section.

- **CL-01 - host callback seams for server requests (P1, feature).** The internal callback target
  still logs `window/showMessage` instead of surfacing it, drops `$/progress`/`telemetry/event`,
  and answers no `workspace/applyEdit`, `window/showMessageRequest`, or `window/showDocument`.
  Planned shape: optional host delegates/events on `LanguageServerClientOptions` (defaults keep
  today's behavior) plus an option to honor dynamic registration. Required before the client can
  serve IDE-grade rename/quick-fix flows.
- **CL-02 - workspace folder mutation (P2, feature).** The folder set stays fixed at construction;
  `workspace/didChangeWorkspaceFolders` is never sent. Planned shape: an explicit folder-change API
  that updates the snapshot and notifies the server, keeping the fixed-set mode documented.
- **CL-03 - rootless (single-file) sessions (RESOLVED 2026-09-19).** `NormalizeWorkspaceRoots`
  accepts an empty list and the handshake emits `rootUri: null` + `workspaceFolders: null`; covered
  by `NormalizeWorkspaceRoots_EmptyList_ModelsAFolderlessSession` and
  `BuildInitializeParams_WithoutWorkspaceRoots_OmitsRootsAndPinsUtf16`.
- **CL-04 - per-resource configuration (P2, feature).** `workspace/configuration` answers every
  item from the single global settings snapshot; `scopeUri` is not deserialized. Planned shape: a
  resource-aware provider overload with the global snapshot as fallback and `scopeUri` preserved.
- **CL-05 - protocol optional-member retention (P2, feature; PARTIALLY RESOLVED 2026-09-19).**
  Diagnostics `tags`/`relatedInformation`/`codeDescription`/`data`, code-action
  `disabled`/`diagnostics`, symbol `tags`/`deprecated`, and workspace-edit `changeAnnotations` are
  modeled now. Remaining (updated 2026-09-19, fifth pass): completion `deprecated`/`labelDetails`/`insertTextMode`;
  code-action resolve flow; `codeActionProvider.codeActionKinds` and `completionProvider.triggerCharacters`;
  `CodeActionContext.only`/`triggerKind`; a typed signature-help active-parameter tri-state; a
  typed `WorkspaceDocumentChangePayload` union. Resolved in the fifth pass: code-action `command`/`data`
  retention (command-only entries are now kept for the host to filter), per-edit `annotationId`, and
  resource-operation `options`. Additive payload/SDK work on top of the existing tolerant readers.
- **CL-06 - POSIX child-lifetime binding (P3, platform).** `ProcessJobObject` gives Windows hosts
  kill-on-parent-close; POSIX hosts only get the graceful handshake, so a crash can strand the
  server. Add a process-group/parent-death equivalent or expose which guarantee applies.
- **CL-07 - IDEKit bridge split (P3, packaging).** `Interop/` plus `SemanticTokensDecoder` and
  `LanguageServerPaths` keep a hard IDEKit.Core/IntelliSense dependency in every consumer's graph;
  the README records the split as deferred until a second, non-IDEKit consumer exists. At that
  trigger, move the bridges into an optional `Nickelony.LanguageServer.Client.IDEKit` package.
- **CL-08 - snapshot fingerprint cost (P3, measurement; PARTIALLY MITIGATED 2026-09-19).**
  `WorkspaceSnapshotTracker` now caps content fingerprinting at 16 MiB (larger files compare by
  timestamp and length alone), but it still hashes the full content of every captured file and of
  every affected file on apply. Deeper options (skip the fingerprint when
  `(LastWriteUtcTicks, Length)` already differ, prefix+length hashing, or moving capture work off the
  dispatch path - see CL-28) remain open pending a measurement.
- **CL-09 - completion-item default parsing (P3, perf).** `CompletionResponseJsonConverter`
  re-parses `itemDefaults` per item through `JsonNode`; parse the defaults once and deep-clone per
  item, and skip the node round-trip entirely when the server advertises no defaults.
- **CL-10 - debouncer clock seam (P3, testability).** `WorkspaceChangeDebouncer` still schedules
  from `Environment.TickCount64` + a raw `Timer`, so supersession/early-tick races can only be
  probed with generous bounds. Accept a `TimeProvider` (net8) and a timer seam.
- **CL-11 - subscriber-set dedup (P3, cleanliness).** `SerializedSignalSubscriberSet` and
  `SerializedDiagnosticsSubscriberSet` duplicate the same add/remove/snapshot/dispatch list
  management; hoist a shared base the next time either is touched by a feature.
- **CL-12 - job-object and crash-path test coverage (P3, test).** `ProcessJobObject` still has no
  test (Windows-gated kill-on-close needs a purpose-built helper process); the real-process
  harness added on 2026-09-19 (compiled fake server with `--exit-immediately`) is the natural home,
  together with a request/response round trip beyond `initialize`. Fifth pass (2026-09-19): the
  scripted-transport success path (typed result, unchanged generation) is covered; the real-process
  round trip and the job-object helper process remain open.
- **CL-13 - naming follow-ups (RESOLVED 2026-09-19, fourth pass).** The
  `VersionedTextDocumentIdentifierPayload` misnomer dropped its suffix and moved to `Common/`;
  `LanguageServerCapabilityJsonConverters.cs` split into per-payload converter files; the remaining
  `TryGetStringProperty` copy collapsed into `JsonElementReadHelpers`; `MarkupContent` became
  `ProtocolMarkupContent`; `TryGetFilePath` became `TryGetLocalPath`. Recorded in `CHANGELOG.md`
  (Client, fourth pass).
- **CL-14 - intermittent handshake test failure (RESOLVED 2026-09-19).**
  `StartAsync_WithScriptedTransportSession_CompletesHandshakeAndBecomesReady`
  (`Tests/Nickelony.LanguageServer.Client.Tests/Transport/LanguageServerClientTests.Startup.cs:90`)
  failed occasionally inside full-suite runs with `CollectionAssert.AreEqual failed. Different
  number of elements` on the recorded handshake messages. Root cause: the assertion read the
  scripted transport's write recording immediately after `startTask` reported ready, but the
  recorder is fed asynchronously, so the last write (`workspace/didChangeConfiguration`) could
  still be missing from the recorded text. Fix: the test now waits (bounded, 5 s) for that method
  to appear in the recording before pinning the sequence, matching the sibling handshake test's
  `TestWait.UntilAsync` synchronization. Evidence: five consecutive suite runs green after the fix
  (`artifacts/client-flake-verify.txt`) plus four green runs before it (`artifacts/client-stability.txt`).
**Open items from the third-pass fresh review (2026-09-19)**

- **CL-15 - rename outcome type (P2, design).** `TrackedDocumentStore.Rename` still returns `null`
  for three different outcomes (paths equivalent, source untracked, destination tracked). Planned
  shape: a `DocumentRenameResult` record struct with a status enum, mirroring `DocumentCloseResult`;
  touches the Provider rename flow and the store tests.
- **CL-16 - close in the synchronization abstraction (P2, design).** `DocumentSynchronizationKind`
  models `Open`/`Change` only, while closes are produced as bare snapshots by `TryClose`,
  `TryReleaseRequest`, and `TrimIdleDocuments`; the Provider hand-rolls `didClose` in two places.
  Add `Kind.Close` (or a close request) so all three notifications share one consumer path.
- **CL-17 - range-parsing consolidation (RESOLVED 2026-09-19, fourth pass).** Position reads
  consolidated into `JsonElementReadHelpers.TryReadPosition` (with a non-negative policy flag) and
  writers into the new `ProtocolJsonWriteHelpers`; the converters keep their per-feature degradation
  policies as a documented, named choice instead of copy-paste drift.
- **CL-18 - pump-termination policy (P2, decide).** An unexpected client-side dispatcher/diagnostics
  pump termination still marks the transport unhealthy (forcing a restart and resync). Decide
  between recreating the pump with backoff and keeping the defensive invalidation; the current
  behavior is tested, so the decision must update `LanguageServerClientTests.BackgroundLoops`.
- **CL-19 - shared disposal task (P3, decide).** Later `Dispose`/`DisposeAsync` callers return
  immediately while the first caller's teardown continues (documented). Caching the teardown task
  would let later callers await completion but changes the documented contract; revisit with the
  next disposal-adjacent feature.
- **CL-20 - not-ready exception taxonomy (P2, design).** "No ready session" is still a bare
  `IOException`, indistinguishable from the `IOException`-derived transport failures; a dedicated
  not-ready exception would clarify catch blocks but ripples through the Provider's
  `catch (IOException)` recovery paths - schedule with a Provider wave.
- **CL-21 - initialize payload extension seam (P2, feature).** The payload remains closed to
  `locale`, `clientInfo`, `trace`, and extra members; add an `InitializeParametersProvider`-style
  hook plus optional `Locale`/`ClientInfo` options when a host needs them.
- **CL-22 - transport/launcher seam (P3, trigger).** The stdio child-process transport remains the
  only way to host a server (documented limitation); revisit when a second transport model appears.
- **CL-23 - watcher source abstraction (P3, trigger).** `WorkspaceFileWatcher` structurally binds
  to `FileSystemWatcher` and checks `Directory.Exists` before the injectable factory; generalize
  the watch source (or document a queue-feed path) when a virtual-workspace host appears.
- **CL-24 - release API consolidation (P3, cleanliness).** `ReleaseRequest` (never evicts) and
  `TryReleaseRequest` (evicts when idle) both remain; fold into one method with a visible outcome
  if the Provider's deferred-close flow can be expressed the same way.
- **CL-25 - semantic-token delta edits (P3, decide; UPDATED 2026-09-19, fourth pass).** The parser
  now validates parsed edits (present, non-negative, ordered, non-overlapping), logs the degradation
  reason, and documents that the client parses but never applies edits. Deleting the edits surface
  remains open until a consumer needs delta application (or a cleanup pass deletes it).
- **CL-26 - capability converter logging (P3, cleanliness).** Capability converters map malformed
  advertisements to `false`/`None` without a warning (unlike the diagnostics converter); wire a
  logger or document the silence.
- **CL-27 - dispatch-failure hardening (P3, decide).** The dispatch callback is awaited without a
  timeout (a hung callback stalls the watcher until disposal) and the owner failure callback runs
  under the watcher's notification lock; both are documented, but a bounded dispatch timeout and an
  invoke-outside-the-lock refactor remain candidates.
- **CL-28 - snapshot capture work placement (P3, measurement).** Content fingerprinting is now
  capped at 16 MiB (see CL-08), but reads and hashing still run inside the host's dispatch path;
  moving capture work off the dispatch callback needs a measurement.
- **CL-29 - non-file URI coalescing casing (P3, platform).** Diagnostics coalescing keys for
  non-file URIs use the local-path comparer, so `untitled:A` and `untitled:a` merge on Windows;
  use ordinal comparison for non-file URIs.
- **CL-30 - remaining host-flavored knobs (P3, trigger).** Watcher timing constants (250 ms / 2 s /
  3 attempts / 64 KiB) are private; workspace folder display names are derived from the path; the
  document version policy is internal (starts at 1). Promote each when a host needs it.
- **CL-31 - job-object first-wins logger (P3, cleanliness).** `ProcessJobObject.InitializeLogger` is
  process-wide first-wins; a shared forwarder would route job diagnostics of every client to its
  own logger.

**Open items from the fourth-pass fresh review (2026-09-19)**

- **CL-32 - exit-grace window before forced termination (P3, decide).** `DisposeSessionAsync` kills
  the process immediately after dispatching `exit` when it has not exited yet; a bounded
  `WaitForExitAsync` grace spending the shutdown budget would let compliant servers finish. Deferred
  because it changes teardown timing and the disposal-budget tests.
- **CL-33 - disposal-from-callback hardening (P3, decide).** Calling `Dispose`/`DisposeAsync` from a
  `TransportUnavailable` handler runs first-caller teardown on the reporting thread; the interface
  now documents the restriction, and backgrounding the first-caller teardown remains open.
- **CL-34 - subscriber-drain quiesce on disposal (P3, hardening).** A payload already queued for a
  subscriber can still be delivered on a thread-pool thread after `DisposeAsync` returns (now
  documented); marking drains disposed from `DisposeCoreAsync` would drop queued payloads and is
  deferred because it changes the documented delivery semantics.
- **CL-35 - URI-first document identity (P3, trigger).** The tracked-document store, rename, and
  trimming paths are local-file-path only; untitled/virtual documents and non-file URI servers need
  URI-first identity overloads when a host requires them.
- **CL-36 - per-client path identity policy (P3, trigger).** `LanguageServerPaths` reads its
  case-sensitivity switches once per process; a per-client policy or comparer parameters on the
  public helpers land when a multi-client or case-sensitive-volume host appears.
- **CL-37 - public payload collection-shape unification (P3, cleanliness).** Payload records mix
  `T[]` and `IReadOnlyList<T>` members (signature parameters vs completion tags); pick one
  convention in a breaking pass when the surface is next touched.
- **CL-38 - hover tolerant converter (P3, cleanliness).** `HoverResponse` is the one feature response
  whose malformed optional `range` fails the whole response (and absent members round-trip as JSON
  null); a small tolerant converter would align it with its siblings.
- **CL-39 - client test hardening batch (P2, test).** Real-`FileSystemWatcher` event tests can fail
  red on a missed OS event (retry-once helper proposed); the fake server still lacks scriptable
  capability payloads for default-options real-process handshakes; remaining coverage gaps: server
  `positionEncoding` accept/reject, concurrent `StartAsync`, retry-after-failure, `LastStartupException`
  clearing, null `SettingsProvider` handshake failure, watcher non-matching suppression, restart
  re-push/teardown assertions, in-flight request cancellation and abandoned-task observation; volatile
  log-text assertions and `[DataRow]`-able duplicates remain. Fifth pass (2026-09-19): scripted
  request success, settings-casing push/pull, disposal-race process termination, cancel of a
  superseded running update, tracker-race smoke, URI shape plus `#`/`?`/Unicode escaping,
  command/data/annotationId/resource-operation retention, capability fallback, and the widened flake
  margins are covered; the remaining gaps above stay open.
- **CL-40 - README restructuring and adoption material (P3, docs; PARTIALLY RESOLVED 2026-09-19, fifth pass).**
  Move Installation/Usage before
  the feature/structure inventory, add Requirements, Events-and-threading, and error-handling
  sections, and add a document-tracking adoption sample (derive, `Synchronize`, send,
  `ReleaseRequest`). Resolved in the fifth pass: the folderless `workspaceFolders` contradiction, the
  `LastStartupException` pointer, and the consumer-to-host terminology sweep; the structural changes
  remain open.

**Open items from the fifth-pass fresh review (2026-09-19)**

- **CL-41 - definition converter strictness (P3, decide).** `DefinitionResponseJsonConverter` drops
  targets whose URI the runtime cannot parse as an absolute URI while sibling readers accept any
  non-blank URI, and it skips non-object array entries silently while object entries warn; align the
  policy or document the deliberate difference.
- **CL-42 - diagnostics payload detachment depth (P3, perf/cleanliness).** The diagnostics router
  copies the outer payload array and every subscriber copies it again (1+N copies per notification),
  while the nested `RelatedInformation`/`Tags` arrays stay shared; drop one copy or document the actual
  detachment depth.
- **CL-43 - client micro-nits and remaining test debt (P4, cleanliness).** `LanguageServerClientRpcTarget`
  formats capability descriptions eagerly even when logging is disabled; `TryMarkTransportUnhealthy`
  leaves the not-ready invalidation silent (no debug line); RpcTarget private-field summaries duplicate
  the constructor parameter docs; `HasActiveWatchers` duplicates `ActiveWatcherCount > 0`; the fake
  server still answers only initialize/shutdown; null-parameter notification framing and store-level
  concurrency tests remain; dozens of log-substring assertions couple tests to message wording, and the
  white-box session helpers in tests bypass the public start path.
- **CL-44 - Lua provider transport-failure reopen test is flaky (P2, test; pre-existing).**
  `UpdateDocument_WithUnchangedContentAfterTransportFailure_ReopensWithFullSemanticTokensRefresh`
  (`Tests\Nickelony.LanguageServer.Lua.Tests\Provider\LuaLanguageServerIntelliSenseProviderTests.DocumentSynchronization.cs`)
  fails roughly half of isolated runs with `didOpenCount=1` after the 5-second polling window. Verified
  pre-existing on the pristine `d4208c8` worktree (isolated baseline runs: pass / fail / fail; both
  outcomes reproduced on the same binary, and with the fifth-pass Client changes applied as well), so it
  is independent of the Client wave. Either the provider's asynchronous failed-send recovery bookkeeping
  needs an awaitable completion seam the test can wait on, or the reopen trigger genuinely races the
  invalidated-state observation; triage with the Provider/Lua wave owner. Evidence:
  `artifacts\r5b-wt.txt`, `artifacts\r5b-wt2.txt`, `artifacts\r5b-lua-single.txt`.
## 13. LanguageServer Lua review residuals (ticketed 2026-09-19)

Residual items from the 2026-09-19 third-pass fresh review of `Nickelony.LanguageServer.Lua`. The
documentation-accuracy fixes, README corrections, and the inline test-seam rationale are recorded
in `CHANGELOG.md` (Lua, "third pass"). A fifth-pass wave (2026-09-19) resolved the handshake-flag
truthfulness, the request-coordinate clamping, the slice placements, the cache-stamp naming and
reset symmetry, and the fake/test-fidelity set; its deferrals start at LUA-09. The items below
stay open because they are trigger-gated or conditional.

- **LUA-01 - handshake and settings policy surface (P3, trigger).** The initialization options
  keep `trustByClient = false` (LuaLS then applies its own plugin-trust check; its client-side
  gate is `script/plugin.lua`) and, since the fifth pass, also keep
  `changeConfiguration`/`viewDocument` at `false` because the client serves neither the
  `$/command` configuration flow nor `window/showDocument`; revisit the two flags together with
  CL-01's host callback seams. `augmentsSyntaxTokens` is advertised for the LuaLS client profile
  and is not read by the server. The settings payload likewise fixes
  `workspace.checkThirdParty = "Disable"` (a library cannot answer interactive prompts) and
  `completion.callSnippet = "Replace"` (the provider consumes snippet insert texts end to end).
  Expose `TrustByClient`, `CheckThirdParty`, and `CallSnippet` (or other flags/knobs) only when a
  host needs them - model-only knobs would be unproduced surface today (same rule as IS-04).
- **LUA-02 - test-fixture platform portability (P3, conditional).** About 20 test files build
  fixtures from `C:\Workspace` literals; convert to temp-root-derived paths when a non-Windows CI
  leg exists, or per file when touched. The suite is Windows-first by construction; the product
  path policy itself is platform-neutral, and the platform-branching tests already assert both
  policies. The tier is dormant in this checkout (no `Tests/TestAssets/LuaLS.zip` asset); supply
  the archive or wire CI when the tier must provide protection.
- **LUA-03 - workspace-edit resource operations (P3, cross-package trigger).** The shared
  `TextWorkspaceEdit` model represents text edits only, so rename edits carrying file
  create/rename/delete fail closed (now documented in the README Limitations); code actions drop
  only the affected action. Extend `Nickelony.LanguageServer.Abstractions` first if a host needs
  resource operations end to end.
- **LUA-04 - fake fidelity residuals (P3, conditional).** The test fake still bypasses the real
  transport's tolerant, logger-backed response converters (malformed-entry tolerance and warning
  behavior), a simulated `didChange` failure leaves the fake ready while a real write failure
  detaches the transport, and the sent-message counters count attempts rather than deliveries.
  Align when a retry-path regression or a converter change makes a divergence load-bearing.
  (Case-insensitive deserialization, configured JSON-null responses, generation retention on the
  unhealthy reset, and the ready-session send rule are covered since the fourth and fifth passes.)
- **LUA-05 - library-authored UI copy (P3, trigger).** The three startup-failure messages and the
  `Unknown Lua diagnostic.` fallback are English strings authored by the package and surfaced
  through its events and diagnostics. Carry structured failure data (or host-supplied copy) when
  a host needs to localize or restyle them; the family-wide fallback convention stays until then.
- **LUA-06 - time-based negative-assertion windows (P3, conditional).** The suite's absence
  proofs (150-250 ms windows; the 250 ms fan-out hold) follow the recorded
  negative-assertion-window stance and cannot observe an absence without a bound. Convert to
  signal-based observation if the provider ever exposes an observation seam for those states.
  (The two 50 ms best-effort waits were converted to recorded-start-call signals in the fourth
  pass, and the 1-second log-poll loop now runs on the shared timeout budget.)
- **LUA-07 - Tomb re-drift after the 2026-09-19 Core/Workspace sweep (P2, cross-repo, resolved
  2026-09-19).** The fourth-pass host gate (`artifacts\r4lua-tomb-wave.ps1`, version
  `1.0.0-r4lua.100`, summary `artifacts\r4lua-tomb-summary-final.txt`) showed all six Tomb targets
  blocked in `TombLib.Scripting.UI\Editing\TextWorkspaceEditApplier.cs`: CS1503
  (`System.StringComparer` -> `LocalPathComparisonPolicy?`) and CS9035 (required
  `WorkspaceDocumentChange.BeforeFileFormat`). The green LF-TMB-03 / `tomb-wave6` runs predate this
  sweep's Workspace/Pathing surface, so this was a fresh consumer-migration pass, not a regression
  of the earlier one. The first r4lua attempt also hit transient `CS2012` file locks from a
  concurrent Tomb build. **Resolved in the Workspace wave-3 host gate:** Tomb was re-migrated
  (both applier construction sites pass `LocalPathComparisonPolicy`, the preparation sets
  `BeforeFileFormat`/`AfterFileFormat`; the wave-3 follow-ups also landed - struct-key `.Value` at
  two view call sites and `ChangeSet.HasChanges` in one service plus four applier tests), and
  `artifacts/tomb-workspace-wave7.ps1` (`1.0.0-preview.101`) reports six targets 0 errors,
  `TombLib.Tests` 932/932, `TombEditor.Tests` 367/367 (`tomb-workspace-wave7-summary.txt`).
- **LUA-08 - transport-failure semantic-tokens tests are timing-flaky (P3, test hygiene).**
  `PublishSemanticTokensRefreshRequested_AfterTransportFailure_KeepsTokensAndRequestsFullRefresh`
  and `UpdateDocument_WithUnchangedContentAfterTransportFailure_ReopensWithFullSemanticTokensRefresh`
  fail intermittently inside full-suite runs (re-confirmed 2026-09-19: 2 of 3 suite runs red) while
  isolated runs pass (`artifacts/r4-lua-flake-check.txt`, `artifacts/r5-lua-catch.txt`). The
  assertions observe the transport recording after a simulated failure; convert the observation to
  a signal or widen the bounded wait against the shared timeout budget, as with the LUA-06 windows.
  (Re-confirmed again 2026-09-19 during the Provider close-out re-verify: two consecutive full-suite
  reds at the 2 s `didOpen` wait, then green in isolation and on two consecutive full re-runs -
  `artifacts/fix2-lua-recheck.txt`, `fix2-lua-one.txt`, `fix2-lua-order.txt`, `fix2-lua-recheck3.txt`.)
  Re-observed once in the final all-suites close-out wave: one red under full-suite load, then
  isolated 2/2 green and the full suite green at 259 passed / 4 skipped on re-run
  (`artifacts/rt-summary.txt`, `lua-single-1.log`, `lua-single-2.log`, `lua-full-2.log`); the
  sibling hover wait now re-issues its trigger through the shared polling helper (landed in the
  same-day wave). Re-observed again in the frozen-tree staged close-out (2026-09-19 evening; all
  other suites green, `artifacts/final-full-verify-summary.txt`): one red at the 5 s re-establish
  poll (`didOpenCount=1`), then isolated 3/3 green and the full suite green on re-run
  (`artifacts/final-lua-retest-summary.txt`). The item stays a watch entry: the negative windows
  and the 2 s `didOpen` wait remain timing-bounded.
- **LUA-09 - server process/transport surface (P3, trigger).** The provider constructs the client
  itself and forwards only the settings/capability/initialization factories, so a host cannot pass
  server arguments (`--configpath`, `--locale`, `--metapath`), environment variables, a working
  directory, or client timeouts even though `LanguageServerClientOptions` exposes all four; the
  client-injection constructor is internal. Expose an optional process-settings object on a public
  constructor or a public prebuilt-client constructor when a host needs a non-default deployment;
  the `--configpath` remark becomes fully actionable once the arguments surface exists.
- **LUA-10 - generic helper placement (P3, second-provider trigger).** `VersionFencedPayloadCache`
  (+ `LuaDocumentVersionPolicy`), `WorkspaceEditConversion`, and `LuaCompletionItemIdentity` are
  protocol-generic internals living in the Lua package; they are internal, so moving them to
  `Nickelony.LanguageServer.Client`/`.Provider` (with the completion merge helper) is
  non-breaking - do it when a second provider appears or when the Provider framework next gains
  shared payload utilities.
- **LUA-11 - semantic-token refresh machinery simplification (P3, next touch).** The per-path
  `CancellationTokenSource` map (retry loop, admission flag, KVP removes, drain) is verified
  race-correct but heavier than its contention profile needs; a `lock` + dictionary expresses the
  same protocol with less code, and the same wave could route the fixed fan-out cap and the
  per-edit refresh allocation through the provider options. Revisit when the area is next changed
  for a feature.
- **LUA-12 - Lua test-fixture consolidation (P4, cosmetic).** About 30 inline markdown-hover
  payloads duplicate one JSON shape (a `TestPayloads.CreateMarkdownHover` candidate), the 26-line
  `References.cs` one-test file can fold into a shared guard file, and the positive
  `WaitForMethodCountAsync`/`WaitForNotificationAsync` assertions could carry observed-state
  failure messages. No behavioral gap; do it when the files are next touched.
- **LUA-13 - parser micro-cleanups (P3, next touch).** The completion/hover/signature markup
  policy ("extract -> markdown-normalize or trim -> blank means absent") is implemented three
  times; centralize it in the parser. Also reword `ResolveDocumentFilePath`'s remark: the raw-path
  fallback only serves parse closures invoked outside the pipeline, which always normalizes first.

## 14. Core fresh review, second pass (ticketed 2026-09-19)

Residual items and recorded decisions from the 2026-09-19 second fresh review of
`Nickelony.IDEKit.Core`. The wave's breaking changes and fixes are recorded in `CHANGELOG.md`
("Fresh review resolution (2026-09-19, second fresh review)"); this section tracks what stays open
and why.

- **CR2-01 - `IChangeNotificationSource` placement (P3, blocked).** The contract is consumed by
  bookmark and code-action consumers, so its `LineStatus` namespace misdescribes it. Move it to a
  neutral `Nickelony.IDEKit.Core.Notifications` slice when the consuming files in
  `Nickelony.IDEKit.AvalonEdit` and `Nickelony.IDEKit.AvalonEdit.LanguageFeatures` are not being
  edited by another workstream (they were mid-edit during the wave), then update the in-repo usings
  and record the consumer migration (Tomb Editor imports the type). **Resolved by the third review
  wave: the type now lives in `Nickelony.IDEKit.Core.Notifications`, and the Tomb grep showed no
  direct imports - see §15.**
- **CR2-02 - shared snapshot range guard (P3, cross-package).** `GetText`'s range validation is
  duplicated across `StringTextSnapshot`, `DeferredTextSnapshot` (`Nickelony.IDEKit.Workspace`) and
  `TextDocumentSnapshot` (`Nickelony.IDEKit.AvalonEdit`). Share one Core-internal guard when those
  files are next touched together (requires an `InternalsVisibleTo` decision).
- **CR2-03 - test parallelization (P3, conditional).** `[DoNotParallelize]` markers are inert (no
  `[assembly: Parallelize]`); the suite runs fully sequential and the process-global `RegexCache`
  bound test assumes that. Enable bounded parallelization only after reworking that test into
  isolation, and verify the suite under parallel execution.
- **CR2-04 - `LineStatus` test ownership (recorded).** The slice ships interfaces only; its
  contract is exercised in `Nickelony.IDEKit.AvalonEdit.Tests` (`FixedLineStatusSource`, margin
  tests). Revisit a Core-side contract test only if a Core implementation ever ships.
- **CR2-05 - string-literal dialect rule (P3, docs).** The maintainer rule "no dialect rule without
  its documented divergences" was removed from `StringLiteralStyle`'s public XML docs; add it to
  `CODE_STYLE_GUIDELINES.md` when that file is next edited by its owner (it was being authored
  concurrently during this wave).
- **CR2-06 - Tomb Editor consumer migrations (P2, cross-repo).** The published API changes in this
  wave require consumer updates when Tomb Editor next takes the package: `IsEmptyOrLineComment` ->
  `IsBlankOrStartsWithLineComment` (renamed again from the wave's `IsBlankOrLineComment` by the third
  review wave - see §15 W3-08); `TextIncrementalChange` -> `TextIncrementalEdit` (where referenced);
  `TextLineCommentPlanner`/`ITextLineCommentService.TryCreateEdit` gained the required
  `insertSpaceAfterDelimiter` parameter; `LatestRequestCoordinator.RunAsync` moved
  `cancellationToken` to the last position; `IVersionedTextEditTarget` is additive (adoption is
  optional). Breaking changes are sanctioned by the owner directive; consumers migrate after the
  library settles. **Resolved 2026-09-19:** the predicate migration was superseded by
  `IsBlankOrStartsWithLineComment` (see §15, W3-08); the remaining listed migrations landed in
  `Tomb-Editor` and the host gates pass (`artifacts/tomb-wave4-summary.txt`, reconfirmed
  `artifacts\r5-tomb-gate2.log`).
- **CR2-07 - recorded keeps (no action).** `CommentSpan.SpanStart`/`DelimiterStart`/`End` (the
  explicit span-versus-delimiter naming beats symmetry); `TruncateIndentationByUnitLength` (the
  name is literal and the remarks are exact); `TextEditKernel` (the pipeline's entry-point name is
  clearest); `UseSmartIndent` (established editor vocabulary); `LocalPathComparisonPolicy.ForCurrentPlatform`
  (documented OS convenience, opt-in at call sites); `DefaultIndentationSize = 4` (documented
  fallback); auto-closing `Auto` provenance without a tracker (documented no-op; ship a tracker
  helper only when a host asks for it).

## 15. Core fresh review, third pass (ticketed 2026-09-19)

Residual items and recorded decisions from the third fresh review of `Nickelony.IDEKit.Core`. The
wave's breaking changes and fixes are recorded in `CHANGELOG.md` ("Fresh review resolution
(2026-09-19, third fresh review)"); this section tracks what stays open and why. The third wave
supersedes the CR2-06 predicate rename with `IsBlankOrStartsWithLineComment` and adds the
migrations below.

- **W3-01 - auto-closing policy seam (P3, design).** The resolution rule set (doubling, quote
  word suppression, unmatched-closer scan) is still gated by flags on `TextAutoClosingOptions` and
  `TextAutoClosingPair` rather than an injectable policy, and the presets remain conventional
  defaults in Core (their docs are now host-neutral). Extract a decision-policy interface only
  when a second host actually needs different rules.
- **W3-02 - adoption track (P3, cross-package).** An incremental/segment-backed snapshot, a thin
  document seam (snapshot + version + apply), and LSP/Roslyn conversion helpers remain the main
  host-adoption gaps; the README documents the intended workflow instead.
- **W3-03 - multi-targeting decision (P3, owner).** Core stays `net8.0`-only
  (`Directory.Build.props`); revisit only if a .NET Framework host becomes a target.
- **W3-04 - API-shape remainders (bundle with CR-01).** `TextEditPreparationDiagnostic` still
  carries no machine-readable failure category (hosts must match message text); the resolver
  overload question (a `IdentifierCharacterPolicy` overload for
  `TextRangeOffsetResolver.TryResolveOffsets`) stays open; the `TextIncrementalEdit` -> delta
  naming option stays declined (the wave-2 rename to "edit" is the recorded choice, and a
  `ToInput()` helper would duplicate the deliberately removed `TextEditInput.FromChange`).
- **W3-05 - test-suite remainders (P3).** Wave-file test names keep the behavior-first style
  (`TextAutoClosingWaveTests`, `EditingWaveTests`); the equals-only masking identity asserts, the
  tautological version-workflow test, the compound tests in `TextRangeTests` and
  `StringTextSnapshotCharacterizationTests`, the bare `[DataRow]` display names, and the two
  Persistence fixture conventions stay as reviewed - the timing probes gained warm-ups and a
  larger floor instead.
- **W3-06 - re-confirmed keeps (no action).** `CommentSpan.SpanStart`/`DelimiterStart`/`End`
  (explicit span-versus-delimiter naming beats symmetry - re-confirmed against the third review's
  symmetry suggestion); `ITextEditTargetVersion` (opaque change stamp; Tomb reads `Version`);
  `LocalPathComparisonPolicy.ForCurrentPlatform` (documented OS convenience; removing it would
  duplicate the probe at four call sites without reducing host coupling); `UseSmartIndent`;
  `DefaultIndentationSize = 4`; `TextEditKernel`; `TruncateIndentationByUnitLength`.
- **W3-07 - first-move API additions deferred (P3).** `FindNextMatch`/`FindPreviousMatch` gained
  the symmetric pattern overloads; the `TextEditPreparationDiagnostic` category enum, the
  `TextRangeOffsetResolver` policy overload, and the `IdentifierSpanMode.Nearest` mode stay
  deferred to the CR-01/CR-02 API-shape wave.
- **W3-08 - Tomb Editor consumer migrations (P2, cross-repo, verified by the host gate).**
  `CommentOperations.IsBlankOrLineComment` -> `IsBlankOrStartsWithLineComment` (three TombLib line
  services); `IdentifierAffinity` -> `IdentifierSpanMode` (TRX `TextAnalysisService`);
  `TextLineMap` moved from `Core.Editing` to `Core.Text` (Lua semantic highlighting). The other
  renames (`IsWrappingSelection`, `DidWrapSelection`, `Superseded`, `Default*AutoCloseBefore`,
  `ApplyEdit`, the `Notifications` move) have no Tomb call sites. Gate evidence (2026-09-19,
  packages packed at `preview.39`): all six Tomb targets build 0 warnings/0 errors and both suites
  pass (`TombLib.Tests` 932/932, `TombEditor.Tests` 367/367; `artifacts\r5-tomb-gate2.log`).

## 16. IntelliSense round-3 review residuals (ticketed 2026-09-19)

Residual items and recorded decisions from the third fresh review of `Nickelony.IDEKit.IntelliSense`.
The wave's fixes are recorded in `CHANGELOG.md` ("Fresh review round 3 resolution (2026-09-19)") and
in `__180923_intellisense-fresh-review-round3.md` section 10; this section tracks what stays open.

- **R3-01 - README structural restructure (P3, docs).** The completion slice bullet is a ~430-word
  run-on, the kernel behavior is described in three places, and stock phrases repeat; restructure the
  README sections without losing the documented conventions.
- **R3-02 - protocol-mirror member-doc density (P3, docs).** 91 one-line member docs restate the
  member name; acceptable for protocol mirrors, but numeric lineage should move to a type-level
  remark once per type instead of repeating per member.
- **R3-03 - localized perf touch-ups (P3).** Snippet O(N^2) adversarial path (memoize the "no `}` to
  the right" boundary); `TextSemanticToken.CaptureModifiers` per-token allocation plus `List.Contains`
  dedup; grouped `DocumentSymbolOutlineBuilder` double child copy; kernel double snapshot read.
- **R3-04 - host-neutrality policy decisions (P3, design).** Open `TextSignatureHelpTriggerKind` (or
  move it to the protocol bridge); resolve-lifecycle placement (`ResolveAsync`/merge rules live in
  the payload); presentation-state framing (`TextCompletionPresentationState` and siblings);
  symbol-kind bridge as an explicit switch instead of cast-after-range-check; `TextDefinitionLocation`
  EOL-counting spec; controller debounce defaults.
- **R3-05 - scanner dedup (P3).** Four snippet scanners replicate escape/brace/digit rules; extract
  shared probes so the "mirrors the expansion path" invariant is enforced by construction.
- **R3-06 - carrier-test remainders (P3, tests).** Carrier equality components never vary (Payload,
  selection offsets, request `StartOffset`); the hover equality fixture reuses one diagnostic
  instance; a "compares every component" case re-discriminates `Source` instead of `Code`; two
  near-duplicate unsupported-placeholder tests; the reflection helper compares `IReadOnlyList`
  members by reference; the merge test asserting the resolved format wins keeps its old name.
- **R3-07 - `DiagnosticHitTester` keep-or-retire (P3).** README-advertised helper with no caller in
  either repository; keep as deliberate surface or retire in a later cleanup wave.
- **R3-08 - Tomb host gate re-run (P2, cross-repo) - DONE (2026-09-19 close-out).** The migration
  landed; the final-tree gate run (`artifacts\r4lua-tomb-wave.ps1`, 13 packages at
  `1.0.0-r4lua.100`) is fully green: six Tomb consumer builds 0 errors, `TombLib.Tests` 932/932,
  `TombEditor.Tests` 367/367 (`artifacts\r4lua-tomb-summary.txt`, `failedProjects=0
  failedTests=0`). The earlier `IAsyncDisposable` test-double blockers were migrated by the
  owning wave.
- **R3-09 - request-session wording (P3, docs).** The line-12 "tracked tokens" summary predates the
  conditional-admission API; reword to match it.

## 17. LanguageServer Provider fresh review residuals (ticketed 2026-09-19)

Residual items and recorded decisions from the fourth fresh review of `Nickelony.LanguageServer.Provider`
and its re-run. The wave's fixes are recorded in `CHANGELOG.md` ("Fresh review resolution (2026-09-19,
fourth pass re-run)"); this section tracks what stays open.

- **PR-01 - deterministic reconciliation-delta test (P2, tests).** The runtime watcher-recovery test
  asserts the replacement watcher only; a non-empty `ReconcileWorkspaceSnapshotAsync` delta cannot be
  driven deterministically today because the live watcher consumes real file-system changes before
  the recovery path can capture them. Add a seam (or a scope-level harness) when the reconciliation
  path is next touched.
- **PR-02 - deferred-close rename bound (P3).** `FollowRenamedDocumentCloseAsync` follows at most
  four rename hops; a pathological rename storm can leave a deferred close pending until a later
  release/open/close/trim touches the newest path. Consider a record-bound close intent if a host
  ever reports the case.
- **PR-03 - startup-failure classification without a generation fence (P3, latent).**
  `TryEnsureStartedForOperationAsync` classifies a plain `IOException` from `EnsureStartedAsync` as
  a transport failure without a generation check; no current client path throws one (the client
  returns `false` instead), so revisit if one appears.
- **PR-04 - hover/rename tolerant converters (P3, watch item).** The dispatcher now reports an
  unprocessable response payload as the fallback value; if a real server is ever observed sending a
  legitimate hover/rename shape that the strict payload types reject, add tolerant converters for
  those roots like the other response types.

## 18. Whole-solution fresh review residuals (2026-09-19, `__190919_` fix wave)

Deferred items from the 2026-09-19 fix wave that resolved the still-valid findings of the
whole-solution fresh review (`__190919_fresh-review.md`). The wave's fixes are recorded in that
document's resolution section and in `CHANGELOG.md`. The `Nickelony.IDEKit.Core` findings are
deliberately not part of this wave (owner handles Core separately).

- **F19-01 - rename/formatting provider coupling (P2, design decision).**
  `Nickelony.LanguageServer.Abstractions` has `ITextEditProvider : ITextFormattingProvider`, so a
  rename-only provider must implement formatting. Decide between a narrow `ITextRenameProvider`
  split (framework consumes the narrow contract where it can) and an explicit documented coupling.
  Backlogged rather than resolved because it changes provider plumbing that the Tomb gate exercises.
- **F19-02 - Abstractions event-args test coverage (P2, tests).** No coverage for
  `StartupFailedEventArgs`, `WorkspaceWatcherFailedEventArgs`, and `DiagnosticsUpdatedEventArgs`
  (stored value + null validation), and `TextCodeAction.Kind` trimming is untested.
- **F19-03 - Client test-seam containment (P2, test seams).**
  `Transport/LanguageServerDiagnosticsRouter.cs` treats `TransportGeneration == 0` as "always
  deliver", `Transport/SerializedDiagnosticsSubscriberSet.cs` exposes a constructor that exists
  only for a concurrency test, and four test-hook delegates are threaded through the client's
  constructor. Move the seams behind an internal options object or a test-only subclass.
- **F19-04 - Client timing-probe hardening (P2, tests).** Negative probes with 100-200 ms budgets
  (transport events/diagnostics/semantic-token tests) silently pass under load; replace with
  signaling asserts or centralize the budgets.
- **F19-05 - Client scheduling surface (P2, design).** `Documents/DocumentOperationScheduler*.cs`
  is ~600 lines of public API the client itself never calls; decide whether it stays, moves beside
  the provider, or becomes a separate host package.
- **F19-06 - Processes async path (P2, design/API).** The sync-only runner hand-rolls a 100 ms
  cancellation poll; add a `RunAsync` built on `WaitForExitAsync` (API addition to decide before
  the pattern hardens).
- **F19-07 - Processes pre-canceled token semantics (P3).** A token canceled before the call still
  launches the process and immediately kills it; decide whether to short-circuit before spawning
  a doomed child.
- **F19-08 - Lua test hardening (P2, tests).** Fixed "best-effort" delays before negative
  assertions in both Lua suites, and duplicated test infrastructure (`FakeLanguageServerClient`
  twice, `TestWait` vs `TestPolling`) that should converge on one shared support layer.
- **F19-09 - Provider dropped-commit queue/replay (P3, improvement).** The restart-replay skip
  window is now documented on the public members; consider queueing pending opens/updates during a
  restart replay instead of dropping them.
- **F19-10 - JsonSchema traversal width (P3, feature).** The README now describes the actual
  traversal; extending it to `additionalProperties`, `patternProperties`, `not`,
  `if`/`then`/`else`, and nested definition sections is deferred until a host needs the reach.
- **F19-11 - Non-public doc-noise trim (P3, hygiene).** One dedicated pass to remove restating
  `<summary>` blocks on private/internal members (cited examples: `BookmarkCoordinator` private
  helpers, `DocumentVersionCache.Invalidate`, `RegexHighlightingDefinition` private members,
  `LanguageServerClientRpcTarget`/`WorkspaceChangeDebouncer`/`LanguageServerTransportSession`
  private docs, `LspMethodNames` const docs, internal reservation records).
- **F19-12 - `LineStatusMarginBase.DrawMarker` accessibility (P3).** The `protected internal
  abstract` seam is callable assembly-wide; revisit when a host subclass scenario appears.
- **F19-13 - TestResults folders (P3, hygiene).** TRX run folders under `Tests/*/TestResults`
  should be confirmed ignored or cleaned before the publication checkpoint.

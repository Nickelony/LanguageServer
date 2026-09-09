# Changelog

All notable changes to the packages in this repository are documented in this file.
Entries list package breaking changes first, then fixes and additions.

<!-- When adding an entry, use the package version as the section title. -->

## 1.0.0-preview.38 (unreleased)

`1.0.0-preview.38` is the single clean wave after `preview.37`. It removes the duplicated
path-identity and range models found by the cross-family duplication audit, extracts the
language-neutral provider framework out of the Lua package into the new
`Nickelony.LanguageServer.Provider` package, and restructures the solution's folders into
vertical slices: `Nickelony.IDEKit.*` namespaces follow their folder paths, while the
`Nickelony.LanguageServer.*` packages keep their deliberately flat per-package namespaces across
folder moves. `preview.38` was never packed, so the vertical-slice wave folds into this section
instead of a new number; packages without changes carry no entry, and no compatibility shims
remain.

### Nickelony.IDEKit.Core

**Breaking changes**

- Edit-application types moved to the `Nickelony.IDEKit.Core.Editing` namespace (they were in
  `Nickelony.IDEKit.Core.Text`): `PreparedTextEdits`, `ITextEditTarget`, and
  `ITextEditTargetVersion`. `TextEditOperation` stays in `Nickelony.IDEKit.Core.Text`.
- Request-coordination types moved to the `Nickelony.IDEKit.Core.Requests` namespace (they were
  in `Nickelony.IDEKit.Core.Infrastructure`): `LatestRequestCoordinator`, `RequestTokenSource`,
  and `RequestOutcome`.
- `TextEditPreparationDiagnosticCode` was deleted (no consumers); the conflict description and
  indices on `TextEditPreparationDiagnostic` carry the information instead.

**Additions**

- New `Nickelony.IDEKit.Core.Pathing.LocalPathComparisonPolicy` - the shared local-path comparison
  policy (`ForCurrentPlatform`, `CaseInsensitive`, `CaseSensitive`, `Comparison`, `Comparer`)
  used by the workspace document family and the language-server client. `ForCurrentPlatform`
  treats Windows and macOS as case-insensitive and every other platform as ordinal. (Named
  `LocalPathComparison` until the 2026-09-19 review wave.)
- New `SidecarLineFile.ValidateExtension(string)` validates a sidecar extension without building a
  path, so a configuration boundary rejects an unusable extension before the first save or
  restore; `GetSidecarPath` uses the same validation internally.

**IntelliSense round-3 review support (2026-09-19)**

- `LatestRequestCoordinator` gained `CanPublish(long)` (an atomic identifier-and-token check for
  publish gates), `BeginRequest(Func<bool> canAdmit)` (a rejected admission returns the zero
  identifier and neither supersedes nor cancels the current request), and
  `CancelPendingRequest(long)` (cancels only the named request). They support the request and
  disposal races fixed in `Nickelony.IDEKit.IntelliSense` and are general coordinator surface.

**Fresh review resolution (2026-09-15)**

- `CommentSyntax` models the block delimiters as one `BlockCommentSyntax` value (`Open`, `Close`,
  `AllowNesting`) instead of three separate parameters, so an unpaired delimiter cannot be
  specified and silently ignored, and block nesting travels with its delimiters:
  `CommentSyntax(string? lineCommentDelimiter, BlockCommentSyntax? blockComments, StringLiteralStyle stringStyle)`.
- `CommentOperations` removed `FindLineComment`, `FindBlockComment`, and `CommentSpan.ToTextRange`
  (no consumers; `FindComment` and `EnumerateComments` cover the queries).
- `TextEditInput.FromChange` was removed (forwarding-only, no consumers); construct
  `TextEditInput` from the range and replacement text directly.
- `IdentifierCharacterPolicy` dropped the quote/punctuation flag layer (`Create`'s `includeQuotes`
  and `includePunctuation`, `IncludeQuotes`, `IncludePunctuation`, `WithQuotes`,
  `WithPunctuation`); pass a predicate instead. `IdentifierOperations.GetWordEndingAt`'s
  `Func<char,bool>` overload was replaced by an `IdentifierCharacterPolicy` overload.
- `IdentifierOperations.TryGetContainingSpan` was renamed to `TryGetTokenSpan`: with
  `IdentifierAffinity.EndingAtOffset` the returned span ends at the probe instead of containing it.
- `IndentationContext.PreviousLineIndentation` was removed (5 to 4 members); policies derive the
  value with `IndentationOperations.GetLeadingWhitespace(context.PreviousLineText)`.
- `FindReplaceText.ReplaceAll(string, string, TextSearchQuery)` was reordered to
  `ReplaceAll(string, TextSearchQuery, string)`, matching the query position of every sibling
  overload.
- `IncrementalLineStateCache.ApplyChange` now rejects a `firstChangedOffset` outside the new
  snapshot (`ArgumentOutOfRangeException`) instead of clamping an oversized offset and silently
  keeping every cached state.
- `TextRangeOffsetResolver.TryResolveOffsets` takes a `TextPositionRange` instead of two separate
  `TextPosition` values (5 to 4 parameters).
- `TextEditPreparationResult`/`TextEditKernel` conflict diagnostics and the
  `TextEditConflictDetector` documentation now describe the actual generation order
  (duplicate insertions first, then pairwise conflicts ordered by the left replacement operation).
- `RequestOutcome.Canceled` documents all three producers (pre-canceled token, delegate that
  observes cancellation, and delegate that throws `OperationCanceledException`), and the class
  remarks warn that `default(RequestOutcome)` reads as `Completed`.
- `LatestRequestCoordinator.CancelPendingRequest` absorbs a faulting cancellation callback with the
  same policy as the supersede path, so an unrelated `AggregateException` no longer escapes a
  mouse/key handler call.
- `CommentScanner` scans raw-string quote runs once per run instead of rescanning the shrinking run
  from every quote (a long quote run went from quadratic to linear); `CommentOperations.IsEmptyOrLineComment`
  no longer allocates a trimmed copy per line.
- `LineDiffResult` (backed by a `FrozenSet`) now implements set-based value equality with a
  matching hash, so content-identical results compare equal.
- `LineDiffer` rents its scratch vectors from `ArrayPool<int>` instead of allocating large-object-heap
  arrays per large diff.
- `SidecarLineFile` rejects a document path that ends in a directory separator (`Save` fails,
  `Restore` returns empty, `GetSidecarPath` throws) instead of silently placing the sidecar inside
  the directory, and describes the weaker guarantees of its temporary-file replacement.
- `RegexCache` normalizes `TimeSpan.Zero` and `Regex.InfiniteMatchTimeout` before building the cache
  key, so the two spellings share one slot; `FindReplaceText`/`TextSearchQuery` document the
  single-engine (regular-expression) design and recommend `RegexOptions.NonBacktracking` for
  untrusted patterns.
- `WhitespaceConverter` returns the original instance when nothing converts, matching
  `TrimTrailingWhitespaceFormatter`; the trim formatter documents that formats with significant
  trailing whitespace (Markdown hard breaks) must not be trimmed.
- Documentation corrections: `StringLiteralStyle.VerbatimDoubleQuoted` (the orders are `$@"..."`
  and `@$"..."`, not `$"@..."`), `GetCodeEnd` closer model, whole-word `\b` limitations, the
  cumulative cost of `PreparedTextEdits.MapOffset`, `IndentationContext`'s default-instance hazard,
  the `IIndentationPolicy` replacement/no-change contract, theme list display ordering, the path
  normalization duty, `TextDiagnosticSeverity` ordering versus precedence, and the
  conflict-diagnostic ordering on `TextEditPreparationDiagnostic.RelatedSourceIndex`.

**Additions (fresh review resolution)**

- `BlockCommentSyntax` (opener, closer, `AllowNesting`), `TextEditOperation.IsNoOp` and
  `TextEditInput.IsNoOp`, `TextRange.GetText(ITextSnapshot)`, `TextPositionRange.IsEmpty`/`Contains`,
  and `TextLineMap.TryGetOffsets(TextPositionRange, out TextRange)` plus an internal line-span
  accessor, so range consumers do not hand-roll pairing and validation.
- `TextEditConflictMessages` (internal) is the single conflict-kind to message map for both the
  preparation diagnostics and the carrier's `ArgumentException`s; both switches are exhaustive.
- `IdentifierOperations.GetWordEndingAt(string, int, IdentifierCharacterPolicy)` replaces the
  predicate overload.
- A README quick start (install, minimal snapshot/range/comment sample, license pointer).
- Core test project gained `InternalsVisibleTo` for `RegexCache` bound tests and a comment-scanner
  invariant suite (mask length/terminator preservation, span ordering, pathological quote runs).

**Fresh review resolution (2026-09-16)**

- `TextEditorDiagnosticSeverity` was renamed to `TextDiagnosticSeverity` (family naming alignment,
  owner-directed 2026-09-16; supersedes the earlier keep decision), so the diagnostics vocabulary
  matches the surrounding `TextDiagnostic*` family.
- `SidecarLineFile` requires an explicit `sidecarExtension` on `Save`, `Restore`, and
  `GetSidecarPath` (the `.sidecar` default is gone; the extension is a caller decision). Entry
  content is one invariant-culture line number per line terminated by a single `\n`, so a sidecar
  is byte-identical on every platform; extension validation uses a fixed portable rejected set
  (path separators, control characters, and `: * ? " < > |`) instead of the platform-dependent
  invalid-file-name set.
- `CommentSpanEnumerator`'s constructor is internal; create enumerators through
  `CommentOperations.EnumerateComments`, the single public entry point.
- `TextEditOrderKey` (internal) replaces `TextEditOperationOrder` as the shared ordering contract
  for the preparation kernel and the conflict detector.
- `PreparedTextEdits` no longer spells the duplicate-insertion rule itself: the shared
  `TextEditConflictDetector` owns conflict classification for both the constructor's
  `ArgumentException`s and the kernel's diagnostics, and `TextEditConflictMessages` owns every
  message. The same-offset ordering rule (an insertion must not precede a replacement at one
  offset) remains the carrier's own contract.
- `TextRangeOffsetResolver.TryResolveOffsets` now builds its representable path on
  `TextLineMap.TryGetOffsets`, so the position-range-to-offset conversion exists once; behavior is
  unchanged (the resolver falls back exactly when the conversion fails or yields a zero-length
  range). The probe policy divergence from `IdentifierOperations.TryGetTokenSpan` (nearest-word
  anchor versus containing-token lookup) is documented and pinned by tests.
- `TextDiagnosticSeverity` no longer promises protocol-aligned numeric values; the values
  are stable identifiers, not a precedence order. The Lua diagnostics parser maps LSP severities
  with an explicit switch instead of casting protocol numbers into the enum.
- Internal consolidations: `LineTerminators` owns the LF/CRLF/CR rules (including CRLF as one
  unit) and `WhitespaceScan` owns the narrow space/tab alphabet, so the terminator and whitespace
  checks exist once; `RegexCache` evicts the oldest insertion (predictable under a typing burst)
  instead of an arbitrary entry; `TextLineSplitter.Split` delegates to the shared line table;
  `LineIndexSearch.FindLineIndex` uses `MemoryExtensions.BinarySearch`; the coordinator's
  duplicated cancel swallow-policy moved into `TryCancelSafely`.
- Documentation corrections: `GetCodeEnd`'s closer model (double-quoted, single-quoted, backtick,
  and verbatim closers count as code; raw and long-bracket closers do not), the MATLAB example
  moved to the span overload, `ThemeCatalog`'s constructor exception (`or`, not `and`),
  `LineDiffResult.IsApproximate`'s two fallback causes, `GetWordEndingAt`'s empty-versus-null
  completeness, `RequestOutcome.Canceled`'s producers, `FindReplaceText`'s literal-text entry
  point, `LocalPathComparison`'s macOS normalization sentence, `ThemeCatalogOptions` and
  `IndentationTextLine` default-instance hazards, and the Core README (a `Pathing` slice bullet,
  null-policy / no-op-identity / whitespace rows in the conventions table, the C-family nesting
  sample, a single `net8.0` statement, deduplicated multi-line-string paragraphs, and no
  hard-coded benchmark figures).
- Test hardening: allocation-count assertions were dropped where instance identity already proves
  the no-copy contract, the launcher test names use the outcome vocabulary, and the duplicated
  `FindCommentStart` helper moved into shared test support. New coverage: `TextPositionRange`
  `Contains`/`IsEmpty`, `TextLineMap.TryGetOffsets`, `TextRange.GetText(ITextSnapshot)`,
  `SidecarLineFile.ValidateExtension`, the `ITextEditTargetVersion` capture/prepare/compare
  workflow, mixed invalid-plus-conflict diagnostic ordering, `TextEditOperation.IsNoOp`,
  `CommentSpanEnumerator` exhaustion, `default(TextSearchQuery)` rejection, and the resolver's
  conversion agreement and probe divergence.

**Backlog execution (2026-09-16, phase 7 - request consolidation)**

*Additions*

- `LatestRequestCoordinator` gained the host-driven request surface: `BeginRequest` admits a
  request that the caller completes itself and returns its identifier, `IsCurrent` decides
  admission for such an identifier (zero is never current), and `RequestCancellationToken`
  reports the latest request's cancellation token (`CancellationToken.None` before any request).
  Both request levels share one request lifetime: a newer request of either kind cancels the
  outstanding token first, `CancelPendingRequest` cancels the pending token without invalidating
  the request, and a canceled token stays observable through `RequestCancellationToken` until a
  newer request begins. A host-driven request supersedes an outstanding run and vice versa.

**AvaloniaEdit readiness extraction (2026-09-18, tier 2)**

*Breaking changes*

- Editor-generic types moved in from `Nickelony.IDEKit.AvalonEdit`: `NavigationLocation` to the
  new `Nickelony.IDEKit.Core.Navigation` slice, `IChangeNotificationSource` and
  `ILineStatusSource` to the new `Nickelony.IDEKit.Core.LineStatus` slice, and
  `TextDiagnosticSegment` to `Nickelony.IDEKit.Core.Diagnostics` (joining
  `TextDiagnosticSeverity`). Consumers update usings; shape and behavior are unchanged, and the
  line-status contracts keep their obligations (no throwing during a consumer's read pass,
  cached per-pass reads, ascending one-based line numbers, any-thread `Changed` with caller
  marshaling) in consumer-agnostic wording.

**AvaloniaEdit readiness extraction (2026-09-18, `AE-EXT`)**

*Breaking changes*

- The auto-closing model moved in from `Nickelony.IDEKit.AvalonEdit.Editing` to the new
  `Nickelony.IDEKit.Core.AutoClosing` slice: `TextAutoClosingOptions`, `TextAutoClosingPair`,
  `TextAutoClosingPairKind`, `TextAutoClosingAction`, `TextAutoClosingActionKind`,
  `TextAutoClosingProvenance`, and `TextAutoClosingResult`. Consumers update usings; shape
  and behavior are unchanged.

*Additions*

- New `Nickelony.IDEKit.Core.AutoClosing.TextAutoClosingResolver` - the editor-neutral
  auto-closing resolution core: `TryResolveAction` resolves the action for entered text
  (insert, skip, or wrap) over an `ITextSnapshot` with the full gate pipeline, and
  `TryResolvePairDeletion` resolves the pair a pair deletion removes (Backspace pair
  matching plus the delete provenance gate). Resolution reads only `TextLength` and
  `GetCharAt` and finds line ends with a terminator scan, so snapshots with lazy line
  metadata stay lazy on the typing path; insertion provenance is caller-supplied through a
  tracking callback (`null` means nothing is tracked).

**AvaloniaEdit readiness extraction (2026-09-18, `AE-LINE`)**

*Breaking changes*

- The line-comment edit model moved in from `Nickelony.IDEKit.AvalonEdit.Comments` to
  `Nickelony.IDEKit.Core.Comments`: `TextLineCommentEdit` and `TextLineCommentAction`.
  Consumers update usings; shape and behavior are unchanged.

*Additions*

- New `Nickelony.IDEKit.Core.Comments.TextLineCommentPlanner` - the editor-neutral line-comment
  edit computation: `TryCreateEdit` expands the selection to the lines it touches, resolves the
  toggle decision with the same indentation rule its transforms use (a line counts as commented
  only when the delimiter follows space/tab indentation), transforms
  each line (leading whitespace preserved, whitespace-only lines unchanged), preserves each
  line's original terminator, and returns a `TextLineCommentEdit` with the replace range, the
  replacement text, and the post-edit selection. Line metadata access materializes a snapshot's
  line table, which is accepted for this user-rate command (recorded in the planner's remarks).

**Fresh review resolution (2026-09-19, third fresh review)**

*Breaking changes*

- `CommentOperations.IsBlankOrLineComment` was renamed to `IsBlankOrStartsWithLineComment`: the
  predicate uses the broad `char.IsWhiteSpace` alphabet (the planner keeps its space/tab rule),
  and the name now says so; a `null` line is documented as blank.
- `IdentifierAffinity` was renamed to `IdentifierSpanMode` (file and call sites): the enum selects
  which token span a probe resolves, not a caret-boundary affinity; `EndingAtOffset` now carries an
  explicit value.
- `TextAutoClosingRequest.WrappingSelection` was renamed to `IsWrappingSelection` and
  `TextAutoClosingResult.WrappedSelection` to `DidWrapSelection`, so permission, intent, and
  outcome no longer differ by one letter.
- `RequestOutcome.SupersededByNewerRequest` was renamed to `Superseded` (it also fires for an
  owner `Invalidate`); its docs and `Canceled`'s now state the checkpoint semantics, including the
  publication window.
- `IncrementalLineStateCache.ApplyChange` was renamed to `ApplyEdit`, and the slice vocabulary is
  uniformly "edit" now.
- `IChangeNotificationSource` moved from `Nickelony.IDEKit.Core.LineStatus` to the new
  `Nickelony.IDEKit.Core.Notifications` slice, and `TextLineMap` moved from
  `Nickelony.IDEKit.Core.Editing` to `Nickelony.IDEKit.Core.Text`; both docs are consumer-neutral.
- `TextAutoClosingOptions.DefaultBracketCharactersBeforeClosing` and
  `DefaultQuoteCharactersBeforeClosing` were renamed to `DefaultBracketAutoCloseBefore` and
  `DefaultQuoteAutoCloseBefore`, matching the `AutoCloseBefore` property they feed.
- `TextAutoClosingAction.Kind` and `ClosingText` are now get-only, so the documented "an insert or
  skip action without closing text cannot be constructed" invariant holds for object initializers
  as well.

*Fixes*

- `PreparedTextEdits` copies its input before validation, so the validated batch cannot disagree
  with the stored one under concurrent caller mutation; `MapOffset` and `MapOffsets` now share one
  mapping core.
- `RegexCache` includes the captured culture in its key for case-insensitive patterns without
  `RegexOptions.CultureInvariant`, matching the BCL's culture-aware case folding; its docs no
  longer claim the BCL cache cannot key on per-query settings.
- `BacktickFenceTextNormalizer` pairs fences (the CommonMark closing rule): only a fence at least
  as long as its opener and without an info string closes it, so fence-like lines nested inside a
  longer fence stay content; the fence-line and indentation docs match the code.
- `SidecarLineFile.TryRestore` reports a directory at the sidecar path as a storage failure
  instead of "nothing was saved"; `LineIndexSearch` rejects an empty line collection with
  `ArgumentException` in all builds (the overloads previously diverged in release).
- `FindReplaceText.FindNextMatch` and `FindPreviousMatch` gained the pattern/options/timeout
  overloads their siblings have; their class remark says "at or before" again.
- Auto-closing documentation is host-neutral and accurate: the `AutoCloseUnconditionally` summary
  matches the options type, the skip bullet covers every pair kind, the pair kind and provenance
  wording was rewritten, and the presets are documented as conventional defaults rather than
  language definitions.
- Removed the stale "duplicate-insertion conflict" text from `TextEditPreparationDiagnostic` and
  `PreparedTextEdits` (the rule does not exist; same-offset insertions are valid), and corrected
  the `TextIncrementalEdit` "delta" wording, the indentation/line-status consumer vocabulary, and
  the `NavigationLocation` equality duplication (its `Equals` now delegates to the ordinal path
  comparison).
- New test coverage for the wave's behavior contracts: auto-closing unconditional bypasses and
  pair-deletion edges, conflict `RelatedSourceIndex`, `TryRestore` failure paths, sidecar BOM/CR
  reads, fence pairing, the default identifier digit rule, `continueOnCapturedContext: false`, and
  a Diagnostics suite; the timing probes gained warm-ups and a larger floor.

**Fresh review resolution (2026-09-19)**

*Breaking changes*

- `LocalPathComparison` was renamed to `LocalPathComparisonPolicy`, so the type name reads as the
  policy it is; members are unchanged (`ForCurrentPlatform`, `CaseInsensitive`, `CaseSensitive`,
  `Comparison`, `Comparer`). The old name only ever existed in the unreleased wave.
- `NavigationLocation.PreferredLine` was renamed to `PreferredDocumentLine`, matching the value
  the bindings pass (a one-based document line) and the documented semantics.
- `LineDiffResult` no longer exposes a public constructor; create results through the new
  `LineDiffResult.Create(IReadOnlySet<int>, bool)` factory, which rejects a `null` set and copies
  it into an immutable frozen set, so the documented frozen-set invariant holds for hand-built
  results as well.
- `TextAutoClosingResolver.TryResolveAction` now takes a `TextAutoClosingRequest` (`Snapshot`,
  `CaretOffset`, `InputText`, `Options`, `WrappingSelection`, `IsTrackedClosingText`) instead of
  six separate arguments: `TryResolveAction(in request, out action)`. The AvalonEdit service
  keeps its document-based method and builds the request internally.
- `CommentSyntax` normalizes a whitespace-only line-comment delimiter to `null` (like an empty
  one), so a stray `" "` delimiter can no longer turn every space into a comment opener while
  the planner refuses to act on it.
- Line-comment toggling now follows the mainstream editor convention: commenting inserts the
  delimiter plus one space (`"first"` becomes `"// first"`), and uncommenting removes the
  delimiter together with one following space (`"// x"` becomes `"x"`). The planner and the
  AvalonEdit binding change together.

*Fixes*

- Auto-closing skip path: a quote-like closing text preceded by an odd number of escape
  characters is no longer skipped when the typed input matches the full (or leading) closing
  text; the escape gate the opening-token path already applied now covers both paths, as the
  docs promised.
- Auto-closing `AutoCloseBefore`: an assigned **empty** set now means "no characters besides
  whitespace and the end of the text" instead of silently falling back to the kind's preset. The
  built-in presets are exposed as `TextAutoClosingOptions.DefaultBracketCharactersBeforeClosing`
  and `TextAutoClosingOptions.DefaultQuoteCharactersBeforeClosing`.
- Line-comment toggle: the decision now uses exactly the transform's indentation rule, so a line
  with unusual whitespace before the delimiter (for example a non-breaking space) comments
  instead of dead-ending in a permanent no-op.
- `LatestRequestCoordinator`: the `RequestCancellationToken` contract now matches the behavior -
  a canceled caller token ends a run but does not cancel the exposed token - and the coordinator
  no longer disposes the exposed source, so the token stays registrable after a completed run
  exactly like the host-driven path. `RequestTokenSource` and the coordinator skip the zero
  identifier produced by the 2^64 wrap, keeping the documented nonzero-token invariant exact.
- `TextRangeOffsetResolver` fallback anchors widen to a full surrogate pair, so a fallback range
  never splits one.
- `LineDiffer` work budget now counts the shared prefix/suffix trims and the backward snake
  extension, and a recursion-depth cap trips the approximate fallback before the thread stack
  could overflow on a pathological input.
- `SidecarLineFile.GetSidecarPath` validates the extension before the path, matching `Save` and
  `Restore`, so the reported exception no longer depends on which member was called.
- `TextEditPreparationDiagnostic` validates its message (`ArgumentNullException`), and
  `TextEditPreparationResult.Invalid` reuses one cached empty batch instead of allocating per
  rejection.

*Documentation*

- Docs now match the code across the slices: the auto-closing skip/escape promise, the no-op
  precedence in `TextEditKernel.Prepare` (ranges validate before the no-op skip), the
  `PreparedTextEdits.MapOffset` end-insertion bullet, the `IncrementalLineStateCache` preserved
  states ("up to and including the first affected line's start state"), `CommentSpan.DelimiterStart`
  (line delimiter or block opener), the resolver's "resolves no action" result, the planner's
  toggle-decision summary, the sidecar/`RegexCache`/diff-budget rationales, and the consumer-neutral
  line-status, severity, and navigation wording.
- Core README: quick-start value corrected, the null-content row covers `TextLineMap.Build` and the
  normalizer's `null` result for blank input, a decision table for the three line-shaped inputs,
  and a clearer host-neutrality statement (text-editor concepts live in Core; framework-typed
  surfaces stay in the bindings).

*Tests*

- +26 Core tests: escape-gate regressions (odd/even parity and an asymmetric pair), empty-`AutoCloseBefore`
  semantics, `DeleteMode.Never`, the planner's unusual-whitespace and whitespace-delimiter cases,
  frozen-set construction, coordinator token lifetime and the corrected caller-token contract,
  kernel no-op precedence, enum value pins, masking expected output, BOM-less sidecar bytes,
  surrogate-widened anchors, and reference-type line-state caching; the TCS-driven supersession
  tests gained `[Timeout(30_000)]` guards.

**Fresh review resolution (2026-09-19, follow-up pass)**

- `CommentOperations.GetCodeEnd` no longer counts a single-character block-comment closer as code:
  the move that consumes a closer is marked as a delimiter move for every closer length, so syntaxes
  such as `{`/`}` or `[`/`]` report the documented code end (and continuation-marker detection
  through `GetCodeEnd` sees the correct end). Regression tests cover one-character closers and a
  continuation marker after such a comment.
- `TextAutoClosingResolver.TryResolvePairDeletion` declares `pair` as nullable with
  `[NotNullWhen(true)]`, so the annotation matches the documented contract (null when no pair
  applies) instead of relying on a null-forgiving assignment.
- `CommentKind`, `TextLineCommentAction`, `IdentifierAffinity`, `RequestOutcome`, and
  `TextAutoClosingActionKind` members carry explicit numeric values (unchanged values, matching the
  rest of the public enums).
- `WhitespaceConverter` tracks whether a conversion changed anything instead of building the
  converted text and comparing it: a no-op returns the original instance without the extra
  allocation and scan. `TextIncrementalEditCalculator` uses the vectorized
  `MemoryExtensions.CommonPrefixLength` for its prefix scan.
- Documentation: Core README corrections (sidecar failure policy, query-only finders, `TextLineMap`
  wording, `##` section structure, voice), default-instance caveats on `TextLineCommentEdit` and
  `BlockCommentSyntax`, the conflict-diagnostic order stated inline on `TextEditPreparationResult`
  (the old cref pointed at an internal type), a compilable `GetCodeRange` example, parameter-clamp
  wording on `TextLineMap`, the line-comment planner's space/tab rule cross-referenced from
  `CommentOperations.IsEmptyOrLineComment`, dense remarks split into paragraphs, and stock phrases
  reduced; a governance note on `StringLiteralStyle` records the documented-approximation rule for
  new members.
- Tests: +6 (comment-scanner invariant sweeps for masking length/terminators, no residual comment,
  and escape parity; `RegexCache` eviction order; auto-closing preset pins; `OvertypeMode.Never`; a
  strengthened randomized diff oracle that validates the changed set against a common subsequence).

**Fresh review resolution (2026-09-19, second fresh review)**

*Breaking changes*

- `CommentOperations.IsEmptyOrLineComment` was renamed to `IsBlankOrLineComment`: the predicate
  also covers blank (whitespace-only) lines, which the old name hid.
- `TextIncrementalChange` was renamed to `TextIncrementalEdit` (type, file and call sites): the
  value is an applied edit, not a change event.
- `TextLineCommentPlanner.TryCreateEdit` and `ITextLineCommentService.TryCreateEdit` gained a
  required `insertSpaceAfterDelimiter` parameter, so the delimiter-space convention is the host's
  decision instead of a baked-in rule.
- `LatestRequestCoordinator.RunAsync` moved `cancellationToken` to the last position (after the
  new `continueOnCapturedContext` flag), matching the standard async parameter order.

*Additions*

- `IVersionedTextEditTarget` (implemented by `AvalonEditTextEditTarget.TryApply`) lets a versioned
  host attempt an atomic apply against the document version captured with the edit.
- `PreparedTextEdits.MapOffsets` maps a sequence of offsets through the prepared batch in one pass;
  `SidecarLineFile.TryRestore` reports failure instead of throwing, and the auto-closing policy is
  host-tunable through `TextAutoClosingOptions.AutoCloseUnconditionally` and the per-pair
  `CheckUnmatchedClosingText` gate.

*Fixes*

- `RegexCache` performs the post-add trim under a dedicated eviction lock, so a concurrent lookup
  can no longer observe the dictionary mid-eviction; a concurrency regression suite pins the
  invariant.
- `FindReplaceText.TryGetSearchRegex` rejects `RegexOptions.RightToLeft` with an
  `ArgumentException` (the engine does not support right-to-left matching), and `StringTextSnapshot`
  builds its line table lazily on first use instead of in the constructor.
- Duplicate-insertion conflicts were removed from the edit-conflict layer: identical insertions at
  the same offset no longer surface as a separate conflict kind, and the conflict diagnostics,
  messages and detector branches shrank accordingly.

*Tests*

- The Core suite grew to 905 tests: `RegexCache` eviction/concurrency invariants, right-to-left
  rejection, lazy snapshot line tables, `MapOffsets`, `SidecarLineFile` failure paths, the
  auto-closing escape/selection gates, and consolidated enum pins; two superseded pin-test files
  were folded into the covering suites.

### Nickelony.IDEKit.Workspace

**Breaking changes**

- `WorkspacePathComparison` was removed; use
  `Nickelony.IDEKit.Core.Pathing.LocalPathComparisonPolicy` (same values and behavior). The document
  store, view manager, file reload coordinator, and local file system constructors, and
  `IWorkspaceDocumentReader.PathComparison`, now carry the Core type.
- The file-system seam moved to the `Nickelony.IDEKit.Workspace.Documents.FileSystem`
  namespace: `IWorkspaceFileSystem`, `LocalWorkspaceFileSystem`, `WorkspaceFileReadResult`,
  `WorkspaceFileReplacementResult`/`WorkspaceFileReplacementStatus`,
  `WorkspaceFileMoveResult`/`WorkspaceFileMoveStatus`,
  `WorkspaceFileDeleteResult`/`WorkspaceFileDeleteStatus`, and `WorkspaceTemporaryFile`.
- `FileReloadCoordinator` and `FileReloadCallbacks` moved to the
  `Nickelony.IDEKit.Workspace.Documents.Reloading` namespace.

**Fresh review resolution (2026-09-19, `__180923_` Workspace review)**

*Breaking changes*

- The directory request model lost its `Documents` batch: a directory rename or delete applies to
  every descendant the store owns, so `WorkspaceDocumentDirectoryRenameRequest` and
  `WorkspaceDocumentDirectoryDeleteRequest` carry only the paths.
- `FileReloadCallbacks<TPromptResult>` callbacks are asynchronous
  (`Func<..., CancellationToken, Task<...>>`) and `FileReloadCoordinator.ProcessQueuedFilesAsync`
  replaces the synchronous pass, so a host prompt no longer has to block its thread.
- `WorkspaceEditApplicationResult` gained `WorkspaceEditApplicationStatus.Canceled` (with the
  `Canceled` factory) for an application canceled before any document change and without a recorded
  failure. The applier also no longer adopts versions from rejected store results: a document whose
  replacement was not applied makes every later target for that document `NotApplied` instead of
  authorizing a write against unobserved state.
- A clean-reload `Unchanged` outcome is defined by the tracked stamp: a caller expectation that
  matches the disk while the tracked stamp does not adopts the disk content instead of reporting
  `Unchanged`.

*Fixes*

- Directory rename and delete re-scan descendants opened while the file-system call was in flight,
  so a newcomer is rebased (rename) or removed from tracking (delete) with its siblings.
- `LocalWorkspaceFileSystem` classifies a failed replacement from the observed path state instead of
  reporting `ReplacementStateUnknown` for known outcomes, and a directory destination reports a
  failure.
- Cancellation and failure mappings cover the documented status families (reload, rename, delete,
  save-as, commit); the `Windows-1252`/BOM encodability invariant is shared by the store and the
  codec, the reload queue de-duplicates paths, and disposal releases per-document gates and
  destination reservations.

**Fresh review resolution (2026-09-19, fresh Workspace review)**

*Breaking changes*

- `WorkspaceDocumentReloadRequest` no longer carries `ExpectedOnDiskStamp`: the reload outcome is
  defined by the tracked stamp (the removed value was never read), so a reload request carries
  only the document identity.
- `IWorkspaceFileSystem.ReplaceAsync` was renamed to `ReplaceFileAsync`, so the file-system seam no
  longer shares the `Replace` verb with the logical `IWorkspaceDocumentStore.Replace`.

*Changes*

- Save-as onto the document's own file (including a case variant under a case-insensitive policy)
  now saves in place and reports `SavedAs` instead of `DestinationInUse`; a replacement conflict on
  that path reports `ExternalFileConflict`, matching the source-file contract.
- A directory rename prepares the rebased identity set before it touches tracking, so a destination
  occupied by a document opened during the move cannot fail the update midway; the moved instance
  wins the id collision, and the delete path documents its residual open ordering.
- Documentation and terminology pass: corrected `Replace` gate wording, four application outcomes
  (including `Canceled`), `ReplacementStateUnknown`/`Canceled` descriptions, reload's tracked-stamp
  rule, and the `file system`/`target ids`/`per-document disk gate`/`external file conflict`
  vocabulary.

*Tests*

- New coverage: `IsDirectory` open and directory read, save-as in place and its
  `ReplacementStateUnknown` branches, rename source-stamp conflict, identity-rejection matrices for
  rename/save-as/delete, disposal during a registered commit, directory-delete stamp conflict and
  cancellation, reload cancellation and read failure, reload-coordinator guards and mid-pass
  cancellation, codec undefined encoding kinds, edit-applier cancellation after a no-change target,
  change-set defensive copy, sorted directory snapshots, local file-system directory read, move
  conflicts and temporary-file round trip, and the directory-rebase collision regression
  (190 to 218 tests).

**Fresh review resolution (2026-09-19, fresh Workspace review follow-up)**

*Breaking changes*

- `FileReloadCallbacks.PromptReload` now receives the `WorkspaceDocumentReloadResult` instead of
  the queued path string, so a host prompt can present the conflict and the current snapshot
  without re-reading document state (the conflict result carries both).

*Changes*

- A clean reload that cannot decode the on-disk bytes now reports the `InvalidEncoding` failure
  code like the open and conflict-resolution paths, instead of the generic `ReadFailed` code.
- Added the `AccessDenied` failure code; `LocalWorkspaceFileSystem` maps permission failures on
  replace, move, and delete operations to it instead of the generic write, move, or delete codes.
- Added `WorkspaceFileSystemDecorator`, a forwarding base class that lets a host override single
  `IWorkspaceFileSystem` members (for example to route deletes through a trash folder).
- Rename and save-as validate the request identity before the destination path, so the documented
  `ArgumentException` holds even when the destination is also unnormalizable.

*Documentation and naming*

- Corrected the stale snapshot-cost remark (`IsDirty` is cached), the delete and rename snapshot
  parameter docs, the `PartiallyApplied`/`NotApplied` cancellation descriptions, the
  conflict-resolution missing-file scope, the `InvalidEncoding` scope, and the workspace-edit
  factory overclaim; added `WorkspaceEditTargetPreparation` to the README slice list.
- The package description no longer claims a pluggable text codec: the stateless
  `WorkspaceTextCodec` provides the encoding vocabulary, and only the file system is pluggable.

*Tests*

- New coverage: expected-stamp forwarding for rename and delete, replacement interleaving with an
  in-flight rename, zero-descendant directory rename and delete, trailing-separator snapshot
  lookup, reload format adoption and decode classification, dirty reload bypassing a held gate,
  disposal with a pending open and a pending delete, second-path save-as replacement conflict,
  coordinator conflict without a resolver, replacement classifier branches and `AccessDenied`,
  byte-order-mark round trip, and the file-system decorator (218 to 236 tests; the duplicated
  directory-delete test was folded into its superset).

**Fresh review resolution (2026-09-19, third fresh Workspace review)**

*Breaking changes*

- `WorkspaceEditTargetPreparation` carries the file format before and after the transformation
  (`BeforeFileFormat` / `AfterFileFormat`; the preparation's `FileFormat` member was renamed), so
  a format-only prepared change is applied instead of being skipped as a no-op; the applier's
  no-op decision requires both content and format to match. `WorkspaceDocumentChange` records both
  formats when the producing path tracks them and leaves them `null` otherwise (for example the
  editor-side application path).
- `WorkspaceEditApplier` and the `WorkspaceEditApplicationResult` factories take the shared
  `LocalPathComparisonPolicy` instead of a `StringComparer`; the default follows the operating
  system, and supplying the store's `PathComparison` keeps de-duplication from drifting. The
  forwarding-only `WorkspaceEditApplicationResult.HasChanges` member was removed (use
  `ChangeSet.HasChanges`).
- `WorkspaceDocumentConflictResolutionResult` places `Choice` after the result family's positional
  tail (identity, snapshot, stamp, failure) and requires all members.
- `WorkspaceFileReplacementStatus` gained `Canceled` and `DestinationExists`; replacement
  cancellation is reported in-band like move and delete. `WorkspaceFileMoveStatus` gained
  `MoveStateUnknown`, mirrored by new `WorkspaceDocumentRenameStatus` /
  `WorkspaceDocumentDirectoryRenameStatus` members; a case-only rename whose rollback fails reports
  it instead of a generic move failure.
- `WorkspaceDocumentKey` is a `readonly record struct`, and `GetSnapshotsUnderDirectory` accepts
  `string?`.

*Changes*

- Dirty reloads compare the captured stamp with the tracked stamp: an unchanged disk reports
  `Unchanged` instead of raising a conflict prompt for a spurious watcher event.
- Reload and `UseDisk` resolution reject a path that is now a directory (`ReadFailed` with the new
  `IsDirectory` failure code), and a missing file keeps the document's current format.
- Windows-1252 encoding rejects the five undefined code points (`U+0081`/`8D`/`8F`/`90`/`9D`) that
  the decoder rejects as bytes, closing the round-trip hole; `EnsureEncodable` validates the
  encoding value itself at `Replace`, `OpenAsync`, and the edit applier.
- Pre-canceled tokens are enforced before any file-system mutation in every store member; open
  waiters re-evaluate iteratively instead of recursively; a stale dirty reload whose instance was
  renamed reports `StaleDocumentInstance` via the document key instead of `DocumentNotFound`.
- Normalized document ids trim trailing directory separators, so `dir` and `dir\` are one identity.
- `FileReloadCoordinator` skips the prompt when no resolver consumes the answer; save-as reports
  the new `DestinationExists` and `Canceled` replacement outcomes.
- New failure codes `IsDirectory`, `MoveStateUnknown`, `TargetSkipped` (same-document cascade), and
  `TargetOutcomeUnknown` (unrecognized replacement status) replace the overloaded
  `TargetNotChanged` uses; a delegate-thrown `OperationCanceledException` now propagates from
  `Apply` instead of becoming a per-target failure.
- The replacement hash format (SHA-256, uppercase hexadecimal), the cancellation contract, the
  directory-read requirement, and the disabled-comparison default are documented on the contracts.

*Documentation*

- Corrected the success-path `ObservedOnDiskStamp` docs, the commit `ExternalFileConflict` and
  `TargetNotChanged` descriptions, the codec/file-system split, the open wait/reload wording, the
  `IsRunning` and `PromptReload` docs, the change-set "persisting or rolling back" wording, the
  `ExistsOnDisk`/`ActualVersion`/`Content` remarks, the dirty-reload and vacated-path comments, and
  the README quick start, slice description, and trash-decorator example.

*Tests*

- New coverage: format-only prepared changes and change records, delegate cancellation
  propagation, empty-id and unencodable-format validation, unrecognized-outcome and cascade codes,
  commit not-found/stale-instance and replacement-outcome mapping, replace not-found and
  format-only dirty discard, reload/`UseDisk` directory rejection, missing-file conflict
  resolution and format retention, dirty-reload unchanged-stamp outcome, trailing-separator
  identity and directory no-op, save-as failure baseline and outcome mapping,
  dispose-during-save-as, real-file-system directory rename, codec encode guards and round trip,
  and coordinator prompt forwarding without a resolver (236 to 261 tests).

**Fresh review resolution (2026-09-19, fourth fresh Workspace review)**

*Breaking changes*

- The comparison parameter on the `WorkspaceEditApplicationResult` factories is named
  `pathComparison` (was `targetIdComparer`), matching every other policy-taking member in the
  package. Only named-argument callers are affected.

*Changes*

- A directory rename or delete that stops on a changed descendant now names that descendant in
  the failure message, so a host can surface the conflicting file without re-validating every
  tracked descendant itself.
- Documentation accuracy: the commit result's observed-stamp contract now describes the actual
  re-capture behavior (a completed replacement whose stamp cannot be established reports
  `ReplacementStateUnknown`), `WorkspaceEditApplier.Apply` documents the
  `ArgumentOutOfRangeException` for undefined encodings, and `LocalWorkspaceFileSystem` lists
  `DeleteTemporaryAsync` in its `UnauthorizedAccessException` surface.
- The README's stamp-cost guidance requires one stamp computation across every stamp-producing
  member (`ReadAsync`, `CaptureStampAsync`, and the `ReplaceFileAsync` result); the previous
  single-member advice could mix stamp vocabularies and produce spurious external conflicts. The
  slice list names `WorkspaceFileSystemDecorator`, the run-on Editing slice text is bulleted, and
  the quick start uses consumer-neutral paths.
- Contract files regrouped without API changes: the open vocabulary
  (`WorkspaceDocumentOpenStatus`, `WorkspaceDocumentOpenOptions`, `WorkspaceDocumentOpenResult`)
  moved to `Documents/WorkspaceDocumentOpenContracts.cs`, and `WorkspaceOperationFailure` moved to
  its own file. Wording unification: "logical" replaces "in-memory", "source stamp" replaces
  "source-file stamp", `<see langword="null"/>` is used in prose, and the reload coordinator and
  edit applier docs use host-neutral phrasing.

*Tests*

- The rendezvous tests that must not block on a held gate carry a method `[Timeout]`, so a
  regression fails with evidence instead of stalling the suite; the disposal quartet asserts the
  untouched state in addition to the canceled status, and two commit mapping tests assert the
  un-advanced version and baseline.
- New coverage: file-level `OperationInProgress` for rename/save-as/delete, initial stale-version
  rejection for reload and conflict resolution, cancellation during the `UseLogical` write, the
  canceled result matrix, live-store null/blank directory snapshots, the applier constructor
  guard, the decorator's default `DeleteAsync` forwarding, and a missing-path stamp capture
  (261 to 272 tests; `Workspace.Views.Tests` stays at 57).

### Nickelony.IDEKit.Workspace.Views

**Fresh review fixes (2026-09-19, `__190919_`)**

*Breaking changes*

- `WorkspaceDocumentManagerRenameResult.StoreResult` and
  `WorkspaceDocumentManagerSaveAsResult.StoreResult` (and their `Status` accessors) are no longer
  nullable: both operations never block on attached views, so the wrapped store result is always
  populated and hosts can drop meaningless null checks.

*Fixes*

- The stop path's view-dispatch await uses `ConfigureAwait(false)`, so a host that blocks on
  `StopAsync()` from a synchronization-context thread can no longer deadlock teardown.
- `WorkspaceDocumentManagerOpenStatus.Opened` documents that a view is only attached when one was
  supplied.

*Tests*

- Test namespaces match the project (`Nickelony.IDEKit.Workspace.Views.Tests`), and
  `BlockingDeleteFileSystem` now derives from the public `WorkspaceFileSystemDecorator` instead of
  hand-forwarding every member.

### Nickelony.IDEKit.AvalonEdit

**Fresh review #4 resolution (2026-09-19, `__190919_` AvalonEdit review)**

*Breaking changes*

- `IBookmarkStore.Load` became `TryLoad(string, out IReadOnlyList<int>)`: `false` reports a load
  that could not be performed (a blank or unusable path, or a failed read) instead of conflating it
  with "nothing was stored". `BookmarkStoreExtensions.RestoreBookmarks` returns `bool` and leaves
  the coordinator untouched (and unbound) when the load fails, so a later `SaveBookmarks` fails
  instead of persisting an empty set that would delete the stored bookmarks.
- `TextEditorMouseNavigation` was renamed to `TextEditorNavigationOperations`, matching the
  `{Receiver}NavigationOperations` family of `TextAreaNavigationOperations`.
- `ITextLineCommentService.ApplyEdit`/`TextLineCommentService.ApplyEdit` take
  `insertSpaceAfterDelimiter` before `editTarget`, so the edit target is the last parameter like
  every other editing helper.
- No-document handling follows one stated policy: `Try*` members and event-apply methods report
  failure (`TryReplaceFirstMatchingLine`, `TryMoveCaretToPoint`, `HandleTextEntering`,
  `HandleBackspace` return their no-op values), mutating command members throw
  `InvalidOperationException` (`TextEditorEditOperations`, `TextEditorLineOperations`,
  `FormatDocument`, `ApplyEdit`), read members report absence (`GetMarkedLineNumbers`,
  `GetNextBookmarkLine`, `GetPreviousBookmarkLine`), and the coordinator's mutations throw
  `InvalidOperationException` (was `ArgumentNullException`) when the provider returns null.

*Changes*

- Fixed: `TextAutoClosingService` tracked its inserted closing text with a `BeforeInsertion` anchor,
  which stays behind text inserted at its position — typing inside the pair left the anchor on the
  typed text, so with the default `Auto` provenance the closing text was no longer recognized and
  the closer was inserted a second time (`(x))`). The anchor now uses the default movement and
  follows the closing text; an undo that removes the inserted closing text ends its tracking
  (documented).
- `BookmarkCoordinator` anchors use the default movement as well, so a bookmark follows the marked
  line's content when a line break is inserted at the line start (documented on the type).
- `ChangeMarkerMargin` is unsealed with `DrawMarker` as the documented customization point,
  matching `BookmarkMargin`.
- `LineStatusIconMarginBase.IconBrush`/`IconGeometry` document the validation-callback
  `ArgumentException`; `LineStatusMarginBase`'s empty explicit constructor is gone.
- Documentation: `IBookmarkStore.TryLoad`, `BookmarkSidecarStore` (sorted and de-duplicated
  persistence, synchronous-I/O guidance), `UnsavedChangesTracker.Dispose` no longer claims the
  instance "must not be used afterwards" (post-dispose reads are documented), `TextEditorEditOperations`
  no longer dangles "when omitted", plus the `DiagnosticsRenderer` comma splice, `TextRunStyleApplier`
  noun, `AvalonEditTextEditTarget.Version` swap wording, `TextAutoClosingService` summary and
  cross-quote remark, `BookmarkCoordinator` read-wording, `BookmarkMargin`/`ChangeMarkerMargin`
  brush and geometry exception docs, and the README fixes (bookmark sidecar sentence, license
  placement, clamp sentence, provenance antecedent, end-of-text consistency).

*Tests*

- New coverage: the tracked-pair lifecycle (typed-inside-then-closer skip, typed-then-deleted
  Backspace pair deletion, typed-then-undone skip, undo/redo ending the tracking); the `TryLoad`
  contract matrix (missing sidecar, empty file and ignored entries, directory at the sidecar path,
  unwritable path, failed-load restore leaving the coordinator unbound); bookmark content-follow on
  a line-break insert; single-bookmark wrap and between-bookmarks navigation; full-text replacement
  states; factory-failure retry in `DocumentVersionCache`; `TextRangeNormalizer` end-of-document
  edge ranges; unhosted and no-document navigation mappings, replacing the process-global
  `PointerPositionResolver` seam with an internal resolver overload (452 tests).

**Fresh review #3 resolution (2026-09-19, `__190919_` AvalonEdit review)**

*Changes*

- `RegexHighlightingDefinition` arms its recursion guard around the cache-version callback as well,
  so a callback that reads its own definition throws the documented `InvalidOperationException`
  instead of re-invoking itself (the guard was previously armed only while `BuildRules`/`BuildSpans`
  ran, and the documented throw for the callback path was unreachable).
- README: the diagnostics snippet no longer references a nonexistent `editor.Diagnostics` member
  (the factory receives the host's diagnostics provider); `Getting started` is a top-level heading;
  the bookmark and change-marker sections no longer repeat the getting-started wiring verbatim; the
  editing bullet is split and no longer says "removes it" ambiguously.
- Doc wording: `TextAutoClosingService` summary now says the pairs come from `TextAutoClosingOptions`
  and its leading-token-skip remark is split; `BookmarkCoordinator` stale-anchor and `Changed`
  remarks were rewritten; `IBookmarkSource` toggle wording; `DiagnosticsRenderer.Draw` no longer
  repeats the type guidance; `LineStatusMarginBase.MarginWidth` duplication removed and
  `GetEffectiveMarginWidth` gained a `<returns>`; the margin subclasses no longer repeat the base
  scaling sentence; `TextAreaNavigationOperations` no longer links the private snapping helper;
  `TextRunStyleApplier` noun fixed; the `RegexHighlightingDefinition` type remarks were split.

*Tests*

- New coverage: `AvalonEditTextEditTarget.TryApply` (matching stamp, stale stamp, no-op batch) -
  the member previously had none; `insertSpaceAfterDelimiter: false` for create and apply, including
  the uncomment round-trip; the cache-version-callback and own-name `GetNamedRuleSet` recursion
  guards; the selection-wrap single-undo unit; the no-document `TextArea` throws of the navigation
  helpers and the negative-offset clamp; `TryGetOffsetFromPoint` without a document; the tracker
  invalidation across undo; bookmark anchors across edit and undo; `TryMoveCaretToMousePosition`
  success path and resolver-reported no-pointer through the new internal `PointerPositionResolver`
  seam (the recorded success-path debt is resolved).
- Hygiene: indentation fixtures neutralized (`then`/`end` became `open`/`close`), stale "cached
  unit" test name and comment fixed, `ClosingToken`/`ClosingString` renamed to closing text,
  "cursor" became "pointer", `script.txt` placeholders became `document.txt`, the
  `FixedLineStatusSource` member reference and `EmptyMarkerSource` rationale were corrected, and the
  misleading test names were replaced (434 tests).

**Fresh review #2 resolution (2026-09-19, `__190919_` AvalonEdit review)**

*Breaking changes*

- `ITextAutoClosingService.HandleTextEntering` and `HandleBackspace` accept a trailing optional
  `ITextEditTarget` (default `null`), so auto-closing joins the other editing helpers in routing
  through a host-owned target; the typed pair (or the pair deletion) travels as one batch, so a
  single undo still removes or restores it.

*Additions*

- `BookmarkMargin` is unsealed and delegates its toggle decision to the new protected virtual
  `OnBookmarkToggleRequested(int)`: the default toggles through the source, and a derived margin can
  add modifier keys, confirmation, or a context-menu route (returning `false` leaves the click
  unhandled). Its icon drawing is now documented as overridable through `DrawMarker` as well.
- `BookmarkCoordinator.IsBoundToCurrentDocument` reports the state in which a save persists rather
  than deletes the stored bookmarks; `BookmarkStoreExtensions.SaveBookmarks` documents the guard, so
  a host that saves from the coordinator's `Changed` event (the README's wiring) can query it
  instead of catching the failure.

*Changes*

- `TextDocumentSnapshot` documents that its captured file name is the host-set
  `TextDocument.FileName` value, which this package never writes.
- `DiagnosticsRenderer` documents the severity-to-shape mapping as its fixed convention.
- README: the package-organization sentence, the auto-closing edit-target seam, and the guarded
  bookmark-save snippet were brought in line with the new members; the highlighting remark's
  "command catalog" example became "rule catalog".

*Tests*

- New coverage: the auto-closing pair insert, selection wrap, and Backspace pair deletion through
  contract and recording edit targets (one undo unit through the target; a skip action does not
  touch the target), the bookmark toggle hook override (a veto leaves the click unhandled),
  `IsBoundToCurrentDocument` across bind, swap, restore, and null-provider states, the
  negative-argument guards of `InsertText`/`ReplaceText`, same-version concurrent rule-set
  rebuilds not raising `RuleSetChanged`, and the renderer's empty-visible-range guard (416 tests).

**Fresh review resolution (2026-09-19, `__190919_` AvalonEdit review)**

*Breaking changes*

- `TextAreaNavigationOperations.CreateCaretLocationAt` was renamed to `CreateCaretLocation` so the
  point-location creator pairs with `CreateRangeLocation`.
- `LineStatusMarginBase.SetSourceChangeHandler(IChangeNotificationSource, EventHandler)` was replaced
  by `SetNotifyingSource(IChangeNotificationSource)`: the base invalidates itself when the source
  raises `Changed`, so a subclass registers the source in one call instead of passing a one-line
  handler. The second-registration guard and the subscribe-on-connect behavior are unchanged; a
  subclass that needs a custom reaction can subscribe to the source's event itself.
- `LineStatusMarginBase.DrawMarker` is `protected internal` instead of `protected`, so the test
  assembly calls it directly and the reflection helper is gone; overrides outside this assembly stay
  `protected override`.

*Changes*

- `BookmarkCoordinator.Clear` now binds the coordinator to the document the provider currently
  returns before clearing, so the clear is persistable for the current document instead of leaving
  the coordinator unbound and making the next save throw. Like every other bookmark mutation, it
  throws `ArgumentNullException` when the provider returns null.
- `RegexHighlightingDefinition` publishes rule sets atomically against the snapshot that is current
  at publication time, so a publication that replaces a snapshot of a different version raises
  `RuleSetChanged` even when it raced another build; the first build of a definition still does not
  raise the event.
- The internal `TextSegmentFactory` became `TextRangeNormalizer` and lost its test-only `TryCreate`
  member; `DiagnosticsRenderer` normalizes through `TryNormalizeRange` before materializing anything.
- Documentation corrections: the `AvalonEditTextEditTarget` class contract now states that an
  all-no-op batch applies nothing, the first-matching-line caret sentence no longer contradicts
  itself, the `PolicyIndentationStrategy` constructor remark was rewritten, the rule-set re-entrancy
  exception texts were rewritten, the pen and brush remarks were deduplicated, and the
  `TextEditorEditOperations` contract list uses the repository's `;`/`.` punctuation.
- README corrections: the diagnostics bullet no longer calls `TextDiagnosticSeverity` "LSP-aligned"
  (its values mirror the LSP numbering, but protocol severities are mapped explicitly), the
  duplicated dependency paragraph and auto-closing snippet were merged or removed, and the bookmark
  lifecycle notes that a clear is bound to the current document.

*Tests*

- New coverage: `Clear` binding/persistence and the no-bookmark notification, duplicate `Restore`
  entries, edit operations as single undo steps, edits not raising the coordinator's event, the
  word-probe quirks (digit-first run, trailing-terminator probe), the rules-only `Create` overload,
  reading the rule set from a `RuleSetChanged` handler, unusable sidecar extension forms, null
  brush/geometry rejection through the DP validators, invalidation coalescing, Ctrl/Alt Backspace
  refusal (fake keyboard device), and the AvalonEdit version-replacement behavior
  `DocumentVersionCache` relies on.
- Test hygiene: misleading names fixed (the provider-null reads are split into the render-facing and
  command-driven tests), temp-directory scaffolding and visible-line checks extracted to shared
  support (`TempDirectoryScope`, `DocumentTestHelpers`), one Backspace key-args helper replaces nine
  inline constructions, and the Lua-flavored fixtures were made language-neutral.

**Navigation narrowing to `TextArea` (2026-09-17, PKG-1 residual)**

*Breaking changes*

- `TextEditorNavigationOperations` is now `TextAreaNavigationOperations`: its members are
  extensions on AvalonEdit's `TextArea` (`GetWordFromOffset`, `CreateCaretLocationAt`,
  `CreateRangeLocation`, `ApplyLocation`); a host with a `TextEditor`
  passes `editor.TextArea`.
- The scroll step delegates to the owning editor when the text area belongs to one
  (`TextEditor.ScrollToLine` carries the centering and viewing-margin rules, so editor-hosted
  locations scroll exactly as before); a standalone text area falls back to the text view's own
  reveal primitive, which scrolls the minimum distance and never pans horizontally.
- The missing-document `InvalidOperationException` message is now "The text area has no document
  assigned."

*Changes*

- The editing, line, first-matching-line, and mouse-navigation helpers stay `TextEditor`-based:
  navigation operates on text-area state, the rest remain editor widget operations.

**Breaking changes**

- The view-state slice was renamed: `TextEditorViewStateCoordinator` and `ZoomOptions` moved
  from the `Nickelony.IDEKit.AvalonEdit.Editors` namespace to
  `Nickelony.IDEKit.AvalonEdit.ViewState`.

**Backlog execution (2026-09-16, HL-1 + STYLE-1)**

*Breaking changes*

- New `Nickelony.IDEKit.AvalonEdit.Rendering.TextRunStyle`, the shared `ITextRunStyle`
  implementation (foreground, bold, italic, text decorations, the `Empty` default value, and
  `CreateTypeface`). It replaces the per-package style records deleted from
  `Nickelony.IDEKit.AvalonEdit.LanguageFeatures` (`SemanticTokenStyle`) and
  `Nickelony.IDEKit.AvalonEdit.TextMate` (`TextMateHighlightingStyle`); see those packages.

*Changes*

- `HasFormatting` treats a non-null but empty `TextDecorationCollection` as no formatting, and
  `TextRunStyleApplier` skips empty decoration collections instead of unioning them into the
  element (STYLE-1).

**Fresh review resolution (2026-09-15, `__150917_` AvalonEdit review)**

*Breaking changes*

- `EditorPosition` was deleted; `TextEditorNavigationOperations.CreateDefinitionLocation` and
  `CreateRangeLocation` take AvalonEdit's `TextLocation` (one-based line and column), so the
  framework's own coordinate type replaces the duplicate one.
- `TextAutoClosingResult` now carries the applied action: `Action` (a `TextAutoClosingAction`)
  plus `WrappedSelection`, instead of flattening the action's kind and closing text; `None` is
  still the default value.
- `IBookmarkStore.Restore` was renamed to `Load` (conventional `Save`/`Load` pairing);
  `BookmarkStoreExtensions` is unchanged.
- Selection wrapping in `TextAutoClosingService.HandleTextEntering` applies only when the
  editor's read-only section provider allows replacing the whole selection; a partially or fully
  read-only selection is left to normal text input (the result is `None` and the event is not
  handled), so a wrap can no longer adjust the selection or report a wrap over an unchanged
  document.

*Fixes*

- `UnsavedChangesTracker.GetMarkedLines` no longer throws after disposal: the render-path read
  returns an empty list (mutating members still throw `ObjectDisposedException`). A disposed
  tracker with an attached margin no longer crashes the next repaint.
- `DiagnosticsRenderer.IntersectsVisibleRange` guards an empty visual-line list instead of
  indexing it.

*Changes*

- `TextAutoClosingService` internals were consolidated without public renames: the three text
  matchers became one `MatchesAt` helper, the pair evaluators share a private `ResolutionContext`,
  the opening-token resolution is its own helper, and one `IsRangeEditable` guard covers both
  Backspace pair deletion and selection wrapping. The backslash-escape rule and the leading-token
  close skip are now documented as intentional conventions on the public members.
- `BrushHelpers.TryParseColor` is the single color-string parse; `RegexHighlightingStyle` uses it
  instead of its private copy (`CreateFrozenBrush(string)` reports `ArgumentException` for every
  invalid value, including values that previously surfaced `NotSupportedException`).
- `PolicyIndentationStrategy.IndentLines` reads the indentation unit once per batch instead of
  once per line, and `UnsavedChangesTracker` counts line breaks without splitting the changed
  text.
- Documentation: the default-implementation boilerplate is single-sourced on the service
  interfaces, the unclear `GetMarkedLines`/`NavigationLocation`/`TextAutoClosingOptions` sentences
  were rewritten, and `ILineStatusSource`, `TextDocumentSnapshot`, and `TextRunStyleApplier` now
  state their cost and caching expectations. The README dropped the duplicated host-integration
  example.
- Tests: read-only selection wrapping (whole, partial, and editable-outside cases), multi-character
  Backspace pair deletion, CRLF-interior navigation snapping, edit-target document swapping,
  margin hit testing, custom identifier policies, a disposed-tracker render pass, and exact
  typeface/pen assertions replacing environment-coupled negatives; two insensitive presets
  negatives now fail when the preset is added to the defaults.

*Follow-up round (same day, second session)*

Breaking changes:

- `ITextAutoClosingService.TryGetAction` was renamed to `TryResolveAction`: the member resolves an
  action from document state and options, and the vocabulary now matches the result type and the
  service internals.
- `ITextEditorFormattingService`/`TextEditorFormattingService` were renamed to
  `ITextDocumentFormattingService`/`TextDocumentFormattingService`, aligning with the LSP
  "document formatting" feature name and the feature-scoped naming of the sibling services.
- The `ViewState` slice was removed: `TextEditorViewStateCoordinator` and `ZoomOptions` moved back
  to Tomb Editor as host-owned helpers. Both halves are host adapter concerns (status-bar relay
  and host zoom preference state), no library component consumed them, and AvalonEdit already
  supplies the underlying events and the `FontSize` knob.

Fixes:

- `TextDocumentSnapshot.GetText` and `GetCharAt` enforce the Core `ITextSnapshot` argument contract;
  a negative length previously surfaced AvalonEdit's `OverflowException` instead of
  `ArgumentOutOfRangeException`.

Changes:

- The `TextAutoClosingService` class remarks no longer restate the interface contract; they carry
  only the implementation deltas (pair evaluation order, the intentional leading-token extension,
  and the backslash-escape rule).

**Fresh review resolution (2026-09-16, `__150922_` AvalonEdit review)**

*Breaking changes*

- `TextAutoClosingActionKind.InsertClosingElement`/`SkipExistingClosingElement` were renamed to
  `InsertClosingText`/`SkipExistingClosingText`, matching the `ClosingText` payload term; the
  auto-closing documentation is locked to "opening token"/"closing text", and the line-status
  margins now uniformly say "marker".
- `ILineStatusSource.GetMarkedLines` was replaced by `GetMarkedLineNumbers()`, returning one-based
  line numbers instead of `DocumentLine` handles: margins only ever consumed `.LineNumber`, and
  the new contract removes expiring document handles and deleted-line exceptions from the public
  surface. `UnsavedChangesTracker` and `BookmarkCoordinator` implement the new member
  (`BookmarkCoordinator.GetMarkedLineNumbers`). The margin `DrawMarker` callback now receives the
  visual line box's top instead of its first text line's top, so markers stay inside the box.
- `TextAutoClosingPair` gained an explicit `Kind` (`TextAutoClosingPairKind.Bracket`/`Quote`) and
  a validating constructor. Quote behavior (skip existing closing text, no doubling,
  word-character suppression, escape exception) is no longer inferred from a closing text that
  happens to start with its opening token; quote pairs set `Kind = Quote`.
- Auto-closing gate defaults changed to the fixed per-kind character sets popular editors use
  (bracket-like pairs accept `"'`;:.,=}])>` before the caret, quote-like pairs omit the quote
  characters); `AutoCloseBefore` replaces the kind's set instead of narrowing a non-word-character
  rule.
- Skipping existing closing text and Backspace pair deletion default to
  `TextAutoClosingProvenance.Auto`: they apply only to closing text the service inserted and
  tracks per document (`OvertypeMode`/`DeleteMode` select `Always`/`Never`). Content loaded or
  typed through other paths is no longer overtyped or removed as a pair by default.
- `TextAutoClosingOptions` gained `EscapeCharacter` (default `\`; `null` disables escape
  handling) and `IdentifierPolicy` (default `IdentifierCharacterPolicy.Default`) for the
  word-character suppression rule.
- `NavigationLocation` equality now compares the navigation identity only (path ordinally, caret
  offset, selection start and length); `PreferredLine` is documented scroll-restoration state
  excluded from equality, so `Equals` and `IsEquivalentTo` are consistent. Hosts with
  case-insensitive document identities compare with `IsEquivalentTo`.
- `TextEditorNavigationOperations.CreateDefinitionLocation` was renamed to `CreateCaretLocationAt`
  (it creates a caret location; nothing definition-specific happens).
- `AvalonEditTextEditTarget.Version` is a monotonic document change stamp (advanced by the
  document's `Changed` event and by document swaps) instead of an edit-application counter, so
  the documented stale-batch workflow detects direct editor edits as well; no-op operations are
  skipped instead of being replayed against the document.
- `RegexHighlightingDefinition` gained `BuildSpans()` and delegate `Create` factories;
  `RegexHighlightingSpan` expresses delimiter-based spans (block comments, long strings) that
  stay highlighted across lines, closing the multiline gap of the per-line rule engine.
- `TextRunStyleApplier` gained generic non-boxing overloads; the interface overloads remain for
  interface-typed callers.

*Fixes*

- `UnsavedChangesTracker` no longer corrupts its line view or throws on edits that split or
  complete a CRLF pair (deleting the CR or the LF of a pair, inserting a CR before an LF, or a
  replacement starting mid-pair): the replaced window is located by line structure instead of
  line-break arithmetic on the changed fragments.
- `TextAutoClosingService.HandleBackspace` declines when the editor has an active selection (the
  editor's own Backspace deletes the selection), and reads modifiers from the event's keyboard
  device instead of the global device state.
- `BookmarkStoreExtensions.SaveBookmarks` resolves the coordinator's document fail-fast: a null
  provider throws `ArgumentNullException` as documented, and bookmarks not bound to the current
  document (for example after a document swap before a restore) throw
  `InvalidOperationException` instead of silently persisting an empty set, which deletes the
  sidecar.
- `DiagnosticsRenderer` filters segments against the visible range before materializing
  `TextSegment` instances, draws in `KnownLayer.Selection` by default (assignable through
  `Layer`) instead of the caret layer, batches hint underlines into one geometry, guards empty
  rectangle lists, and keeps the squiggle inside the line box.
- `LineStatusMarginBase.SetSourceChangeHandler` subscribes immediately when the margin is already
  connected (the late registration previously never subscribed), tolerates a null source result
  in the render pass, and coalesces queued invalidations.
- `BookmarkMargin`/`ChangeMarkerMargin` dependency properties validate their brush and geometry
  values, so XAML and `SetValue` null assignments fail at the assignment instead of in the render
  pass.
- `DocumentLineStateCache` clamps the coalesced pending change offset to the new snapshot length,
  so a deletion that shrinks the document below the earliest pending offset no longer throws from
  the incremental cache.
- `TextEditorFirstMatchingLineOperations` captures the matched line's offset during the scan
  instead of reading it back after the selector ran.
- `TextSegmentFactory` rejects a null document with `ArgumentNullException`; mouse navigation
  treats an editor without a document as an unmappable point instead of a null-reference failure.

*Changes*

- Documentation: README truth-up (wiring snippets for the host seams, the snapshot capture
  contract, the auto-closing provenance/gate semantics, the `GetMarkedLineNumbers` name), the
  empty-match probe corpus was widened and its limitation documented on the definition and rule
  (`RegexMatchTimeoutException` and undetected empty-capable patterns fail inside the render
  pass), `RegexHighlightingDefinition` documents concurrent-build thread safety and the
  `RuleSetChanged` propagation, `PolicyIndentationStrategy` documents the captured options
  instance, `NavigationLocation`/`GetWordFromOffset`/`ApplyLocation` over-claims were corrected,
  `DocumentLineStateCache` documents its invariant, and comment-slice terminology now uses "line
  terminator" for CR/LF.
- `TextDocumentFormattingService` applies its prepared batch through a new internal
  `ApplyOperations` overload instead of re-wrapping the operations.
- Tests: CRLF-boundary tracker cases with undo replay plus a seeded randomized differential
  test, Backspace-with-selection, bookmark sidecar preservation (null provider and document
  swap), an attached-renderer integration pixel test, auto-closing provenance/gate/kind/escape/
  identifier-policy cases, span mapping and widened-probe rejection, edit-target stamp semantics,
  and `TextDiagnosticSegmentFactory` projection coverage. A follow-up hardening batch added the
  matched-closer depth-path cases, decoration-union assertions, scroll-target visibility
  assertions in the navigation and first-matching-line tests, and the nonzero-scroll premise
  guard (448 AvalonEdit tests).

**AvaloniaEdit readiness extraction (2026-09-18, tier 2)**

*Breaking changes*

- The editor-generic types moved to `Nickelony.IDEKit.Core` (see that package's entry):
  `NavigationLocation` from the `Navigation` slice, `IChangeNotificationSource` and
  `ILineStatusSource` from the `Rendering` slice, and `TextDiagnosticSegment` from the
  `Diagnostics` slice. Consumers update usings; behavior is unchanged.

**AvaloniaEdit readiness extraction (2026-09-18, `AE-EXT`)**

*Breaking changes*

- The auto-closing model types moved to `Nickelony.IDEKit.Core.AutoClosing` (see that
  package's entry): `TextAutoClosingOptions`, `TextAutoClosingPair`,
  `TextAutoClosingPairKind`, `TextAutoClosingAction`, `TextAutoClosingActionKind`,
  `TextAutoClosingProvenance`, and `TextAutoClosingResult`. Consumers update usings.
- `TextAutoClosingService` now composes the editor-neutral `TextAutoClosingResolver`: the
  service snapshots the document, delegates the pair evaluation, and applies the resolved
  action as document edits. Behavior is unchanged, including undo grouping, selection
  wrapping, read-only policy, and the per-document insertion tracking that feeds the
  resolver's provenance callback; `ITextAutoClosingService` keeps its shape.

**AvaloniaEdit readiness extraction (2026-09-18, `AE-LINE`)**

*Breaking changes*

- The line-comment edit model types moved to `Nickelony.IDEKit.Core.Comments` (see that
  package's entry): `TextLineCommentEdit` and `TextLineCommentAction`. Consumers update usings.
- `TextLineCommentService` now composes the editor-neutral `TextLineCommentPlanner`: the service
  captures the document into a `TextDocumentSnapshot`, delegates the edit computation, and keeps
  the application concerns (editor document edit or host edit target, no-op skip, selection
  restore). Behavior is unchanged.

**Fresh review resolution (2026-09-19, `__180923_` AvalonEdit review)**

*Breaking changes*

- `TextAreaNavigationOperations.CreateCaretLocation` (the bare caret/selection capture over a
  `TextArea`) was deleted: it was unproduced and used the document's file name as document
  identity. `CreateCaretLocationAt` remains the definition-location helper.
- `RegexHighlightingSpan` takes its three required values in the constructor (`begin`, `end`,
  `spanStyle`) and exposes `BeginStyle`/`EndStyle` as init-only properties, matching the
  AvalonEdit span shape instead of trailing optional constructor parameters.

*Changes*

- `UnsavedChangesTracker`'s update path no longer copies the line tail: the parallel offset list
  was removed, old window ends are derived from the line-count delta, and the tracked line strings
  live in a gap buffer positioned at the last edit (measured at 72 microseconds per
  single-character edit on a 20k-line document in Debug). The marked-lines diff keeps its bounded
  full-document `LineDiffer` call by design - the differential and randomized tests pin its exact
  alignment semantics.
- The internal `TextSegmentFactory` moved from the `Documents` slice to `Diagnostics`
  (`Nickelony.IDEKit.AvalonEdit.Diagnostics`), next to its renderer consumer.

*Documentation*

- README: the package family is named, the LanguageFeatures (semantic tokens) and TextMate
  attribution replaces the IntelliSense reference, a Getting started section was added, and the
  API-shape rule (fixed behavior as a static helper, per-editor substitutable behavior as an
  instance service) and the unbound-save window are documented.
- XML docs: the formatting service states its replacement-range and caret-shift contract, "the
  editor document" phrasing was swept to "the editor's document", crefs were shortened,
  `MarginWidth` validation is documented as an `<exception>`, and the auto-closing capture/skip
  terminology was aligned.

*Tests*

- Added coverage for the auto-closing gate set (pre-handled veto, read-only typing, read-only pair
  deletion, non-Back keys, whitespace-only and equal-quote wrap fallbacks, tracked-wrap skip and
  deletion), formatting boundary mappings (caret after the changed range, caret at the range end,
  pure deletion at the caret), CRLF selection snapping (both interiors and the collapse branch),
  struct `TextRunStyle` application, the version-cache factory-mutation branch, direct
  planner/range-normalization cases, and the rule/span property round-trips; the assertion API was
  standardized on `Assert.AreSequenceEqual`.

### Nickelony.IDEKit.IntelliSense

**Whole-solution review resolution (2026-09-19, `__190919_` fresh review, IntelliSense section)**

*Changes*

- Documentation: the `TextCompletionPresentationState` and `TextSignatureHelpPresentationState`
  remarks each state their own visibility union's contract (which request states are counted, and
  that an in-flight completion request is observed separately through the request session); the
  cross-comparing sentences that read as contradictory are gone, and the controller README keeps
  the deliberate contrast in one place.
- The section's other items were re-checked against the current tree: the camelCase constructor
  parameters (V-8), the `<paramref name="value"/>` exception wording, and the snippet expander's
  copied escape blocks were already resolved by the round-4 wave; the remaining review micro-items
  are recorded in the backlog (`future-backlog-2026-09-17.md`, IntelliSense residuals).

**Fresh review round-4 resolution (2026-09-19, IntelliSense round-4 review)**

*Breaking changes*

- `TextCodeActionContext` and `TextCodeActionRequestState` constructors use camelCase parameter
  names (`documentText`, `caretOffset`, `selectionStartOffset`, `selectionEndOffset` and
  `documentText`, `startOffset`, `endOffset`), matching every sibling constructor; named-argument
  callers must update.
- `TextCompletionSessionDecision.Deconstruct` uses camelCase out-parameter names (`shouldClose`,
  `items`, `startOffset`, `endOffset`, `allItemsFilteredOut`); deconstruction stays positional.

*Fixes and additions*

- `DiagnosticHitTester` validates that the supplied diagnostics contain no `null` element and
  throws `ArgumentException` at the entry point instead of dereferencing mid-scan.
- The Lua completion parser consumes an insert/replace edit whose ranges violate the shared
  relation as a single replace-range edit instead of dropping the edit and inserting the plain
  text at the caret; added tests pin the degradation.
- The snippet expander's three copied escape blocks share one helper with the same escape set
  (choice text keeps its additional `\,` and `\|` escapes; no behavior change);
  `DocumentSymbolOutlineBuilder` builds its failure-context strings only on the failure path.
- Documentation accuracy pass: repaired the `RequestDocumentVersion` parenthetical splice and the
  README getting-started branch (now `decision.AllItemsFilteredOut`; the previous `ShouldClose`
  branch is unreachable for kernel decisions); corrected `TextCompletionRequestSession` disposal
  and `IsCurrent` wording (including the documented disposal-guard window of
  `CancelInFlightRequest`/`InvalidateRequests`), `TextCompletionPresentationState` equality
  wording, `TextSignatureInformation`/`TextSignatureHelp` `-1` wording, `SortText`/`Priority`/
  `AdditionalTextEdits` merge wording, `TextDiagnosticsRequest`/`TextDefinitionLocation`
  normalization wording, `TextHoverRequest`/`TextHoverInfo` wording, `OptionalText` and
  controller-options docs; removed duplicated passages in the session, kernel, text-edit, and
  snippet docs.
- Added tests for request-stamp validation, `WithResolvedContent` identity fields, and snippet
  escape edges (trailing lone backslash, escaped backslash at a default's end, `\$` in a choice).

**Wave-4 follow-up (2026-09-19, after the round-4 wave settled)**

*Additions*

- `TextHoverEvaluationState.DiagnosticFallback` returns the diagnostic to surface when a hover
  request was not made or failed (or `null` when the state disallows the fallback), applying the
  documented precedence rule once for every host.
- `TextDefinitionLocation.NavigationStart` returns the position a host navigates to (the selection
  range's start, otherwise the target range's start), applying the documented navigation rule once
  for every host. The LanguageFeatures binding consumes both members instead of re-implementing the
  rules, and its redundant blank re-checks on `SymbolName`/`DocumentId` were dropped now that the
  records normalize on construction.

*Changes*

- Documentation: `TextCodeActionControllerOptions` now describes the record split accurately
  (editor-presentation sizing such as the menu geometry lives with the editor binding that creates
  the menu; timing policy stays framework-neutral), and the README's vocabulary-policy paragraph
  matches.

*Tests*

- New coverage for `DiagnosticFallback` (flag enabled, flag disabled, and a missing diagnostic) and
  `NavigationStart` (selection range present and absent); the suite is green (466/466).

**Fresh review round 3 resolution (2026-09-19, `__180923_` round-3 review)**

*Breaking changes*

- `TextCompletionSessionDecision`'s constructor uses camelCase parameter names (`shouldClose`,
  `items`, `startOffset`, `endOffset`, `allItemsFilteredOut`) and `Deconstruct` gained a trailing
  `out bool allItemsFilteredOut`, so `NoMatches` and `None` deconstruct differently.
- `TextCompletionTextEdit`'s constructor and `TryCreate` use camelCase parameter names
  (`insertRange`, `replaceRange`, `newText`); the class documents the producer-side LSP
  constraints (single-line ranges that contain the request position).
- The snippet expander requires a choice index above zero and every choice element non-empty
  (VS Code semantics: `${0|a,b|}`, `${1||}`, and `${1|a,b,|}` stay literal), and a closed but
  unsupported braced body keeps its literal text while constructs inside it still resolve
  (`${x${1}}` expands to `${x}` plus tabstop 1).
- `TextDocumentSymbol` rejects `TextDocumentSymbolKind.Undefined` with
  `ArgumentOutOfRangeException`.
- `TextDefinitionRequest.SymbolName` and `TextSemanticToken.Type` are trimmed on construction;
  whitespace-only symbol names canonicalize to an empty name.
- `TextDiagnostic` gained `==` and `!=` structural-equality operators.

*Fixes and additions*

- Repaired merge-splice corruption in the README (position-model paragraph, equality-exceptions
  bullet, and getting-started sample) and in the `TextCodeActionItem` and `TextDiagnostic`
  member docs.
- `TextCompletionRequestSession` admits work through a conditional admission in
  `LatestRequestCoordinator` and cancels only its own admission; `CanPublish` delegates to the
  coordinator's atomic check (see `Nickelony.IDEKit.Core`), closing two disposal races.
- Request stamps reject negative values, placeholder ordering is deterministic for equal-offset
  mirrors, and the payload-conventions documentation now covers the ordered-range checks.

**Fresh review resolution (2026-09-15, `__150917_` IntelliSense review)**

*Breaking changes*

- `TextCompletionTrigger` is an open vocabulary again: the enum (with the host-heuristic values
  `Automatic`, `EmptyLine`, `Contextual`, and `Word`) became an identifier-backed class with the
  shared `Invoked` value and `CreateCustom(string)`. A request that does not name a trigger now
  carries `Invoked` (was `Automatic`), and a host defines its own trigger categories instead of
  the library naming one editor's heuristics.
- `TextCompletionItem.Description`/`IsDescriptionMarkdown` were replaced by
  `Documentation`/`DocumentationKind`, aligning with LSP `documentation`/`MarkupKind`.
  `TextHoverContentKind` was replaced by the shared `TextMarkupKind` (root
  `Nickelony.IDEKit.IntelliSense` namespace), now used by both hover content and completion
  documentation.
- `TextCompletionItemKind.IsWellKnown` was deleted, and custom kinds are no longer interned or
  restricted to an ASCII identifier charset (blank and reserved identifiers are still rejected).
  Equality remains the normalized ordinal identifier, so custom categories need no registry.
- `TextSignatureHelpInfo` was renamed to `TextSignatureHelp` (the LSP `SignatureHelp` object),
  removing the `Info`/`Information` confusion with `TextSignatureInformation`.
- `TextDocumentSymbolRequest.Filter` was renamed to `FilterText`, matching
  `TextCompletionItem.FilterText`.
- `TextDiagnostic` no longer widens a zero-length or reversed span by one character: spans
  are stored as supplied (reversed becomes an empty span at the start). An empty span matches
  exactly its offset through `ContainsOffset` and acts as a point in `Intersects`.
- `DiagnosticHitTester.SelectHoverDiagnostics` was deleted because its exact-offset-then-line
  fallback is host tooltip presentation; hosts compose `GetDiagnosticsAtOffset` and
  `GetDiagnosticsForRange` instead (the adopting host moved the policy into its tooltip service).
- `TextCompletionWordSpan` normalizes a null or empty word to the empty state in the constructor
  and the `Word` accessor, so `default(TextCompletionWordSpan)` behaves like an explicitly empty
  word and is safe to pass to filtering code.
- `TextDiagnosticsRequest.EngineVersion` was removed entirely: a diagnostics request now carries
  only the document snapshot and the `Version?` constructor parameter is gone. Rule-set selection
  (for example a target engine version) is provider configuration captured when the provider is
  constructed, mirroring the LSP model where the server is configured per workspace and requests
  carry only document data.

*Fixes and improvements*

- `TextCompletionItem.WithResolvedContent` tracks "was set" explicitly: explicitly assigned
  insertion or filter text is never replaced, and only unset (label-fallback) text is filled from
  the resolved item; the previous "assigned equal to the label is indistinguishable" caveat is
  gone. `FilterText` stores `null` for a blank assignment (the getter still falls back to the
  label).
- `TextSignatureHelpContext.TriggerCharacter` trims surrounding whitespace, and
  `TextSemanticToken.HasModifier` trims its argument; `TextSignatureInformation` now shares one
  empty parameter snapshot.
- Documentation: the "mirroring the LSP rule" filter claim and the "every call allocates a
  document-sized copy" kernel claim were corrected (the filter follows the LSP
  `filterText`/label fallback convention with a client-defined substring test; a full-range read
  copies only for rope-backed snapshots), and the kernel notes that a host which caches decisions
  owns protocol-incompleteness handling. The README payload conventions are now a bullet list and
  the host-specific consumer matrix was replaced by short adoption notes.
- Tests: explicit-text merge adoption, default word-span filtering, empty diagnostic spans
  (exact-offset and range intersection), trigger trimming, decision equality by list reference,
  and reworked kind/trigger coverage for the open vocabularies.

**Fresh review resolution (2026-09-16, `__150922_` IntelliSense review)**

*Breaking changes*

- `TextEditorDiagnostic` was renamed to `TextDiagnostic` (family naming alignment, owner-directed
  2026-09-16), matching `TextDiagnosticSegment` and `TextDiagnosticsRequest`.
- `TextHoverInfo.Range` is a zero-based UTF-16 document offset range (`TextRange?`) instead of LSP
  line/character positions (`TextPositionRange?`). The hover request always carried the snapshot
  the span indexes, so the payload no longer needs a coordinate conversion at the host boundary;
  `TextDefinitionLocation` remains the only LSP-unit payload because a definition can name a
  document the provider has not materialized. The Lua hover parser converts the protocol range
  against the requested document and drops a range that maps to a reversed offset range.

*Fixes and improvements*

- README corrections: three structural-equality exceptions (`TextCompletionItemKind`,
  `TextCompletionTrigger`, `TextSemanticToken`), the position checks on `TextCompletionRequest`,
  `TextHoverRequest`, and `TextSignatureHelpRequest` named next to the null guards, the complete
  record/record-struct payload list, `TextMarkupKind`'s shared root namespace, and the
  `Documentation` trim caveat (PlainText only; Markdown passes through).
- The six provider interfaces state the null contract as an implementation obligation
  ("Implementations must reject a `null` request with `ArgumentNullException`"), not as a library
  guarantee, and `ITextDefinitionProvider`'s summary names the symbol context.
- Ordering authority is explicit: `TextCompletionItem.SortText` is the authoritative protocol
  ordering key, and `Priority` is the producer-derived single-number hint for hosts whose list
  cannot rank by sort text.
- `TextDefinitionLocation` documents `SelectionRange` as "should be contained" in the target range
  (matching the LSP `targetSelectionRange` convention and the no-validation note), and the
  hover/definition coordinate notes no longer describe a tab as special.
- Documentation wording pass: the filter label fallback is stated once and the input-order contract
  is documented; `DiagnosticHitTester.GetDiagnosticsForRange` notes that an empty or reversed range
  selects nothing; `TextDiagnostic` names its offset members; `DocumentSymbolOutlineBuilder`
  documents output/child order; `TextDocumentSymbolRequest` and `TextSignatureHelp` no longer
  restate their member docs; `TextSemanticTokenTypes` says "theme styles"; hyphen-as-dash outliers
  were reworded; `TextDefinitionRequest`'s discriminator parameter wraps like its siblings; and the
  trigger-kind enum loses its trailing comma.
- Tests: dedicated `TextCompletionTrigger` and `TextCompletionWordSpan` suites; the kernel
  default-locator probe table carries explicit per-probe item sets and replacement ranges instead
  of a predicate-derived oracle, plus the `(null, null)` constructor path; hover/definition record
  equality asserts per-member inequality; semantic-token null/blank type, differing hash codes,
  explicit-null commit and detail values, blank-label filter fallback, null children, negative
  offsets, and caller-captured filter identity are covered; duplicated tests and the duplicated LSP
  kind table were folded away, and shared test helpers moved to `TestSupport`.

**Backlog execution (2026-09-16, phase 6 - snippet insertion)**

*Breaking changes*

- `TextCompletionItem.InsertCaretOffset` was deleted; a single post-commit caret cannot represent
  placeholders or choices, and the snippet model supersedes it end to end. `WithResolvedContent`
  adopts the replacing insert-text format only when the current item never assigned one, so an
  explicit `TextCompletionInsertTextFormat.PlainText` assignment stays intentional.

*Additions*

- `TextCompletionItem.InsertTextFormat` (`TextCompletionInsertTextFormat.PlainText`/`Snippet`,
  protocol-aligned numbers) marks snippet insertion and edit text. The text itself is always
  passed through verbatim, so the raw server payload stays available to every consumer.
- `TextSnippetExpander.Expand` expands LSP snippet syntax (`$1`, `${1}`, `${1:default}`,
  `${1|a,b|}`, `\$`/`\}`/`\\` escapes, nested placeholders) into a `TextSnippetExpansion` with
  the visible text and an ordered `TextSnippetPlaceholder` model (index, range, choice texts).
  A construct outside the subset (named placeholders, an unterminated `${`, an unterminated
  choice list, an unrepresentable index) stays literal without truncating input. Tab navigation,
  placeholder linking, and undo grouping stay host behavior.

**Completion protocol depth (2026-09-17, phase 1)**

*Additions*

- `TextCompletionItem` gains `Tags` (new `TextCompletionTag` enum; `Deprecated` today),
  `CommitCharacters`, and `AdditionalTextEdits`. All three default to empty lists, store defensive
  read-only snapshots, and merge through `WithResolvedContent` as documented: tags merge as a
  union, commit characters and additional edits are adopted only when the item has none.

**Document symbols (2026-09-18, phase 4)**

*Additions*

- `TextDocumentSymbolKindConversion.FromLspKind(int)` resolves LSP `SymbolKind` numerics onto
  `TextDocumentSymbolKind` members (1-26 one-to-one); a value outside the protocol range falls
  back to `File`, the protocol's first kind, matching the completion-kind mapping policy. The
  Lua provider maps document-symbol responses through it.

**AvaloniaEdit readiness extraction (2026-09-18, tier 1)**

*Breaking changes*

- Framework-neutral host-state records moved in from
  `Nickelony.IDEKit.AvalonEdit.LanguageFeatures`: `TextCompletionRequestSession` and
  `TextCompletionPresentationState` to `Nickelony.IDEKit.IntelliSense.Completion`,
  `TextSignatureHelpControllerOptions` and `TextSignatureHelpPresentationState` to
  `Nickelony.IDEKit.IntelliSense.Signatures`, `TextHoverRequestState` to
  `Nickelony.IDEKit.IntelliSense.Hover`, and the new
  `Nickelony.IDEKit.IntelliSense.CodeActions` slice gains `TextCodeActionContext`,
  `TextCodeActionRequestState`, `TextCodeActionItem`, and `TextCodeActionControllerOptions`.
  Consumers update usings; behavior is unchanged.
- `TextCompletionRequestSession` exposes its constructor, its `Coordinator` property, and
  `Dispose()` publicly and implements `IDisposable`, because its controllers live in editor
  binding packages; its documentation no longer names one binding's controller.

**Fresh review resolution (2026-09-19, `__180923_` IntelliSense review)**

*Breaking changes*

- `TextCompletionItemKind.FromLspKind(int)` moved to the new
  `TextCompletionItemKindConversion.FromLspKind(int)`, and `TextDocumentSymbolKindConversion`
  moved into its own file: both protocol bridges now follow one `*Conversion` shape beside
  their vocabulary (an enum cannot declare members), and `TextDocumentSymbolKind.cs` keeps one
  top-level type.
- `TextCodeActionControllerOptions.MenuMaxHeight` moved out of the neutral package: the host
  sizing policy now lives in
  `Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions.TextCodeActionMenuOptions`
  (`MaxHeight`, default 320); the neutral options record keeps `RequestDebounceDelay` only and
  stays free of window-typed members like `TextSignatureHelpControllerOptions`.
- `TextHoverRequestState` was renamed to `TextHoverEvaluationState` (and the binding hook member
  `TextHoverControllerHooks.BuildRequestState` to `BuildEvaluationState`): the record is host
  hover-evaluation input, not a request snapshot, and the name no longer collides with
  `TextCodeActionRequestState`.

*Fixes and improvements*

- Documentation accuracy: the README no longer overstates kernel incompleteness handling, names
  the snippet-variable limitation, and records the verified adoption story; the
  `WithResolvedContent` remarks split into content/commit/identity paragraphs with the full merge
  table in the README; the commit-text rule lives once on `TextCompletionTextEdit` (the payload
  members point at it); diagnostics dedup guidance names a stable key; `TextDefinitionRequest`
  names the asynchronous provider; `TextCompletionWordSpan` normalization wording corrected.
- `TextCompletionFilter.FilterByWord` evaluates `FilterText` in a single pass (the no-match path
  still returns the shared empty array).

*Tests*

- New suites and cases: `TextCompletionRequestSession` (supersede/cancel/invalidate/dispose),
  the hover evaluation-state record, code-action carriers and options, snippet edge cases
  (`${}`, `${1:}`, `${1x} tail`, nested choice, `$01`, `\n`), defensive snapshots for
  `CommitCharacters`/`AdditionalTextEdits`, outline-builder projection-delegate guards, signature
  payload immutability, and LSP numeric pins for `TextCompletionTag`,
  `TextCompletionInsertTextFormat`, and `TextMarkupKind`. Duplicate and tautological tests were
  removed, hash-inequality assertions were replaced by the equality contract, and test naming
  normalized on `Member_Scenario_Expectation` with one `Equality_` idiom. 387/387 green.

*Adoption notes*

- "No in-repo consumers" is not a deletion reason on its own: Tomb Editor consumes the
  synchronous stack (kernels, provider contracts, helpers, item stamps) in production, and the
  package README now records that verified adoption story instead of a generic claim.

### Nickelony.IDEKit.AvalonEdit.LanguageFeatures (formerly `Nickelony.IDEKit.AvalonEdit.IntelliSense`)

**AvaloniaEdit readiness extraction (2026-09-18, tier 1)**

*Breaking changes*

- The framework-neutral host-state records moved to `Nickelony.IDEKit.IntelliSense` (see that
  package's entry): the completion request session and presentation state, the signature help
  options and presentation state, the hover request state, and the code-action records. The
  `Presentation/` slice is gone; the remaining types keep their namespaces. Consumers update
  usings; behavior is unchanged.
- The internal `NumericValidation` helper moved to `Shared/Infrastructure` (namespace
  `Nickelony.IDEKit.Infrastructure`) and is compile-linked into this package and
  `Nickelony.IDEKit.IntelliSense`.

**Code actions UI (2026-09-18, host-UI groundwork)**

*Additions*

- New `CodeActions` slice: `TextCodeActionController` watches the caret, selection, and document,
  debounces requests (250 ms default), and publishes the available actions to
  `TextCodeActionMargin` - a light-bulb indicator margin over `LineStatusMarginBase` whose left
  press opens the actions menu. The menu is a skinned WPF `ContextMenu`
  (`TextCodeActionMenuSkin`, optional `ConfigureMenu` hook) over the standard `MenuItem`
  containers; a preferred action renders bold, and invoking an item calls the host's execute hook
  with the item's opaque `Payload` (for example the language-server action a host adapter kept).
  Focus returns to the editor when the menu closes.
- `TextCodeActionControllerHooks` (`BuildRequestState` - a `null` state vetoes the context;
  `RequestCodeActionsAsync`; `ExecuteActionAsync`; optional `ConfigureMenu`), the options record
  (`RequestDebounceDelay`, `MenuMaxHeight`), and the host records `TextCodeActionContext`,
  `TextCodeActionRequestState`, and `TextCodeActionItem`.
- The controller implements `IChangeNotificationSource`, so an attached margin repaints without
  host calls. `RefreshAsync` refreshes immediately (for example after new diagnostics);
  `TryOpenActions`/`CloseActions` drive the menu outside the margin (host key bindings);
  `CancelScheduledRequest`/`CancelInFlightRequest`/`InvalidateRequests` expose the request
  lifetime. Context changes cancel and invalidate in-flight requests, superseded results are
  discarded, and every host-hook failure is contained and logged (event ids 1030-1031).

**Navigation narrowing to `TextArea` (2026-09-17, PKG-1 residual)**

*Breaking changes*

- `TextDefinitionNavigation` extension methods take AvalonEdit's `TextArea` instead of
  `TextEditor`; a host with a `TextEditor` passes `editor.TextArea`. This completes the PKG-1
  composition-point narrowing for the package - every controller and helper now composes with the
  text area.

**Completion protocol depth (2026-09-17, phase 2)**

*Additions*

- The commit-character policy: `TextCompletionController` accepts the selected item when a typed
  character is declared by the item's completion data through the new
  `ICommitCharacterCompletionData` contract (the default `TextCompletionItemCompletionData` bridge
  implements it from the shared item's `CommitCharacters`). The character is still typed, so one
  keystroke both accepts the item and inserts the character. The policy runs on the text-input
  preview stage, before text-composition services such as an auto-closing service see the character,
  with a text-entering fallback for programmatic input; it composes with an auto-closing service
  regardless of subscription order. `TextCompletionControllerOptions.AcceptOnCommitCharacters`
  (default `true`) turns the policy off.

*Changes*

- `TextCompletionItemCompletionData` applies an item's `AdditionalTextEdits` together with the primary
  insertion inside one document update, so the whole commit - including an auto-import edit - is one
  undo unit. Malformed, stale, or overlapping additional edits are skipped individually instead of
  failing the commit, matching the response parsers' per-entry tolerance.
- A deprecated item (`TextCompletionTag.Deprecated`) renders its label struck through in the default
  bridge without changing how the item is filtered, selected, or committed.

**PKG-1 execution (2026-09-17, package rename + TextArea narrowing)**

*Breaking changes*

- The package was renamed from `Nickelony.IDEKit.AvalonEdit.IntelliSense` to
  `Nickelony.IDEKit.AvalonEdit.LanguageFeatures`, reflecting the full package scope (completion,
  hover, signature help, definition navigation, and semantic-token rendering). The namespaces
  moved with it (`Nickelony.IDEKit.AvalonEdit.LanguageFeatures.*`) and the old package id no
  longer ships; consumers update the package reference and usings.
- `TextCompletionController` takes AvalonEdit's `TextArea` instead of `TextEditor`, so a host
  that composes its own `TextArea` can drive the completion window; a host with a `TextEditor`
  passes `editor.TextArea`. The internal sizing follows the text area's typography.

**Changes**

- The package now declares its `Nickelony.IDEKit.Core` dependency directly; it was previously
  transitive. No API changes.
- New `TextDiagnosticSegmentFactory` (`Diagnostics` slice) projects
  `Nickelony.IDEKit.IntelliSense.Diagnostics.TextDiagnostic` values into the AvalonEdit
  package's `TextDiagnosticSegment` renderer model: `Create`, `CreateProvider`, and
  `CreateRenderer` close the previously disconnected diagnostics pipeline so hosts no longer
  hand-map diagnostics. Covered by a new test suite in the package's test project.

**Fresh review resolution (2026-09-15, `__150917_` AvalonEdit.IntelliSense review)**

*Breaking changes*

- `TextCompletionController` constructor validation for a hooks-supplied tooltip skin now reports
  the `hooks` argument instead of the unrelated `skin` parameter when the offset is non-finite or
  a thickness is negative. The constructor also documents the `InvalidOperationException` it
  throws off the editor thread.

*Fixes*

- `TextCompletionController.RequestAsync` rejects a completed result whose cancellation token was
  canceled (a superseded request, `CancelInFlightRequest`, or disposal), so a provider that
  ignores cancellation can no longer publish a canceled decision; the result-pipeline contract
  now matches the hover and signature help controllers.
- An in-place completion refresh with an unchanged empty query displays the replaced item set
  instead of keeping the memoized filtered snapshot of the previous set on screen.
- Provider continuations are marshalled back to the owning thread explicitly, also when the
  creating thread captured no synchronization context (a manually pumped dispatcher, tooling,
  tests): the completion decision apply and tooltip resolve, the hover pointer resolution and
  display callbacks, and the signature help popup callback and state bookkeeping no longer touch
  dispatcher-bound state from a thread-pool thread.
- The signature help refresh timer is bound to the constructor's dispatcher (or the creating
  thread's dispatcher by default) instead of silently never running when the controller is
  created before the owner thread has a dispatcher.
- The completion window measurement stops once the configured maximum displayable width is
  reached instead of measuring every item on every open and refresh.
- `SemanticTokensColorizer` skips the per-line token lookup when no tokens are applied, and its
  class and `ISemanticTokenStyleResolver` remarks now describe the tolerated throwing resolver
  (the previous set stays in effect and the push can be retried) instead of forbidding throws.

*Additions*

- `TextSignatureHelpController` takes an optional `Dispatcher` so a host whose owner thread has
  no dispatcher yet can bind the refresh timer and the provider continuations explicitly.
- `TextCompletionPresentationState.IsActiveOrPending` reports whether a completion presentation
  is shown or pending, matching `TextSignatureHelpPresentationState`.

*Documentation*

- The README and XML remarks no longer claim that a null or partial tooltip skin leaves stock WPF
  values (the default skin always applies placement, offset, border, and padding; only the two
  brushes leave the theme colors), that `CancelInFlightRequest` rejects results it did not, or
  that continuations resume on the creating thread without a synchronization context. The
  internal `Infrastructure` slice is documented.

**Follow-up resolution (2026-09-15, owner-directed)**

*Breaking changes*

- `TextCompletionController.RequestAsync` and `ApplyDecision` no longer take a per-call
  `itemFactory` parameter; `TextCompletionControllerHooks.CompletionItemFactory` is the single
  item-mapping seam, falling back to the default `TextCompletionItemCompletionData` adapter.
  Hosts that mapped items per call configure the factory on their hooks instead.
- `TextHoverControllerHooks.GetCurrentRequestOffset` was renamed to `ResolveRequestOffset`. The
  hook resolves the request target for a hovered offset and can still veto the completed result
  with `null`; the old name read like the state getters, which never veto.

*Tests*

- `TextCompletionControllerWindowTests` was split by concern into
  `TextCompletionControllerWindowTests` (window lifecycle, sizing, configuration, selection,
  click), `TextCompletionControllerTooltipTests` (tooltip pipeline, skin, availability pin), and
  `TextCompletionControllerWindowRefreshTests` (refresh/replace semantics); the shared test item
  factory moved to `CompletionTestHost.CreateItem`. 218/218 tests green.

*Documentation*

- The hover controller deliberately exposes no readable presentation state (the host owns the
  tooltip surface); the XML remarks and README now state this non-goal, and the README sentence
  claiming every controller exposes `CurrentPresentation` was corrected. The over-long class and
  member remarks of the colorizer, completion hooks, hover controller, `OpenOrRefresh`, and
  definition navigation were trimmed or split.

**Fresh review resolution (2026-09-16, `__150922_` AvalonEdit.IntelliSense review)**

*Breaking changes*

- Hover tooltip hooks and request-state types now reference the renamed `TextDiagnostic` payload
  (`TextHoverControllerHooks.ShowToolTip`, `TextHoverRequestState.DiagnosticInfo`).
- `TextCompletionController.RequestAsync` now contains provider and callback failures the same way
  the scheduled path does: the failure is logged with event id `1000` and the method reports `false`
  instead of faulting the caller. The provider-failure convention is now uniform across all three
  controllers (hover and signature help already contained and logged), so timer, dispatcher, and
  event-handler callers never observe provider exceptions; only argument validation still throws.

*Additions*

- `TextDefinitionNavigation.TryGoToDefinitionAtOffset`/`TryGoToDefinitionAtOffsetAsync` resolve a
  definition from the document snapshot text and a zero-based offset, for position-based providers
  (for example an LSP-backed resolver) that resolve a location without a hover or symbol step; the
  optional cross-file callbacks report locations in other documents. The hover-first entry points
  stay as the convenience path for name-catalog providers.
- `TextCompletionControllerOptions.CloseWhenEmpty` (default `true`) controls whether a completion
  window whose filtered list becomes empty closes itself instead of showing AvalonEdit's empty
  template.
- `TextSignatureHelpController.ScheduleRefresh` takes an optional trigger kind and character
  (`ScheduleRefresh(triggerKind = TextSignatureHelpTriggerKind.ContentChange, triggerCharacter = null)`),
  so a host that debounces a trigger-character request no longer has its context rewritten to a
  content change; the defaults preserve the previous behavior.

*Fixes*

- README and XML documentation corrected where they contradicted the implementation: the
  AvalonEdit completion tooltip is reached through a reflected private field (the "exposes the
  getter publicly" claim was wrong), the tooltip resolve debouncer is created on construction and
  armed by selection changes (not "armed on construction"), the refresh section no longer
  describes an impossible one-end call (a decision that carries items without both offsets is
  rejected by `ApplyDecision` with `ArgumentException`), and the navigation remarks no longer
  promise rejection of a location state that cannot occur.
- `SemanticTokensColorizer` remarks state that tokens whose start offset equals the document end
  are ignored as well, and the line-transformer removal note no longer says "when the view is
  detached".
- `DispatcherInvocation.Run<TResult>` documents the `Invoke<TResult>(Func<TResult>)` overload it
  calls; `NumericValidation`'s exception text parses as intended.
- The package log-id comment no longer claims cross-package distinguishability (sibling packages
  reuse ids `1`-`3`, so a shared logger demultiplexes by event name), the internal tracker's
  mirrored XML docs point to the controller's public contracts instead of copying them, and
  `OpenOrRefresh` documents that it reports `true` when the refresh was applied even when the
  window closed itself because its filtered list became empty.
- Hook and skin documentation now consistently states that `ConfigureToolTip` runs after the
  tooltip skin and can override any value the skin applied; descriptive member aliases (`<c>...`)
  were replaced with `<see cref>` references; the naming, punctuation, and "canceled" spelling
  sweeps were applied; `CompletionWindowCoordinator.Create`'s long summary moved into remarks.
- The README now states the provider-failure convention once, documents that
  `TextCompletionPresentationState.IsActiveOrPending` and
  `TextSignatureHelpPresentationState.IsActiveOrPending` cover different unions (in-flight
  completion requests stay observable through `CurrentRequestCancellationToken`), and the
  `TextHoverRequestState` remarks spell out the hover-versus-diagnostic precedence rule and that
  the host selects the single diagnostic to surface.

*Tests*

- New coverage: the automatic empty-window close (open and refresh paths), `CloseWhenEmpty`, a
  resolver returning null content hiding the tooltip, `CloseWindow` hiding a visible tooltip,
  `RequestAsync` failure containment, tracker idle states (uninitialized scheduling, no-op
  cancellation, no token before a request, a close cancelling a scheduled request), hover failure
  before offset resolution, async navigation parity (offset bounds, blank-symbol short-circuit,
  pre-canceled token, null guards), the offset-first navigation API, the presentation-state truth
  tables and the 50 ms signature refresh default, coordinator null-brush guards and
  close-without-show, the sizing early stop, and the default adapter's label fallback.
- The click tests wait for item-container realization instead of trusting a fixed 20 ms delay
  (flake risk) and share the inline-template setup helper; the logic-free hover record echo test
  was deleted; the unenforced `Smoke` test category was dropped; the request-token tests moved
  out of `TextCompletionControllerDisposalTests` into
  `TextCompletionControllerRequestTokenTests`. 268/268 tests green.

**Backlog execution (2026-09-16, HL-1 + test organization)**

*Breaking changes*

- `SemanticTokenStyle` was deleted; `ISemanticTokenStyleResolver.Resolve` and the colorizer's
  style handling now use the shared `Nickelony.IDEKit.AvalonEdit.Rendering.TextRunStyle` record
  (constructor order: foreground, bold, italic, text decorations).

*Changes*

- The suite's remaining blocking waits on synchronously completing tasks were migrated to
  `await` with `async Task` test methods (T8 remainder); awaited calls keep running on the STA
  test thread because the awaited tasks complete inline. Pump-gated waits and off-thread
  `Task.Run` joins stay deliberately blocking.

**Backlog execution (2026-09-16, phase 6 - snippet insertion)**

*Changes*

- `TextCompletionItemCompletionData.Complete` expands a `TextCompletionInsertTextFormat.Snippet`
  insertion text through the shared expander before inserting it, and places the caret after the
  final tabstop's text when the snippet carries one. Plain items commit exactly as before, and tab
  navigation across the remaining placeholders stays host behavior.

**Backlog execution (2026-09-16, phase 7 - request consolidation)**

*Breaking changes*

- The completion controller's low-level request members moved onto a new
  `TextCompletionRequestSession` exposed as `TextCompletionController.Requests`: `BeginRequest`,
  `IsCurrent`, `CurrentRequestCancellationToken`, `CancelInFlightRequest`, and
  `InvalidateRequests`; the controller-level members were deleted. Disposing the controller ends
  the session; its operations then report their safe defaults (`BeginRequest` reports `-1`) and
  the tracked tokens stay observable.
- `TextCompletionPresentationState.IsActiveOrPending` and
  `TextSignatureHelpPresentationState.IsActiveOrPending` were renamed so each member states its
  own union: `IsPresentationVisibleOrRequestScheduled` (a window or tooltip is visible, or a
  request is scheduled; an in-flight completion request is observed through the `Requests`
  session) and `IsPresentationVisibleOrRequestPending` (the popup is visible, or a request is in
  flight or a refresh is pending).

*Changes*

- The completion request pipeline now runs on the shared core `LatestRequestCoordinator` behind
  the `Requests` session, so `RequestAsync` and manually driven requests supersede each other and
  the controller no longer owns hand-rolled request-lifetime state. `RequestAsync` keeps its
  documented behavior: a superseded or canceled result is never applied (even when a provider
  ignores its token), cancellation reports `false`, and failures are contained and logged with
  event id `1000`. The debounced scheduling lives in an internal `CompletionRequestScheduler`.
- `CancelInFlightRequest` cancels the pending request's token without disposing its cancellation
  source, so the canceled token stays usable for late registrations; it remains observable through
  `CurrentRequestCancellationToken` exactly as documented.

**Fresh review resolution (2026-09-19, `__180923_` IntelliSense review follow-up)**

*Breaking changes*

- The default completion commit path now honors the item's edit payload:
  `TextCompletionItemCompletionData.Complete` commits `TextEdit.NewText` when the item carries
  an edit (falling back to `InsertText`), and `CompletionCommitEditApplier` applies the primary
  replacement at the edit's own range (`InsertRange`, or `ReplacementRange` in overstrike mode)
  while the range still fits the current document, with the completion window's segment as the
  fallback for a missing or stale range. Snippet expansion applies to the committed text.
- `TextHoverControllerHooks.BuildRequestState` was renamed to `BuildEvaluationState` (the state
  type is now `TextHoverEvaluationState`), and the menu sizing policy moved in from the neutral
  package as `TextCodeActionMenuOptions` (`MaxHeight`, default 320), accepted by the controller
  through a new optional `menuOptions` parameter.

*Tests*

- New commit-path tests (edit text preference, edit-range use, stale-range fallback, snippet
  expansion of edit text) and menu-options validation tests. The suite is green (349/349 in the
  latest run).

**Host-neutrality and polish wave (2026-09-19, LanguageFeatures review follow-up)**

*Breaking changes*

- `TextCodeActionController` now requires its `hooks` argument (it moved before the optional
  parameters), matching the required hook groups of the hover and signature help controllers.
- Package log event ids moved into the package's own `1000` block - completion `1000`-`1002`,
  hover `1010`-`1012`, signature help `1020`-`1021`, code actions `1030`-`1031` - so ids no longer
  collide with the small per-package ids used by sibling packages.

*Additions*

- `TextCodeActionMargin.OpenOnLeftPress` (default `true`): a host that owns the open gesture sets
  it to `false` and drives `TextCodeActionController.TryOpenActions(line, anchor)` itself.
- `TextCodeActionControllerHooks.ConfigureMenuItem` configures every created menu item before the
  menu opens, and `TextCodeActionMenuOptions` now carries the menu anchor offsets
  (`AnchorXOffset`, `CaretAnchorYOffset`, `MarginAnchorYOffset`).
- `IPreselectedCompletionData` exposes preselection as an opt-in contract, so custom host
  completion data participates in the preselection policy like the default
  `TextCompletionItemCompletionData`.

*Changes*

- The commit payload is bundled into an internal `CompletionCommitPayload` record consumed by a
  single-argument applier, and the internal null guards were dropped to match the family
  convention for trusted in-assembly callers.
- `TextCompletionController.ApplyDecision` relies on the shared decision type's state invariant
  instead of keeping an unreachable partial-decision guard: `TextCompletionSessionDecision` now
  enforces (constructor and init accessors) that a decision carrying items carries a valid, ordered
  replacement range, so the controller's guard and its `ArgumentException`/
  `ArgumentOutOfRangeException` contract were removed.
- Documentation: the `TextDefinitionNavigation` cross-file contract moved into the type remarks
  (the six parameter docs link to it), `TextDiagnosticSegmentFactory` distinguishes the caching
  `Create` path from the uncached `CreateProvider` convenience, and the README documents the new
  event-id block, gesture seam, item hook, and preselection interface.

*Tests*

- Coverage added for the hover-first cross-file callback, the trigger-character refresh, the
  margin gesture opt-out and the rendered indicator, the menu item hook and anchor options, and
  custom-data preselection; the suite is green (360/360 - the removed
  `ApplyDecision_ItemsWithoutOffsets_ThrowsArgumentException` case is superseded by the IntelliSense
  package's `TextCompletionSessionDecisionTests.Constructor_ItemsWithoutOffsets_Throws`, because the
  invalid decision can no longer be constructed).
- Tomb Editor isolation gate at the shared `1.0.0-preview.98` feed: the six consumer targets build
  with 0 warnings/0 errors and both suites pass (`TombLib.Tests` 932/932, `TombEditor.Tests`
  367/367), independently reproduced by `artifacts/lf-tomb-verify-98.ps1` (status
  `artifacts/lf-tomb-verify-status.txt`); the shared wave-4 run records the same result
  (`artifacts/tomb-wave4-summary.txt`).

**Fresh review resolution, second pass (2026-09-19, fresh LanguageFeatures review)**

*Breaking changes*

- `TextCompletionItemCompletionData.Text` returns the item's `FilterText` (falling back to the
  label when the item declares none) instead of the label, aligning the adapter with AvalonEdit's
  contract (its `ICompletionData.Text` documents the property as the list's filter key) and with
  the shared completion-session kernel, which matches on `FilterText`. An item whose label carries
  decoration the filter text does not - for example a callable label `foo(a, b)` with the filter
  text `foo` - now matches a query for `foo`, and the decorated label stays the displayed content.
  The default window measurement reads the displayed content (a text-block content resolves to its
  text, falling back to the filter text) instead of the filter key, so the window keeps sizing for
  what the list shows.

*Fixes*

- `TextCompletionController` scrolls a preselected item into view (`CompletionList.ScrollIntoView`).
  AvalonEdit's selected-item setter does not scroll while its own selection path centers the best
  match, so a preselected item ranked below the visible window was selected but invisible (Enter
  still committed it). The preselection policy itself is unchanged: the hint is re-applied on an
  in-place refresh as well, because a refresh replaces the item set with a fresh provider answer
  that the previously selected item cannot survive either way.
- `CompletionCommitEditApplier` treats an additional text edit that covers a zero-length completion
  insertion point and extends beyond it as overlapping and drops it. Previously the edit was applied
  after the insertion at the same offset and consumed the first characters of the freshly inserted
  text (inserting `print` at offset 2 with an edit replacing `[2, 3)` with `X` produced `prXrintx`
  instead of `prprintx`). A zero-length edit at the insertion point, and an edit ending exactly at
  it, still merely touch and are applied.
- `TextCodeActionController` validates the `TextCodeActionMenuSkin` brushes and throws
  `ArgumentNullException` for a null brush, matching the completion window skin's validation.

*Tests*

- New coverage: preselection beyond the viewport is scrolled into view, the zero-length-insertion
  overlap rule (both the dropped covering edit and the applied touching edit), the adapter's
  filter-text mapping and label fallback, the display-content measurement when the filter text is
  shorter than the label, and the menu-skin brush validation. Host-flavored test fixtures
  (Lua-like samples, `.lua` document ids) were replaced with neutral, length-preserving samples.
  The suite is green (367/367).
- Tomb Editor isolation gate at a unique `preview.101` package version (dependencies pinned to the
  shared `preview.98` regime through `artifacts\lf-pack-101.nuspec`; the closure-wide `preview.99`
  regime collided with concurrent gates in the shared NuGet cache): the six consumer targets build
  (`TombLib.Scripting.UI` with its 8 pre-existing warnings, the other five 0 warnings/0 errors) and
  both suites pass (`TombLib.Tests` 932/932, `TombEditor.Tests` 367/367), recorded by
  `artifacts\lf-tomb-verify-101.ps1` (status `artifacts\lf-tomb-verify-101-status.txt`, logs
  `artifacts\lf101-tomb-*.log`).

**Fresh review resolution, third pass (2026-09-19, repeated fresh LanguageFeatures review)**

*Additions*

- `TextSignatureHelpControllerOptions` (IntelliSense) gained `Cycle` (default `true`). With cycling
  disabled, overload navigation past the first or last signature dismisses the presentation instead
  of wrapping; the default preserves the existing wrap-around behavior. The signature help
  controller stores its options record for the controller's lifetime.

*Changes*

- The completion request scheduler's two cancel entry points collapsed into one
  `CancelScheduledRequest`; the controller guards the public path, and the pending-work and
  disposal paths rely on the debouncer's post-disposal no-op, so the internal "core" variant
  disappeared.
- Documentation: the `TextCompletionControllerHooks` scheduling claim, the
  `CompletionWindowCoordinator` reuse sentence, the code-action hooks' failure sentence, and the
  duplicated `TextDefinitionNavigation` cross-file paragraphs were repaired;
  `CompletionCommitEditApplier` documents the overstrike insert-vs-replace rule at XML level; the
  tooltip presenter records the dual-writer parity as a permanent, test-pinned constraint; and the
  README gained a Requirements section, the missing `Diagnostics` slice, the second linked shared
  helper, the hover-precedence and filtering-authority clarifications, and corrected id-range and
  options-placement wording. The IntelliSense README follows the widened signature-help options
  record.

*Tests*

- New coverage: overstrike and distinct-range commits, refresh-path preselection, the
  content-forbidden hide decision and the failure-without-diagnostic-fallback hide, the
  request-path show failure, per-hook constructor guards for the hover and signature hooks, the
  code-action anchor/margin option validation, a late-resolution pump in the tooltip supersession
  test, a `TextBlock`-content measurement case, a strengthened width assertion, and unit tests for
  `DispatcherDebouncer`. The suite is green (387/387).
- Tomb Editor host gate: the package was packed alone at the unique `preview.102`
  (`artifacts\lf-pack-102.nuspec`, `artifacts\lf-tomb-verify-102.ps1`). The first restore found the
  shared closure already moved from `.98` to the concurrent `.99` regime, and every target failure
  belonged to FOREIGN migration leftovers (`TextHoverEvaluationState`, `CommentOperations`,
  `IdentifierSpanMode`, `TextLineMap`) or a concurrent-build file lock; the second attempt hit the
  concurrent full-family `.38` repack replacing the feed mid-restore. The usage audit
  (`artifacts\lf3-usage-audit.txt`) found zero Tomb references to every surface this wave changed.
  The follow-up `artifacts\lf-tomb-verify-38.ps1` then ran against the completed `.38` family
  repack: all six targets stop on FOREIGN consumer migration still pending in the Tomb tree
  (`TextWorkspaceEditApplier.cs` against the Workspace/Core `LocalPathComparisonPolicy` and the
  required `WorkspaceDocumentChange.BeforeFileFormat`, plus concurrent-build file locks); again, no
  failure referenced LanguageFeatures. **Completed on the settled tree:** the closeout wave repacked
  the whole family at `preview.100`, and this gate's independent reproduction
  (`artifacts\lf-tomb-verify-100.ps1`, status `artifacts\lf-tomb-verify-100-status.txt`) confirms
  all six targets with 0 errors (`TombLib.Scripting.UI` keeps its 8 pre-existing warnings; the
  `TombIDE.ScriptingStudio` warnings are the foreign TombLib.Forms CAS/unused-member set) and both
  suites green (`TombLib.Tests` 932/932, `TombEditor.Tests` 367/367), matching the canonical
  closeout gate (`artifacts\tomb-wave6-summary.txt`).

**Fourth fresh-review resolution (2026-09-19, fourth fresh LanguageFeatures review)**

*Changes*

- The default completion measurement is icon-aware: `ItemIconWidth` is added only for items that
  supply an image (`ICompletionData.Image`), instead of unconditionally reserving an icon column
  the package's own `TextCompletionItemCompletionData` adapter never renders. Imageless items now
  measure as wide as their text; the sizing documentation (options record, defaults, sizing helper,
  README) describes the profile as optional icon and detail columns.
- Internal dispatcher plumbing moved to the shared linked infrastructure: `DispatcherInvocation`
  and `DispatcherDebouncer` now live in `Shared/Infrastructure` (namespace
  `Nickelony.IDEKit.Infrastructure`, linked into this package), following the
  NumericValidation/BrushHelpers precedent so future WPF packages share one implementation.
- `Nickelony.IDEKit.AvalonEdit.Rendering.LineStatusIconMarginBase` is new: the icon-margin
  machinery (icon brush/geometry dependency properties with validation, the font-scaled centered
  drawing, click-position and visual-line resolution, and hit testing) is shared there;
  `TextCodeActionMargin` and `BookmarkMargin` register their icon defaults through property
  metadata overrides instead of repeating the implementation.
- Naming and internal consistency: the completion scheduler's log helper follows the package's
  private `s_log*` convention behind an internal `LogRequestFailed` method; the thin
  `RunOnOwnerThread`/`RunOnDispatcherThread` wrappers were replaced with direct
  `DispatcherInvocation` calls; the code-action and completion controllers use the shared
  `ClampOffset` helper; the signature help controller contains a dispatcher-shutdown failure during
  its final request bookkeeping.
- Documentation: the README's options-placement rationale now states the real reason (the record
  describes the AvalonEdit completion window's presentation), "typically Ctrl+." became "for
  example Ctrl+.", the `MeasureItemWidth` negative-width wording matches the implementation, and
  `CompletionCommitEditApplier` documents its deliberate failure-policy difference from the Core
  text-edit kernel.

*Tests*

- New coverage: icon-aware sizing (imageless versus image-bearing items), whitespace-only detail,
  multi-entry commit-character lists, handled preview input, typing without an open window, a
  throwing hover liveness provider, the success-plus-diagnostic combination with the fallback flag
  disabled, the code-action document-edit re-request, the menu's external close and focus restore,
  a delta-based anchor-options assertion (replacing the implementation-restating one),
  post-disposal `OpenOrRefresh` with items, and `ScheduleCloseIfEmpty` with items present; the
  coordinator test now closes its created window. The suites are green (LanguageFeatures 400/400,
  AvalonEdit 434/434).

*Host verification*

- Tomb Editor gate against a private `1.0.0-preview.104` family feed (`artifacts\lf4-pack.ps1`,
  `artifacts\lf4-tomb-gate.ps1`, restore config `artifacts\tomb-restore-nuget-lf4.config`, private
  package cache `artifacts\lf4-packages`; the shared `.100` review-feed regime used by the
  concurrent waves was left untouched): the feed is complete (13 packages), all six consumer
  targets build with exit 0, `TombLib.Tests` passes 932/932 and `TombEditor.Tests` 367/367
  (`artifacts\lf4-tomb-gate-summary.txt`, status `artifacts\lf4-tomb-gate-status.txt`).
- Follow-up (same day, after the IntelliSense wave settled): the hover-fallback and
  definition-navigation rules moved onto the shared records
  (`TextHoverEvaluationState.DiagnosticFallback`, `TextDefinitionLocation.NavigationStart`); this
  binding consumes them instead of re-implementing the rules, and the redundant blank re-checks on
  `SymbolName`/`DocumentId` were dropped. Re-verified afterwards: IntelliSense 466/466,
  LanguageFeatures 400/400, and the host gate re-run at `1.0.0-preview.105` (six targets exit 0,
  `TombLib.Tests` 932/932, `TombEditor.Tests` 367/367; the summary file now records the rerun).

**Whole-solution review resolution (2026-09-19, `__190919_` fresh review)**

*Fixes*

- `TextCompletionController` tolerates a detached document: `OpenOrRefresh` reports no-op instead
  of dereferencing the missing document, and the window query falls back to the empty query, so the
  posted initial selection cannot fault on a document that detaches before it runs; the returns
  contract documents the no-op case and tests pin both paths.
- `TextSignatureHelpController.ScheduleRefresh` contains a throwing caret-offset getter like the
  other host callbacks: the failure is logged (host-callback event id `1021`) and the pending
  refresh is canceled, matching a getter that reports no caret; the hook type documents the
  contract and a test pins it.
- `SemanticTokensColorizer`: the render path clamps the end index with a subtraction so an
  overflowing character-plus-length sum cannot silently hide a token, and `SetTokens`/`Rebuild`/
  `ClearTokens` verify their documented UI-thread affinity (`InvalidOperationException` off the
  owning thread) instead of leaving it implied.

*Documentation*

- The README no longer duplicates the XML remarks for the non-activatable window policy, the
  tooltip-decoration constraint, and the display-text measurement resolution; each rationale lives
  on its type and the README keeps the host-facing summary.

*Tests*

- New coverage for the detached-document no-op (including the reattach path), the
  detach-before-initial-selection pump, the contained caret hook (event id `1021`, canceled
  refresh, no provider call), and off-thread mutator calls; the suite is green (404/404).

*Host verification*

- Tomb Editor gate against the private `1.0.0-preview.106` family feed (`artifacts\lf4-pack.ps1`,
  `artifacts\lf4-tomb-gate.ps1`, the wave-4 private cache and restore config): the feed is complete
  (13 packages), all six consumer targets build with exit 0, `TombLib.Tests` passes 932/932 and
  `TombEditor.Tests` 367/367 (`artifacts\lf4-tomb-gate-summary.txt`); the shared `.100` regime was
  not touched. Full solution verification on the same tree: rebuild 0 warnings / 0 errors and all
  15 suites green (3724 passed / 7 skipped / 0 failed).

### Nickelony.IDEKit.AvalonEdit.TextMate

**Breaking changes**

- `TextMateHighlightingAttachment.StyleResolver` was removed; the attachment already hands its
  local resolver to the transformer.
- `TextMateHighlightingStyle` was deleted in favor of the shared
  `Nickelony.IDEKit.AvalonEdit.Rendering.TextRunStyle` record;
  `TextMateThemeStyleResolver.Resolve` returns it, and the record's `Empty` value and
  `CreateTypeface` method replace the style type's own.

**Fresh review fixes (2026-09-19, `__190919_`)**

*Fixes*

- Inserting a line feed directly after a lone carriage return (CR -> CRLF terminator merge) now
  updates the snapshot line text, so `GetLineTextIncludingTerminators` always mirrors the
  document; previously the previous line kept its stale one-character terminator.
- The coalescing redraw gate now reopens when a queued dispatcher operation aborts between being
  queued and the abort handler being attached, instead of leaving every later token refresh unable
  to queue a redraw.

*Changes*

- The resolver's style cache uses a concurrent dictionary instead of a lock, removing a lock
  acquisition per resolved token on the paint path.
- `TextMateTokenThemeRule.Foreground` documents the four-digit `#RGBA` form alongside `#RRGGBBAA`.

### Nickelony.IDEKit.AvalonEdit.Markdown

**Changes**

- The package no longer references `Nickelony.IDEKit.AvalonEdit`: no source or test used a type
  from it, and the reference forced the base package (and AvalonEdit) onto Markdown consumers;
  the shared `BrushHelpers` file is compiled into the package directly.

### Nickelony.IDEKit.JsonSchema

**Fresh review fixes (2026-09-19, `__190919_`)**

*Breaking changes*

- `JsonSchemaVocabularyPropertyDescriptor.IsArray` was renamed to `IsArrayOnly` (the member is true
  only for a single `Array` type; unions report false).

*Changes*

- `Types` is emitted in the declaration order of `JsonSchemaPropertyType` and documents that order;
  the README no longer claims that validation-only keywords (`additionalProperties`,
  `patternProperties`, `not`, `if`/`then`/`else`) are traversed, and the `JToken` overload scopes
  its failure diagnostic to schema conversion instead of wrapping indexing failures.

*Tests*

- New coverage: string-only `const` extraction, multi-type union ordering, and `IsArrayOnly`
  (17 to 20 tests).

### Nickelony.IDEKit.KeyBindings

**Breaking changes**

- `IKeyBindingService<TCommandId>.Apply` and `Clear` were removed (no consumers). Override
  mutation is limited to `Reset`/`ResetAll`, which drop a command's overrides and fall back to
  the catalog defaults.
- `CommandCatalog<TCommandId>.TryGetDescriptorById` and
  `CommandCatalog<TCommandId>.ValidateNoDuplicateDefaults` were removed. Use
  `TryGetDescriptor(TCommandId)` for lookups; the constructor still rejects duplicate commands
  and serialized identifiers.

**Fresh review fixes (2026-09-19, `__190919_`)**

*Fixes*

- `Reset` now removes the override by the catalog descriptor's `SerializedId`; previously it
  matched the command's display string, so an override stayed active whenever the serialized
  identifier differed from `ToString()`.
- A host-reserved command ignores every loaded override, including an empty binding list that
  would otherwise have unbound it.
- Numeric strings that do not name a defined `Key` value are rejected when overrides are loaded.
- `KeyCombo.GetDisplayText` uses friendly names for `Back`/`Return`/`Escape`/`Space`, numpad
  operators, `OemBackslash`, and `OemClear`.

*Changes*

- The published lookup maps are swapped as one immutable snapshot, so concurrent readers never
  observe the two maps from different rebuilds; `CommandDescriptor` copies its default bindings
  and rejects an empty serialized identifier; `Dispose` documents that it only detaches change
  subscribers.
- The unreachable override parse-failure log was removed; the package log event ids end at 4
  (README updated).

*Tests*

- New coverage: reset with a differing serialized identifier, empty overrides for reserved and
  remappable commands, numeric undefined keys, validation outcomes, and friendly display names
  (18 to 24 tests).

### Nickelony.IDEKit.Processes (formerly `Nickelony.IDEKit.Tooling`)

**Breaking changes**

- The package, assembly, and namespace were renamed to `Nickelony.IDEKit.Processes`; update the
  package reference and usings. The package scope stays batch execution: one request runs one
  process to completion, redirected output is captured in full, and there is no standard-input
  piping or streaming by design (the LSP transport lives in `Nickelony.LanguageServer.Client`).

**Fixes**

- A process that writes more than the pipe buffer to redirected standard output or error no
  longer deadlocks the run: both streams are drained while the process runs, and the handle's
  output properties return the complete captured text.

**Fresh review fixes (2026-09-19, `__190919_`)**

*Breaking changes*

- `ProcessRunResult.ExitCode` is `int?`: it stays `null` when a terminated process's exit could
  not be observed, instead of the runner blocking indefinitely on the exit.

*Fixes*

- Termination is bounded: after the tree-kill and single-kill attempts, the runner waits a fixed
  grace period for the exit and reports an unobservable exit (`null` exit code and output)
  instead of waiting forever.
- `ProcessRunRequest.Timeout` is validated at construction (negative values except
  `Timeout.InfiniteTimeSpan`, and values beyond `int.MaxValue` milliseconds, are rejected).
- `IProcessHandle.StandardOutput`/`StandardError` are non-nullable (they throw when redirection
  is disabled; they never returned `null`), and a live handle's pending drain tasks are observed
  on dispose.

*Tests*

- New coverage: failed-termination reporting, and timeout validation (zero, infinite, negative,
  excessive) (13 to 18 tests).

### Nickelony.LanguageServer.Abstractions

**Breaking changes**

- The diagnostics payload follows the family rename: `TextDiagnostic` replaces
  `TextEditorDiagnostic` in `ILanguageServerIntelliSenseProvider.GetDiagnostics` and
  `DiagnosticsUpdatedEventArgs`.
- `TextDocumentRange` and `TextDocumentRangeNormalizer` were removed. `TextEdit.Range` and
  `TextReferenceLocation.Range` are now the shared zero-based
  `Nickelony.IDEKit.Core.Text.TextPositionRange`; range values are stored as supplied instead of
  being silently clamped, and `TextReferenceLocation` takes `(filePath, range)`.
- The package now references `Nickelony.IDEKit.Core` directly for the shared range primitive.
- `ILanguageServerIntelliSenseProvider` now follows the standard .NET event pattern:
  `DiagnosticsUpdated`, `StartupFailed`, and `WorkspaceWatcherFailed` are
  `EventHandler<TEventArgs>` events that raise with the provider as the sender, and their data
  moved into `DiagnosticsUpdatedEventArgs` (file path plus the diagnostics snapshot),
  `StartupFailedEventArgs`, and `WorkspaceWatcherFailedEventArgs` (both wrap the existing
  failure value as `Failure`).
- `ILanguageServerIntelliSenseProvider` now returns the renamed `TextSignatureHelp` payload
  (from `TextSignatureHelpInfo`, see the IntelliSense package).

**Signature fidelity (2026-09-17, phase 3)**

*Breaking changes*

- `ILanguageServerIntelliSenseProvider.GetSignatureHelpAsync` gains an optional
  `TextSignatureHelpContext? context` parameter before the cancellation token, so the editor-side
  trigger context (trigger kind, trigger character, the retrigger flag, and the currently shown
  payload) reaches the language server instead of being dropped. A caller without a context omits
  the parameter and keeps the position-only request shape.

**Document symbols over LSP (2026-09-18, phase 4)**

*Breaking changes*

- `ILanguageServerIntelliSenseProvider` gains `GetDocumentSymbolsAsync(filePath, content,
  cancellationToken)`, the document-symbol (outline) contract. Document symbols are best-effort:
  the request is skipped with an empty result when the connected server did not advertise
  `documentSymbolProvider` support.

**Code actions (2026-09-18, phase 6 groundwork)**

*Breaking changes*

- `ILanguageServerIntelliSenseProvider` gains `GetCodeActionsAsync(TextCodeActionRequest,
  cancellationToken)`, the code-action (quick fix / refactoring) contract. Code actions are
  best-effort: the request is skipped with an empty result when the connected server did not
  advertise `codeActionProvider` support, and actions that the server expresses as a command
  without an edit are not representable by the shared edit model.

*Additions*

- `TextCodeAction` carries one action's title, protocol kind, preferred flag, and its
  `TextWorkspaceEdit`; `TextCodeActionRequest` carries the document, its content, and the
  zero-based range (negative coordinates are clamped to zero).

**Clean-before-publish review resolution (2026-09-19)**

*Breaking changes*

- `TextCompletionItemKindConversion` and `TextDocumentSymbolKindConversion` moved out of this
  protocol-free package to the protocol boundary (`Nickelony.LanguageServer.Client.Interop`).
  `TextDocumentSymbolKindConversion.FromLspKind` now rejects a value outside the protocol range
  (`ArgumentOutOfRangeException`) and exposes `TryFromLspKind` for callers that choose their own
  fallback; the completion bridge keeps the protocol's documented `Text` fallback.
- `TextSnippetPlaceholder.HasChoices` was deleted (no producer or reader).
- `TextCompletionSessionDecision` enforces its state invariant: a decision that carries items
  supplies both replacement offsets, and a decision without items carries no offsets; the
  constructor and init accessors throw `ArgumentException`/`ArgumentOutOfRangeException` for
  anything else. A decision may still request a close and carry a replacement (the close is applied
  first).
- `TextCompletionTextEdit` validates the insert/replace relation (the replace range must start at
  the insert range's offset and contain it) in its constructor and init accessors; `TryCreate`
  offers the non-throwing path for tolerant parsers, and the Lua parser skips an edit whose pair
  violates the relation instead of failing the item.
- `TextDiagnostic` no longer clamps: a negative start offset or an end offset before the start
  throws, and a blank message is rejected. `TextHoverInfo` rejects blank content and an undefined
  `TextMarkupKind`; `TextDefinitionRequest` accepts a blank symbol name as the no-symbol-at-position
  state (providers resolve it to no definition) while still rejecting a null name;
  `TextCompletionItem.Priority` rejects a non-finite value; `TextSemanticToken` trims and
  deduplicates modifier names and rejects null or blank elements; `DiagnosticHitTester` rejects
  negative offsets.

*Fixes and improvements*

- `TextSnippetExpander` parses choices by the reference grammar: choice text runs to the first
  unescaped `|}`, so a bare `}` is plain choice text (`${1|a}b|}` expands to the choice `a}b`), a
  nested construct inside choice text stays literal, a malformed choice keeps scanning inside the
  construct, and nesting deeper than 32 levels stops expanding instead of exhausting the stack.
  The expansion orders the final stop `$0` last (was: first).
- `TextCompletionItem.WithResolvedContent` returns the current item when the resolved item
  contributes nothing (matching the sibling `With...` methods); `TextCompletionFilter` returns its
  freshly built match list without another copy; the session kernel validates the caret before
  materializing the document snapshot; `TextCompletionRequestSession` serializes admission,
  disposal, and current-state checks, so a request admitted concurrently with disposal is canceled
  by it or rejected with `-1`, never left pending.
- Documentation: the completion-item merge rules are documented on the API (the README table
  mirrors them), hover evaluation precedence matches the controller behavior, the LSP mapping
  table moved to the client bridge, and the README lost its maintainer/process notes and its
  duplicated position-model, filter, empty-result, and merge-claim passages.

*Tests*

- New coverage for choice text containing `}`, nested constructs in choice text, malformed choices
  followed by valid ones, the nesting limit, `$0` ordering, decision-state validation, text-edit
  range validation and `TryCreate`, diagnostic/hover/definition/semantic-token validation, and
  non-retrigger signature context; the LSP kind-mapping tests moved to
  `Nickelony.LanguageServer.Client.Tests`.

**Fresh review round 2 resolution (2026-09-19, `__180923_intellisense-fresh-review-round2.md`)**

*Breaking changes*

- `TextCompletionSessionDecision` is constructor-only (`Items`/`StartOffset`/`EndOffset` are
  get-only), rejects an empty item list, and gains the `NoMatches` state with
  `AllItemsFilteredOut` for a word that filtered every candidate out; the kernel returns
  `NoMatches` instead of `None` for that case.
- `TextCompletionTextEdit.InsertRange`/`ReplaceRange` are get-only (the order-dependent
  init-accessor validation is gone); `NewText` stays assignable with a `with` expression.
- `TextCompletionRequestSession.BeginRequest` admits outside the session lock (the coordinator
  cancels the superseded token before the lock is re-taken), and the new `CanPublish(token)`
  combines current-state and cancellation in one call.
- `TextSignatureHelpContext` rejects an undefined `TextSignatureHelpTriggerKind`;
  `TextSemanticToken` rejects a blank type name.
- `TextCodeActionContext`/`TextCodeActionRequestState` are explicit record structs whose
  constructors validate offsets against the document and reject a reversed selection or range;
  `TextCodeActionItem` defaults `kind`/`isPreferred` and normalizes a blank kind to `null`.
- `TextDiagnostic` uses structural value equality (severity, message, source, code, and span)
  and trims `Source`/`Code`; `TextDiagnosticsRequest` gains the optional opaque `DocumentId`.
- `TextCompletionItem` merge rules: `Kind` and `InsertTextFormat` are adopted when the resolved
  item explicitly set them (unset behaves as absent), so a resolve response can reset either
  field; `TextDefinitionLocation.DocumentId` and `TextHoverInfo.SymbolName` are trimmed with
  blank treated as absent.
- `TextCompletionPresentationState` member renames: `HasOpenWindow` -> `IsListVisible`,
  `IsToolTipVisible` -> `IsDetailVisible`, `ToolTipContent` -> `DetailContent`;
  `TextHoverEvaluationState.CanShowToolTip` -> `CanShowHoverContent` (the hover content, not the
  tooltip control, is what the flag gates).
- `TextDocumentSymbol.HasChildren` was removed (a derived convenience with no library reader);
  consumers use `Children.Count > 0` (Tomb Editor's document-outline converter was migrated).
- `TextSignatureHelpRequest` reuses the shared snapshot-offset guard (the hover/signature
  request records no longer duplicate the validation).

*Fixes and improvements*

- `TextSnippetExpander`: tabstop digits are ASCII-only (a non-ASCII digit no longer becomes a
  bogus index), an unterminated nested choice no longer splits the enclosing construct
  inconsistently (the boundary scan and the expansion path share one rule, and a bare `|`
  rejects the choice), and a remainder without any close brace expands in a single linear pass;
  the remarks now match the implementation.
- `DocumentSymbolProjection<TItem>` gains optional `RangeSelector`/`SelectionRangeSelector`/
  `DetailSelector`; `TextDocumentSymbol` reports the `Children` property name in its exception;
  the definition-provider contract documents the blank-symbol no-symbol obligation; hover and
  definition containment is documented as a provider contract (not validated).
- `DiagnosticHitTester` avoids per-query closure allocations; `TextSemanticToken` types are
  documented as producer-supplied; `TextMarkupKind` and the kind-conversion bridge document
  their mappings host-neutrally; `SemanticTokenConversion` skips tokens without a type name.
- New internal helpers `SnapshotOffsetValidation` and `OptionalText` centralize the shared
  validation and normalization; the AvalonEdit hover controller lost its dead blank-content
  guards; the README documents the vocabulary policy, the corrected merge table, the equality
  exceptions, and the diagnostics document identifier; the editor-binding guide documents
  blank parameter labels.

*Tests*

- New/updated coverage: decision `NoMatches`/empty-list/parameter-name rules, session
  `CanPublish`/post-dispose/never-issued identifiers, text-edit `with` semantics, non-ASCII
  snippet digits, malformed-choice consistency, the brace-free fallback, `Priority` finiteness,
  merge explicit-reset cases, hover blank/undefined guard rejections, definition request
  equality and `DocumentId` trimming, diagnostic equality and trimming, hit-tester
  empty/reversed/negative guards, code-action offset validation and item defaults,
  semantic-token blank-type rejection, and signature trigger-kind rejection; the
  whitespace-hover host test and the unknown-trigger Lua tolerance test were removed with the
  now-unrepresentable states.

### Nickelony.LanguageServer.Client

**Breaking changes**

- The one-based range model was removed: `OneBasedDocumentRange`, `ProtocolTextRangeConversion`,
  and `ProtocolRangeConversion.TryGetOneBasedRange`/`TryGetOneBasedLineAndColumn` are gone.
  `ProtocolRangeConversion.TryGetTextPositionRange` and `TryGetTextPosition` convert LSP payloads
  directly into the shared zero-based `TextPositionRange`/`TextPosition` (validating non-negative
  coordinates without changing their basis).
- `DefinitionTargetResponse.TargetRange`/`SelectionRange` are now zero-based `TextPositionRange`
  values; the JSON wire form is unchanged.

**Changes**

- Folder slices were reorganized (`Transport/`, `Protocol/`, `Interop/`, `Workspace/`,
  `Pathing/`, `Documents/`, `SemanticTokens/`); the package keeps its single flat namespace, so
  no code changes are required. Wire DTOs and their converters live under
  `Protocol/<Feature>/`.
- `TextCompletionItemKindConversion.FromLspKind` maps an unrecognized protocol kind to
  `Generic` (the presentation fallback) instead of `Text`, so unsupported kinds no longer
  masquerade as the concrete `Text` kind (2026-09-19 IntelliSense round-3 review).

**Additions**

- Provider-neutral helpers moved here from the Lua provider and are public documented API:
  `LanguageServerRequestDispatcher`, `WorkspaceSnapshotTracker` with `WorkspaceSnapshotEntry`,
  and `SignatureLabelParser`.

**Fresh review resolution (2026-09-15, `__150917_` Client review)**

*Breaking changes*

- `WorkspaceSnapshotTracker` validates its constructor arguments (null specifications and a blank
  workspace root throw) and `ReplaceTrackedSnapshotWithCurrent` returns a detached clone, so a
  caller's captured baseline can no longer be mutated by later tracked changes.
- `LanguageServerPaths.TryNormalizeLocalPath` accepts a nullable path and returns `false` for
  `null` instead of throwing, matching the package's other `Try*` contract.
- `ProtocolRangeConversion.TryGetTextPositionRange` rejects an inverted protocol range (end before
  start) instead of converting a malformed payload into a real edit; `SemanticTokenConversion`
  clamps a token's length to the remaining line text and skips tokens that leave an empty range.
- `SnippetPlaceholderParser.Strip` rejects a `null` snippet and changes its output for nested
  placeholders, snippet escapes, and choice placeholders (it inserts the first choice).
- `TrackedDocumentStore<TTrackedDocumentState>.CreateTrackedDocumentState` now takes a single
  `TrackedDocumentInitialState` record instead of eight scalar parameters.

*Fixes*

- `WorkspaceFileWatcher`: disposal initiated from the final-flush callback throws
  `InvalidOperationException` as documented instead of deadlocking; a throwing lifetime-cancellation
  callback can no longer hang every disposal caller; the dispose-versus-requeue decision is atomic,
  so a failed retry batch is always requeued or flushed instead of silently dropped; the escalation
  notification runs outside the dispatch operation, so the owner can dispose the failed watcher from
  the callback; `Start` resets the failure ladder so a restarted watcher notifies again; and the
  declared 5 s retry cap is now reachable.
- `WorkspaceChangeDebouncer` bounds dispatch latency for sustained change streams (2 s) and never
  shortens a pending retry backoff; `WorkspaceFileWatcher` forwards only rename endpoints that still
  match the watch pattern and includes file-size changes in the notify filter.
- `SnippetPlaceholderParser` handles nested placeholders (`${1:foo ${2:bar}}`), `\$`/`\}`/`\\`
  escapes, and choice placeholders; previously nested placeholders and escapes were corrupted.
- `LanguageServerCapabilityStore.TryMarkTransportUnhealthy` is idempotent per generation, so one
  transport loss raises `TransportUnavailable` once.
- `SemanticTokensDecoder` no longer aliases modifier bits for legends with more than 32 modifiers;
  `CompletionResponseJsonConverter` tolerates duplicate property names and malformed default payloads
  instead of failing the whole response; `SignatureLabelParser` returns `false` for non-numeric
  offset elements instead of throwing.
- `TrackedDocumentStore`: a stale or double request release no longer evicts an idle server-open
  record, and the incremental change range is computed outside the store lock.
- Signature-help responses tolerate malformed signature and parameter elements (they are skipped
  with a warning) instead of failing the whole response, matching the completion converter.
- `DocumentOperationScheduler`: superseding a queued latest-only update can no longer cancel an
  update that just started running.

*Changes*

- `Interop/` now holds every IDEKit-typed public member (the definition-target bridge moved there);
  the README documents the exact dependency boundary and the package limitations (no dynamic
  registration, no custom launcher or transport seam).

*Additions*

- Focused tests for `SnippetPlaceholderParser`, `SignatureLabelParser`, the tolerant signature-help
  converters, and `LanguageServerRequestDispatcher` (timeout ladder, threshold marking, retry and
  fallback paths through a controllable `ILanguageServerClient` double), plus regression tests for
  the watcher dispose/retry/escalation contracts, per-generation transport invalidation, and the
  protocol edge cases.

**Event-pattern alignment (2026-09-15)**

*Breaking changes*

- `DiagnosticsPublished` and `TransportUnavailable` are now `EventHandler<TEventArgs>` events that
  raise with the client as the sender: the diagnostics payload is exposed as
  `eventArgs.Parameters` on `DiagnosticsPublishedEventArgs`, and the lost transport generation as
  `eventArgs.Generation` on `TransportUnavailableEventArgs`.

**Client fresh review resolution (2026-09-16, `__150922_` Client review)**

*Breaking changes*

- `LanguageServerClientOptions.RequireTextDocumentSynchronization` (default `true`) replaces the
  unconditional startup rejection of servers without full/incremental `textDocumentSync`; set it to
  `false` to accept servers that do not support text synchronization.
- The initialize payload now forces `dynamicRegistration = false` on every declared `workspace` and
  `textDocument` capability, because this client never services dynamic registration requests; the
  previous fixed capability list is gone.
- `SendNotificationAsync` accepts `null` parameters (notification without parameters) and rejects
  blank method names; `LanguageServerRequestDispatcher` validates its constructor and `SendAsync`
  arguments instead of clamping or failing later.
- `WorkspaceFileWatcher.IsDisposed` is public; the bool `Start()` overload was removed (use
  `Start(out Exception?)`); `WorkspaceWatcherStartStatus.None` is the new zero value, so a default
  status no longer claims a successful start.
- `PublishDiagnosticsParams.Diagnostics` is a read-only `IReadOnlyList<DiagnosticPayload>`.
- `TrackedDocumentStore.PrepareForRestart` snapshots must be replayed through the new
  `TryReopenTrackedDocument(string)`, which reopens only documents that are still tracked with an
  open-editor reference and never creates records; a case-only rename on a case-insensitive host now
  still updates the tracked content.
- The `Nickelony.LanguageServer.Lua.Tests` internals grant was removed from this package.

*Fixes*

- A late `initialize` completion can no longer republish capabilities for a transport generation that
  was already marked unhealthy.
- Session teardown bounds the graceful `exit` notification by the shutdown request timeout and reads
  the process exit code before background cleanup, so a restart cannot hang on a server that stopped
  draining stdin and exit diagnostics keep their code.
- `DocumentOperationScheduler` runners yield before invoking consumer delegates (no delegate starts
  under the scheduler lock), a superseded latest-only update cannot run after its replacement, and a
  linked cancellation token is classified as cancellation.
- Diagnostics callbacks queued before `TransportUnavailable` are dropped instead of delivered after
  the transport stopped accepting callbacks.
- `DefinitionResponseJsonConverter` validates ranges through `ProtocolRangeConversion` (inverted
  ranges are rejected); `SignatureLabelParser` requires the exact two-number offset form; the
  capability converters implement `Write` with documented normalization instead of throwing.
- `DocumentReferenceTracker` reads both reference counts atomically; `TrackedDocumentStore`
  `GetOpenDocuments` returns path-ordered snapshots and `TrimIdleDocuments` returns early when
  nothing can be evicted.
- Dead members removed (`_shutdownRequestTimeout`, `TrackedDocumentCountCore`,
  `WorkspaceChangeDebouncer._scheduledDueTimestamp`) and documentation corrections across the
  transport-unavailable and threading contracts, the README examples, and the watcher/snippet/path
  remarks.

*Additions*

- Internal scripted-session startup seam with end-to-end tests for `StartAsync` success and restart,
  plus regression tests for invalidation-aware capability capture, post-invalidation callback
  dropping, guarded restart replay, case-only rename content, capability serialization, and the sync
  option.
- Consumer follow-through: `Nickelony.LanguageServer.Lua` replays restart documents through the
  guarded reopen, keeps the newest pending document update across a transport failure, and its test
  suite drives watcher recovery through the owner callback instead of client internals.

**Backlog execution (2026-09-16, phase 4 - RI-2/RI-3)**

*Breaking changes*

- `WorkspaceFileWatcher.DisposeWithoutFinalFlush` was removed. Disposal is now one idempotent,
  non-blocking teardown that can be initiated from any context (including the dispatch callback):
  the lifetime token is cancelled so in-flight dispatches can unwind, and changes that are still
  buffered or scheduled for a retry are dropped instead of being flushed once at dispose time.
- The watcher's dispatch retry is bounded: a failed dispatch is retried with exponential backoff
  (250 ms, then 500 ms); when the final attempt fails, the watcher stops watching, drops the pending
  changes, and notifies the owner once so it can dispose this watcher and create a replacement. The
  previous escalation ladder (warn at 3, escalate at 5, retry forever at a clamped backoff,
  dispose-time flush, and reentrancy guard) is gone.

*Fixes*

- Workspace watching forwards a case-only replacement instead of collapsing it: a create of a new
  casing plus a delete of the old casing within one debounce window now forwards both occurrences
  with their own casing, so the server can learn the casing change.
- `WorkspaceSnapshotTracker` capture includes hidden and system files (matching what the
  file-system watcher observes) and excludes reparse points (symbolic links and junctions), so
  capture can no longer diverge from the watched set or escape the workspace through links.
- `WorkspaceSnapshotTracker.ApplyChanges` re-validates the snapshot version before applying:
  updates computed against a snapshot that a concurrent capture replaced are skipped instead of
  clobbering the newer capture. Enumeration races (a directory removed mid-capture) are contained
  and reported at debug level instead of throwing out of provider start or recovery; `ApplyChanges`
  and `BuildDeltaBatch` validate their arguments.
- `WorkspaceFileWatcher` admission checks consult disposal in the dispatch path itself: a dispatch
  that starts or resumes after disposal began is skipped, or its drained batch is dropped with a
  debug log, instead of being forwarded after disposal. A partially constructed `FileSystemWatcher`
  is disposed when activation fails instead of leaking its 64 KiB buffer, the failure notification
  is serialized against disposal so the owner callback cannot run after `Dispose` returned, and the
  consecutive-failure counter uses one synchronization discipline.
- `DocumentOperationScheduler`: the per-document tail, latest-update chain, exclusion barrier, and
  update registration of a path now live in one queue object with generation-checked cleanup
  (replacing four dictionaries and their duplicated clear-if-current helpers). The public surface
  and behavior are unchanged, including the yield-before-delegate, supersede-cancel-under-lock,
  and slot-identity guarantees.

*Changes*

- Watcher recovery is snapshot-driven: the owner replaces a failed watcher and reconciles missed
  changes through `WorkspaceSnapshotTracker` instead of relying on in-watcher replay.
- Removed the watcher's `AsyncLocal` reentrancy guard, dispose finalization gate, deferred-replay
  set, and dual disposal modes; the class remarks document the bounded retry and disposal
  contracts, and the Lua coordinator disposes failed and replacement watchers through the single
  `Dispose` surface.

**Backlog execution (2026-09-16, phase 5 - protocol type-safety sweep)**

*Breaking changes*

- Protocol enum fields are typed and keep their protocol numbers on the wire: `CompletionContextPayload.TriggerKind` is a `CompletionTriggerKind`, `CompletionItemPayload.Kind` a nullable `CompletionItemKind`, `CompletionItemPayload.InsertTextFormat` a nullable `InsertTextFormat`, `DiagnosticPayload.Severity` a nullable `DiagnosticSeverity`, and `WindowMessageParams.Type` a nullable `MessageType`. Nullability is unchanged, and an unknown numeric value stays representable as an unnamed enum member.
- `ProtocolNullablePosition` was deleted and `ProtocolRangePayload` now carries required `ProtocolPosition` start and end values: a range that is missing an endpoint or a coordinate fails deserialization instead of degrading into a partially valid range (`ProtocolRangePayloadJsonConverter`). `ProtocolRangeConversion.TryGetTextPosition` takes a `ProtocolPosition` and still rejects negative coordinates.
- `TextDocumentUriPayload` was deleted; `WorkspaceDocumentChangePayload.TextDocument` is a `TextDocumentIdentifier?`, so the protocol surface has one document-identifier shape.
- `CompletionTextEditPayload` is an abstract union base with `CompletionRangeTextEditPayload` (classic `range` shape) and `CompletionInsertReplaceTextEditPayload` (`insert` plus `replace` shape). A payload that mixes the shapes, carries only one insert/replace range, or carries none fails deserialization with `JsonException`; `NewText` stays optional in both shapes.

*Fixes*

- `CompletionResponseJsonConverter` now honors its documented element contract for object-shaped items: an item whose members do not bind (an illegal text-edit union, a partial range, a wrong-typed member) is skipped with a warning instead of failing the whole completion response.
- `PublishDiagnosticsParams` deserialization drops malformed diagnostic entries individually with a warning instead of failing the whole notification, and a non-object payload degrades to an empty payload (`PublishDiagnosticsParamsJsonConverter`); the transport registers the converter with its logger.

*Changes*

- The diagnostics, reference, and workspace-edit payloads bind strict range values, so a partial position is a malformed payload that fails loudly at the transport boundary instead of surfacing as a null-ed position.

**Backlog execution (2026-09-16, phase 6 - snippet insertion)**

*Breaking changes*

- `SnippetPlaceholderParser` and `SnippetTextResult` were removed. Snippet text passes through the
  client verbatim (its `insertTextFormat` is a typed `InsertTextFormat` member), so the lossy
  strip-and-caret model is gone; expansion belongs to the consuming host, and the IntelliSense
  package's `TextSnippetExpander` replaces the parser with a full placeholder model (the old
  parser's focused tests migrated there).

**Hardening program (2026-09-16, round 2 phase 4)**

*Documentation*

- De-duplicated XML documentation: the capability properties (`TextDocumentSyncKind` and the
  `Supports*` family) and `StartAsync` inherit their contracts from `ILanguageServerClient`
  instead of restating shortened copies, the forwarder methods inherit the send contracts
  (including their exception lists) instead of repeating them, and the interface remarks no
  longer restate the event members' delivery remarks.

**Completion protocol depth (2026-09-17, phase 1)**

*Additions*

- `CompletionItemPayload` models the completion-item fields that were previously dropped:
  `Tags` (typed `CompletionItemTag` values; `Deprecated = 1`, unknown values stay representable),
  `CommitCharacters`, and `AdditionalTextEdits` (plain `TextEditPayload` edits carrying explicit
  replacement text). The `itemDefaults` path already copied `commitCharacters`; it now lands on the
  typed member, and resolve requests round-trip the new members.

**Signature fidelity (2026-09-17, phase 3)**

*Additions*

- `SignatureHelpParams`, `SignatureHelpContextPayload`, and the `SignatureHelpTriggerKind` enum
  (protocol numbers, unknown values representable) model the `textDocument/signatureHelp` trigger
  context: trigger kind, trigger character, the retrigger flag, and the optional active payload. The
  `activeSignatureHelp` member reuses the typed `SignatureHelpResponse` shape, and every optional
  member is omitted when unset.

*Changes*

- `SignatureHelpParameterPayload.Documentation` is omitted when null instead of being written as a
  JSON null, so a reconstructed signature-help payload stays spec-shaped; reading is unchanged.

**F6 multi-root (2026-09-18, phase 2 - client core)**

*Breaking changes*

- `LanguageServerClient` takes a workspace root list (`IReadOnlyList<string>`) instead of a single
  root path. The list is validated (null, empty, whitespace-only, and duplicate entries are
  rejected after normalization) and every entry is advertised in caller order through
  `workspace/workspaceFolders`, with the first entry as the primary `rootUri`. There is no
  single-root overload.
- `LanguageServerClientOptions.ClientCapabilitiesProvider` and `InitializationOptionsProvider` are
  now `Func<IReadOnlyList<string>, object?>` delegates receiving the normalized roots in caller
  order; the first entry is the primary root.
- `LanguageServerRequestDispatcher`'s `workspaceRootDirectoryPath` parameter is renamed to
  `workspaceRootsDisplayText` (a comma-joined display string for diagnostics); behavior is
  unchanged.

*Additions*

- Multi-root workspace folders: the `initialize` payload and the `workspace/workspaceFolders` server
  callback advertise the full folder set (URI and drive-root-aware display name per root). Workspace
  folders are fixed at construction time; `workspace/didChangeWorkspaceFolders` is not sent.

**Document symbols (2026-09-18, phase 4)**

*Additions*

- New `Protocol/DocumentSymbols/` payload family: `DocumentSymbolParams`, the tolerant
  `DocumentSymbolsResponse`/`DocumentSymbolPayload` pair (hierarchical `DocumentSymbol` and flat
  `SymbolInformation` shapes share one payload; malformed entries are skipped with a warning, a
  range whose end is missing or malformed degrades to an empty range at its start, and negative
  coordinates remain representable for the consuming conversion to clamp), and
  `SymbolLocationPayload` for flat entries.
- `documentSymbolProvider` is captured from the server capabilities: `ILanguageServerClient`
  gains `SupportsDocumentSymbols`, which returns false until the active transport negotiates the
  capability, mirroring the existing `Supports*` flags.

**Code actions (2026-09-18, phase 6 groundwork)**

*Additions*

- New `Protocol/CodeActions/` payload family: `CodeActionParams`/`CodeActionContextPayload` (the
  request context carries the diagnostics currently reported for the requested range), the
  tolerant `CodeActionsResponse`/`CodeActionPayload` pair (literal `CodeAction` entries with an
  inline `edit` are kept; command-only entries are skipped with a warning because
  `workspace/executeCommand` is not supported, and a malformed edit drops its entry with a
  warning), and reuse of `WorkspaceEditResponse` for the nested edit.
- `codeActionProvider` is captured from the server capabilities: `ILanguageServerClient` gains
  `SupportsCodeActions`, which returns false until the active transport negotiates the capability,
  mirroring the existing `Supports*` flags.

**Fresh review resolution (2026-09-19, `__180923_` Client review)**

*Breaking changes*

- `TrackedDocumentStore<TTrackedDocumentState>.Rename` is a no-op when both paths identify the same
  file (for example a case-only rename on a case-insensitive host): it no longer rekeys the record
  or advances tracked content, so the tracked text always equals the text the server received.
  Deliver content through `Synchronize` instead.
- `DefinitionResponse.Targets` carries protocol-native `DefinitionTargetPayload` values with
  `ProtocolRangePayload` ranges (the IDEKit-typed `DefinitionTargetResponse` is gone); hosts convert
  through `ProtocolRangeConversion`, and the `Protocol/` slice no longer exposes IDEKit types.
- `LanguageServerRequestDispatcher` and `WorkspaceFileChangeForwarder` moved to the
  `Nickelony.LanguageServer.Provider` package (namespace `Nickelony.LanguageServer.Provider`).
- `TrackedDocumentState` takes a single `TrackedDocumentInitialState` in its constructor;
  `DocumentOperationScheduler.QueueLatestUpdateAsync` was renamed to `EnqueueLatestUpdateAsync`;
  `SemanticTokensDecoder.Decode` accepts `IReadOnlyList<int>`;
  `WorkspaceDocumentChangePayload.TextDocument` is an `OptionalVersionedTextDocumentIdentifier`
  that round-trips the server-sent version; `WorkspaceChangeAccumulator` is public and exposes a
  single `DrainBatch()` drain shape; `WorkspaceFileWatcher` copies its watch-specification list.

*Fixes*

- Completion-list `itemDefaults.editRange` builds the default edit text as `textEditText ?? label`
  (LSP 3.17); `insertText` is no longer used.
- Reference and formatting response arrays tolerate null or malformed elements (skipped with a
  warning) instead of failing the whole response, matching the other list converters.
- Disposal hardening: a throwing lifetime-cancellation callback can no longer abort client teardown;
  a disposal that races session activation tears the session down instead of activating it; a
  disposal-raced `ObjectDisposedException` in `LanguageServerRequestDispatcher.SendAsync` returns the
  documented fallback; a throwing subscriber-failure logger can no longer abort a drain or escape a
  pool callback; the semantic-token delta cursor accumulates in 64-bit arithmetic so malformed
  deltas cannot wrap onto a wrong in-range line; pump-task reads are published with a memory barrier.
- `WorkspaceChangeDebouncer` no longer lets a superseded timer callback clear the newer schedule's
  retry backoff (an early platform-timer tick is re-armed for the remaining delay instead of being
  dropped), and scheduler generations are 64-bit.

*Changes*

- `SemanticTokensDecoder` freezes one modifier list per modifier mask and `SemanticToken` adopts it
  through an internal factory, so decoded tokens with the same mask share the list instead of copying
  it per token (the per-mask cache previously cached only the computation).

*Documentation*

- Corrected the missing-severity, duplicate-normalization, window-message-severity, and
  configuration-scope claims; documented the disposal-drop semantics of the file-change forwarder;
  the package README now links to `https://github.com/Nickelony/LanguageServer`, lists the missing
  capability, and separates server events from the outbound settings push.

*Additions*

- `OptionalVersionedTextDocumentIdentifier` and `TolerantCollectionJsonConverter<TElement>`, plus
  focused tests for request wire shapes, the RPC-target callbacks, the scripted mid-handshake
  invalidation path, real-watcher rename forwarding, completion-resolve capability capture, and the
  tolerant list converters.
- `TextCompletionItemKindConversion` and `TextDocumentSymbolKindConversion` (Interop) bridge
  protocol `CompletionItemKind`/`SymbolKind` numerics onto the shared IDEKit vocabularies, moved
  from `Nickelony.IDEKit.IntelliSense` to complete the Interop boundary rule; the symbol bridge
  rejects out-of-range values and offers `TryFromLspKind`.

**Fifth fresh review resolution (2026-09-19)**

*Breaking changes*

- `CodeActionPayload` now keeps every entry with a usable title and at least one of an edit or a
  command: command-only entries are no longer dropped (the host owns the `workspace/executeCommand`
  policy), an edit-bearing action keeps its `Command` payload, and the opaque `Data` member is
  preserved as a detached clone for `codeAction/resolve` hosts. `CodeActionCommandPayload` is new.
- `WorkspaceDocumentChangePayload` gained `Options` (`WorkspaceResourceOperationOptionsPayload`)
  with the create/rename/delete flags; `TextEditPayload` gained `AnnotationId` for annotated edits;
  `DefinitionTargetPayload` gained `OriginSelectionRange`.
- `LanguageServerPaths.CreateFileUri` builds the file URI from the normalized path shape: rooted
  POSIX paths keep exactly three slashes instead of being parsed as UNC authorities on non-Windows
  hosts, and extended-length prefixes are stripped before building.

*Fixes*

- `WorkspaceSnapshotTracker.ReplaceTrackedSnapshotWithCurrent` clones the baseline under the
  snapshot lock, removing an enumeration race with in-place `ApplyChanges` commits during watcher
  recovery.
- Disposing a session before its JSON-RPC transport was attached now terminates the live process
  directly instead of leaving it until host exit.
- `WorkspaceDocumentChangePayload.IsResourceOperation` is `[JsonIgnore]`-ed so computed state can
  no longer leak into serialized protocol payloads.
- An outgoing `workspace/didChangeConfiguration` payload is normalized to the serialized settings
  element the cache and the `workspace/configuration` callback share, so both channels always use
  the same member casing.
- `DocumentOperationScheduler.CancelQueuedUpdate`/`CancelAllQueuedUpdates` reach an update that a
  superseding enqueue removed from the slot while it kept running.
- A client-capabilities provider result that does not serialize to a JSON object falls back to
  empty capabilities instead of sending a protocol-invalid `capabilities` member.
- Snapshot capture uses the same simple wildcard grammar as the watch-specification matcher.
- The `LanguageServerTransportUnavailableException` default message and the server-argument
  validation parameter name are corrected.

*Additions*

- Test coverage: scripted request-success path, settings-casing push/pull parity, disposal-race
  process termination, cancel-of-superseded-running update, tracker-race smoke, per-shape file-URI
  construction, `#`/`?`/Unicode escaping round trips, annotated edits, resource-operation options,
  command/data retention, capability fallback, global-queue serialization, and definition-link
  origin ranges; the flake-prone wall-clock bounds were widened and the real-watcher-error probe
  now tolerates missed OS events.
- Documentation: completion/symbol-kind fallback wording aligned (the protocol defines no default
  kind), release-contract and close-result corrections, a canonical JSON-null clause, host
  terminology unified, README folderless-session and `LastStartupException` corrections, and the
  malformed-root policy spelled out for the tolerant collection readers.

**Client review resolution - second pass (2026-09-19, fresh full review)**

*Breaking changes*

- `DocumentOperationScheduler` is a single ordered chain per document: the separate latest-update
  chain and the exclusion-barrier machinery are gone, exclusive operations join the affected
  paths' chains directly, and a re-entrant enqueue or wait for the caller's own document path
  throws `InvalidOperationException` instead of deadlocking the chain. The public member set is
  unchanged. This fixes the reproduced permanent deadlock between an in-flight latest update and a
  rename (the provider's `EnqueueLatestUpdateAsync` delegate nesting `EnqueuePerDocumentAsync`);
  `Nickelony.LanguageServer.Provider` now synchronizes in the latest-update slot directly.
- `SemanticTokensDeltaParser.ApplyEdits`, `SemanticTokensDeltaState`, and
  `SemanticTokensDecodeResult` were deleted as unproduced surface (the parser keeps normalizing
  full and delta responses through `Parse`); `MessageType` is now internal.
- `DocumentSymbolPayload.Kind` is the typed `SymbolKind` enum; `SymbolLocationPayload.Uri` is
  non-nullable; `SemanticToken.HasModifier` accepts a nullable modifier and trims it (matching
  `TextSemanticToken.HasModifier`); `LanguageServerPaths.GetPathKeyFromNormalizedPath` is gone
  (the diagnostics router compares document keys with the configured local-path comparer).
- Serializing a definition target or a symbol location without a URI throws instead of writing a
  JSON `null` member; `PublishDiagnosticsParamsJsonConverter` skips an absent optional `version`.
- `ILanguageServerClient` gains `SupportsHover`, `SupportsDefinition`, and `SupportsSignatureHelp`.

*Fixes*

- `DefinitionResponseJsonConverter` validates the JSON kind before `TryGetInt32`, so a non-numeric
  `line`/`character` value is skipped with a warning instead of throwing `InvalidOperationException`
  out of the tolerant path; the tolerant element readers also treat `JsonElement` access exceptions
  as malformed input.
- Transport: the server's stderr stream is owned and disposed with the session; the stderr read
  loop drains lines buffered at process exit instead of dropping the crash tail; lifecycle timeouts
  above the platform timer limit are rejected at assignment instead of failing every startup;
  `StartAsync` publishes the observed cause through `LastStartupException`.
- `WorkspaceChangeDebouncer.Stop` clears its scheduling state so a later `Queue` arms a fresh
  dispatch, and `Requeue` replays the failed batch ahead of newer changes (an older delete can no
  longer cancel against a newer create and forward nothing).
- `WorkspaceFileWatcher` reports one failure per sequence when a watcher error and a dispatch
  escalation race; `Start` returns `Disposed` when disposal wins while watchers are created; watch
  specifications are validated at construction (null, rooted, or separator-bearing filters are
  rejected before the watcher is configured).
- `WorkspaceChangeAccumulator.AddRange` validates its argument and ignores whitespace paths as
  documented; a delete/create pair refreshes the preserved create casing; `WorkspaceSnapshotTracker`
  copies its specification list, ignores change entries without a usable path, and clones
  `ReplaceTrackedSnapshotWithCurrent` under the snapshot lock.
- `TrackedDocumentStore.Rename` keeps the record reachable when a derived mutation throws, and the
  rename hook runs after the rekey is committed; `Synchronize` documents the commit-order contract
  for callers.
- `LanguageServerPaths.TryNormalizeLocalPath`/`TryGetFilePath` no longer swallow fatal exceptions,
  and `NormalizeLocalPath` documents the Unicode-normalization caveat.

*Changes*

- `LanguageServerClientOptions` gains `ServerWorkingDirectory` (default: the executable's
  directory); request/response logging wording, the scheduler summaries and remarks, and the
  converter write-path docs were corrected to match behavior.

*Additions*

- `LanguageServerClient.LastStartupException` exposes the most recent startup failure without log
  scraping.
- Real-process transport tests over a compiled fake server: process spawn, real stdio handshake,
  stderr capture, graceful shutdown, crash before the handshake, and a missing executable - the
  process layer is no longer exercised only through mocks.
- Capability tests for the three new providers, options tests for the timeout bound and
  `ServerWorkingDirectory`, scheduler regression tests for the rename-versus-update interleaving
  and the fail-fast re-entrancy guard, and a pinned maximum-latency assertion for the debouncer.

**Provider review resolution - second pass (2026-09-19, `__180923_` Provider review)**

*Breaking changes*

- `LanguageServerRequestRejectedException` (public sealed, carries the server's JSON-RPC
  `ErrorCode`): the request forwarder translates a server error response into this type instead
  of letting StreamJsonRpc's exception escape, so a rejection is distinguishable from a transport
  failure - the transport stays ready, and consumers can map the outcome onto their documented
  fallback.
- `TrackedDocumentStore<TTrackedDocumentState>.TryClose` returns the `DocumentCloseResult` enum
  (`Untracked`, `Closed`, `StillOpen`, `BusyWithRequests`) instead of a Boolean, so a caller can
  distinguish a completed close, a document that stays open for editor references, and one whose
  close is deferred until its last request reference drains.

*Tests*

- The close-result matrix and the request-rejection translation are covered (suite: 472 tests,
  469 passed / 3 skipped).

**Second fresh review resolution (2026-09-19, fresh Provider review)**

*Breaking changes*

- `TrackedDocumentStore<TTrackedDocumentState>.WithTrackedDocument` renamed its fallback
  parameter to `fallbackValue` (family-wide naming consistency); derived stores using the named
  argument must update (Lua updated in-wave).

*Additions*

- `DocumentRequestReference` (public sealed): identifies one temporary request-driven reference
  acquired from a tracked document. `Synchronize` takes an optional `requestReference` and binds
  it while the store lock is held, and the new `ReleaseRequest(DocumentRequestReference)`
  overload releases the reference by identity - after a rename rekeyed the record, and even when
  a store hook threw after the acquisition - exactly once.

*Tests*

- Identity release across a rename and single-release semantics are covered in the store suite.

**Third fresh review resolution (2026-09-19, `__180919_` fresh review)**

*Breaking changes*

- `Protocol/References/ReferenceResponse.cs` renamed to `ReferenceLocationPayload` (one reference
  location, not a response); the Lua parser and the tolerant reference-array registration follow.
- Client capabilities and settings payloads are normalized to the protocol's lower-camel convention:
  a consumer capability object such as `new { HoverProvider = true }` is sent as `hoverProvider`, and
  the `workspace/didChangeConfiguration` push now uses the same casing as the cached
  `workspace/configuration` responses.
- Queued scheduler delegates must not call back into `DocumentOperationScheduler`: every enqueue and
  wait member rejects nested scheduler work from inside an operation with `InvalidOperationException`
  (previously only same-path and global-chain nesting were rejected; a cross-path enqueue could
  deadlock against a queued exclusive operation). `WaitForPerDocumentOperationsAsync` follows the
  same rule.
- `WorkspaceSnapshotTracker` rejects an empty watch-specification list, and watch specifications
  reject empty or whitespace-only filter patterns.
- `LanguageServerTransportUnavailableException`/`LanguageServerTransportChangedException` lost their
  unused `(string?, int)` constructors; `DocumentReferenceTracker` mutators are internal (the counts
  stay public) so only the store can change reference accounting.
- `HoverResponse.Contents` is `JsonElement?` (`null` when the payload carried no contents member).

*Fixes*

- `WorkspaceChangeDebouncer.DrainBatch` drains the replayed changes together with the changes that
  arrived during a retry backoff, so a change queued while a retry was pending can no longer be
  stranded until an unrelated change re-arms the timer.
- Position encoding is pinned: `initialize` always advertises
  `general.positionEncodings: ["utf-16"]` (merging into a consumer-supplied `general` object), and a
  server that selects another encoding fails the transport startup with `NotSupportedException`.
- Empty workspace root lists now model a folderless session: `rootUri` and `workspaceFolders` are
  sent as `null`, and `LanguageServerPaths.NormalizeWorkspaceRoots` accepts an empty list.
- `window.workDoneProgress` is forced to `false` when a consumer payload advertises it (the client
  acknowledges progress creation without exposing a progress sink).
- `workspace/configuration` and `workspace/workspaceFolders` callbacks use the same invalidation
  fence as diagnostics, so an invalidated transport generation is no longer answered.
- `DocumentOperationScheduler`: latest-update nodes keep the chain fault-free (a delegate failure
  surfaces on the caller's task instead of faulting the chain tail), `CancelQueuedUpdate` and
  `CancelAllQueuedUpdates` contain exceptions from cancellation callbacks, the active-operation
  context is per scheduler instance, and delegate start is gated until the enqueuing call released
  the scheduler lock.
- `TrackedDocumentStore.Rename` recovery re-keys under the state's current path, so a throwing
  derived mutation can no longer leave the dictionary key out of sync with `state.FilePath` and
  strand the record from trimming.
- Incremental change ranges are computed with one forward scan instead of two O(offset) scans.
- `StartAsync` classifies the initialize timeout by the timeout token (an unrelated
  `OperationCanceledException` is no longer reported as a timeout), the default server working
  directory is computed explicitly (the inheritable fallback is reachable for bare executable
  names), and disposal disposes the startup gate while holding it (a concurrent startup can no
  longer release a disposed semaphore from its `finally` block).
- Server stderr is decoded as UTF-8; `MarkupContentReader` drops unsafe code-fence language
  identifiers; `SemanticTokensDecoder` skips null or blank token-legend entries; the document-symbol
  writer always writes the required `kind` member.
- Tolerant converters for code actions, document symbols, and signature help are registered with the
  transport logger, so their skipped-entry warnings are no longer silently discarded.

*Additions*

- Diagnostics payloads model `tags`, `codeDescription`, `relatedInformation`, and `data` (new
  `DiagnosticTag`, `DiagnosticCodeDescriptionPayload`, `DiagnosticRelatedInformationPayload`, and
  the shared `ProtocolLocationPayload`).
- Code actions keep server-disabled entries with their `disabled.reason` and carry their
  `diagnostics`; document symbols model `tags`/`deprecated`; workspace edits model
  `changeAnnotations`.
- `ILanguageServerClient.LastStartupException` exposes the cause of the most recent failed startup
  attempt without a log scrape.
- `LanguageServerClientOptions` gained a default constructor (`settingsProvider` is optional and
  defaults to an empty payload); optional members omit JSON nulls on write (`TextEditPayload`,
  `WorkspaceDocumentChangePayload`, `DiagnosticPayload` optional members, `CompletionParams.Context`),
  and `WorkspaceEditResponse` validates `kind` for resource operations instead of treating any
  non-blank kind as one.
- Watch filters gained a dedicated matcher and filter validation; the *fourth* fresh review then
  established that the runtime watcher uses its simple wildcard grammar on every platform, and the
  matcher was corrected accordingly (see the fourth-pass entry below). `WorkspaceSnapshotTracker`
  orders concurrent applies per path by ticket and caps content fingerprinting at 16 MiB so a
  change burst cannot stall the dispatch path on very large files.

*Tests*

- Regression coverage: the debouncer delivers changes queued during a retry backoff; the scheduler
  rejects nested global/exclusive/cross-path enqueues and covers `CancelQueuedUpdate` and
  `CancelAllQueuedUpdates`; startup payloads pin UTF-16, override consumer encodings, and omit roots;
  watch specifications pin the Win32 `*.*` behavior and filter validation; diagnostics pump awaits
  are bounded so a delivery regression fails instead of hanging; the client test project and the
  fake server no longer participate in packing, and the client suite gained a session-timeout
  `test.runsettings`.

**Workspace-file forwarding failure moved (2026-09-19)**

*Breaking changes*

- `WorkspaceFileForwardingFailure` moved to the `Nickelony.LanguageServer.Provider` package, where
  the workspace-file forwarder is its only producer and consumer (the record previously stayed
  behind in this package after the forwarder was extracted).

**Fourth fresh review resolution (2026-09-19, `__190919_` fresh review)**

*Breaking changes*

- `VersionedTextDocumentIdentifierPayload` renamed to `VersionedTextDocumentIdentifier` and moved to
  `Protocol/Common/`, matching the `TextDocumentIdentifier`/`OptionalVersionedTextDocumentIdentifier`
  family.
- `LanguageServerPaths.TryGetFilePath` renamed to `TryGetLocalPath`, aligning the local-path verb set
  (`NormalizeLocalPath`, `TryNormalizeLocalPath`, `TryGetLocalPath`); the Lua parsers, diagnostics
  router, docs, and tests follow.
- `Interop/MarkupContent.cs` renamed to `ProtocolMarkupContent` to stop colliding with the LSP
  `MarkupContent` type of a different shape.
- `LanguageServerCapabilityJsonConverters.cs` split into per-converter files
  (`SupportedCapabilityJsonConverter`, `TextDocumentSyncCapabilityJsonConverter`,
  `SemanticTokensFullCapabilityJsonConverter`).
- `SemanticTokensDeltaParser.Parse` gained a logger overload and now validates parsed edits
  (present, non-negative, ordered, non-overlapping); a violating payload degrades to an empty result
  instead of passing the edits through unchecked.
- `DocumentRequestReference` is single-use: binding an already bound or released reference throws
  `InvalidOperationException`, and `TrackedDocumentStore.Synchronize` rejects a `requestReference`
  supplied without `acquireRequestReference` with `ArgumentException`.
- `LanguageServerClientOptions.ServerArguments` rejects null entries on assignment; a null
  `initialize` result is treated like a missing capabilities payload (`NotSupportedException`
  instead of `NullReferenceException`); capability enforcement no longer adds a
  `dynamicRegistration` member to objects that never declared it and now reaches nested capability
  objects such as `workspace.fileOperations.didCreate/didRename/didDelete`.

*Fixes*

- Watch specifications mirror the runtime watcher exactly: `""`/`*.*` normalize to `*` and the
  simple wildcard grammar applies on every platform (the previous matcher used Win32 `?` semantics
  on Windows and dropped dotless-file renames for `*.*` specifications off Windows); directory
  aliases (`.`/`..`) are rejected; a superseded watcher's queued error callback can no longer stop
  a replacement watcher set, and the dispatch-escalation log states whether the owner notification
  was actually raised.
- The `workspace/didChangeConfiguration` push and `workspace/configuration` replies are served from
  the same serialized settings element, so one settings object reaches the server with one member
  casing on both channels (previously the push used declared member names while replies used lower
  camel case); `SettingsProvider`'s refresh documentation matches the payload-refresh behavior.
- `WorkspaceSnapshotTracker.ApplyChanges` orders commits per path: an older batch still applies
  paths a newer batch did not commit (previously the whole older batch was discarded), and tickets
  are cleared whenever a capture replaces the snapshot.
- `LanguageServerPaths.CreateFileUri` escapes a literal `%` before parsing, so file names containing
  percent sequences (`a%41b.txt`) round-trip through `TryGetLocalPath` unchanged.
- Signature-help placeholders round-trip: `SignatureHelpParameterPayload` gained a converter that
  omits an undefined label instead of throwing on write, and the response doc states the
  placeholder policy.
- Code actions skip malformed diagnostics per element (previously one bad entry dropped the whole
  list), write a schema-valid `disabled.reason` (empty string when absent), and document that
  `data` is intentionally dropped because `codeAction/resolve` is not implemented.
- Hand-written member lookups (`PublishDiagnosticsParamsJsonConverter`,
  `ProtocolRangePayloadJsonConverter`, capability unregistration, settings refresh) now fall back to
  a case-insensitive match like the transport's reflection binding, and server-request arrays skip
  null entries instead of failing the whole request.
- A `Process.Start` returning false is logged as a startup failure, and a startup session that never
  attached its JSON-RPC transport skips the graceful shutdown/exit sequence instead of logging a
  failure for an impossible sequence.
- Range/position reads consolidated into `JsonElementReadHelpers` and writes into the new
  `ProtocolJsonWriteHelpers`; `MarkupContentReader`'s private string reader delegates to the shared
  helper; `TolerantCollectionJsonConverter` lost an unreachable null guard and redundant null
  coalescing; doc corrections landed across `DiagnosticSeverity`, `MessageType`, `SymbolKind`
  (member-level suppression), `ProtocolLocationPayload`, `ReferenceLocationPayload`,
  `CompletionTextEditPayloadJsonConverter`, `SemanticToken.HasModifier`, `DocumentCloseResult`,
  `TrimIdleDocuments`, `WorkspaceChangeAccumulator`, `DocumentOperationScheduler`, and the options
  wording; both READMEs were corrected (grammar, delta handling, notification wording, dependency
  lines, restart attribution).

*Tests*

- New coverage: watcher grammar parity and directory-alias rejection, per-path apply tickets,
  single-use references and the acquire-flag validation, the delta validation matrix, the
  signature-help placeholder round-trip, the PascalCase-settings push shape, the null initialize
  result, the percent round-trip, and nested dynamic-registration rewriting without member addition
  (client suite: 499 tests).

**Fresh review fixes (2026-09-19, `__190919_`)**

*Fixes*

- Session teardown releases its resources independently: a throwing stream disposal can no longer
  skip the remaining streams or the language-server process handle.
- The combined background-loop task is observed when the disposal wait times out, so a late fault
  no longer surfaces as an unobserved task exception.
- The detached-cleanup wait is bounded by the shared disposal budget instead of relying on
  producers to stop replacing the queued cleanup task.

*Changes*

- `ClientCapabilitiesProvider` documents all three capability rewrites (dynamic registration,
  `window.workDoneProgress`, and `general.positionEncodings`).

### Nickelony.LanguageServer.Lua

**IntelliSense round-3 review (2026-09-19)**

- Signature help maps a negative protocol `activeParameter` value to the no-active-parameter state
  (nothing highlighted) instead of clamping it into range; an absent value keeps the
  not-specified state and an explicit JSON `null` keeps its LSP 3.18 meaning.
- Completion items whose protocol kind is missing or out of range map to
  `TextCompletionItemKind.Generic` (see `Nickelony.LanguageServer.Client`).

**Event-pattern alignment (2026-09-15)**

*Breaking changes*

- `SemanticTokensUpdated` is now `EventHandler<SemanticTokensUpdatedEventArgs>` (payload via
  `eventArgs.FilePath` and `eventArgs.SemanticTokens`), and the inherited payload events follow the
  base contract (provider as sender, one `EventArgs` payload object).

**Changes**

- Hover parsing converts the protocol hover range into snapshot offsets against the requested
  document content, matching the offset-based `TextHoverInfo.Range` payload from the shared
  IntelliSense package (2026-09-16, `__150922_` IntelliSense review); malformed or reversed ranges
  are dropped.
- Removed the redundant completion text-edit identity adapter and hand-written identity comparer;
  completion duplicate detection uses the identity record's equality directly. Rename, formatting,
  and reference parsing convert protocol ranges in one step.
- Provider feature partials moved beside their feature folders, and the provider-neutral helpers
  listed under the client package moved out of this package; the helper move removes only
  internal types, so the Lua provider surface is unchanged.

**Backlog execution (2026-09-16, phases 1-3)**

*Fixes*

- The tracked synchronization for a failed document send is invalidated exactly once, inside the
  per-document scheduler slot: the outer catches no longer re-run the invalidation after the slot
  released, which could mark a document closed again after a queued operation had already reopened
  it (the semantic-token refresh path then skipped the document). `InvalidateDocumentSynchronization`

- Response parsing is hardened: malformed `file:` URIs are dropped through the non-throwing path
  normalizer, text edits without `newText` are skipped instead of silently becoming deletions,
  diagnostic severities outside the LSP 1-4 range fall back to `Warning`, and a rename workspace
  edit prefers `documentChanges` over `changes` and fails closed when any target URI cannot be
  resolved to a local file.
- Provider disposal no longer disposes its dispose token source, so in-flight request paths can
  always link their timeout tokens against it and return the documented fallback instead of
  throwing `ObjectDisposedException`; the disposal-race contract is pinned by a test.
- Signature-help documentation is normalized to plain text (fence lines removed) to match the
  signature model contract, and the LSP 3.18 `activeParameter: null` state is now preserved at
  both payload and signature level (the wire payloads expose the raw JSON value so absent and
  explicit null stay distinguishable).
- The protocol completion kind is authoritative; the prose kind heuristic only fills an omitted
  kind. Insertion and filter text stay unset when the payload omits them, so item resolution can
  adopt the server's text.
- `GetReferencesAsync(string …)` was removed (use `TextReferenceRequest`); references, rename, and
  formatting now share the same synchronized request pipeline as the position-based requests, and
  negative positions are clamped to zero on that shared path.
- Host-specific workspace flavours were removed: the hardcoded `.API` folder and the intermediate
  `ApiDirectoryName` option are gone. A host declares stub folders through the standard LuaLS
  mechanisms - a `workspace.library` entry in the workspace's `.luarc.json` (which LuaLS reads and
  re-reads on change) or `AdditionalLibraryDirectories` - and the default disabled-diagnostics list
  is now empty. The option defaults are documented as LuaLS-aligned with the single deliberate
  deviation (third-party checks disabled because a library cannot answer LuaLS's interactive
  prompts) called out. The Tomb Editor's TEN `.luarc.json` declares `.API` as a library, and the
  provider receives `DisabledDiagnostics = ["duplicate-set-field"]` explicitly.
- Internal consolidations: a shared background-task observer replaces the duplicated
  observe-and-log helpers, the workspace coordinator takes a callbacks record instead of five
  delegate parameters, the watcher create/start sequence is shared, and the `.luarc.*`
  configuration rule lives beside the watch specifications in `LuaWorkspaceConventions`.

**Lua fresh review resolution (2026-09-16, `__150922_lua-fresh-review`)**

*Breaking changes*

- The diagnostics contract follows the family rename: `GetDiagnostics` surfaces `TextDiagnostic`
  values, and the parser maps severities into the renamed `TextDiagnosticSeverity`.
- Completion ranking is protocol-faithful: `LuaCompletionRankingOptions` and
  `LuaLanguageServerOptions.CompletionRanking` were deleted (no consumer produced non-default
  weights), and the prose-derived kind/scope heuristics were removed, so item kinds come from the
  protocol kind only. `TextCompletionItem.Priority` agrees with the protocol `sortText` order
  (label fallback, response order as tie-break) and still surfaces a preselected item first.
- A case-only document rename stays a no-op on case-insensitive file systems (platform path
  policy); the covering test asserts the behaviour on both platform policies instead of reporting
  `Inconclusive` on Windows.

**Changes**

- A rename workspace edit with an empty `documentChanges` list falls back to a populated `changes`
  map instead of discarding the rename; unresolvable URIs still fail the response closed.
- The internal request helper lost the always-null `timeoutValue` parameter; a transport fallback
  is the default payload consumed by the existing `defaultValue` results.
- The five provider events share one admission-gated subscribe/raise helper, replacing the
  per-event plumbing without changing delivery semantics; configuration-path classification
  follows the platform path policy so it matches watcher delivery.
- Fixed a reopen race after a failed synchronization send: the tracked-state invalidation now runs
  while the per-document scheduler slot is still held, so a request or a coalesced update queued
  behind the failed send deterministically observes the invalidated state and reopens the document;
  the rename path's close/reopen sends get the same treatment.
- README and XML documentation use consumer/document/platform vocabulary and document the
  local-file document contract and the lazy-start `Supports*` behaviour.

**Backlog execution (2026-09-16, phase 5 - protocol type-safety sweep)**

*Changes*

- The provider and its parsers consume the typed protocol values: the completion context builds
  `CompletionTriggerKind.Invoked`/`TriggerCharacter` instead of local numeric constants, completion
  items map `CompletionItemKind` through the shared taxonomy (an absent kind still maps to the
  protocol default), the interim snippet strip checks `InsertTextFormat.Snippet`, the diagnostic
  severity mapping switches on `DiagnosticSeverity` members (unknown values still fall back to
  `Warning`), and the completion text-edit parser dispatches on the
  `CompletionRangeTextEditPayload`/`CompletionInsertReplaceTextEditPayload` union. The diagnostics
  parser reads the now-required range endpoints directly; its clamping of coordinates beyond the
  document snapshot is unchanged.

**Backlog execution (2026-09-16, phase 6 - snippet insertion)**

*Breaking changes*

- `LuaLanguageServerOptions.DisableCompletionCallSnippet` was removed; call snippets are always
  requested with LuaLS's `Replace` mode (the previous `"Enable"` value was not a valid LuaLS
  setting), and the client capability now advertises `snippetSupport: true`.
- Snippet completion text is no longer stripped toward a single caret offset: `insertText` and
  text-edit `newText` values pass through verbatim with the shared
  `TextCompletionInsertTextFormat.Snippet` marker, so a host expands tabstops and placeholders at
  commit time. An absent or unknown protocol format reads as plain text.

**Completion protocol depth (2026-09-17, phase 1)**

*Additions*

- Completion parsing maps the new protocol fields: `tags` map onto `TextCompletionTag` (unknown
  protocol values are ignored), `commitCharacters` pass through verbatim, and
  `additionalTextEdits` resolve to offset-based edits (a malformed entry is skipped individually).
  Duplicate detection includes the new fields, so two items that differ only in tags, commit
  characters, or secondary edits are both kept.

**Signature fidelity (2026-09-17, phase 3)**

*Additions*

- `GetSignatureHelpAsync` forwards the editor-side trigger context to `textDocument/signatureHelp`
  when the caller supplies one: trigger kind, trigger character, the retrigger flag, and - when a
  payload is showing - a spec-shaped `SignatureHelp` rebuilt from the visible model (signature and
  parameter labels, documentation text, the active signature, and the effective active parameter).
  Without a context the request stays position-only.

**F6 multi-root (2026-09-18, phase 3 - Lua provider)**

*Breaking changes*

- `LuaLanguageServerIntelliSenseProvider` takes a workspace root list (`IReadOnlyList<string>`)
  instead of a single root path. The list is validated (null, empty, whitespace-only, and duplicate
  entries are rejected after normalization) and every entry is watched for external workspace
  changes; the first entry is the primary root. There is no single-root overload.

*Changes*

- The workspace change coordinator owns one watch scope per root: each root gets its own file
  watcher, tracked snapshot, recovery state, and failure latch, while a shared forwarder buffers
  and replays `workspace/didChangeWatchedFiles` notifications. Forwarded or replayed changes are
  applied to the tracked snapshot of the scope that owns each path (when roots nest, the longest
  matching root wins). A watcher failure is contained to its own root and the
  `WorkspaceWatcherFailed` messages name the failing root.
- Documents stay path-keyed across every root: open/update/close, diagnostics, semantic tokens,
  and the restart re-open all cover documents from every configured root.

**Document symbols (2026-09-18, phase 4)**

*Additions*

- `LuaLanguageServerIntelliSenseProvider` implements `GetDocumentSymbolsAsync`: it requests
  `textDocument/documentSymbol` (gated on the negotiated capability) and maps hierarchical and
  flat responses into the shared offset-based `TextDocumentSymbol` tree. Protocol ranges convert
  through the document line map (stale coordinates clamp instead of failing), unknown protocol
  kinds fall back through the shared kind mapping, flat `SymbolInformation` entries are kept only
  for the requested document, and the client capabilities now advertise
  `hierarchicalDocumentSymbolSupport`.

**Provider framework extraction (2026-09-18)**

*Changes*

- The language-neutral provider machinery moved out of this package into the new
  `Nickelony.LanguageServer.Provider` package, and `LuaLanguageServerIntelliSenseProvider` now
  derives from `LanguageServerIntelliSenseProviderBase<LuaDocumentState>`: the base owns the
  lifecycle, document synchronization, workspace watching, and request pipeline, while this
  package supplies the Lua hooks (language id, watch specifications, the `.luarc.*`
  configuration rule, the settings payload, failure messages, diagnostics handling,
  semantic-token refresh, and the feature members). The public Lua surface is unchanged; the
  package now depends on `Nickelony.LanguageServer.Provider`, and the Lua document store/state
  stay in this package.
- The duplicated orchestration was deleted with the move: the Lua lifecycle and requests
  partials, the Lua workspace coordinator/callbacks/watch-scope types, and the transitional
  `InternalsVisibleTo` grant that let the framework reach Lua internals.

**Code actions (2026-09-18, phase 6 groundwork)**

*Changes*

- The shared workspace-edit parser now reports "Lua workspace edit" in its warnings (it is used
  by rename and code actions alike).

*Additions*

- `GetCodeActionsAsync` requests `textDocument/codeAction` and maps literal actions with an
  inline edit into `TextCodeAction` values; an edit that cannot be represented (an unsupported
  resource operation or an unresolvable target URI) drops only that action. The request context
  is populated from the provider's cached diagnostics for the requested range, so LuaLS's
  diagnostic quick-fix family stays reachable; command-only actions (config-based fixes) are
  omitted.
- The client capabilities advertise `codeActionLiteralSupport` (kinds `quickfix`,
  `refactor.rewrite`, `refactor.extract`) plus `isPreferredSupport`, so LuaLS answers with the
  literal action shape the provider consumes.

**Lua fresh review resolution (2026-09-19, `__180923_` review)**

*Breaking changes*

- `ILuaIntelliSenseProvider` was renamed to `ILuaLanguageServerIntelliSenseProvider` (the type
  already pairs with `LuaLanguageServerIntelliSenseProvider`); no compatibility alias exists.
- The semantic-token delta path was deleted: LuaLS registers full token requests only (a boolean
  capability without result ids), so the provider always requests the full payload, the cache no
  longer stores result ids or raw token streams, the capability advertisement drops
  `full.delta = true`, and `SemanticTokensDeltaState` is no longer consumed by this package.
- A diagnostics payload without a protocol severity now maps to `TextDiagnosticSeverity.Error`
  (the provider's documented fallback for an omitted severity) instead of `Warning`; unknown numeric
  severities still map to `Warning`.
- `LuaLanguageServerOptions` list properties state their blank-entry filtering explicitly;
  `DisabledDiagnostics` blank entries are dropped before the settings payload is sent.

*Fixes*

- The client capabilities now advertise `workspace.semanticTokens.refreshSupport`, which LuaLS
  requires before it sends `workspace/semanticTokens/refresh`; configuration changes therefore
  invalidate the cached token colors instead of waiting for the next document edit.
- The post-synchronization token refresh no longer blocks the document-sync pipeline (and the
  restart replay): the refresh runs detached, so a stalled token round trip cannot delay the next
  `didChange`; the server-refresh fan-out runs its per-document refreshes concurrently.
- A failed token refresh keeps the previously cached token set and raises no event instead of
  clearing the highlighting (a stale-by-one-refresh set is the better fallback).
- The rename workspace-edit fallback judges `documentChanges` by usable edits: entries whose edits
  are all skipped or absent now fall back to a populated `changes` map instead of discarding the
  rename.
- A position-only (zero-length) diagnostic keeps the exact range the server published instead of
  widening to the enclosing word; widening only happens when the range cannot be mapped at all, so
  the code-action context echoes the server's own range.
- Duplicate completion items are merged instead of discarded: a later variant's preselect flag,
  snippet insert format, and non-blank sort text are preserved on the retained item.
- Completion and document-symbol ranges validate the raw protocol order before clamping, so an
  inverted range can no longer collapse into an accepted zero-length range; malformed signature
  help signatures and parameters keep their array position as empty placeholders, so
  `activeSignature`/`activeParameter` indexes stay aligned with the server's arrays.
- Diagnostic ordering ranks severities explicitly (the enum values are identifiers, not a
  ranking), and the token-refresh request plumbing no longer lets a throwing cancellation callback
  escape cleanup or strand a request source during disposal.
- `LuaLanguageServerIntelliSenseProvider` validates the workspace roots before creating the client,
  so an invalid root list cannot leak a client that never becomes provider-owned.

*Changes*

- The workspace conventions are documented as workspace-root configuration watching (LuaLS loads
  one workspace-level `.luarc.` file) over a cached static watch specification list; the
  diagnostics store routes both version fences through `LuaDocumentVersionPolicy.IsPayloadCurrent`.
- README and XML documentation were corrected (diagnostics trimming, document contract, semantic
  tokens, `serverExecutablePath: null` behavior, a new Limitations section and options example).

**Lua fresh review resolution (2026-09-19, second pass)**

*Breaking changes*

- `LuaDocumentVersionHelper` was renamed to `LuaDocumentVersionPolicy` (internal). The policy now
  also backs a consolidated internal `VersionFencedPayloadCache<TItem>`, which replaces
  `LuaDiagnosticsCache` and `LuaSemanticTokensCache`; the diagnostics cache additionally stores the
  content snapshot its offsets refer to.
- `LuaLanguageServerOptions.RuntimeVersion` rejects an empty or whitespace-only value
  (`ArgumentException`) instead of forwarding it to LuaLS.
- The permanent startup-failure text no longer references "the log", so the library does not
  assume the host keeps a user-visible log; hosts that display the message show the shortened text.

*Fixes*

- The semantic-token request bookkeeping captures the linked cancellation token before publishing
  the source, so a refresh that races provider disposal (or a superseding refresh) can no longer
  read a disposed source and fault with `ObjectDisposedException`; the admission-closed path also
  cancels the source it just superseded.
- Code-action context diagnostics are converted and range-matched against the content snapshot
  they were parsed against, so the emitted ranges stay in the server's coordinate space when the
  caller's document text has changed since the last publish.
- Hover markdown normalizes line endings the same way completion documentation does.
- A document-symbol payload that carries both a flat `location` and hierarchical ranges logs the
  flat-wins choice instead of silently discarding the hierarchical half.

*Changes*

- The formatting slice passes the raw request path to the shared pipeline (restoring its
  unusable-path log) and resolves the normalized edit target through one helper shared with the
  document-symbol closure.
- The semantic-token refresh fan-out runs through a bounded gate (four concurrent requests)
  instead of sending one unbounded burst per refresh request.
- The diagnostics hook reports version staleness accurately (a stale payload is no longer logged
  as a parse failure) and logs a payload superseded at the store fence; the shared range-order
  rule moved to the parser root, and unreachable defensive branches were deleted.
- XML documentation was aligned with behavior (`activeParameter` interpretation, `TryParse`
  returns, diagnostic code formatting, hover normalization, initialization-option flags) and the
  README now documents the severity fallback and points its repository links at this repository.

*Changes (2026-09-19)*

- The completion parser validates an insert/replace edit pair through
  `TextCompletionTextEdit.TryCreate`; an edit whose pair violates the insert/replace relation
  degrades to a single replace-range edit (see the fifth-pass entry), matching the tolerant
  per-entry handling of the other malformed payloads. A server entry without a symbol kind still
  falls back to `File` at the call site (the shared bridge now rejects out-of-range values instead
  of choosing a fallback).

**Lua fresh review resolution (2026-09-19, third pass)**

*Changes*

- Documentation accuracy: the diagnostics severity fallback is stated as the provider's own policy
  instead of attributing it to another client's convention, and the resource-operation
  fail-closed rationale now cites the shared workspace-edit model (`TextWorkspaceEdit`) instead
  of assuming a consumer-side rename flow; the README gains the matching Limitations entry.
- The initialization-options remarks state the grounded semantics: `trustByClient: false` keeps
  LuaLS's own plugin-trust checks (the library cannot present or answer a trust prompt), and
  `changeConfiguration`/`viewDocument` route LuaLS configuration and document-reveal flows to the
  client instead of letting the server write user configuration files itself.
- The README usage example builds from local variables with a noted Unix/macOS equivalent, and
  test-seam comments carry their rationale inline instead of referencing an archived ticket ID.

**Lua fresh review resolution (2026-09-19, fourth pass)**

*Fixes*

- Provider construction with an empty workspace-root list now fails before any client exists:
  `CreateClient` rejects the list explicitly, and the provider framework validates it as its own
  constructor contract (the shared path helper accepts the list for folderless client sessions);
  the framework base also disposes an owned client when construction fails after taking it, so a
  failed construction can no longer leak the client, and the ownership remarks state the
  on-success transfer rule.

*Changes*

- Documentation accuracy: the semantic-token refresh contract now reads "open or edit
  synchronization" everywhere (request-driven synchronization does not issue an extra refresh),
  the lazy-start contract lists renames among the calls that do not start the server, the hover
  range remark distinguishes clamped overruns from rejected negative coordinates, the definition
  `<returns>` names the missing-range cause, and the LuaLS configuration-path remark drops the
  `CONFIGPATH`-suffix overclaim; the code-action parser documents the dropped disabled state and
  per-action diagnostics, the diagnostics parser documents its ordering and keeps storage policy
  in `<remarks>`, and the settings factory speaks "settings payload" throughout.
- The README drops "real" from the LuaLS attribution, and `InternalsVisibleTo` names the actual
  exposed seams.

*Tests*

- New coverage (suite: 262 tests): the handshake wiring of the real client is pinned against the
  three factories, semantic-token modifier decoding is asserted from the legend, the refresh
  fan-out runs against six documents with the bounded gate held and released, an incremental
  `didChange` payload asserts its range and replacement text, options validation and the
  equal-version fence are pinned, invalidation-plus-reopen is asserted at the store, and a null
  definition response exercises the documented fallback.
- The fake gains the real client's case-insensitive deserialization, configured JSON-null
  responses, and a mirroring `DisposeAsync`; the startup-blocked dispose waits on the recorded
  start call instead of a fixed delay, the contained-fault log observation runs on the shared
  timeout budget, the BOM update asserts the full payload, and the integration session cleans up
  its temp roots when construction fails. The reopen-after-failed-change test now waits for the
  transport-failure cleanup before hovering, so the transiently unavailable window cannot race
  the reopen under test.

**Lua fresh review resolution (2026-09-19, fifth pass)**

*Fixes*

- A merged completion duplicate now re-ranks from the sort key it adopts, so the priority hint can
  no longer contradict the merged `sortText` (the hint had kept the retained label rank).
- Code-action context diagnostics are intersected with the requested range only when the caller's
  text equals the parsed snapshot; otherwise the context keeps every cached diagnostic instead of
  dropping entries through a comparison between two coordinate spaces (the emitted positions stay
  snapshot-based, as before).
- Document symbols reject negative protocol coordinates per range, matching the hover and
  workspace-edit parsers, so a malformed position yields an entry without a range instead of an
  entry pointing at the file start.

*Changes*

- The initialization options no longer advertise `changeConfiguration`/`viewDocument`: the client
  answers neither the `$/command` configuration flow nor `window/showDocument`, so LuaLS falls
  back to its own configuration handling instead of assuming those flows were served;
  `trustByClient`/`useSemanticByRange` stay `false` for the implemented-surface reasons, and the
  remark is grounded in the initialization-options field LuaLS actually reads.
- References, rename, and code-action requests clamp negative coordinates to zero like the shared
  position-based request path.
- Slice placement: the shared workspace-edit parser moved from `Rename/` to
  `Parsing/LuaLanguageServerResponseParser.WorkspaceEdits.cs`, and the diagnostics provider hook
  moved from `Documents/` to `Diagnostics/LuaLanguageServerIntelliSenseProvider.Diagnostics.cs`.
- Cleanups: the cache stamp reset is `ResetVersionStamp` (distinct from the store's server-sync
  invalidation), which now resets both payload caches symmetrically; the resolve factory returns a
  non-null callback (the parser's needs-resolve gate decides when it is consulted); the dead
  client-null resolve branch is gone (the callback captures the client that negotiated the
  capability); the no-op insertion-text ternary and duplicated interface wording were removed.
- Documentation: option defaults are stated per property (including the LuaLS-matching `false`
  keyword switch), the settings factory names both consuming channels, the `.luarc` root-only
  coverage and the unreachable `--configpath` surface are stated in the workspace conventions and
  README, the `LuaDocumentState` accessibility sentence, the store fence comment, the
  signature-help `activeParameter` summary, the identity "kind" wording, and the provider class
  summary are corrected, and the root README lists the Provider package in the language-server tier.

*Tests*

- The fake mirrors the real client's unhealthy-reset contract (the generation is retained instead
  of zeroed, and the pinned assertions now check retention) and rejects sends while the transport
  is not ready, like the real forwarder.
- The integration tier's process seam walks the client's capability-store path through the shared
  test-access helper and is pinned by a non-skipping handshake test, so a rename fails loudly
  instead of surfacing only when the opt-in archive is configured.
- New coverage (suite: 284 tests): merge ranking and the merge+resolve callback count,
  commit-character identity,
  references/rename coordinate clamping, hover clamping and negative rejection, signature-help
  `activeSignature`/`activeParameter` edges, the cache contract (`TryStore(null)`, stamp reset),
  the diagnostics snapshot fallback and stamp reset, null change-map entries, code-action snapshot
  filtering, and the dispose-time event unsubscription. The startup-failure reopen test now polls
  the recorded start attempt instead of waiting a fixed delay.

### Nickelony.LanguageServer.Provider

**New package.** The extracted language-neutral provider framework: a language package derives
from `LanguageServerIntelliSenseProviderBase<TDocumentState>` and implements the language hooks,
so the orchestration is shared instead of copied.

*Additions*

- `LanguageServerIntelliSenseProviderBase<TDocumentState>` (public abstract, generic over the
  tracked document state) implements `ILanguageServerIntelliSenseProvider` end to end -
  availability and state, the lifecycle events, the document lifecycle, cached diagnostics
  reads, and the request scaffolding - and exposes the language hooks: `ProviderDisplayName`,
  `LanguageId`, `CreateWatchSpecifications`, `CreateTrackedDocumentStore`, `IsConfigurationPath`,
  `CreateSettingsPayload`, `CreateStartupFailure`, `CreateMissingClientFailure`,
  `HandleDiagnosticsPayload`, `GetTrackedDiagnostics`, and
  `InvalidateTrackedDocumentSynchronization`, plus the virtual `OnDocumentSynchronizedAsync`,
  `OnTrackedDocumentInvalidated`, `OnDocumentRenamed`, and `OnDisposing` extension points and
  the protected accessors (`Client`, `RequestDispatcher`, `Logger`,
  `IsDisposed`).
- `LanguageServerProviderOptions` (public sealed, init-only; `Default`): request timeout
  (10 seconds), timeout-restart threshold (2), hard startup-failure threshold (3), and
  tracked-idle-document cap (16) - the values the Lua package previously hardcoded.
- `CreateWorkspaceFileWatcher` (protected virtual): the workspace-watcher creation seam, so a
  host or test substitutes watcher creation through an ordinary override; the default
  implementation plumbs the provider logger into the watcher.
- The extracted mechanics stay internal behind `InternalsVisibleTo` for the package's test
  project: the startup state machine, the background task observer, the generalized workspace
  watch scope, the change coordinator, and the callbacks record.

The package references `Nickelony.LanguageServer.Abstractions`, `Nickelony.LanguageServer.Client`,
`Nickelony.IDEKit.IntelliSense`, and `Microsoft.Extensions.Logging.Abstractions`.
`Nickelony.LanguageServer.Lua` is the reference implementation.
`Nickelony.LanguageServer.Abstractions` and `Nickelony.LanguageServer.Client` carry no entries:
the extraction needed no contract or primitives change from either.

**Code actions (2026-09-18, phase 6 groundwork)**

*Breaking changes*

- `LanguageServerIntelliSenseProviderBase<TDocumentState>` gains the abstract
  `GetCodeActionsAsync(TextCodeActionRequest, cancellationToken)` feature member; implementations
  must map the new member.

**Fresh review resolution (2026-09-19, `__180923_` Client review)**

*Additions*

- `LanguageServerRequestDispatcher` (per-request timeout, transport-generation fencing, a single
  retry after a transport boundary, and timeout-driven restart hysteresis) and
  `WorkspaceFileChangeForwarder` (buffered replay of recoverable delivery gaps behind an
  ensure-started gate) moved here from the Client package, together with their focused tests; the
  client package no longer ships provider-composition helpers, and the dispatcher now returns the
  documented fallback when disposal races a send.

**Fresh review resolution (2026-09-19, `__180923_` Provider review)**

*Breaking changes*

- The public `WorkspaceFileWatcherFactory` delegate was deleted: `WorkspaceFileWatcher` creation is
  now the protected virtual `LanguageServerIntelliSenseProviderBase.CreateWorkspaceFileWatcher`
  seam, and the protected constructor no longer takes a factory parameter.
- `LanguageServerProviderOptions` is validated at construction; an out-of-range value throws
  `ArgumentOutOfRangeException` instead of surfacing later as distant runtime misbehavior.
- Requests evaluate the capability gate before document synchronization (ensure transport, then
  the gate, then synchronization, then dispatch), so an unsupported request produces no document
  traffic.
- The unproduced protected `WorkspaceRootsDisplayText` accessor was removed; the coordinator
  receives the joined display text as a constructor argument.

*Fixes*

- An empty `CreateWatchSpecifications` list disables workspace watching entirely (no scopes, no
  snapshot, no replay), and watcher-creation or start failures are contained as startup failures
  that surface through `WorkspaceWatcherFailed` instead of escaping request APIs.
- Nested workspace roots no longer forward duplicate watched-file notifications: a watcher delivery
  is filtered to the paths its scope owns (longest prefix wins), so overlapping watchers produce
  one notification. While the owning scope's watcher is inactive (not started yet, or being
  recovered), the delivering watcher covers the root instead of dropping its changes, and the
owning scope's recovery reconciliation takes over once it restarts.
- Disposal hardening: the post-disposal synthetic `didClose` trim cannot surface
  `ObjectDisposedException` from a request, `UpdateDocument`/`RenameDocument` honor the disposed
  no-op contract, and `SynchronizeDocumentAsync` absorbs the disposal-driven cancellation.
- Rejected document paths are logged at debug level, absolute-local-path identity and the
  per-platform casing policy are documented at the provider surface, and a case-only rename is
  documented as a no-op there.
- `LanguageServerPaths.NormalizeWorkspaceRoots` is the single root validation/normalization
  implementation shared by the client and the provider (messages unchanged); the coordinator's
  redundant per-path re-normalization was removed.

*Changes*

- The default watcher factory plumbs the provider logger into the watcher (new
  `WorkspaceWatcherLogger` adapter); the direct `IDEKit.Core` project reference was dropped (the
  types flow through IntelliSense).
- Log and failure copy is host-neutral ("consumer path", neutral watcher-failure text); disposal
  remarks are single-sourced, coordinator remarks restructured, and the README dependency list
  aligns with the csproj.
- `docs/ProviderAuthoring.md` was corrected against the code: five public Lua types, the
  code-action member in the feature recipe, construction-time versus lazy hooks, the
  watch-specification grammar with the empty-list rule, and the generation invariants.

*Tests*

- New framework coverage for disposal, transport-unavailable, restart/reopen, trimming,
  construction/options validation, path rejection, position clamping, case-only rename, and
  missing-client behavior; the nested-root regressions pin that overlapping watchers forward each
  change exactly once and that a nested root stays covered while its own watcher is inactive
  (suite: 79 tests).

**Provider review resolution - second pass (2026-09-19, `__180923_` Provider review)**

*Breaking changes*

- `LanguageServerProviderOptions.MaxTrackedRequestOnlyDocuments` was renamed to
  `MaxTrackedIdleDocuments` (the trim policy was never request-only: every idle document is
  bounded).
- The protected `SendPositionRequestAsync` helper was renamed `SendDocumentPositionRequestAsync`,
  and the protected `TrackedDocuments` accessor was removed: the base owns the tracked store, and
  derived code works through the document lifecycle and the documented hooks.

*Fixes*

- Server rejections now produce the documented fallback result: the request dispatcher catches
  `LanguageServerRequestRejectedException` without retrying and logs a method-not-found rejection
  at debug level (other rejection codes at warning level), while transport failures keep the
  single retry. A plain `IOException` at the transport boundary is treated as a transport failure
  (retry once, then fall back).
- A canceled or timed-out document send invalidates the tracked server synchronization instead of
  leaving the document marked as synchronized, so the next request reopens and resends it.
- Close handling honors a request reference that is still active: `CloseDocument` and idle
  trimming defer the `didClose` until the last request reference is released (distinguishing
  `StillOpen` from `BusyWithRequests`), and the request path releases its reference exactly once.
- Provider extension hooks are contained: a throwing post-synchronization, tracked-invalidation,
  diagnostics, rename, configuration-path, settings-payload, or disposal hook is logged and the
  remaining work continues instead of failing the request or the shutdown.

*Changes*

- Naming consistency: `WorkspaceChangeCallbacks.TryMarkTransportUnhealthy` (and the forwarder's
  matching `tryMarkTransportUnhealthy` constructor parameter) replace `MarkTransportUnavailable`;
  the internal startup-state flag is invalidated by `InvalidateStartupSucceeded`.

*Tests*

- New coverage for cancellation-driven invalidation and reopen, the transport-`IOException`
  fallback, rejection fallback, deferred close with a live request reference, hook containment
  (post-synchronization, diagnostics, configuration probe, settings payload), and attempted-send
  observability in the client fake (suite: 93 tests).

*Documentation*

- `docs/ProviderAuthoring.md` and the package README were updated for the renamed option and
  helper, the idle-document cap, the rejection-to-fallback contract, and the deferred-close
  behavior.

**Third pass - second fresh review resolution (2026-09-19)**

*Breaking changes*

- The protected request helpers renamed their fallback parameter to `fallbackValue` (was
  `defaultValue`), aligning with `LanguageServerRequestDispatcher`; derived providers using the
  named argument must update (Lua updated in-wave).

*Fixes*

- Request references are released by identity through the new `DocumentRequestReference`: a
  document renamed while a request held its temporary reference no longer strands that
  reference, so the renamed record stays closable and trimmable; an acquisition is also
  releasable when a store hook throws after it.
- Idle-trim closes run through each evicted document's own scheduler slot and re-check the
  tracked state first, so a record recreated by a concurrent open or update keeps its server
  document open instead of receiving an out-of-order `didClose`; a deferred close follows a
  renamed record to its current path through bounded hops.
- Every restart reopen runs in the document's own scheduler slot; a failed reopen invalidates
  the tracked synchronization instead of leaving a record that claims a server-open document,
  and the unfinished remainder is resumed by the next start even when the client stayed ready.
  Closes and renames that land during a replay no longer suppress the close of a document the
  replay already reopened.
- The dispatcher's retry path classifies startup failures like the first attempt (cancellation,
  transport, and disposal produce the documented fallback; unexpected faults stay faults), an
  unowned cancellation in the synchronization and startup wrappers maps to the documented
  `false`/fallback result, and a caller cancellation racing a rejection or transport boundary
  surfaces as cancellation.

*Documentation*

- `docs/ProviderAuthoring.md` documents the identity-bound request reference, the serialized
  trimmed closes, and the resumable restart replay; the options remark now describes the cap as
  an idle-document cap.

*Tests*

- New regressions: rename-during-request release, trimmed-close recreate guard, failed-replay
  resume, close-during-replay, caller cancellation before/in-flight (with reference release),
  editor-open trim immunity, occupied-destination rename no-op, a real-response parse test, and
  event-isolation/disposal-guard extensions (suite: 105 tests).

**Lua fourth-pass ownership fix (2026-09-19)**

*Fixes*

- The constructor validates an empty workspace-root list explicitly and fails before taking the
  client (the shared path helper accepts the list for folderless client sessions, so the fatal
  condition previously surfaced from the request dispatcher after the client was already taken);
  when construction fails after taking the client, the constructor disposes it as it unwinds, so
  a failed construction cannot leak a client. The ownership remarks state the on-success transfer
  rule; two constructor regressions pin both behaviors (suite: 107 tests).

**Third fresh review resolution (2026-09-19, second execution)**

*Breaking changes*

- `ILanguageServerIntelliSenseProvider` (Abstractions) now extends `IAsyncDisposable`; the provider
  base implements a shared idempotent teardown, with `DisposeAsync` awaiting the owned client's
  asynchronous disposal instead of blocking the calling thread. Consumers that call `Dispose` are
  unaffected.
- Document entry points ensure the transport outside the document scheduler slot; document slots no
  longer start the transport themselves, and a session that negotiated no synchronization mode
  skips a document change with a warning instead of failing the operation (the tracked content is
  re-established in full on the next session; opening remains expressible).

*Fixes*

- Restart replays no longer run from inside a document scheduler slot: an update or open that
  triggered a restart previously hit the scheduler's reentrancy guard (`InvalidOperationException`,
  leaving the transport started but the tracked documents unopened) or deadlocked the document chain
  against the startup lock. Both interleavings are pinned by new regressions.
- Hook containment is uniform: `CreateStartupFailure`, `CreateMissingClientFailure`, the rename-path
  `GetTrackedDiagnostics` read, and `OnDocumentRenamed` are contained like every other hook, so a
  throwing hook can no longer escape the documented fallback or surface as an unobserved task fault;
  the containment message lives once in `HookContainment`.
- A restart triggered by a request is no longer abortable by that caller's cancellation: the startup
  shield keys off a completed-startup history instead of the readiness flag that transport loss clears
  first.
- The workspace forwarder keeps a failed batch ahead of changes buffered while the attempt was in
  flight, so a re-buffered delete can no longer cancel against a newer create for the same path.
- Timeout bookkeeping resets on a server rejection (a completed exchange with a responsive transport)
  instead of counting toward the restart threshold, and the dispatcher validates the timeout upper
  bound its cancellation can enforce.

*Documentation*

- Corrected the watch-specification grammar claim (the matcher is the platform's runtime matcher, not
  one cross-platform grammar), the `ProviderAuthoring.md` working-directory and "fails loudly"
  claims, and the Lua-defaults attribution; unified the "fallback value" and "canceled" terminology;
  consolidated duplicated rationale comments and removed unreachable guards.

*Tests*

- New regressions: update-triggered and open-triggered restarts, the replay-versus-update deadlock
  interleaving, restart cancellation shielding, multi-document replay resume, workspace-send transport
  invalidation with buffered replay, missing-synchronization-mode gating, hook containment
  (startup/missing-client/rename), rejection streak reset, the dispatcher timeout bound, delete/create
  ordering across the replay buffers, and `DisposeAsync` idempotence (suite: 121 tests).

### Documentation

- Package READMEs document the moved path-comparison primitive, the zero-based range contract,
  and the vertical-slice layout (`Editing`/`Requests` in Core, `Lifecycle` in Abstractions, the
  client folder map, Workspace `FileSystem`/`Reloading`, AvalonEdit `ViewState`, and the renamed
  Processes scope).
- The document-symbol surface is documented in the Lua, Client, and IntelliSense package READMEs
  (the provider member and capability gate, the protocol payload family and
  `SupportsDocumentSymbols`, and the shared kind mapping).
- The code-action surface is documented in the Abstractions, Client, and Lua package READMEs
  (the action model and request, the protocol payload family and `SupportsCodeActions`, and the
  provider member with its context-diagnostic behavior).
- New provider-authoring guide (`docs/ProviderAuthoring.md`) for implementing a language
  provider on the extracted `Nickelony.LanguageServer.Provider` framework: the package shape and
  ownership split, the language hooks, the construction, startup, document, watch, feature,
  capability, callback, and disposal recipe, the learned traps (path normalization, zero-based
  UTF-16 coordinates, snapshot ownership, reference semantics, dispatch/timeout policy, test
  strategy), and the server compatibility constraints. The guide targets the package as the
  authoring baseline and keeps `Nickelony.LanguageServer.Lua` as the reference implementation.
- The provider-authoring guide and Provider README were corrected against the resolved framework:
  five public Lua types, the code-action member in the feature recipe, the watch-specification
  grammar with the empty-list rule, construction-time versus lazy hooks, the generation
  invariants, and a dependency list that matches the csproj.
- The new Provider package README documents the framework scope, the hook surface, the package
  dependencies, and the reference implementation; the Lua README notes the framework
  relationship. The L1 placement inventory is retired - the extraction shipped as the package
  (record in the backlog archive) - and the language-#2 kickoff checklist stays in
  `future-backlog-2026-09-17.md`.
- New editor-binding guide (`docs/EditorBindingGuide.md`) for adding a new editor binding (for
  example `Nickelony.IDEKit.AvaloniaEdit`): the tier model and dependency direction, the neutral
  types a binding reuses per feature, the framework-typed surface every binding writes (using
  the AvalonEdit packages as the reference implementation), the intentional per-binding pieces,
  the adapter patterns to copy (snapshot, edit target, resolve/apply split, margin-over-source,
  request lifecycle), the suggested porting order with the test layering, and the do-not-port
  list. The root README tier notes name the editor-generic Core slices and point at the guide.
- The editor-binding guide reflects the post-review names (`LocalPathComparisonPolicy`,
  `TextHoverEvaluationState`, the `TextCodeActionMenuOptions`/`TextCodeActionControllerOptions`
  split) and points at the archived review plans.

**Fresh review resolution (2026-09-19, fourth pass re-run)**

*Fixes*

- An unusable response payload no longer escapes the request pipeline: a response that cannot be
  deserialized as the expected type is logged and reported as the documented fallback value instead
  of surfacing a serializer exception, and the exchange breaks the consecutive-timeout streak like
  a server rejection without marking the transport unhealthy.
- The restart replay re-captures its document list when `StartAsync` replaces the session while a
  partial resume is in flight (a concurrent invalidation between the readiness decision and the
  start): the replacement session receives `didOpen` for every tracked document instead of only the
  remaining tail, so a silently skipped document can no longer report Ready with a phantom server
  copy.
- The request helper classifies outcomes instead of response values: a non-nullable struct response
  returns the caller's fallback value on a fallback path instead of parsing a default struct
  (`SendAsync` keeps its signature and semantics; the outcome channel is internal).

*Changes*

- `WorkspaceFileForwardingFailure` moved here from `Nickelony.LanguageServer.Client`, next to the
  workspace-file forwarder that is its only producer and consumer.
- Documentation precision: hook containment scoping (synchronous faults vs. asynchronous
  cancellation), watch-pattern casing follows the configured local-path comparison policy, hook
  threads (transport/scheduler threads, and events can be raised while the startup lock is held),
  the complete `SendAsync` fallback-mode list, the `InvalidateTrackedDocumentSynchronization`
  reopen-gating obligation, normalized-path duplicate semantics, and member docs for the document
  synchronization internals; wording standardized on the documented fallback value and the
  per-document scheduler slot.

*Tests*

- New regressions: an unprocessable response payload returns the fallback without marking the
  transport; both struct-response paths (real response parsed, fallback preserved); a
  session-replacement replay reopens every tracked document; a failed rename reopen marks the
  transport unavailable and recovers; a hook-containment suite (synchronous fault, result fallback,
  asynchronous fault, asynchronous cancellation silence); the fake client mirrors the real
  `StartAsync` fast path (a ready session keeps its generation), and the suite is 130 tests.

## 1.0.0-preview.37 (unreleased)

`1.0.0-preview.37` is the single clean wave for every package in this repository. It supersedes the
interim `1.0.0-preview.35` artifacts - cut for `Nickelony.IDEKit.Core`,
`Nickelony.IDEKit.AvalonEdit`, `Nickelony.IDEKit.AvalonEdit.IntelliSense`,
`Nickelony.IDEKit.IntelliSense`, `Nickelony.IDEKit.Workspace`, and
`Nickelony.IDEKit.AvalonEdit.Extras` before their review resolutions finished, which left the
`preview.35` feed mixed - and the interim `1.0.0-preview.36` wave that Tomb Editor consumed while
the post-`preview.36` execution program (package split, prefix renames, provider-generic extraction,
injectable ranking options, and structural refactors) landed. Entries below cover every changed
package across both waves and list breaking changes first; packages without changes carry no entry.

### Nickelony.IDEKit.Core

**Breaking changes**

- Moved types (namespace and assembly change):
  - `NavigationLocation` → `Nickelony.IDEKit.AvalonEdit.Navigation`.
  - `TextWorkspaceEditApplicationResult`, `TextWorkspaceEditApplicationStatus`,
    `TextWorkspaceEditTargetResult`, `TextWorkspaceEditTargetStatus`,
    `TextWorkspaceEditTransaction` (renamed `TextWorkspaceEditChangeSet`),
    `TextWorkspaceDocumentChange` → `Nickelony.IDEKit.Workspace.Editing`.
  - `EnterInsertionContext`/`EnterInsertionResult`, `CompletionInsertionContext`/`CompletionInsertionResult`
    → host-owned (Tomb Editor: `TombLib.Scripting.Lua.Editing`).
  - `TextEditorRequestIdentity` → host-owned (Tomb Editor: `TombLib.Scripting.UI.Diagnostics`).
- `MarkupTextNormalizer` was renamed to `BacktickFenceTextNormalizer`, and
  `NormalizeForPlainText` now takes the line terminator as a required second parameter:
  `NormalizeForPlainText(string? text, string newLine)`. Pass `Environment.NewLine` to keep the
  previous host output.
- `TextEditOperation` validates its constructor arguments; a null `NewText` is rejected with
  `ArgumentNullException`, and the prepared-edits carrier rejects a null replacement text with
  `ArgumentException` (this also covers `default(TextEditOperation)`).
- `FindReplaceText` rejects a negative match timeout with `ArgumentOutOfRangeException` (previously
  the value was silently treated as infinite) and documents the full exception surface of its
  helpers; `Regex.InfiniteMatchTimeout` is accepted as the documented unbounded value.
- `TextEditOperation` properties are now get-only; the validating constructor is the only way to
  create a non-default value (object initializers and `with` expressions no longer compile).
- `TextRange` rejects a range whose end exceeds `int.MaxValue` with `ArgumentOutOfRangeException`
  instead of wrapping to a negative `EndOffset`.
- `TextEditPreparationResult` now carries a `PreparedTextEdits` batch (`Edits`) instead of an
  `IReadOnlyList<TextEditOperation>` (`Operations`): the carrier validates the highest-to-lowest
  order and the non-null replacement text once at construction, owns `MapOffset`, and is what
  `ITextEditTarget.Apply` accepts. `TextEditKernel.ValidateOperations` and the static
  `TextEditKernel.MapOffset` were removed.
- `TextEditorRequestOutcome` was renamed to `RequestOutcome`; `Stale` and `Superseded` became
  `RejectedByCurrentState` and `SupersededByNewerRequest`, and the `Failed` member was removed
  (nothing returns it; a compute failure propagates as an exception).
- `SynchronousRequestAdapter` was removed.
- Renamed members: `WhiteSpaceConverter` → `WhitespaceConverter`, `ConvertSpacesToTabs` →
  `ConvertIndentationSpacesToTabs`, `LineDiffer.GetChangedLines` → `GetChangedLineNumbers`,
  `RequestTokenSource.Begin` → `BeginRequest`, `IdentifierAffinity.BeforeCaret` →
  `EndingAtOffset`, `IdentifierOperations.GetPrefix` → `GetWordEndingAt`, `CommentSpan.Start` →
  `SpanStart`, and `TextEditPreparationDiagnostic.EditIndex`/`RelatedEditIndex` →
  `SourceIndex`/`RelatedSourceIndex`.
- Parameter reshapes: `TextRangeOffsetResolver.TryResolveOffsets` takes a `TextLineMap`, two
  `TextPosition` endpoints, an `out TextRange`, and an optional word rule (eight parameters
  before); `ThemeCatalog` takes a `ThemeCatalogOptions<TTheme>` record. The new
  `TextPosition` type and the `TextLineMap.GetPosition`/`GetOffset` conversions replace manual
  offset ↔ (line, character) arithmetic.

- `TextEditOperation` is a sealed record class with validating constructor arguments: a negative
  start offset and an end offset before the start offset are rejected, so `Length` is never
  negative; `PreparedTextEdits` additionally rejects overlapping operations, duplicate insertions,
  and an insertion ordered before a replacement at the same start offset.
- `TextEditPreparationDiagnostic.RelatedSourceIndex` is now `int?`; a diagnostic that concerns a
  single edit carries `null` instead of the `-1` sentinel.
- `FindReplaceText` replaced its section-based search: `GetMatchesFromSection`,
  `GetTextBeforeSelection`, `GetTextAfterSelection`, `GetAbsoluteMatchOffset`, `GetFirstMatch`,
  and `GetLastMatch` were removed together with the `FindingOrder` enum. `FindNextMatch` and
  `FindPreviousMatch` locate matches in the full document and return absolute-offset `Match`
  values (or `null`), and regular-expression anchors and boundary assertions now resolve against
  the document instead of the searched section.
- `FindReplaceText.BuildRegexOptions` adds `RegexOptions.CultureInvariant` to case-insensitive
  matching, so results do not depend on the current culture (for example the Turkish dotted and
  dotless I).
- `TextLineMap.GetPosition` clamps the character to the line length: an offset inside a line
  terminator maps to the end of the preceding line instead of a character beyond it, so the
  conversion always yields a valid protocol position.
- `TextIncrementalEditCalculator.Compute` widens a computed range when a boundary would split a
  UTF-16 surrogate pair or a CRLF sequence, so emitted ranges never start or end mid-unit.
- `TextRange.GetText` throws `ArgumentOutOfRangeException` (not `ArgumentException`) when the
  range extends beyond the source, matching `ITextSnapshot.GetText`.
- `LineDiffer.GetChangedLineNumbers` returns `LineDiffResult`: the changed-line set (now an
  immutable set) plus `IsApproximate`, which reports the work-budget fallback instead of degrading
  silently. The differ uses the linear-space refinement of Myers' algorithm, so a large span with a
  few edits keeps its locality; the budget now trips on search work rather than on span size.
- `WhitespaceConverter` renames: `ConvertIndentationSpacesToTabs` → `ConvertIndentationToTabs` and
  `ConvertTabsToSpaces` → `ExpandTabs`, stating the scope (indentation-only versus whole-text
  expansion).
- `ThemeCatalog<TTheme>.ThemesByLookupName` was removed; `TryGetTheme` and `GetTheme` are the
  lookup surface.

**Fixes**

- `BacktickFenceTextNormalizer` drops only blank leading and trailing lines instead of trimming
  the joined result, so the first retained line keeps its indentation like every other line.
- `CommentScanner` recognizes C# verbatim strings through the new
  `StringLiteralStyle.VerbatimDoubleQuoted` (doubled-quote escapes, multi-line), so a verbatim
  string containing a trailing backslash no longer swallows the following comment.
- `ContinuationOperations.IsValidContinuation` accepts an optional whitespace rule for languages such
  as Visual Basic, where the marker must be preceded by whitespace because an identifier may end
  with the marker character.
- `FindReplaceText` reuses a bounded cache of `Regex` instances keyed by pattern, options, and
  timeout, so repeated searches with one pattern no longer re-parse it.
- `IncrementalLineStateCache.ApplyChange` preserves the state at the start of the first affected
  line as well, because it depends only on unchanged text.
- `ThemeCatalog` validates the options record as a unit and names `options` in the exception.
- `IndentationOperations.BuildIndentation` computes its capacity hint in `long`, so a large indent
  level cannot overflow into an unrelated exception.

- `CommentScanner` consumes the remaining characters of a multi-character opener/closer before
  closer matching, so `/*/` no longer false-closes a block and nested openers are handled.
- `PreparedTextEdits.MapOffset` maps an offset at the end of a replaced range to the position after
  the replacement while the start keeps mapping before it.
- `CommentOperations` transform helpers return the original string instance when no comment is found.
- `BacktickFenceTextNormalizer` normalizes CRLF and lone CR to LF before splitting so lines no
  longer glue together.
- `FindReplaceText` treats an empty pattern consistently: `CountMatches` returns 0,
  `FindAllMatches` returns an empty list, and `ReplaceAll` returns the input unchanged.
- `SidecarLineFile` falls back to the `.sidecar` default for a null/blank extension instead of
  using the document path, and saves atomically via a temp file.
- `AvalonEditTextEditTarget.Apply` validates the batch (highest-offset-first order and non-null
  replacement text) before applying anything; an invalid batch throws `ArgumentException` and
  leaves the document untouched instead of corrupting it silently in release builds.
- `TextEditKernel` conflict diagnostics no longer scan insertion-to-insertion pairs, so a batch of
  same-offset insertions is linear again (n - 1 diagnostics for n insertions).
- `IdentifierOperations.GetWordEndingAt` and `TryGetContainingSpan` now share one boundary walk, so the
  word-being-typed rule cannot drift between the two primitives.

**Additions**

- `LineDiffResult` (the changed-line set with `IsApproximate`),
  `StringLiteralStyle.VerbatimDoubleQuoted`, and the optional
  `ContinuationOperations` marker whitespace rule.
- `PreparedTextEdits.MapOffset(int)` maps offsets over a validated batch without repeating the
  operations validation.
- `ThemeCatalog<TTheme>` indexes lookup names in a `FrozenDictionary` for faster reads.
- Added the line-relative match offset on `FindReplaceItem` (`MatchRangeInLine`);
  `ResolveSearchResultLocation` prefers the exact range when it fits the line and keeps the literal
  match as the fallback.
- `ITextSnapshot.Lines` is typed `IReadOnlyList<ITextLine>` (the cached backing view), so callers can
  use `Count` and the indexer without casting.
- Added the host-neutral models moved back from `Nickelony.IDEKit.AvalonEdit`: `SidecarLineFile`
  (`Nickelony.IDEKit.Core.Persistence`) and `FindReplaceSource`/`FindReplaceItem`
  (`Nickelony.IDEKit.Core.FindReplace`).
- Documentation rewrites for `SidecarLineFile` (blank-path and cross-process semantics), the
  find/replace result models (builder/reader contract), and the indentation helper exceptions.

**Review resolution (2026-09-14)**

- Breaking: `TextEditPreparationResult` is created through the `Valid`/`Invalid` factories, so a
  result is either valid (no diagnostics, applicable edits) or rejected (diagnostics, no
  operations) and the contradictory combination cannot be represented.
- Breaking: `TextEditKernel.Prepare` ignores edits that change nothing (an empty range with an
  empty replacement text), so a batch of no-op edits is valid and produces no operations;
  `PreparedTextEdits` likewise ignores no-op operations.
- Breaking: `ITextDocumentFormatter.FormatDocument` returns `string?`; returning `null` means the
  formatter produced no output, which `TextEditorFormattingService` treats as "no changes" instead
  of throwing `InvalidOperationException`.
- Breaking: `TextLineSyntaxService` (a forwarding facade) was removed; its predicate moved to
  `CommentOperations.IsEmptyOrLineComment(string?, CommentSyntax)`, and callers use the static
  comment operations directly.
- Breaking: `FindReplaceItem.MatchRange` was renamed to `MatchRangeInLine`, stating that the range
  is measured against the captured line.
- Breaking: `NavigationHistory<TLocation>` was removed; it had no consumer in either repository,
  and hosts that need back/forward history own it.
- Breaking: `ThemeCatalog<TTheme>` and `ThemeCatalogOptions<TTheme>` require a reference theme type
  (`where TTheme : class`), so `TryGetTheme`'s `[NotNullWhen(true)]` contract is honest.
- Breaking: `IndentationOperations.RemoveSingleIndentLevel` was renamed to
  `TruncateIndentationByUnitLength`; the method truncates by unit length rather than matching a
  level, and the new name states that.
- Fix: `CommentScanner` models `TripleSingleQuoted` strings with Python's rule (the delimiter is
  exactly three quotes and surplus opening quotes are content), so `''''a''' # note` finds the
  comment; raw-string openers ignore an escaped first quote.
- Fix: `LatestRequestCoordinator` swallows faults from a superseded run's cancellation callbacks
  instead of failing the new request before its compute delegate starts.
- Fix: `SidecarLineFile` validates the sidecar extension; a degenerate extension (`.`, a trailing
  dot, or a path or invalid file-name character) throws `ArgumentException` instead of resolving to
  the document itself, and deleting a missing sidecar no longer depends on an existence check.
- Fix: `FindReplaceText` accepts `Regex.InfiniteMatchTimeout` as the documented unbounded timeout
  and evicts one cached regex instead of clearing the whole cache when it is full.
- Fix: `IndentationOperations.GetLeadingWhitespace*` counts only spaces and tabs, matching the
  whitespace converter and trim formatter, so a non-breaking space is never replaced as
  indentation; `IdentifierOperations.GetWordEndingAt` clamps out-of-range offsets like the span
  API.
- Addition: `CommentSpan.ToString()` returns the span in half-open interval notation
  (`[SpanStart..End)`).
- Documentation: corrected scanner move counts, `CommentKind.Line` span rules, `CommentOperations`
  span-rule duplication, `GetCodeRange` examples, `TextEditPreparationResult.Diagnostics` ordering,
  `RequestOutcome` and `LatestRequestCoordinator` publication wording, `FindAllMatches` lazy
  timeout enforcement, `TextSearchQuery` default state, theme case-insensitivity, and the
  diagnostic severity out-of-range caveat.

**Diagnostics alignment (2026-09-13)**

- `TextEditorDiagnosticSeverity` moved to `Nickelony.IDEKit.Core.Diagnostics` (from
  `Nickelony.IDEKit.IntelliSense.Diagnostics`) as the single LSP-aligned severity vocabulary shared
  by producers, renderers, and hosts.

**Naming standardization (2026-09-13)**

- The stateless operation facades dropped the `-Helper` suffix: `CommentHelper` →
  `CommentOperations`, `ContinuationHelper` → `ContinuationOperations`, `IdentifierHelper` →
  `IdentifierOperations`, and `IndentationTextHelper` → `IndentationOperations`.

**Review resolution - `__140916_idekit-core-fresh-review.md` (2026-09-14)**

- Breaking: `ContinuationOperations.IsValidContinuation` was renamed to
  `EndsWithContinuationMarker` (both overloads); the predicate answers whether the code portion of
  the line ends with the marker, not whether the line is a semantically valid continuation.
- Breaking: `TextRangeOffsetResolver.TryResolveOffsets` requires an explicit `isWordCharacter`
  predicate. The previous default word rule (letters, digits, `_`, `.`, `:`, `'`, `"`) was host
  search policy; the Lua diagnostics parser passes its rule explicitly, preserving its behavior.
- Breaking: `TextRange` and `CommentSpan` are `readonly record struct`s; the hand-written equality
  members are gone (generated equality, unchanged `ToString` formats and validating constructors).
- Breaking: hosts own the search-results workflow again. `FindReplaceItem`, `FindReplaceSource`
  (from `Nickelony.IDEKit.Core.FindReplace`) and `SearchResultLocation`,
  `SearchResultLocationStatus`, and the `TextDocument.ResolveSearchResultLocation` extension (from
  `Nickelony.IDEKit.AvalonEdit.Navigation`) moved to the Tomb IDE Scripting Studio
  (`TombIDE.ScriptingStudio.FindAndReplace`, resolver now `SearchResultLocationResolver.Resolve`).
  The package keeps the text-level `FindReplaceText`/`TextSearchQuery` primitives only.
- Fix: `CommentScanner` recognizes the `@$"..."` verbatim-string order beside `$@"..."`, so a
  trailing backslash in such a verbatim string no longer swallows the rest of the line.
- Fix: `TrimTrailingWhitespaceFormatter.FormatDocument` returns the input instance when no line has
  trailing whitespace, so callers can detect a no-op by reference; `ITextDocumentFormatter`
  documents the decline-versus-no-change distinction.
- Fix: `TextEditKernel.Prepare` detects edit conflicts once per batch (the prepared carrier adopts
  kernel-validated operations through an internal factory), and the kernel and the conflict detector
  share one three-key operation order (`TextEditOperationOrder`).
- Fix: `RequestOutcome.Canceled` documentation corrected: the compute delegate either was never
  invoked (an already-canceled token) or completed without observing the cancellation.
- Fix: `StringTextSnapshot.GetText` returns the backing string for a whole-text range without
  copying it, and `BacktickFenceTextNormalizer` joins retained lines with `List<T>.GetRange`
  instead of LINQ iterators.
- Addition: `FindReplaceText` query overloads - `CountMatches(string, TextSearchQuery)`,
  `FindAllMatches(string, TextSearchQuery)`, and `ReplaceAll(string, string, TextSearchQuery)` -
  so all helpers accept the `TextSearchQuery` shape the find helpers already used.
- Addition: tests for the `@$"` verbatim orders, finite query timeouts through the find helpers and
  the new overloads, `tr-TR` theme lookup, a non-invariant-culture sidecar round trip, CRLF/CR
  variants of the multi-line scanner tests, `GetCodeEnd` with line terminators, `LineDiffer` with
  empty-string lines, `TextRangeOffsetResolver` on an empty document, CRLF common-prefix widening,
  and the formatter no-op identity.
- Documentation: README restructured as an adoption guide (conventions table, deliberate
  trade-offs section, no duplicated member contracts), host-flavoured wording removed
  (`TextRangeOffsetResolver`, `FindReplaceItem`), duplication trimmed in `SidecarLineFile`,
  `CommentScanner`, `LatestRequestCoordinator`, `LineDiffResult`, and `IncrementalLineStateCache`,
  the `GetCodeEnd` closing-quote rule explained, and ASCII punctuation restored in the three files
  that used em dashes.

### Nickelony.IDEKit.AvalonEdit

**Breaking changes**

- Added the `NavigationLocation` type moved out of `Nickelony.IDEKit.Core` (Navigation). The
  host-neutral `SidecarLineFile` and the find/replace result models moved back to Core
  (`Nickelony.IDEKit.Core.Persistence`, `.FindReplace`); update the using directives when you
  consumed them from this package. `IIndentationPolicy`/`IndentationContext` stayed in Core
  (`Nickelony.IDEKit.Core.Indentation`).
- `NavigationLocation.FilePath` is nullable now. Capturing a location from an untitled
  document stores `null`; consumers must tolerate a missing path. The type documentation also spells
  out the difference between the record's generated equality and `IsEquivalentTo`.
- `TextEditorViewStateCoordinator.Zoom` was renamed to `ZoomPercent`; the property stores a
  percentage and does not apply it (use `TryApplyZoomStep`).
- `LineStatusMarginBase.SetSourceChangeHandler` takes an `IChangeNotificationSource` and a
  handler instead of three delegates. `IBookmarkSource` and `IChangeMarkerNotificationSource` now
  derive from the new shared `IChangeNotificationSource`, and assigning a second handler throws
  `InvalidOperationException`.
- `TextEditorFormattingService.FormatDocument` applies the minimal replacement range
  instead of replacing the whole document, so the caret keeps its exact offset and a selection
  outside the changed range survives; the line-based fallback still applies when the changed range
  covers them. A formatter that returns `null` now means "no changes" (the Core
  `ITextDocumentFormatter` contract is `string?`).
- Added `EditorPosition` (a one-based line/column record) and reshaped the navigation
  endpoint factories: `TextEditorNavigationOperations.CreateDefinitionLocation`/`CreateRangeLocation` take
  `EditorPosition` values instead of raw line and column numbers.
- The line-comment API now uses ranges: `ITextLineCommentService.TryCreateEdit` takes a
  `TextRange` selection, and `TextLineCommentEdit` stores `ReplaceRange`/`ReplacementText`/
  `Selection` instead of four raw offsets.

**Behavior changes**

- `AvalonEditTextEditTarget.Apply` validates the batch before applying anything: an out-of-order
  sequence or a `null` replacement text throws `ArgumentException` and leaves the document
  untouched.
- `TryReplaceFirstMatchingLine` places the caret at the end of the replacement text in both the
  replaced and the equality no-op branch.

**Fixes**

- `TryReplaceFirstMatchingLine` no longer clamps the caret against the pre-edit document length, so
  a replacement that grows the document places the caret at the end of the replacement text.

**Additions**

- Added `TextEditorFirstMatchingLineOperations` (moved from
  `Nickelony.IDEKit.AvalonEdit.Extras`): selector-driven operations that edit the first matching
  document line (`TryReplaceFirstMatchingLine`).

**Documentation**

- Documentation additions and wording fixes across `DiagnosticsRenderer`, `UnsavedChangesTracker`,
  `TextDocumentSnapshot`, `DocumentVersionCache`, `BookmarkMargin`, `ITextAutoClosingService`,
  `TextEditorEditOperations` (bulleted edit-target contract), `TextEditorNavigationOperations`, and the README
  (including neutral host-integration sample names).

**Review resolution (2026-09-13)**

- Breaking: the line-status source contracts are unified. `IChangeMarkerSource` and
  `IChangeMarkerNotificationSource` are gone; `ILineStatusSource` (Rendering) provides
  `GetMarkedLines()`, `IBookmarkSource` derives from it, and both margins subscribe only when their
  source also implements `IChangeNotificationSource`. `BookmarkCoordinator.GetBookmarkedLines` was
  renamed to `GetMarkedLines`.
- Breaking: auto-closing is data-driven. `TextAutoClosingOptions` carries an ordered
  `TextAutoClosingPair` list (`Open`/`Close`/`WrapSelection`/`SuppressAfterWordCharacter`) with the
  language-neutral pairs (parentheses, braces, brackets, double quotes, single quotes) as the
  default; angle brackets and backticks are opt-in presets. The 14 property matrix, the tuple-based
  resolution, and the default name extractor are removed; the service evaluates pairs in list order
  and resolves a multi-character closing text from its leading token.
- Breaking: `TextEditorFirstMatchingLineOperations.TryReplaceNameInFirstMatchingLine` was removed;
  the host-flavoured rename workflow (line regex + name extractor + equality guard) composes the
  generic `TryReplaceFirstMatchingLine` selector in the consuming host instead.
- Breaking: `TextEditorFirstMatchingLineOperations.TryReplaceFirstMatchingLine` renamed its
  `workspaceEditTarget` parameter to `editTarget` (breaking for named arguments).
- Breaking: `TextWorkspaceEditSelectionState` moved back to its consuming host; the library no
  longer carries workspace-edit vocabulary.
- Breaking: `IIndentationPolicy`/`IndentationContext` moved to
  `Nickelony.IDEKit.Core.Indentation`; `PolicyIndentationStrategy` stays as the AvalonEdit adapter.
- Breaking: `BookmarkSidecarStore` requires the sidecar extension (for example `.bkmrk`) instead of
  defaulting to it, so sidecar naming stays a host decision.
- Breaking: `NavigationLocation.IsEquivalentTo` compares file paths ordinally by default; the
  parameterless convenience overload is removed (pass `StringComparison.OrdinalIgnoreCase` for
  case-insensitive identities).
- Breaking: `TextEditorViewStateCoordinator.TryApplyZoomStep` takes a `ZoomOptions` record
  (bounds/step/base font size) and `ZoomPercent` rejects non-positive assignments.
- Breaking: `TextEditorEditOperations.InsertText`/`ReplaceText` reject negative offsets and lengths with
  `ArgumentOutOfRangeException`; `TextEditorLineOperations.MoveCaretToDocumentEnd` and
  `MoveCaretToLineEnd` (forwarding-only helpers) were removed.
- Fixed: `TextEditorFormattingService` no longer documents an unreachable "offset inside the range"
  mapping; the offset mapping covers exactly the offsets its callers can pass.
- Changed: `DocumentLineStateCache` captures its document snapshot lazily only when a request needs
  line states after the earliest coalesced change; requests for earlier lines are served from the
  cache. The class is documented as single-threaded instead of carrying a partial lock.
- Documentation and tests: the edit-target contract is stated once in `TextEditorEditOperations` and
  referenced elsewhere; a contract-honoring `ContractEditTarget` test double verifies the contract
  while `RecordingEditTarget` now documents its deliberate non-updating behavior; new coverage for
  margin source notifications, multi-segment diagnostic batching, auto-closing wrapping, comment
  clamping, build-failure recovery, lazy line-state snapshots, preferred-line fallback, and the
  margin source-handler guard; the README's non-existent "Enter/completion insertion records" claim
  and other stale text were removed.

**Diagnostics alignment (2026-09-13)**

- Breaking: `TextDiagnosticSeverity` was removed. `TextDiagnosticSegment.Severity` and
  `DiagnosticsRenderer` use `Nickelony.IDEKit.Core.Diagnostics.TextEditorDiagnosticSeverity`, so
  hosts map diagnostics onto segments by value without a translation table.
- `TextEditorDiagnosticSeverity.None` and values outside the defined members draw nothing in the
  renderer.

**Shared text-run styling (2026-09-13)**

- Added `ITextRunStyle` and `TextRunStyleApplier` (Rendering): one styling contract
  (`Foreground`, `IsBold`, `IsItalic`, `TextDecorations`, `HasFormatting`) plus the shared
  paint-time application. `Apply(element, style)` derives the typeface from the element,
  `Apply(element, style, typeface)` uses a caller-supplied (typically cached) typeface, and
  `CreateTypeface(baseTypeface, style)` holds the bold/italic derivation. The typeface is
  only touched when a style requests bold or italic text, and text decorations keep
  AvalonEdit's union semantics.

**Naming standardization (2026-09-13)**

- Stateless facade renames: `TextEditorEditHelper` → `TextEditorEditOperations`,
  `EditorNavigationHelper` → `TextEditorNavigationOperations`, and
  `TextEditorStatusCoordinator` → `TextEditorViewStateCoordinator` (still the stateful
  caret/selection/zoom coordinator).
- Documentation wording: the auto-closing service describes its skip behavior as "auto-closing
  overtype" (VS Code terminology), and the default-state paragraph for a missing action now lives
  on `TextAutoClosingAction` only; `TextAutoClosingResult` refers to it.

**Event convention (2026-09-13)**

- Breaking: `IChangeNotificationSource.Changed`, `BookmarkCoordinator.Changed`, and
  `UnsavedChangesTracker.Changed` are `EventHandler?` instead of `Action?`: the raising source is
  the sender and the event carries `EventArgs.Empty`. Margins register their source handler
  through `LineStatusMarginBase.SetSourceChangeHandler`, which now takes an `EventHandler`.

**Review resolution - `__130923_avalonedit-fresh-review.md` (2026-09-14)**

- Breaking: `ZoomOptions.DefaultFontSize` is renamed to `ReferenceFontSize` (the font size that a
  `100`% zoom applies). `TextEditorViewStateCoordinator.TryApplyZoomStep` validates the options
  through a private helper and computes steps without integer overflow on extreme sizes.
- Breaking: `TextDocument.ResolveSearchResultLocation` returns a `SearchResultLocation` record
  struct (`Status` and `Location`) instead of a status with an `out` parameter.
- Fixed: `DiagnosticsRenderer` normalizes a diagnostic segment before the visibility test, so an
  empty or reversed range that normalizes into the visible area is no longer hidden until the view
  is scrolled.
- Fixed: `UnsavedChangesTracker` honors its cross-thread `SetBaseline` contract. The tracker keeps a
  revision epoch that participates in `DocumentVersionCache<T>` validity and publication, so a
  baseline replaced while `GetMarkedLines` computes its result is never served from the cache.
- Fixed: `BrushHelpers.CreateFrozenBrush(string)` reports an invalid color as `ArgumentException`
  (wrapping the converter's `FormatException`), so every invalid value follows one exception contract.
- Fixed: `TextEditorEditOperations.ReplaceText` rejects a range end that would overflow
  `Int32.MaxValue`, and the default caret offset is computed in 64-bit arithmetic.
- Changed: `RegexHighlightingDefinition.MainRuleSet` builds and publishes the rule set without
  holding a lock while host callbacks run: the snapshot (rule set plus version) is published with a
  volatile write, concurrent rebuilds can build twice with the last publication winning, and
  `RuleSetChanged` is raised once per rebuild after publication. The recursion guard is now
  thread-local and per definition.
- Changed: `TextEditorMouseNavigation.TryGetOffsetFromPoint` uses AvalonEdit's public
  `TextEditor.GetPositionFromPoint`; the private duplicate helper was removed.
- Documentation: the "providers must not throw" render-pass contract is stated on
  `ILineStatusSource` and both margins; the zoom remark matches the implementation (a rejected step
  leaves the stored value unchanged); the bookmark-anchor and change-notification stories have
  single authoritative locations; the README acknowledges the shipped sample defaults; and
  `GetWordFromOffset`, `TextEditorFirstMatchingLineOperations`, and `ResolveSearchResultLocation`
  record their reference consumers.
- Tests: new coverage for normalized diagnostic ranges, the partial-batch `AvalonEditTextEditTarget`
  failure, the inside-line `DocumentLineStateCache` cache hit, first-match stop and `scrollToLine` in
  `TextEditorFirstMatchingLineOperations`, the `BookmarkMargin` click path, and the unarranged-margin
  fallback of both margins; the misleading auto-closing test was corrected and its negative case
  restored; the typeface-preservation, diagnostic batching, revision-cache, and non-owner-thread
  tests were strengthened.

**Core review resolution - `__130923_core-project-review-handoff.md` (2026-09-14)**

- Breaking: `TextEditorFormattingService` treats a formatter that returns `null` as "no changes"
  (the Core `ITextDocumentFormatter` contract is now `string?`) instead of throwing
  `InvalidOperationException`.
- `TextLineCommentService` consumes `CommentOperations.IsEmptyOrLineComment` directly and keeps no
  cached helper, because the rule moved out of Core's removed `TextLineSyntaxService`.
- `TextEditorNavigationOperations.ResolveSearchResultLocation` reads
  `FindReplaceItem.MatchRangeInLine` (renamed from `MatchRange`).

**Core review resolution - `__140916_idekit-core-fresh-review.md` (2026-09-14)**

- Breaking: `TextDocument.ResolveSearchResultLocation`, `SearchResultLocation`, and
  `SearchResultLocationStatus` were removed from this package; the complete search-results workflow
  (together with `FindReplaceItem`/`FindReplaceSource`) moved to the Tomb IDE Scripting Studio
  (`TombIDE.ScriptingStudio.FindAndReplace`, resolver now `SearchResultLocationResolver.Resolve`).
  See the Core section for the full move.

**Review resolution - `__140916_avalonedit-fresh-review-findings.md` (2026-09-14)**

- Breaking: `DiagnosticsRenderer` drops its document provider; segments are clamped against the
  document of the text view actually being drawn, and a view without a document or with invalid
  visual lines draws nothing.
- Breaking: `RegexHighlightingDefinition` no longer falls back to black for an unparseable rule
  color: the foreground stays unset so the editor's theme applies unless the host supplies a
  fallback color. Rule patterns that can match empty text are rejected when the rule set is built,
  because AvalonEdit's highlight engine cannot advance past a zero-length match
  (`RegexHighlightingRule` is a plain sealed class now, not a record).
- Breaking: the auto-closing service gains `HandleBackspace` (wire it from a key-down handler) and
  `TextAutoClosingOptions.AutoCloseBefore`, the character-set gate for the character after the caret.
- Breaking: the auto-closing action/result docs describe the interface instead of the concrete
  service, and `TextAutoClosingOptions.Pairs` copies the assigned list.
- Fixed: a typed auto-closing pair is one document change now, so a single undo removes the typed
  text and the inserted closing text together.
- Fixed: `RuleSetChanged` is raised after the build guard is released, so a handler can reinstall
  the editor's highlighter; the cache-version callback is read once per access.
- Fixed: line-comment toggling no longer deletes non-space/tab whitespace (for example a
  non-breaking space); only spaces and tabs count as indentation.
- Fixed: end-of-file and empty-line diagnostics render as a minimum-width underline instead of
  being dropped by the width cull.
- Fixed: `UnsavedChangesTracker` maintains its line view incrementally from the document's change
  notifications (work proportional to the changed lines, not the document) and raises `Changed` on
  edits; `Dispose` detaches the subscription. It also yields an empty list when its document
  provider returns `null`, as the render-pass contract requires (same for `BookmarkCoordinator`).
- Fixed: `TextDocumentSnapshot` wraps AvalonEdit's own constant-time, thread-safe document snapshot,
  so a background thread can capture and read a document.
- Fixed: `ApplyLocation` snaps every applied boundary out of a CRLF interior, and the navigation
  helpers reject an editor without a document with `InvalidOperationException`.
- Fixed: `BookmarkSidecarStore` validates the extension at construction instead of failing every
  save and restore.
- Behavior: auto-closing follows editor conventions: an opening token is not doubled in front of
  its closing text (an unmatched closing text on the caret's line suppresses the insert), the
  autoCloseBefore gate applies to the character after the caret, wrapping keeps the enclosed text
  selected, a backslash-escaped quote is not overtyped, and `TryMoveCaretToPoint` collapses a
  selection like a plain click.
- `PolicyIndentationStrategy` delegates its indentation unit to the virtual
  `TextEditorOptions.GetIndentationString`, `TextEditorFormattingService` maps offsets through
  Core's `PreparedTextEdits`, and `AvalonEditTextEditTarget` resolves the document once per apply.
- Tests: undo regression for the auto-closing pair, minimum-width diagnostics, render guards, the
  margin click seam (no physical-cursor positioning), WPF automation switches for the test process,
  incremental line tracking (equivalence with a full rebuild), and UTF-16 surrogate offsets.

### Nickelony.IDEKit.AvalonEdit.Markdown

`Nickelony.IDEKit.AvalonEdit.Markdown` is a new package (assembly and namespace
`Nickelony.IDEKit.AvalonEdit.Markdown`): the Markdown tooltip rendering split out of
`Nickelony.IDEKit.AvalonEdit.Extras`, which is removed (see its entry below).

**Breaking changes**

- `MarkdownToolTipOptions.Default.HighlightingAliases` is frozen and cannot be modified through
  the read-only view; assigned option collections are copied and frozen (see the option-collection
  policy entry).
- `MarkdownToolTipTheme.Background` was renamed to `SurfaceBackground` (the color from which code
  backgrounds, borders, quote bars, and separators are derived; it is not applied as the viewer
  background).

**Behavior changes**

- Markdown soft line breaks render as a space; hard breaks still render as a line break.
- Markdown code-block highlighting resolution now tries the supplied language casing first, then a
  case-insensitive definition-name match, then a file extension, and finally the configured
  aliases; common fences such as `python` and `javascript` now resolve.
- Default-theme borders, quote bars, table borders, and separators blend toward a contrasting pole
  (black on light surfaces, white on dark surfaces) instead of always blending toward white.
- Wheel input over a scrollable code block scrolls the code block before the tooltip viewer; the
  viewer takes over only at the code block's scrolling boundary.
- The plain-text fallback element is not focusable, matching the documented contract.
- Hyperlink activation runs through `Hyperlink.Click` with `RequestNavigate` suppressed, so the
  configured `OpenHyperlink` callback or the operating system handler activates a link exactly once.

**Fixes**

- Markdown code fences resolve their highlighting definition case-insensitively; previously the
  lowercased lookup could never match AvalonEdit's capitalized definition names.

**Additions**

- `MarkdownToolTipOptions.HighlightingAliases` maps code-fence languages to highlighting
  definitions (defaults for `csharp` and `json5`).
- The default hyperlink scheme set is frozen.
- `MarkdownToolTipRenderer.CreateFlowDocument` renders Markdown into a `FlowDocument` for hosts
  that present the content in their own container.
- `MarkdownToolTipOptions.OpenHyperlink` and `MarkdownToolTipOptions.Logger` add a hyperlink-opener
  seam and configurable rendering diagnostics; unsupported code-fence languages are reported at
  debug level.
- GFM table column alignment is applied to table cell text.

**Review resolution (2026-09-13)**

- Breaking: `MarkdownToolTipTheme.CodeBorderBlendRatio` was renamed to `BorderBlendRatio` (it
  also drives quote bars, table borders, and separators).
- Breaking: `MarkdownToolTipTheme` validates its numeric constraints on assignment (positive
  sizes, at least one visible code-block line, positive heading scales).
- Changed: the default `HighlightingAliases` map contains only effective mappings (`csharp` →
  `.cs`, `json5` → `.json`); `cs`, `js`, and `ts` never applied because extension, name, or
  missing-definition resolution already decided those lookups.
- Changed: code backgrounds blend toward the contrasting pole like the other derived colors, so a
  dark surface no longer produces a code background indistinguishable from the surface.
- Changed: Markdown rendering and hyperlink-open failures are logged through
  `MarkdownToolTipOptions.Logger` only; the `System.Diagnostics.Trace` fallback was removed to match
  the resolver's logger-only policy. Diagnostic events use distinct event ids.
- Documentation: README de-duplication plus the design rationale for the custom Markdown renderer.

**Shared text-run styling and renderer decomposition (2026-09-13)**

- The Markdown renderer was decomposed without public-surface changes:
  `MarkdownToolTipRenderer` stays the facade, `MarkdownFlowDocumentBuilder` (internal) owns
  parsing, block and inline building, hyperlink activation, and the derived theme brushes,
  and `MarkdownCodeBlockFactory` (internal) owns code-block editor creation, measurement,
  and language resolution.

**Review resolution round 2 (2026-09-14)**

- Breaking: `MarkdownToolTipTheme.TextMaxWidth` was renamed to `CodeMaxWidth`; it constrains code
  blocks and inline code only. Body text wraps at the width the document or viewer is given.
- Breaking: `MarkdownToolTipOptions.InstallCustomHighlighting` was renamed to
  `CustomHighlightingInstaller` (a noun-shaped callback name; the boolean result still means
  "handled").
- Breaking: `MarkdownToolTipTheme` validates every numeric value on assignment: sizes and code
  widths must be positive and finite, `BlockSpacing` must be non-negative and finite, and heading
  scales must be positive and finite. Blend ratios stay clamped when rendered.
- Fixed: `AllowScrolling = false` now truly disables scrolling. The viewers and code blocks use
  `ScrollBarVisibility.Disabled`, so overflow is clipped at the theme limits and the mouse wheel is
  left to an enclosing host scroller.
- Fixed: `MarkdownToolTipOptions.OpenHyperlink` returning `false`, or throwing, counts as "not
  handled" and falls back to the operating system's default protocol handler, exactly like a missing
  callback; a host that wants to suppress an activation returns `true` or excludes the scheme.
- Fixed: email autolinks (`<user@example.com>`) target `mailto:` while still displaying the bare
  address; they become openable when the supported scheme set allows `mailto`.
- Fixed: code-block heights are measured with the editor's own text view, which accounts for
  AvalonEdit's tab stops and inherited word-wrap indentation; blocks below the visible-line limit
  keep an automatic height, clamped blocks pin to the limit, and the vertical scroll bar is automatic
  when scrolling is allowed and disabled when it is not.
- Fixed: code-block text has its line endings normalized to line feeds and its trailing blank lines
  removed, and both behaviors are now documented on `CreateCodeBlockEditor`.
- Fixed: mouse-wheel steps match the native ones (three text lines per notch, or one page when the
  operating system is configured to page-scroll); the wheel over a code block's border also scrolls
  the block, and an event at the viewer's scrolling boundary is left for an enclosing host scroller.
- Fixed: table-cell paragraphs no longer carry the block spacing margin, which previously added dead
  space below every cell.

**Option collection policy (2026-09-14)**

- Breaking: `MarkdownToolTipOptions.SupportedHyperlinkSchemes` and `HighlightingAliases` are
  copied and frozen on assignment with fixed case-insensitive semantics (the shipped defaults),
  matching `MarkdownToolTipTheme.HeadingFontSizeScales` and `TextMateTokenTheme.Rules`. Later
  changes to an assigned collection are no longer observed, mutating the stored copy is impossible,
  and an assigned collection's own comparer is no longer preserved; assigning `null` now throws
  `ArgumentNullException`. The renderer's alias lookup drops its now-dead lowercased fallback
  because every stored map already matches any casing.

### Nickelony.IDEKit.AvalonEdit.TextMate

`Nickelony.IDEKit.AvalonEdit.TextMate` is a new package (assembly and namespace
`Nickelony.IDEKit.AvalonEdit.TextMate`): the TextMate syntax-highlighting infrastructure split out
of `Nickelony.IDEKit.AvalonEdit.Extras`, which is removed (see its entry below).

**Breaking changes**

- `TextMateDocumentLineList.GetChangeInfo` was removed from the public surface; the line-span
  helper is now internal (`GetAffectedLineCounts`).
- The TextMate theme models are now `sealed record` types with `init` accessors
  (`TextMateTokenTheme`, `TextMateTokenThemeRule`, `TextMateHighlightingStyle`). They are no longer
  mutable after construction and now use value equality; deserialization remains supported.
- `TextMateThemeStyleResolver.Resolve` now accepts `IReadOnlyList<string>?` (callers passing
  `List<string>` are unaffected).

**Behavior changes**

- Space-separated TextMate descendant selectors now match (previously they never matched). The
  `-`, `>`, and parenthesis selector operators remain unsupported and are now reported as warnings.
- After a multi-line document change, the whole changed line region is invalidated so replacement
  lines are re-tokenized instead of keeping tokens from removed lines.
- TextMate selectors that use the wildcard (`*`) or priority (`L:`) operators never match and are
  now reported as warnings.
- TextMate theme rules are now ranked with VS Code's token-theme specificity order: scope depth first,
  then parent scope name length (compared from the deepest parent towards the root), then parent count.
  The previous ranking also used selector and matched-scope lengths, and resolved equal-specificity ties
  to the first-listed rule; ties now favor the later-listed rule, so appended theme overrides win. Host
  themes with multi-part selectors can resolve to different colors than before.

**Fixes**

- TextMate theme selectors that use unsupported operators and unrecognized `FontStyle` traits now
  report warnings through the resolver logger instead of being ignored silently (invalid foreground
  colors were already reported).

**Additions**

- The style resolver and colorizing transformer use bounded caches.
- `TextMateDocumentLineList` invalidates a changed region with a single `InvalidateLineRange` call
  instead of one model round-trip per line.

**Review resolution (2026-09-13)**

- Breaking: `TextMateTokenThemeRule.FontStyle` is nullable now; `null` (an absent `fontStyle`
  field) keeps inherited traits, while any present value, including `""`, resets them before the
  recognized traits are applied, matching TextMate/VS Code theme data. The `none` keyword is still
  accepted as an explicit reset.
- Fixed: eight-digit TextMate colors (`#RRGGBBAA`) are normalized to the WPF `#AARRGGBB` order
  instead of being silently misread with a stray alpha component.
- Fixed: the colorizing transformer no longer tokenizes from the paint path. That pass drove
  TextMateSharp's tokenizer concurrently with the model's own tokenizer thread, which can corrupt
  tokens; lines stay with the base style until the background pass completes and raises the
  coalesced redraw.
- Fixed: a queued redraw that fails because the text view's dispatcher has shut down no longer
  escapes the token-change callback or wedges the coalescing gate.
- Changed: resolved-style cache access is synchronized, so one `TextMateThemeStyleResolver` can be
  shared across editors and threads; unsupported selector rules are dropped once at construction
  instead of being re-checked on every resolution.
- Documentation: TextMate `fontStyle` reset semantics, eight-digit color handling, the serialized
  JSON contract (case-insensitive property matching), direct-child terminology, thread-affinity
  and disposal contracts; and the design rationale for the custom resolver.

**Shared text-run styling and renderer decomposition (2026-09-13)**

- `TextMateHighlightingStyle` implements the shared `ITextRunStyle` contract and derives
  typefaces through `TextRunStyleApplier.CreateTypeface`; `TextMateColorizingTransformer`
  applies styles through `TextRunStyleApplier` while keeping its bounded typeface cache.
  Behavior is unchanged: the foreground, font traits, and decorations are applied in the
  same order and under the same conditions as before.

**Review resolution round 2 (2026-09-14)**

- Fixed: line lists no longer corrupt or throw when an edit splits or forms a CRLF pair (for
  example deleting the line feed of `\r\n`, removing the carriage return, or inserting a lone
  carriage return before a line feed). The affected line range is derived from the document's line
  geometry instead of the line breaks found in the changed fragment, the model's line-state list is
  reconciled with the snapshot, and a follow-up edit can no longer observe a poisoned state.
- Fixed: TextMate theme rules with a blank scope now provide the theme defaults applied to every
  token (the layout of VS Code theme files), and four-digit `#RGBA` colors are normalized to the WPF
  order.
- Fixed: unsupported TextMate selector operators are detected anywhere in a selector now - embedded
  parentheses (`keyword(foo)`), wildcard suffixes (`scope*`), and lower-case priority prefixes
  (`l:keyword`) are reported through the resolver logger instead of being silently accepted.
- Changed: theme resolution follows VS Code's scope-push model. For every scope of a token, the
  selector's rightmost part is anchored at that scope, the matching rules of the push are applied
  from the most specific to the least specific, and the result overwrites the accumulated attributes,
  so a deeper push can override a shallower rule's outcome. Previously all matches were ranked
  globally and the rightmost part could match any scope. Theme rules that resolve differently than
  before should be reviewed against VS Code's own rendering.
- Changed: the colorizing transformer reopens its coalescing gate when a queued dispatcher operation
  is aborted (dispatcher shutdown) instead of dropping notifications for the rest of the session;
  type remarks also document that removing the last model listener stops TextMateSharp's tokenizer
  thread and that coloring follows the background pass without viewport prioritization.
- Changed: the line-list remarks describe the geometry-based delta computation, the intentional
  out-of-range reads of the snapshot members, the transient-tear contract for the tokenizer thread,
  and the model/line-list disposal order instead of the previous "line breaks in the fragment"
  premise.
- Added: `TextMateHighlightingAttachment` wires the line list, model, resolver, and colorizing
  transformer to an editor in one step and disposes them in the safe order, so hosts no longer
  hand-wire the quartet (with its one-model-per-list and disposal-order contracts).

### Nickelony.IDEKit.AvalonEdit.Extras (removed)

The `Nickelony.IDEKit.AvalonEdit.Extras` package was removed; there is no compatibility
meta-package. The Markdown tooltip rendering moved to `Nickelony.IDEKit.AvalonEdit.Markdown`
(`Nickelony.IDEKit.AvalonEdit.Extras.Markdown` → `Nickelony.IDEKit.AvalonEdit.Markdown`) and the
TextMate syntax-highlighting infrastructure to `Nickelony.IDEKit.AvalonEdit.TextMate`
(`Nickelony.IDEKit.AvalonEdit.Extras.TextMate.Highlighting` →
`Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting`); the assembly and package ids change with the
namespaces. A host that consumed both namespaces adopts both packages.

### Nickelony.IDEKit.Workspace

**Breaking changes**

- Added the workspace-edit result group moved out of `Nickelony.IDEKit.Core` under
  `Nickelony.IDEKit.Workspace.Editing`.
- Store requests now carry a `WorkspaceDocumentRequestIdentity` (document key, normalized id, and
  expected version) as their first argument instead of three separate members, and results echo it
  as `RequestedIdentity` instead of `RequestedDocumentKey`/`RequestedDocumentId`/`RequestedVersion`.
- View state no longer appears in `Documents` contracts. Store enums no longer expose
  `ViewNotSynchronized`, `ViewUpdateFailed`, `CommittedWithUnsynchronizedView`, or
  `ResolvedWithUnsynchronizedView`, and store results no longer carry `BlockingViewIds` or
  `FailedViewIds`. Manager operations that coordinate views return composed manager results
  (`WorkspaceDocumentManagerCommitResult`, `...RenameResult`, `...SaveAsResult`, `...DeleteResult`,
  `...ConflictResolutionResult`, and the directory variants) that wrap the store result and add a
  `WorkspaceDocumentViewSynchronization` outcome (`Blocked`, `Unsynchronized`, or `Synchronized`).
- `WorkspaceFileCodec` was split: the static text operations moved to `WorkspaceTextCodec`, and the
  default `IWorkspaceFileSystem` implementation is now `LocalWorkspaceFileSystem`.
- `IWorkspaceDocumentStore.TryReplace` was renamed `Replace`, and
  `WorkspaceDocumentMutationStatus.Replaced` was renamed `Changed`.
- `EditorSession` and the editor-view host/session contracts (`IEditorViewHost`, `IEditorSession`,
  `EditorSessionOptions`, `EditorSessionMode`, `EditorSessionOpenResult`, `EditorSessionOpenStatus`)
  were removed from this package: they were a host-side editor-leasing policy the package never
  consumed. The only consumer (Tomb Editor) now owns them under
  `TombIDE.ScriptingStudio.TextEditing`.
- `TextWorkspaceEditTargetResult` is created through an object initializer (the constructor and its
  duplicated validation were removed).
- `TextWorkspaceEditApplicationResult` de-duplicates `ChangedTargetIds` and
  `UnknownTargetIds` with an ordinal comparison by default. Pass the new optional
  `targetIdComparer` parameter (`StringComparer.OrdinalIgnoreCase`) to keep the previous
  case-insensitive behavior for file-path target ids; `WorkspaceEditApplierCore` accepts
  the same comparison and forwards it to the results it creates.
- `WorkspacePathComparison.Default` was renamed `ForCurrentPlatform` (same behavior: case-insensitive
  on Windows and macOS, ordinal elsewhere), and only fully qualified paths are accepted as document
  ids: a relative path is invalid input instead of being resolved against the process current
  directory. A case-sensitive macOS or Linux volume must pass
  `WorkspacePathComparison.CaseSensitive` explicitly.
- `WorkspaceDocumentStore.RenameDirectoryAsync` reports
  `WorkspaceDocumentDirectoryRenameStatus.NoChange` when the destination normalizes to the
  source directory (previously `InvalidPath`); equivalent spellings such as a trailing
  separator or a `"."` segment are now handled the same way instead of reaching the file
  system.
- `WorkspaceDocumentManager.OpenWithView` (synchronous) was removed; use `OpenWithViewAsync`. The
  manager's open operations (`OpenAsync`, `OpenWithViewAsync`) return the unified
  `WorkspaceDocumentManagerOpenResult` family with `WorkspaceDocumentManagerOpenStatus`.
- `TextWorkspaceEditApplicationResult` is created through the `Completed`/`PartiallyApplied`/
  `ValidationFailed` factories (the constructor is private), so a result cannot contradict its
  status; the factories no longer take a `changedTargetIds` list (it is derived from the change set),
  `PartiallyApplied` rejects a null failure, and the failure detail is a `WorkspaceOperationFailure`
  (`TextWorkspaceEditFailure` was removed).
- `FileReloadCoordinator.ProcessQueuedFiles` takes a `FileReloadCallbacks<TPromptResult>` bundle
  instead of four delegates.
- `WorkspaceDocumentDeleteStatus.InvalidPath` was removed; deleting a missing document reports
  `DocumentNotFound`.
- Review hardening (2026-09-14): `IWorkspaceDocumentView.SetDeleteGuard(bool)` was split into
  `ApplyDeleteGuard()`/`ReleaseDeleteGuard()`, `DiscardPendingEdits` was removed from the interface
  (hosts keep their own view affordance), and `DocumentId` is nullable.
- `IWorkspaceDocumentManager.GetSnapshotsUnderDirectory` was removed in favor of the new
  `IWorkspaceDocumentReader Documents` accessor; `ReloadAsync` now returns the composed
  `WorkspaceDocumentManagerReloadResult` and blocks on blocking view state before the store.
- `WorkspaceDocumentManagerOpenStatus.ViewInConflictState` was renamed `ViewUnavailable`; the new
  members `ViewInUse` (duplicate view id or a view attached elsewhere) and `ViewRejected` (the view
  did not accept the attachment; snapshot present) replace the previous conflation with
  `AlreadyOpen`/`OpenFailed`, and `OpenFailed` now means an unexpected exception. `NotFound` was
  added to the store and manager open statuses alongside
  `WorkspaceDocumentOpenOptions.CreateIfMissing` (default `true`).
- `TextWorkspaceEditApplicationResult.Targets` was renamed `TargetResults`;
  `TextWorkspaceDocumentChange` lost its three-string constructor (use an object initializer);
  `WorkspaceEditTargetPreparation` embeds `WorkspaceDocumentRequestIdentity Identity` instead of
  `DocumentKey`/`DocumentId`/`ExpectedVersion`.
- `WorkspaceEditApplierCore` gained a `continueOnFailure` constructor option and an optional
  `CancellationToken` on `Apply`; two prepared changes to one document now both apply because the
  applier tracks the version each accepted replacement returns.

**Fixes**

- `WorkspaceDocumentStore.DeleteDirectoryAsync` no longer leaves earlier documents
  rejecting `Replace` with `OperationInProgress` when a later document's gate is busy.
- A replacement that completes without reporting its resulting stamp is handled consistently:
  the store re-captures the stamp (saving or committing with the true post-write stamp) or reports
  `ReplacementStateUnknown` when the stamp cannot be established, instead of recording a stale
  pre-write stamp or a missing stamp for a file that exists.
- `WorkspaceDocumentStore.OpenAsync` waits for an in-flight delete of the same path to resolve
  before it answers, and a successful delete closes views that were attached while the delete ran,
  so no view stays bound to a removed document instance.
- `FileReloadCoordinator` no longer drops a path that a watcher queues while its own pass is
  running; the path stays queued for the next pass, and a callback exception retains the pending
  entries instead of losing them.
- Disposed stores throw `ObjectDisposedException` from every reader member regardless of the
  supplied path, and the exception reports the store or manager type name instead of
  `System.String`.
- `LocalWorkspaceFileSystem` no longer misreports an I/O failure of a case-only rename as
  `DestinationExists`; only a destination that appeared after the pre-check classifies as an
  existence conflict.
- `WorkspaceDocumentStore` writes temporary files for volume-root document ids into the
  root directory instead of the process current directory, keeping the replacement on one
  volume.
- `WorkspaceDocumentStore` re-validates the tracked instance after a dirty reload's stamp
  capture and reports `DocumentNotFound` or `StaleDocumentInstance` instead of returning a
  result for a detached instance.
- `WorkspaceDocumentStore` passes normalized directory ids to the file system for directory
  moves and deletes instead of the caller's display spelling.
- `LocalWorkspaceFileSystem.ReplaceAsync` reports a destination that appears while its expected
  stamp was missing as `ExternalFileConflict`, and uses a non-overwriting move for the
  missing-destination case.
- `WorkspaceEditApplierCore.Apply` rejects a null prepared target with `ArgumentException`
  instead of a `NullReferenceException`.
- Review hardening (2026-09-14): rename, save-as, and directory-rename destination checks now
  include in-flight open reservations and report `DestinationBusy`, so a move can no longer drop a
  tracked document instance when a concurrent open completes first.
- The temporary file of an atomic save is flushed to the physical device before it is published, so
  a power loss cannot leave a zero-length destination behind a reported `Committed`.
- A dirty reload that races disposal completes with `Canceled` instead of leaking an
  `ObjectDisposedException`, and it no longer installs its possibly stale observed stamp over a
  newer stamp a commit installed meanwhile.
- Directory-rename rebasing slices the validated source prefix with the configured path comparison
  instead of `Path.GetRelativePath`, which compares ordinally on Unix and could produce
  non-normalized destination ids for case-divergent descendants.
- An encoding failure during commit or save-as reports failure code `InvalidEncoding`, and `Replace`
  rejects a Windows-1252 format with a byte-order mark instead of storing a format that can never be
  written.
- `ResolveExternalConflictAsync` throws `ArgumentOutOfRangeException` for an undefined conflict
  resolution choice instead of silently taking the `UseDisk` branch.
- Delete guards are released in a `finally` block and all guard teardown reads are exception-safe,
  so a throwing view or host pump can no longer latch a guard permanently and block every later
  synchronization. Views are unsubscribed before they are closed after a delete, and a throwing
  event accessor no longer faults the memoized stop task.
- The view manager no longer reads view members under its state lock or outside the host callback;
  view ids are captured on the host at registration and used for duplicate detection, tracked
  versions only advance, unregistered views are not recorded as unsynchronized, and result view-id
  lists are de-duplicated and ordinally sorted.
- `LocalWorkspaceFileSystem.ReadAsync` reports a file that vanishes between the existence probe and
  the open as missing instead of a load failure, and a commit reads and hashes the destination once
  instead of twice.

**Additions**

- `WorkspaceDocumentViewSynchronization` reports whether attached views blocked a manager
  operation or stayed unsynchronized after it; manager result records compose it with the store
  result so store-only consumers never handle view states.
- `WorkspaceOperationFailureCodes` centralizes the stable failure-code strings carried by
  `WorkspaceOperationFailure.Code`.
- `LocalWorkspaceFileSystem` accepts a `WorkspacePathComparison` that drives case-only rename
  handling, so case-insensitive file systems on macOS use the same intermediate-rename
  strategy as Windows.
- `WorkspaceEditApplierCore` and `TextWorkspaceEditApplicationResult` accept an optional
  target-id comparer for the changed and unknown target lists.
- `IWorkspaceDocumentReader` exposes the read-only store surface (`PathComparison`, `OpenAsync`,
  `TryGetSnapshot`, `GetSnapshotsUnderDirectory`); `IWorkspaceDocumentStore` composes it, so
  snapshot-only consumers no longer depend on the mutation surface.
- A clean no-op commit no longer captures a file stamp (it reads and hashes the whole file for a
  write that does not happen); dirty state is cached on the tracked document instead of re-compared
  against the persisted baseline for every snapshot.
- Review hardening (2026-09-14): `IWorkspaceDocumentManager.Documents` exposes the read-side
  document authority, `WorkspaceOperationFailureCodes` gained `Canceled`, `TargetNotChanged`, and
  `TargetApplicationFailed`, and `WorkspaceDocumentViewSynchronization.ViewIds` now documents its
  de-duplicated ordinal ordering.

**Review resolution - `__140916_workspace-fresh-review.md` (2026-09-14)**

- Breaking: the view slice moved to the new `Nickelony.IDEKit.Workspace.Views` package (see its
  entry below). `Nickelony.IDEKit.Workspace` now ships the document authority and the editing core
  only, so a headless consumer never sees view contracts.
- Breaking: `WorkspaceOperationFailureCodes` moved out of the
  `Nickelony.IDEKit.Workspace.Documents` namespace into the root namespace
  `Nickelony.IDEKit.Workspace`, because the codes are shared vocabulary across the workspace
  packages.
- Breaking: the editing slice uses one `WorkspaceEdit*` family: `WorkspaceEditApplierCore` →
  `WorkspaceEditApplier`, `TextWorkspaceEditApplicationResult` → `WorkspaceEditApplicationResult`,
  `TextWorkspaceEditApplicationStatus` → `WorkspaceEditApplicationStatus`,
  `TextWorkspaceEditTargetResult` → `WorkspaceEditTargetResult`, `TextWorkspaceEditTargetStatus` →
  `WorkspaceEditTargetStatus`, `TextWorkspaceEditChangeSet` → `WorkspaceEditChangeSet`, and
  `TextWorkspaceDocumentChange` → `WorkspaceDocumentChange`. The applier remarks state that the
  name is deliberately narrower than the Language Server Protocol's `WorkspaceEdit`.
- Breaking: a clean no-op commit validates the caller's expected stamp against the tracked stamp and
  reports `ExternalFileConflict` (with the tracked stamp as the observed stamp) when they disagree,
  instead of reporting `Committed` for any expected stamp; the no-op path still skips the file read.
- Breaking: the internal six-parameter `LocalWorkspaceFileSystem.ReplaceAsync` test seam was
  replaced by a constructor-injected `WorkspaceFileReplaceOperations` strategy; the public member
  keeps its three-parameter shape.
- Fix: the applier tracks accepted-replacement versions with the configured target-id comparison, so
  case-variant target ids that the result de-duplication treats as one document are also tracked as
  one document instead of producing a spurious `StaleDocument`.
- Fix: directory rename and directory delete include tracked descendants that the request does not
  list, with their current tracked stamp as the expectation, so a descendant that opened after the
  caller enumerated the directory can no longer keep a stale identity (or stay tracked after its
  file was deleted).
- Fix: `WorkspaceDocumentSnapshot.Content` returns the captured string directly for store-created
  snapshots instead of copying a substring on every access.
- Fix: a case-only rename whose rollback move also fails reports an aggregate exception carrying
  both the original move failure and the rollback failure instead of replacing the original one.
- Fix: `OpenAsync` adds a loaded document only after its first snapshot was created, so a snapshot
  failure cannot leave a tracked document behind a reported load failure.
- Documentation: `IWorkspaceDocumentReader.OpenAsync` no longer claims relative paths are resolved;
  the commit remarks describe the no-op stamp validation; the store remarks describe disposal of an
  in-flight dirty reload; `Replace`/`Discard` document how they exclude interleaving operations; the
  file-stamp cost sentence was split; and the applier's `Apply` result bullets list each
  `NotApplied` cause separately.
- Tests: new coverage for the no-op commit stamp validation, directory batch auto-inclusion for
  rename and delete, the canceled file-system delete status, and the file-system cancellation
  matrix.

### Nickelony.IDEKit.Workspace.Views

`Nickelony.IDEKit.Workspace.Views` is a new package (assembly and namespace
`Nickelony.IDEKit.Workspace.Views`): the document-view slice split out of
`Nickelony.IDEKit.Workspace` so a headless host that only needs document authority never takes the
view contracts. It references `Nickelony.IDEKit.Workspace` and `Nickelony.IDEKit.Core`.

**Breaking changes** (relative to the view slice that shipped inside `Nickelony.IDEKit.Workspace`)

- The manager's constructor parameter is named `dispatchViewAction` (was `invokeOnHost`) and takes
  `Func<Action, Task>`: every view member access is awaited through the delegate, so an
  asynchronous dispatcher (WinUI/MAUI/web) is supported; a synchronous host executes the action
  inline and returns a completed task.
- Rename, save-as, and directory rename no longer block on attached-view state (pending edits,
  conflicts, or unsynchronized failures). They proceed and report an identity-acknowledgment
  failure as `Unsynchronized`. Delete, delete-directory, commit, reload, and conflict resolution
  keep blocking on view state, which the `IWorkspaceDocumentManager` remarks state as policy;
  `Discard` is a host-intent operation and never blocks.
- `AlreadyOpen` from `OpenWithViewAsync` reports the current snapshot of the document the view is
  attached to (or `null` when that document is no longer tracked), on both the validation
  short-circuit and the concurrent-registration path.
- `WorkspaceDocumentManager.StopAsync` releases every view-tracking collection (previously the view
  id index was retained, keeping every stopped view alive).

**Fixes**

- Every manager operation that accepts a request validates it synchronously (an immediate
  `ArgumentNullException` instead of a faulted task).
- The `ApplyRequested` subscription is added outside the manager's state lock; a failing event
  accessor rolls the registration back and closes the view instead of tracking a view without its
  event channel.
- Directory delete closes views that attached during the operation for every removed descendant,
  including descendants the manager did not enumerate.
- `MapOpenStatus` covers every store open status explicitly; an unmapped value maps to `OpenFailed`
  with the documented remark instead of throwing.

**Fresh review resolution (2026-09-19, `__180923_` Workspace review)**

*Breaking changes*

- `ReplaceAsync`/`DiscardAsync` are the only replace/discard members and return
  `Task<WorkspaceDocumentManagerMutationResult>` (store result + view synchronization + snapshot);
  the synchronous `Replace`/`Discard` members are deleted.
- The delete-guard and edit capabilities are separate interfaces:
  `IWorkspaceDocumentDeleteGuardView` (a view that does not implement it is skipped by delete and
  directory-move guards) and `IWorkspaceDocumentEditView : IWorkspaceDocumentView,
  ITextEditTarget`.

*Fixes*

- A view attached while a directory rename is in flight is acknowledged by its document key and
  rekeyed to the moved identity instead of staying bound to the vacated id; a failed
  acknowledgment is reported as `Unsynchronized`.
- Delete guards are released before views are closed on the success path; the guard rollback after
  a rejected or throwing guard leaves the rollback state consistent and keeps reporting the views.
- Commit retries the synchronization of a view whose only outstanding state is a prior failure
  (also at the current version) instead of blocking the commit permanently.

**Tests**

The view tests moved to the new `Nickelony.IDEKit.Workspace.Views.Tests` project; new coverage for
the relaxed rename/save-as blocking policy, the `AlreadyOpen` snapshot invariant, concurrent
same-instance opens, the subscription rollback, the view-id rejection branches, the stop cleanup,
the explicit path-comparison override, and the deleted-instance fallback for views that cannot
report their document key.

### Nickelony.IDEKit.IntelliSense

**Breaking changes**

- `TextHoverInfo` requires a non-null `Content` and takes only `content`/`contentKind` in its
  constructor; `SymbolName`, `Range`, and the typed `DefinitionDiscriminator` are init accessors
  assigned with an object initializer.
- `TextDocumentSymbolRequest` is now a `sealed record` class (was a `readonly record struct`) with a
  constructor that rejects a `null` document text or filter, matching the other request contracts.
  Construction call sites are unchanged; `default(TextDocumentSymbolRequest)` no longer exists as
  an invalid value (a default variable is now a null reference).
- Signature help is now modeled as a signature list instead of a single signature.
  `TextSignatureHelpInfo` carries `Signatures` (the new `TextSignatureInformation` payload with the
  label, documentation, parameters, and optional active parameter of one signature option),
  `ActiveSignatureIndex`/`ActiveSignature`, and a `WithActiveSignature` copy operation for overload
  navigation; the single-signature `Label`, `Documentation`, and `Parameters` accessors are
  removed. `ActiveParameterIndex` is `int?` and never a sentinel: `null` is the LSP 3.18 "no active
  parameter" state (and the result for a signature without parameters), the LSP 3.16+ resolution
  order applies (the active signature's own index wins, the payload-level default selects the first
  parameter), a value outside the parameter range is clamped to an existing parameter, and
  `TextSignatureHelpInfo` rejects `null` signature elements.
- `TextSignatureHelpRequest` accepts an optional `TextSignatureHelpContext` describing how the
  request was triggered (`TextSignatureHelpTriggerKind`, trigger character, retrigger flag, and the
  previously active payload). The context becomes the standard way for providers to keep the
  selected overload stable across content changes.
- `TextHoverInfo` carries an optional zero-based `Range` (`TextPositionRange`) that identifies the
  hovered span when the provider can supply one, and the follow-up navigation target moved from the
  untyped `ProviderData` channel to the typed `DefinitionDiscriminator`.
- `TextDefinitionLocation` is now a `sealed record` carrying `TargetRange`/`SelectionRange`
  (zero-based LSP positions via `TextPositionRange`) plus an opaque `DocumentId`; the one-based
  `LineNumber`/`ColumnNumber`/`FilePath` properties are gone.
- Completion request metadata stays on the item (`WithRequestContext`, `WithoutTextEdit`,
  `ResolveAsync`); resolved results are returned exactly as the resolve callback supplied them, so
  hosts that must keep resolved edit data out of the commit path re-apply `WithoutTextEdit`
  themselves.
- `TextCompletionSessionKernel.GetDecision` is synchronous; the asynchronous entrance was removed
  (hosts own threading).

**Additions and fixes**

- Added well-known completion kinds so LSP-backed providers can map protocol kinds without
  collapsing them: `Function`, `Constructor`, `Event`, `Operator`, `Interface`, `Enum`, `Struct`,
  `TypeParameter`, `Module`, `Unit`, `Value`, `Reference`, `Snippet`, and `Color`; `Text` and
  `EnumMember` were added later, so the set now covers every LSP 3.17 `CompletionItemKind`
  one-to-one (see the completion-model standards entry). The LSP mapping table is pinned in the
  `TextCompletionItemKind` documentation, so a provider keeps the protocol kind instead of mapping
  it onto the closest rendered category.
- `DiagnosticHitTester` selections return document order (start offset, then end offset) and are
  computed in a single pass; generic selection no longer encodes severity-first hover policy.
  `SelectHoverDiagnostics` results are consequently in document order as well.
- Diagnostic presentation moved to editor hosts: `DiagnosticHitTester` performs selection only
  (document order, single pass) and no longer composes hover messages; severity labels and message
  prefixes are host presentation rules. `TextEditorDiagnosticSeverityExtensions` and
  `DiagnosticHitTester.FormatMessage`/`BuildCombinedMessage` were removed from this package (Tomb
  Editor hosts the formatting in `TombLib.Scripting.UI.Diagnostics.TextDiagnosticMessageFormatter`).
- `TextEditorDiagnostic` exposes a `Range` property and a `(severity, message, TextRange)`
  constructor overload; the saturated `int.MaxValue` remark now distinguishes containing an offset
  from intersecting a range.
- Argument-out-of-range errors for `TextCompletionRequest`, `TextHoverRequest`, and
  `TextSignatureHelpRequest` report the offending value and the document length.
- Documentation: definition columns are documented with
  null-path and clamping semantics, symbol-kind numeric stability and the 0-is-invalid rule are
  documented, `TextDocumentSymbolRequest.Filter` matching semantics are provider-defined,
  semantic-token sets note the LSP 3.17 baseline (3.18 adds `label`), and the completion filter
  documents whitespace-word
  and label-fallback semantics.
- `DocumentSymbolOutlineBuilder` (replacing `DocumentSymbolTreeBuilder`) builds a flat outline
  (`BuildFlatOutline<TItem>`) or one group root per group (`BuildGroupedOutline<TGroup, TItem>`)
  through a `DocumentSymbolProjection<TItem>` selector bundle.

**Review follow-ups (IntelliSense slice)**

- `TextCompletionTextEdit` carries `NewText` (the LSP-style replacement text); commit paths prefer
  it over `TextCompletionItem.InsertText`, which now falls back to the label only when it is
  `null`, so a whitespace-only commit is representable. The Lua parser keeps the edit text on the
  edit payload instead of folding it into the insertion text, and the report-side completion
  identity includes the edit text.
- `TextCompletionItemKind` reserved-name matching is case-insensitive (`FromIdentifier("method")`
  resolves to the well-known `Method`), custom kinds are interned per identifier, and
  `TextCompletionFilter` matches
  the filter text (label fallback) only, mirroring the LSP rule; `FilterByCurrentWord` is removed.
- Outline-builder contracts tightened: `DocumentSymbolProjection<TItem>.TextSelector` is
  renamed `NameSelector`, `DocumentSymbolOutlineBuilder` reports null selector results as
  `InvalidOperationException` naming the selector, `TextDocumentSymbol` rejects `null` child
  elements and takes `range` before `selectionRange` (LSP order), and `TextCompletionSessionDecision`
  documents the positional constructor's aliasing.
- Return-contract violations in `TextCompletionSessionKernel` (provider or filter returning `null`)
  throw `InvalidOperationException` instead of a misattributed `ArgumentNullException`.
- `TextDefinitionRequest.Identifier` is renamed `Discriminator` (constructor parameter
  `discriminator`); the navigation helpers use the same name.
- `TextSemanticToken` uses structural value equality (range, type, modifier sequence),
  `TextSemanticTokenTypes.Label` covers the LSP 3.18 addition, and the lua-language-server
  `global` modifier moves to the Lua package's capability declaration.
- Documentation corrections: diagnostic range normalization wording, `TextHoverInfo.Content`
  wording, `ITextCompletionProvider`/`ITextDocumentSymbolProvider` contract wording, and the
  README's equality/host statements.

**Diagnostics alignment (2026-09-13)**

- Breaking: `TextEditorDiagnosticSeverity` moved to `Nickelony.IDEKit.Core.Diagnostics`; update the
  using directive when you consume the severity from this package.
- `TextEditorDiagnostic` carries the optional LSP-standard `Source` (`Diagnostic.source`) and `Code`
  (`Diagnostic.code`) fields; blank values are normalized to `null`. Producers store the raw
  message, and hosts compose the severity label and attribution for display.

**Completion model standards (2026-09-13)**

- Added the `Text` and `EnumMember` well-known completion kinds. The well-known set now covers
  every LSP 3.17 `CompletionItemKind` one-to-one, the LSP↔member mapping table is pinned in the
  `TextCompletionItemKind` documentation, and `Generic` is documented as the presentation fallback
  for items whose producer cannot supply a category (it is not a protocol kind). `Array`,
  `Section`, `Directive`, `Parameter`, and `Namespace` remain documented library-only extensions.
- `TextCompletionItem` carries the protocol ordering data: `SortText` (the protocol sort text,
  preserved verbatim because ordering is lexicographic, blank treated as absent) and
  `IsPreselected` (the protocol `preselect` flag). `Priority` is documented as the producer-derived
  higher-first hint for hosts that order by one number, while `SortText` is the authoritative
  protocol ordering. `WithResolvedContent` adopts a sort text only when the original item lacks one
  and never changes the preselect state.
- `TextCompletionSessionKernel` documents that it holds no completion cache: every decision invokes
  the provider again, so a provider that reports an incomplete protocol list (`isIncomplete`)
  needs no caching or re-query workaround.

**Completion review alignment (2026-09-14)**

- Breaking: the three completion-family names that lacked the `TextCompletion` prefix are aligned:
  `CompletionWordInfo` is renamed `TextCompletionWordSpan`, `CompletionWordLocator` is renamed
  `TextCompletionWordLocator`, and `CompletionSessionKernel` is renamed
  `TextCompletionSessionKernel`. No compatibility shims are provided.
- Breaking: `TextCompletionContext` is renamed `TextCompletionRequest` (constructor and provider
  parameter names are `request`), and its `ArgumentIndex` field is removed: the request carries no
  language-specific contextual state, so a provider resolves it from the caret position itself.
- Breaking: `TextDefinitionLocation` normalizes a blank `DocumentId` to `null` (previously stored
  verbatim), matching the other optional-text payloads.
- Fix: `TextCompletionItemKindJsonConverter` writes JSON `null` for a null value instead of
  throwing `NullReferenceException`; the documented strict read path still rejects a `null` token.
  (Superseded later in the same preview: the converter and its attribute were removed with the
  fresh-review resolution below.)
- Fix: `TextSignatureInformation` rejects `null` parameter elements with `ArgumentException`
  naming the offending index, matching `TextSignatureHelpInfo` and `TextDocumentSymbol`.
- Fix (documentation): the signature-help `-1`/`null` resolution passages and the
  `TextSignatureInformation` clamping wording now match the verified behavior (payload `-1` selects
  the first parameter when neither level specifies an index; `null` at either level reports no
  active parameter; values below `-1` clamp to the first parameter), and the LSP-version attribution
  for the sentinel is removed. `WithResolvedContent`'s merge documentation now matches the code
  exactly: insertion text yields to an existing edit payload, filter text is still adopted from a
  label fallback, and resolved request stamps are ignored. The README payload-convention,
  helper-surface, and position-model statements were corrected and a quickstart was added.
- `TextCompletionItemKind` members are ordered by LSP kind value with the extensions last, operator
  docs are complete, and host-named examples were generalized (`Priority`, `ProviderData`,
  `TextDocumentSymbol.Data`, semantic-token modifier docs).
- Tests: signature parameter validation, resolved-content
  fill-in/stamp/filter-text pins, request record-equality coverage, and a trigger-flow matrix that
  includes `TextCompletionTrigger.EmptyLine`.

**Fresh-review resolution (2026-09-14)**

- Breaking: `TextCompletionSessionDecision.CloseWindow` is renamed `ShouldClose`; the record
  describes completion-session state rather than an editor window.
- Breaking: `TextHoverInfo`'s untyped `ProviderData` is replaced by the typed
  `DefinitionDiscriminator` (`TextDefinitionDiscriminator`), and the constructor is slimmed to
  `content`/`contentKind` with init accessors for the optional values;
  `TextDefinitionNavigation` passes the discriminator through without a cast.
- Breaking: `TextDocumentSymbol` takes `name`/`kind`/`range`/`selectionRange` only; `Detail`,
  `Children`, and `Data` are validating init accessors (`Children` still rejects null elements).
- Breaking: `TextEditorDiagnostic` takes `severity`/`message`/`startOffset`/`endOffset` (or a
  `TextRange`); `Source` and `Code` are init accessors that normalize blank values to null, and the
  clamping/widening contract moved into the offset parameter docs.
- Breaking: signature-help normalization follows the LSP default rule: a value outside a
  signature's parameter range falls back to the first parameter, and an out-of-range active
  signature index falls back to zero (previously both clamped to the last entry). A producer that
  wants clamp-to-last behavior (Tomb Editor's ClassicScript provider derives the index from the
  caret) clamps its own value before constructing the payload.
- Breaking: `TextSignatureHelpContext` treats a blank trigger character as absent.
- Breaking: `TextCompletionItem` resolve-contract violations throw `InvalidOperationException`
  (matching the session kernel), and a `null` `Kind` assignment yields `Generic`.
- Breaking: the unproduced `TextCompletionItemKindJsonConverter` and its `[JsonConverter]`
  attribute were removed; the library does not serialize completion kinds.
- Additions: `TextCompletionItemKind.FromLspKind(int)` maps protocol `CompletionItemKind` numerics
  one-to-one (falling back to `Text` outside the protocol range), and custom-kind interning is
  bounded so hostile identifiers cannot grow the process cache without limit.
- `DocumentSymbolOutlineBuilder` renamed its `itemsSelector` parameter to `groupItemsSelector` and
  reports projections without selectors as `ArgumentException` naming the projection parameter;
  builder output is documented as host outline entries rather than an LSP-shaped symbol tree.
- `DiagnosticHitTester` documents the shared-empty mutability contract and the presentation-adjacent
  role of `SelectHoverDiagnostics`; the README position model, kind-type statement, and
  payload-convention claims were corrected, the signature-help resolution description was
  consolidated, and the definition request is documented as a symbol-name lookup.
- Tests: LSP fallback pins, `FromLspKind` mapping, resolve-contract exception type, kind-null
  default, merge precedence when both sides carry commit fields, symbol range-only/ownership
  coverage, semantic-token duplicate/blank modifier handling, builder null/empty coverage, kernel
  mid-identifier truncation and locator suppression, stronger filter assertions, and
  `[DoNotParallelize]` on the culture-dependent filter test.

### Nickelony.LanguageServer.Abstractions

**Breaking changes**

- `ILanguageServerIntelliSenseProvider.CapabilitiesChanged` is now `EventHandler?` instead of
  `Action?`: the raising provider is the sender and the event carries `EventArgs.Empty`.

### Nickelony.LanguageServer.Client

**Breaking changes**

- `DefinitionResponse` lost its compatibility accessors (`Uri`, `LineNumber`, `ColumnNumber`);
  consumers read `FirstTarget` or `Targets` instead.
- `DefinitionTargetResponse` carries full one-based ranges - `TargetRange` plus an optional
  `SelectionRange` for location-link payloads - instead of a single line/column pair.

**Additions**

- `CompletionItemPayload.SortText` models the protocol `sortText` field (previously it only
  round-tripped through the extension-data fallback), so completion ordering survives parsing in
  the typed payload.
- `HoverResponse.Range` carries the protocol range of a hover response, so hosts can map hover
  content onto the hovered span.

**Naming standardization (2026-09-13)**

- The static utility facades were renamed: `ProtocolRangeHelper` → `ProtocolRangeConversion`
  (protocol positions/ranges to one-based editor coordinates) and `LanguageServerPathHelper` →
  `LanguageServerPaths` (path/URI normalization behind document identity).

**Event convention (2026-09-13)**

- Breaking: `ILanguageServerClient.SemanticTokensRefreshRequested` is now `EventHandler?` instead
  of `Action?`; the active client is the sender and the event carries `EventArgs.Empty`.

**Provider machinery extraction (2026-09-14)**

- `SemanticToken`, `SemanticTokensDecoder`, and `SemanticTokensDecodeResult` moved here from
  `Nickelony.LanguageServer.Lua`; the decoder turns raw LSP integer streams into the shared token
  model next to `SemanticTokensDeltaParser`.
- `ProtocolTextRangeConversion.ToTextPositionRange` converts one-based protocol ranges into the
  zero-based `TextPositionRange` used by shared editor payloads.

**Semantic-token bridge (2026-09-14)**

- New `SemanticTokenConversion.ToTextSemanticTokens(tokens, lineMap)` converts the decoded protocol
  token sequence into the shared offset-based `TextSemanticToken` payload (line-map offsets, stale
  lines skipped, character indices clamped), so editor hosts no longer reimplement the mapping. The
  package now references `Nickelony.IDEKit.IntelliSense` for the payload type.

**Client fresh-review resolution (2026-09-14)**

- Breaking: `CompletionResponse`, `DefinitionResponse`, `HoverResponse`, and `SignatureHelpResponse`
  are sealed classes with validating constructors instead of records, and `DefinitionResponse`
  copies the target list it is given.
- Breaking: `ILanguageServerClient.MarkTransportUnhealthy()` was removed; the remaining
  `TryMarkTransportUnhealthy(long transportGeneration)` reports the failing generation. Transport
  failures observed while sending now surface as `LanguageServerTransportUnavailableException`,
  including `ConnectionLostException`.
- Breaking: `SemanticTokensParams` carries only the text document; delta requests use the new
  `SemanticTokensDeltaParams` with a non-nullable `previousResultId`.
- Breaking: `DidChangeWatchedFilesParams.FileEventPayload` stores a typed `FileChangeKind`.
- Breaking: `LanguageServerPaths.NormalizeLocalPath(Uri)` rejects relative and non-file URIs with
  `ArgumentException`, and `DefinitionResponseJsonConverter` logs and skips malformed definition
  targets instead of dropping the whole response.
- Additions: `LanguageServerClientOptions.ServerArguments` and `EnvironmentVariables` seed the
  language server process; `Synchronize` gained `includeChangeRange` for servers without incremental
  sync support; the workspace watcher re-queues unapplied changes across dispatch failures (with a
  single escalation warning) and disposal waits for the final flush.

### Nickelony.LanguageServer.Lua

**Additions and fixes**

- The advertised semantic-token legend in `LuaLanguageServerClientCapabilitiesFactory` is now built
  from the shared `TextSemanticTokenTypes`/`TextSemanticTokenModifiers` constants, so the
  initialize payload cannot drift from the token model. The lua-language-server `global` modifier is
  declared by the package itself (`GlobalSemanticTokenModifier`).
- Completion parsing keeps `textEdit.newText` on the edit payload
  (`TextCompletionTextEdit.NewText`) instead of folding it into `InsertText`, applies snippet
  stripping to the text that actually commits, and includes the edit text in the duplicate-detection
  identity.
- Completion kinds map one-to-one onto the shared taxonomy: the parser keeps the protocol kind
  (`Text`, `Method`, `Function`, `Constructor`, `Interface`, `Module`, `Enum`, `EnumMember`,
  `Struct`, `Snippet`, `Event`, `Operator`, `TypeParameter`, `Unit`, `Value`, `Reference`, …)
  instead of collapsing it onto a renderable category, and a missing or out-of-range kind uses the
  protocol default `Text`. Hosts that cannot render a kind fall back to their own default
  presentation.
- Parsed completion items carry the protocol ordering fields: `sortText` becomes
  `TextCompletionItem.SortText` and `preselect` becomes `IsPreselected`, while `Priority` stays the
  derived higher-first hint (kind weights, scope hints, preselect bonus, response order).
- `ParseSignatureHelp` returns every labelled signature of a response instead of only the active
  one, keeps the LSP 3.16+ active-parameter precedence (the signature-level value overrides the
  payload-level value), and skips signatures without a label.
- Diagnostics parsing no longer pre-formats messages: the raw trimmed server message is stored (with
  the `"Unknown Lua diagnostic."` fallback), and the protocol `source`/`code` values fill
  `TextEditorDiagnostic.Source`/`Code` so hosts compose labels and attribution.

**Event convention (2026-09-13)**

- `LuaLanguageServerIntelliSenseProvider.CapabilitiesChanged` is an `EventHandler` (sender = the
  provider, `EventArgs.Empty`), following the shared event convention and the updated Abstractions
  interface.

**Provider machinery extraction (2026-09-14)**

**Breaking changes**

- `LuaSemanticToken` was replaced by the shared `Nickelony.LanguageServer.Client.SemanticToken`;
  `SemanticTokensUpdated` and `GetSemanticTokens` now carry the shared token type.
- `LuaLanguageServerSemanticTokensDecoder` moved to `Nickelony.LanguageServer.Client` as
  `SemanticTokensDecoder`; its test suite and the semantic-token ownership pin moved to the client
  test project with the type.

**Additions and fixes**

- The internal provider building blocks (`WorkspaceSnapshotTracker`/`WorkspaceSnapshotEntry`,
  `LanguageServerRequestDispatcher`, `LanguageServerStartupState`, `SnippetPlaceholderParser`,
  `SignatureLabelParser`) live in `Nickelony.LanguageServer.Lua` next to the provider that consumes
  them; `ProtocolTextRangeConversion` stays in `Nickelony.LanguageServer.Client` as shared protocol
  conversion.

**Completion ranking injection (2026-09-14)**

- New `LuaCompletionRankingOptions` carries the completion ranking weights (preselected bonus,
  response order, local-scope and upvalue/parameter weights, and the per-kind weights/penalty) and
  is exposed through `LuaLanguageServerOptions.CompletionRanking`. The defaults reproduce the
  weights the provider always applied, so default ordering is unchanged; overriding individual
  weights re-ranks the derived `Priority` hints. The ranking is applied client-side when
  completion responses are parsed and is not sent to LuaLS. The internal
  `LuaCompletionPriorityWeights` constants were removed.

**Fresh-review resolution (2026-09-14)**

- Completion kind mapping uses `TextCompletionItemKind.FromLspKind` instead of a provider-local
  numeric table; a missing or out-of-range protocol kind keeps resolving to `Text`.
- Hover and diagnostic parsing construct their payloads through the new initializer shapes
  (`TextHoverInfo` with `Range`, `TextEditorDiagnostic` with `Source`/`Code`).

### Nickelony.IDEKit.AvalonEdit.IntelliSense

**Breaking changes**

- `TextCompletionControllerHooks.ResolveDescriptionAsync` is now
  `Func<ICompletionData, CancellationToken, Task<object?>>?`. The hook receives a cancellation token that
  is canceled when a newer tooltip update starts, the completion window closes, or the controller is
  disposed, and it always returns a task; returning null content hides the tooltip. Hosts whose items only
  carry a synchronous description either do not set the hook or echo the item's description to keep it
  visible. The previous nullable-task contract, where returning `null` meant "keep the synchronous
  description", is gone.
- `TextHoverControllerHooks.SessionGenerationProvider` was renamed to `ContextVersionProvider`; the
  semantics are unchanged (a cheap host value captured before a request; completed results are discarded
  when it changed). Tomb Editor's Lua hover controller is updated.
- `TextCompletionController.ApplyDecision` renamed its `mapItem` parameter to `itemFactory` (breaking for
  named arguments).
- `TextDefinitionTriggerController` now handles F12 only without modifier keys; Ctrl+F12 and Shift+F12 are
  left to the host, and Ctrl+Click ignores multi-clicks. The event is marked handled before the
  asynchronous navigation starts (with a rollback when navigation fails or the controller is disposed), so
  WPF routing no longer passes the input on while navigation runs.
- `TextSignatureHelpController` now dismisses the presentation when the provider returns no result, even
  when signature help was visible; previously a visible popup was kept on a null refresh.
- `TextSignatureHelpControllerHooks.RequestSignatureHelpAsync` is now
  `Func<int, TextSignatureHelpContext, long, Task<TextSignatureHelpInfo?>>`. The callback receives
  the trigger context (explicit invocation, trigger character, or content change; retrigger flag;
  the visible payload) so hosts can pass it to `TextSignatureHelpRequest` and providers can keep
  the selected overload stable. `RequestAsync` gained optional trigger kind/character parameters,
  and `ScheduleRefresh` reports content changes.
- `TextCompletionControllerOptions` validates every numeric option in the constructor: negative delays,
  widths, chrome, icon widths, spacings, and the measurement limit, non-finite floating-point values, and a
  maximum window width below the minimum window width throw `ArgumentOutOfRangeException` instead of
  surfacing as WPF exceptions later.
- `TextCompletionController.OpenOrRefresh` validates its replacement offsets: negative offsets or an end
  offset before the start offset throw `ArgumentOutOfRangeException`; offsets beyond the document end are
  clamped to the document length instead of reaching the commit path.
- `CompletionWindowCoordinator.Initialize` now closes a window created by an earlier `Initialize` call that
  was never shown, so a repeated `Initialize` cannot strand an untracked window.
- `SemanticTokensColorizer.SetTokens`/`Rebuild`/`ClearTokens` now return a Boolean reporting whether the call
  applied a change, and `SetTokens` treats a push equal to the applied set for the same document instance
  (same ranges, types, and modifiers in the same order) as a no-op: the token list is not copied, the resolver
  is not invoked again, and the view is not redrawn. Hosts that relied on `SetTokens` to re-resolve unchanged
  tokens after a resolver-configuration change must call `Rebuild`, which always re-resolves.

**Fixes**

- Host callbacks can no longer crash dispatcher or timer paths: completion presentation, tooltip configure,
  and description-resolve callbacks and the signature show/dismiss/cancel/presentation callbacks are
  contained and logged, and hover failure recovery contains its tooltip and presentation callbacks as well.
- `TextSignatureHelpController.Dismiss` and `Dispose` now cancel an in-flight provider request through the
  `CancelInFlightRequest` hook (best-effort, after invalidation) instead of leaving it running.
- Completion tooltip resolve failures caused by cancellation are no longer logged as failures.
- The AvalonEdit completion tooltip field accessor logs a warning once per controller when the reflected
  field is missing, so an AvalonEdit upgrade that breaks tooltip support is visible to hosts instead of
  failing silently.
- `TextCompletionController.HandleCompletionListClick` ignores list items without a data context instead of
  passing `null` to `ScrollIntoView`.
- `SemanticTokensColorizer.Rebuild`/`ClearTokens` use the raw token set as their single guard, so a rebuild
  after an all-unstyled token set still refreshes the cache and redraws.

**Additions**

- `TextCompletionControllerHooks.MeasureItemWidth` lets a host with a different item template measure its
  own item content width, replacing the default icon + text + detail measurement; the shared defaults are
  documented as a conventional icon + text + detail template baseline.
- `TextCompletionControllerHooks.ConfigureToolTip` runs after the controller applies its baseline tooltip
  chrome, so host skins can override placement, offsets, padding, and open behavior.
- `TextHoverControllerHooks.GetCurrentRequestOffset` is now optional; when it is omitted, the hovered offset
  doubles as the request offset, which was already the common host implementation.
- `TextDefinitionNavigation.TryGoToSymbol` defaulted its `identifier` parameter to `null`.
- `TextSignatureHelpController.SelectNextSignature`/`SelectPreviousSignature` navigate the signature
  options of the visible payload (wrap-around, no provider round-trip) and re-notify the host show
  callback with the updated active signature.
- `TextDefinitionNavigation` gained `TryGoToDefinitionAsync`/`TryGoToSymbolAsync` overloads that
  await asynchronous resolver delegates with cancellation support for providers that perform I/O;
  the synchronous overloads remain for in-memory providers and both forms are documented.

**Documentation**

- Added thread-affinity remarks to `TextHoverController`, `TextDefinitionTriggerController`, and
  `CompletionWindowCoordinator`; documented that the hover controller does not debounce
  (hover-intent stays with the host) and that dispose does not close host tooltips.
- Documented the signature-help queued-refresh dependency on the host cancel hook, the request-token
  correlation contract, the `ISemanticTokenStyleResolver` no-throw contract, and the completion
  presentation tooltip-content contract.
- Corrected the semantic-token edit-anchoring remarks (styles are line/character snapshots, not live
  offsets), the hover request-state wording, the `WindowMinWidth` clamp contract, and the README trigger
  policy carve-out, `ResolveDescriptionAsync` casing, and sizing/tooltip sections.

**Review resolution (2026-09-13)**

- The three controllers now share one cancellation model: the hover and signature provider hooks receive a
  `CancellationToken`, and every controller exposes `CancelInFlightRequest` (cancel the provider call)
  separately from `InvalidateRequests` (reject results without canceling).
- `TextSignatureHelpController` passes a cancellation token to
  `TextSignatureHelpControllerHooks.RequestSignatureHelpAsync` instead of the opaque correlation token;
  `TextSignatureHelpControllerHooks.CancelInFlightRequest` is removed, the controller cancels its own
  provider token on supersession and dismissal, and its constructor takes
  `TextSignatureHelpControllerOptions` instead of a bare refresh delay.
- `TextCompletionController.CancelInFlightRequest` is now public and `InvalidateRequests` only invalidates.
  Canceled request tokens stay safe to observe: registration and waits report cancellation instead of
  throwing `ObjectDisposedException`, because a superseded source is disposed one request later and the
  final source is left to the garbage collector.
- `CompletionWindowCoordinator` takes a `CompletionWindowSkin` record (border/background/foreground),
  renamed `Initialize` to `Create`, and no longer implements `IDisposable`; hosts close the tracked
  window through `Close`, which remains safe and reusable.
- `CompletionWindowToolTipAccess` is internal again; the reflection over AvalonEdit's private tooltip
  field stays an implementation detail rather than public API.
- `TextDefinitionTriggerController` gestures are configurable through
  `TextDefinitionNavigationGestures` (F12 without modifiers and a single Ctrl+LeftClick by default), and
  the F12 check reads the modifiers from the routed event's keyboard device instead of global keyboard
  state.
- `TextHoverPresentationState.EmptyAt` no longer has a default parameter; `Empty` covers the no-offset
  state. `TextCompletionController.CancelTooltipUpdate` was renamed to `CancelToolTipUpdate`, and the
  tooltip-related private members follow the same casing.
- The semantic-token repeat-push check now also compares the line/character anchors derived from the
  current document, so a same-length edit that moved an anchor (for example a space replaced by a line
  break) re-applies the unchanged push instead of skipping it.
- Hover: a `GetCurrentRequestOffset` answer of `null` now vetoes the completed result even when the raw
  hovered offset equals the request offset (previously the null was conflated with "hook absent").
- Completion: a scheduled request that observes cancellation is no longer logged as a failure; a decision
  with items but without both replacement offsets throws `ArgumentException` instead of silently doing
  nothing; out-of-document replacement offsets are validated before clamping, so a reversed raw range
  throws even when both offsets would clamp to the same value; the completion-list click handler ignores
  already-handled events, and the redundant `StaysOpen` baseline assignment is gone.
- Semantic tokens: `SetTokens` and `Rebuild` build the complete styled state before committing it, so a
  throwing resolver leaves the previously applied set (and its anchors) in effect; the shared empty map is
  frozen.
- Definition navigation: a negative line or column is rejected instead of clamping to line 1/column 1, and
  the one-based column addition saturates so `int.MaxValue` clamps to the line end.
- The completion measurement typeface follows the editor's font style, weight, and stretch.
- `TextHoverControllerHooks.GetCurrentPointerPosition` lets hosts that drive hover from pen, touch, or
  synthetic input supply the liveness-check position (defaults to the mouse position against the owner).
- Corrected the hooks failure-propagation and required/optional remarks, the completion tooltip
  presentation-state contract, the hover failure-recovery precondition, the signature queued-refresh
  trigger-context wording, and the navigation sync/async thread-affinity remarks; the README wiring sample
  now uses `TextEditorMouseNavigation.TryGetOffsetFromPoint`.

**Shared text-run styling (2026-09-13)**

- `SemanticTokenStyle` implements the shared `Nickelony.IDEKit.AvalonEdit.Rendering.ITextRunStyle`
  contract, and `SemanticTokensColorizer` applies resolved styles through
  `TextRunStyleApplier` from `Nickelony.IDEKit.AvalonEdit`. Behavior is unchanged: the
  typeface is only changed for bold or italic styles, and text decorations keep AvalonEdit's
  union semantics.

**Fresh review resolution (2026-09-14)**

- `CompletionWindowHost` was merged into `CompletionWindowCoordinator`: the coordinator now takes the
  `TextArea` and the default `CompletionWindowSkin` directly, owns the window lifecycle (creation,
  show, close, tracking) itself, and validates its constructor and `Create` arguments. The
  double-callback `Close(CompletionWindow, Action)` overload is gone; the callback supplied to `Show`
  observes every closure of a shown window exactly once.
- Moved back to the host (Tomb Editor): the WPF input policy types `TextDefinitionTriggerController`
  and `TextDefinitionNavigationGestures`, and the host view contract `ISyntaxPreviewSource`. The
  package now contains only the pure navigation helpers and does not ship editor input-gesture policy.
- `TextHoverPresentationState` and the `ApplyHoverState` hook were removed: every member was derivable
  from the host's own hover inputs and callbacks, and the sole consumer already passed a no-op. The
  completion and signature help controllers keep their `CurrentPresentation` records, which expose
  state a host cannot otherwise observe (scheduled request, tooltip visibility, refresh pending);
  their `ApplyPresentationState`/`ApplySignatureState` push hooks were removed, so hosts read the
  properties instead.
- `TextSignatureHelpController.CancelPendingRefresh` was renamed to `CancelScheduledRefresh`, matching
  the completion controller's cancel verb set.
- `TextCompletionController` disposes a canceled request's `CancellationTokenSource` immediately
  (previously disposal was deferred by one request). `CurrentRequestCancellationToken` now keeps
  returning the canceled token of the last request until a newer request begins, matching its
  documentation; the token is no longer promised to be registrable after its source is disposed.
- `TextDefinitionNavigation.TryGoToDefinitionAsync` takes the definition resolver before the hover
  resolver, matching the synchronous overloads (the previous order was swapped). Both forms
  materialize the document text once per navigation, so the hover and definition requests evaluate
  the same snapshot.
- `TextCompletionControllerOptions.WindowMinWidth`/`WindowMaxWidth`/`WindowMaxHeight` are `double`
  (device-independent pixels), matching WPF sizing and DPI scaling; non-finite window dimensions are
  rejected. `WidthMeasurementItemLimit` was removed.
- Fixes:
  - The completion-list click handler resolves the clicked item through
    `ItemsControl.ContainerFromElement`, so an item template that renders its text with inline
    elements (`Run`) no longer throws from `VisualTreeHelper` on every left-click.
  - Every completion item is measured when the window width is computed; previously only the first 80
    items were sampled, which under-sized windows whose widest items appeared later.
  - `ApplyDecision` documents `ArgumentOutOfRangeException` for negative or unordered decision
    offsets instead of claiming `ArgumentNullException`.
  - Hover logs a one-time warning when `BuildRequestState` requests a different offset than the
    hovered one without a `GetCurrentRequestOffset` hook, a configuration in which every completed
    result is discarded.
  - `SemanticTokensColorizer` caches one apply delegate per styled token, so the paint path allocates
    no closures; `SemanticTokenStyle.HasFormatting` now inherits its contract from `ITextRunStyle`.
  - The completion option defaults are pinned by a test, and the README wiring example compiles again
    (it used a stale coordinator constructor).

**Fresh review resolution (2026-09-14, AvalonEdit.IntelliSense second pass)**

- `TextCompletionController` construction takes the editor, its `CompletionWindowSkin`, the options,
  the hooks, and the logger: `(editor, windowSkin, options, hooks, logger)`. Hosts no longer compose a
  `CompletionWindowCoordinator`; the controller creates it for the editor's text area and exposes it as
  `WindowCoordinator`, which is also where closure reporting lives now. The constructor verifies the
  editor's dispatcher thread.
- `TextCompletionController.OpenOrRefresh` requires both replacement offsets
  (`OpenOrRefresh(items, startOffset, endOffset)`): a half-specified range used to stay caret-anchored
  and silently replace nothing, so it is now rejected instead of guessed. When a window is open and the
  requested start offset matches the open window's start offset, the window is refreshed in place - its
  end offset, item list, and initial selection are updated without a close/reopen flash and without a
  `WindowClosed` report. A different start offset closes the old window (reporting the closure) and opens
  a new one for the new range.
- `CompletionWindowCoordinator` reports closures through the `WindowClosed` event, raised exactly once
  per shown window (window, replacement, `Close`, or `Dispose`); the callback parameter on `Show` is gone
  and `Close()`/`Show()` take no arguments. `Create` takes only the optional content height.
- Hover hooks: `GetOffsetFromPoint` is `Func<Point, int?>` (a point that does not map to a hoverable
  target yields `null` instead of the `-1` sentinel), and the diagnostic/hover/combined tooltip sinks are
  replaced by the single required `ShowToolTip(TextHoverInfo?, TextEditorDiagnostic?)`. Both arguments
  `null` is the hide request, so display and hide are one newest-wins callback.
- `TextSignatureHelpController` dropped the pass-through `CurrentSignatureHelp`/`IsVisible`/
  `IsActiveOrPending` properties; `CurrentPresentation` is the state surface. Requests are
  cancel-and-restart: starting a request cancels the in-flight one through the shared
  `LatestRequestCoordinator`, and a superseded response is never applied.
- `TextCompletionControllerOptions.ToolTipHorizontalOffset` was removed; tooltip chrome is supplied by
  the `ToolTipSkin` hook (`CompletionToolTipSkin` record with placement, offset, border, padding, and
  optional brushes). The new `NonActivatingWindow` option (default `true`) lets a host opt back into
  WPF's native window activation; when it is `false` the controller installs neither the activation hook
  nor the explicit list-click selection that non-activating windows require.
- Fixes:
  - Option validation moved onto the init accessors, so `with` expressions and object initializers can
    no longer bypass it (a `Default with { RequestDebounceDelay = <negative> }` used to sneak past the
    validating constructor).
  - An in-place refresh re-filters AvalonEdit's list by re-selecting the window query, because
    AvalonEdit's `SelectItem` early-returns for unchanged text and would leave the previous filter's
    item source active.
  - The completion resize path clamps the required content width by `WindowMaxWidth` on every refresh
    (previously an in-place resize could exceed the configured maximum), and the measurement text uses
    the editor's `FlowDirection`.
  - `CompletionWindowToolTipAccess` walks the base types of the window (`DeclaredOnly`), so the
    reflected tooltip field is still found on an AvalonEdit version whose completion window derives from
    a common base.
  - Hover failures that occur before an offset is known are logged without a hide decision; every
    concluded evaluation (including "nothing to show" and a vetoed result) is reported once through
    `ShowToolTip`.
- Additions:
  - `TextCompletionItemCompletionData`, the default `ICompletionData` bridge (label as text and
    content, description, priority, insert-text replacement), so `ApplyDecision`/`RequestAsync` work
    without a host factory. An explicitly supplied factory that returns `null` for an item now throws
    `InvalidOperationException` instead of silently producing nothing.
  - `TextCompletionController.RequestAsync`, the convenience pipeline for the standard scenario: it
    begins the request, discards superseded results, reports cancellation as `false`, and applies the
    decision.
  - `TextCompletionController.WindowCoordinator` and the static `IsToolTipSupported`, which reports
    whether the running AvalonEdit version still exposes its completion-window tooltip field.
- Documentation: the package README was rewritten for the new wiring (skin-based controller, tooltip
  skin, single hover callback, in-place refresh, decision path, tooltip availability, logging event-id
  blocks); `Dispose`/`CloseWindow` remarks no longer overpromise window or tooltip teardown; the
  navigation remarks state the asymmetric position policy (line/column is one-based only through the
  line/column overloads) and the "carry a document by default" contract.

### Nickelony.IDEKit.KeyBindings

**Breaking changes**

- `KeyBindingValidationResult.InvalidKey` was removed. The service never returned it, and
  `KeyCombo` rejects `Key.None` at construction, so the value was unproducible.

**Additions**

- Key-binding override diagnostics now carry stable named event ids (`1`-`5`):
  `HostReservedOverrideIgnored`, `AllOverrideBindingsInvalid`, `EmptyKeyName`, `InvalidKeyName`,
  and `BindingParseFailed`. The package README documents the block, so hosts can filter these
  warnings in structured logs.

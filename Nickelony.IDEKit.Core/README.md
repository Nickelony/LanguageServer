# Nickelony.IDEKit.Core

Dependency-free text primitives, editing utilities, and host-independent
contracts for editor hosts and language tooling. The package targets `net8.0`.

## Conventions

| Concept | Policy |
|---|---|
| Offsets | Zero-based UTF-16 code-unit indexes |
| Line numbers | One-based for snapshots, diffing, and the incremental line-state cache; `TextLineMap` and `TextRangeOffsetResolver` use zero-based line indexes |
| Line lengths and end offsets | Exclude line terminators |
| Line terminators | LF, CRLF, and lone CR are recognized by the line-aware types; CRLF counts as one terminator |
| Whitespace alphabets | Indentation and trailing-whitespace trimming recognize spaces and tabs only; blankness and comment scanning use `char.IsWhiteSpace` (any Unicode whitespace) |
| Null content text | `StringTextSnapshot`, `TextLineSplitter.Split`, `TextLineMap.Build`, and `TextIncrementalEditCalculator.Compute` treat a `null` content argument as empty; `BacktickFenceTextNormalizer` returns `null` when the input or the result is blank; other content arguments throw `ArgumentNullException` |
| No-op identity | The transform helpers (`CommentOperations` string overloads, `WhitespaceConverter`, `TrimTrailingWhitespaceFormatter`) return the original instance when nothing changed, so a no-op is detectable by reference |
| Record-struct defaults | Payload record structs (queries, requests, contexts, results) document that a `default` instance carries `null` text members despite the annotations; construct payloads through their initializers or factories |
| Out-of-range coordinates | `StringTextSnapshot` throws; `TextLineMap` clamps (see [Choosing a line-shaped input](#choosing-a-line-shaped-input)), so hosts that may pass stale coordinates pick the type whose policy matches |
| Invalid ranges | `TextRange` throws `ArgumentOutOfRangeException`; `TextRange.GetText(string)` names `source` and parenthesizes the offending range component so a stale range is easy to identify |

This file is the adoption guide (conventions, quick start, and the feature-slice map).
The normative per-member contracts live in the XML documentation of the types
named below.

## Getting started

Install the package from the feed you configured for preview packages:

```powershell
dotnet add package Nickelony.IDEKit.Core
```

A minimal scan: wrap the text in a snapshot, resolve the code range before the
first comment, and mask comments in place:

```csharp
using Nickelony.IDEKit.Core.Comments;
using Nickelony.IDEKit.Core.Text;

const string line = "let total = 1; // running total";
var snapshot = new StringTextSnapshot(line);
var syntax = new CommentSyntax("//", null, StringLiteralStyle.DoubleQuoted);

TextRange codeRange = CommentOperations.GetCodeRange(line, syntax);
string code = codeRange.GetText(snapshot);                     // "let total = 1;"
string masked = CommentOperations.MaskComments(line, syntax);  // comments become spaces

Console.WriteLine($"{code.Length} code chars, masked length {masked.Length}");
```

The package has no package dependencies; the license terms are in the
repository's `LICENSE` file.

## Feature slices

The package is organized into vertical feature slices, each with its own
namespace (the `Text` slice is listed first because every other slice builds on
it):

- `Nickelony.IDEKit.Core.Text` - the text model: immutable snapshots, lines,
  ranges, and edit operations (`ITextSnapshot`, `StringTextSnapshot`,
  `TextRange`, `TextPosition`, `TextPositionRange`, `TextEditOperation`),
  the clamped zero-based line map (`TextLineMap`), plus the shared line splitter
  (`TextLineSplitter`) and plain-text
  normalization of markup (`BacktickFenceTextNormalizer.NormalizeForPlainText`, which
  takes the line terminator used to join retained lines). The normalizer stays in
  this package because it is dependency-free text tooling for documentation
  payloads: a host can strip Markdown fences without taking a dependency on an
  IntelliSense/UI package, and the language-server tier (the Lua signature-help
  parser) consumes it for the same reason. Three line shapes are
  available: snapshots expose `ITextLine` (offset/length/number),
  `IndentationTextLine` carries a line's content, terminator, and start offset
  (without line lengths or numbers) for indentation workflows, and
  `TextLineEnumerator` is the internal span-based splitter that backs both.
- `Nickelony.IDEKit.Core.AutoClosing` - the editor-neutral auto-closing model
  and resolver (`TextAutoClosingResolver`, `TextAutoClosingRequest`, `TextAutoClosingOptions`,
  `TextAutoClosingPair`,
  `TextAutoClosingPairKind`, `TextAutoClosingAction`, `TextAutoClosingActionKind`,
  `TextAutoClosingProvenance`, `TextAutoClosingResult`). The resolver evaluates the
  configured pairs against an `ITextSnapshot` and resolves an insert (including wrapping a
  selection), a skip of existing closing text, or no action; it also resolves the pair that a
  pair deletion removes.
  - Resolution reads only `TextLength` and `GetCharAt` and finds line ends
    with a terminator scan, so a snapshot with lazy line storage stays lazy on the typing
    path.
  - Insertion provenance reaches the resolver through a caller-supplied tracking
    callback, and a `null` callback means nothing is tracked (no skips in `Auto` mode).
- `Nickelony.IDEKit.Core.Comments` - comment scanning, masking, removal, and
  continuation-marker detection (`CommentOperations`, `CommentSyntax`, `CommentSpan`,
  `CommentKind`, `CommentSpanEnumerator`, `StringLiteralStyle`, `ContinuationOperations`),
  plus the editor-neutral line-comment planner (`TextLineCommentPlanner` with its
  `TextLineCommentEdit` and `TextLineCommentAction` model), which computes commenting,
  uncommenting, and toggling edits for the lines a selection touches.
  Line comments end at the next
  line terminator (CR, LF, or CRLF), which stays outside the comment span, and
  whitespace immediately before the delimiter belongs to the span. A line
  that holds only a comment also absorbs its indentation and the single line
  terminator that precedes it, so removing it removes the whole comment line while
  blank lines above it stay intact. A comment-only first line has no preceding
  line terminator; removing it leaves an empty first line.
- `Nickelony.IDEKit.Core.Diagnostics` - the shared diagnostic range model and
  severity vocabulary (`TextDiagnosticSegment`, `TextDiagnosticSeverity`). A segment
  pairs a zero-based document range with a severity; `None` marks a diagnostic that
  does not specify one, and `Error`, `Warning`, `Information`, and `Hint` have stable
  numeric values that are not a precedence order. Protocol packages map their own
  numbers explicitly instead of casting them.
- `Nickelony.IDEKit.Core.Diffing` - changed-line detection between two
  line-split texts (`LineDiffer`).
- `Nickelony.IDEKit.Core.Editing` - the edit kernel and the edit-application
  boundary: neutral edit preparation,
  incremental editing, and line-state caching (`TextEditKernel`,
  `TextEditInput`, `TextEditPreparationResult`, `TextEditPreparationDiagnostic`,
  `PreparedTextEdits`, `ITextEditTarget`, `IVersionedTextEditTarget`, `ITextEditTargetVersion`,
  `TextIncrementalEditCalculator`,
  `TextIncrementalEdit`, `TextRangeOffsetResolver`,
  `IncrementalLineStateCache`). `TextRangeOffsetResolver` resolves a selectable
  range for empty or reversed positions; its word fallback takes a caller-supplied
  character rule and shares its boundary walk with `IdentifierOperations`.
  `TextEditPreparationResult` carries the prepared
  `PreparedTextEdits` batch, and `PreparedTextEdits.MapOffset` maps offsets over
  it without repeating the ordering validation.
  `IncrementalLineStateCache` computes cached states lazily
  under a lock, so its transition delegate should be fast and pure - readers
  serialize while a transition runs, the first affected line's start state
  survives `ApplyEdit`, and the remainder is recomputed from the new snapshot.
- `Nickelony.IDEKit.Core.AutoClosing` - the editor-neutral auto-closing model
  and resolver (`TextAutoClosingResolver`, `TextAutoClosingRequest`, `TextAutoClosingOptions`,
  `TextAutoClosingPair`,
  `TextAutoClosingPairKind`, `TextAutoClosingAction`, `TextAutoClosingActionKind`,
  `TextAutoClosingProvenance`, `TextAutoClosingResult`). The resolver evaluates the
  configured pairs against an `ITextSnapshot` and resolves an insert (including wrapping a
  selection), a skip of existing closing text, or no action; it also resolves the pair that a
  pair deletion removes.
  - Resolution reads only `TextLength` and `GetCharAt` and finds line ends
    with a terminator scan, so a snapshot with lazy line storage stays lazy on the typing
    path.
  - Insertion provenance reaches the resolver through a caller-supplied tracking
    callback, and a `null` callback means nothing is tracked (no skips in `Auto` mode).
- `Nickelony.IDEKit.Core.Formatting` - document formatting contracts and
  whitespace conversion (`ITextDocumentFormatter`,
  `TrimTrailingWhitespaceFormatter`, `WhitespaceConverter`).
- `Nickelony.IDEKit.Core.Identifiers` - identifier-extraction helpers for
  completion, hover, and definition navigation scenarios (`IdentifierOperations`,
  `IdentifierCharacterPolicy`, `IdentifierSpanMode`).
- `Nickelony.IDEKit.Core.Indentation` - language-independent indentation
  helpers (`IndentationOperations`, `IndentationTextLine`) and the policy
  contracts (`IIndentationPolicy`, `IndentationContext`) consumed by editor
  toolkits.
- `Nickelony.IDEKit.Core.LineStatus` - the line-status source contract
  (`ILineStatusSource`) consumed by margins and
  indicators that mark document lines: sources report one-based marked line
  numbers sorted ascending, must not throw during a consumer's read pass, and
  may raise change notifications from any thread (the subscriber marshals).
- `Nickelony.IDEKit.Core.Notifications` - the generic change-notification contract
  (`IChangeNotificationSource`) that consumers subscribe to so they can refresh when
  a source's marked content may have changed.
- `Nickelony.IDEKit.Core.Navigation` - the text-editor navigation identity
  (`NavigationLocation`): the logical file path, caret offset, selection range,
  and an optional preferred document line for scroll restoration, with identity
  equality that excludes the preferred line and a configurable path comparison.
- `Nickelony.IDEKit.Core.Pathing` - the default case-comparison policy for local
  file-system identity paths (`LocalPathComparisonPolicy`): case-insensitive on Windows
  and macOS and ordinal elsewhere, with explicit `CaseSensitive` and
  `CaseInsensitive` overrides. The policy covers case only; it normalizes nothing.
- `Nickelony.IDEKit.Core.Requests` - asynchronous request invalidation
  and request classification (`RequestTokenSource`, `LatestRequestCoordinator`,
  `RequestOutcome`). The coordinator covers both coordination levels: `RunAsync`
  awaits work and publishes results through a current-state check, while
  `BeginRequest`/`IsCurrent` admit and validate a request that the caller
  completes itself (a canceled `RequestCancellationToken` is a rejection, the
  same rule a run applies). `RequestTokenSource` stays the token-only option for
  callers that validate results themselves and need no cancellation; its tokens
  are `long` values.
- `Nickelony.IDEKit.Core.FindReplace` - text-level find and replace
  primitives (`FindReplaceText`, `TextSearchQuery`). Most regex-based helpers accept
  a `TextSearchQuery` or separate pattern/options/timeout arguments; `FindNextMatch`
  and `FindPreviousMatch` take only a query. The default is
  the infinite timeout and a negative timeout is rejected (the framework's
  `Regex.InfiniteMatchTimeout` is accepted
  and means the same). The helpers reuse a bounded internal cache keyed by pattern,
  options, and timeout (`RegexCache`), so repeated searches over one pattern do not
  re-parse it. Hosts that pass user-entered patterns should supply a
  finite timeout to bound catastrophic backtracking; a timeout throws
  `RegexMatchTimeoutException`.
- `Nickelony.IDEKit.Core.Persistence` - sidecar line persistence for
  line-marker features (`SidecarLineFile`). Entries are one-based line numbers,
  one per line with a `\n` terminator; an empty set deletes the sidecar;
  `Save` replaces it through a temporary file; and `Save`/`Restore` report
  file-operation failures as `false` or an empty list instead of throwing (an invalid
  extension is still rejected with `ArgumentException`). The line numbers have no built-in meaning; the
  caller supplies the sidecar extension (for example `.bkmrk`), and it is
  validated per call on every platform.
- `Nickelony.IDEKit.Core.Themes` - named theme resolution with alias and
  default-selection support (`ThemeCatalog<TTheme>`). The catalog carries theme identity
  only (names, aliases, ordering, default selection), so a host without a view tier can
  resolve its configured theme before a presentation layer exists; the view tier builds
  the actual theme objects.

The IntelliSense feature contracts and payloads (completion, diagnostics,
hover, navigation, and signature help) live in the separate
`Nickelony.IDEKit.IntelliSense` package, which depends on this package for
the text primitives such as `TextRange`. The provider lifecycle (document
open/update/close/rename, cancellation, and startup/capability events) stays in
the `Nickelony.LanguageServer.Abstractions` package.

## Choosing a line-shaped input

Three entry points produce the line shapes the slices consume; pick by what the
caller needs:

| Shape | Use when | Policy differences |
|---|---|---|
| `StringTextSnapshot` | Line metadata (`Lines`, `ITextLine`) is read repeatedly | Out-of-range coordinates **throw**; the line table is materialized on first line access |
| `TextLineMap` | Coordinates may be stale or come from a different revision | Out-of-range line indexes and characters **clamp** |
| `TextLineSplitter.Split` | A plain `string[]` is enough | No metadata; one string per line |

The package is deliberately toolkit-agnostic: editor-generic concepts and
contracts that every editor binding reuses (the `NavigationLocation` record,
the line-status source contracts, the diagnostic segment) live here, because
they are text-editor concepts - caret, selection, and scroll positions - that
no framework-typed API is required to express. Only framework-typed editor
surfaces stay in the editor-binding packages (for example the AvalonEdit
binding); a host whose editor model does not match those concepts adapts at its
binding boundary instead. The multi-file workspace-edit
result contracts (`WorkspaceEditApplicationResult`,
`WorkspaceDocumentChange`, and related types) live in
`Nickelony.IDEKit.Workspace`. Host-independent editor utilities that any host
can reuse (for example sidecar line persistence) live here.

## Comment scanning in depth

### Continuation markers

Several languages continue a statement onto the next line with a marker at the
end of the line. The marker can be a single character (PowerShell uses the
backtick) or a multi-character sequence (MATLAB uses `...`):

```powershell
Get-ChildItem `
    -Path C:\Temp
```

`ContinuationOperations.EndsWithContinuationMarker` detects such markers, ignoring a
trailing comment for languages that support one after the marker:

```csharp
bool continues = ContinuationOperations.EndsWithContinuationMarker(
    "Get-ChildItem `",
    new CommentSyntax("#", null, StringLiteralStyle.None),
    '`');   // true
```

The same check accepts a multi-character marker, such as MATLAB's `...`:

```csharp
bool continues = ContinuationOperations.EndsWithContinuationMarker(
    "total = 1 + 2 + ...",
    new CommentSyntax("%", null, StringLiteralStyle.None),
    "...");   // true
```

The same check accepts a trailing block comment before the marker when the
language's `CommentSyntax` includes block-comment delimiters:

```csharp
bool continues = ContinuationOperations.EndsWithContinuationMarker(
    "total = 1 + 2 /* carry */ ...",
    new CommentSyntax("//", new BlockCommentSyntax("/*", "*/"), StringLiteralStyle.DoubleQuoted | StringLiteralStyle.TripleDoubleQuoted),
    "...");   // true
```

### Block comments

Languages that delimit comments with an opening and closing marker, such as C's
`/* ... */` or Lua's `--[[ ... ]]`, are handled by `CommentOperations` with a
`CommentSyntax` that includes both delimiters plus the line-comment delimiter.
Block comments are recognized alongside line comments, so a block opener inside
a `//` comment does not start a block comment. The string-literal styles are stated
explicitly; the C family passes `DoubleQuoted | TripleDoubleQuoted`, while a
language without string literals passes `None`:

```csharp
var cStyle = new CommentSyntax("//", new BlockCommentSyntax("/*", "*/"),
    StringLiteralStyle.DoubleQuoted | StringLiteralStyle.TripleDoubleQuoted);

int start = CommentOperations.FindComment(
    "let x = 42; /* note */", cStyle)?.DelimiterStart ?? -1;   // 12 (the first comment, line or block)

string cleaned = CommentOperations.RemoveBlockComments(
    "a /* c */ b", cStyle);   // "a  b" (surrounding whitespace kept)

string masked = CommentOperations.MaskBlockComments(
    "a /* c */ b", cStyle);   // same length, comment replaced by spaces
```

Nested comments, as allowed by Lua, are supported by passing a
`BlockCommentSyntax` whose `AllowNesting` is `true`:

```csharp
var luaStyle = new CommentSyntax("--", new BlockCommentSyntax("--[[", "]]", allowNesting: true),
    StringLiteralStyle.LongBracketQuoted);
```

An unclosed comment extends to the end of the text. Block-comment delimiters are
fixed strings, so parameterized long-bracket forms such as Lua's `--[==[` are
not modeled. A host that already has a tokenizer or grammar should use it for
full-fidelity scanning; these helpers are dependency-free approximations for
hosts that do not. Passing `null` or an empty string as the line-comment delimiter
disables line-comment awareness; passing `null` for the block pair disables
block-comment awareness.

Masking preserves both the string length and the line structure: CR and LF
characters inside a comment span are kept and every other character becomes a
space, so a masked comment-only line stays a blank line of its own instead of
merging with the line above it.

### Enumerating comment spans

`CommentOperations.EnumerateComments` returns a `CommentSpanEnumerator`, a `ref struct`
that walks every comment span in a single forward pass and supports `foreach`:

```csharp
foreach (CommentSpan span in CommentOperations.EnumerateComments(text, syntax))
{
    Console.WriteLine($"{span.SpanStart}..{span.End}");
}
```

Prefer the one-shot `CommentOperations` methods (`FindComment`, `RemoveComments`,
`MaskComments`, `GetCodeRange`, `GetCodeEnd`) unless iterating the spans in order is
required. Like all `ref struct`s, the enumerator cannot cross an `await`
boundary or be stored in a field; keep its lifetime local to a synchronous method.

### Multi-line strings

String-aware scanning also recognizes multi-line strings, so comment delimiters
inside them are ignored. The styles combine as flags on `CommentSyntax.StringStyle`,
and their dialect approximations are documented on `StringLiteralStyle`:

- `TripleDoubleQuoted` - multi-line strings delimited by a quote run, as in C#
  raw string literals. The opening quote run is the delimiter and the string
  closes on a run of at least that length, so content that must contain a
  three-quote sequence uses a longer opener.
- `TripleSingleQuoted` - multi-line strings delimited by exactly three single
  quotes, as in Python docstrings.
- `BacktickQuoted` - backtick strings (JavaScript template literals) that span
  lines; a backslash escapes the closing backtick, so other escaping rules (for
  example Go raw strings) are approximated.
- `LongBracketQuoted` - Lua-style long brackets (`[[ ... ]]`, `[=[ ... ]=]`) that
  span lines, where the closer carries the same number of equals signs as the
  opener; no escape sequences are processed.
- `VerbatimDoubleQuoted` - C# verbatim strings (`@"..."`, `$@"..."`, and
  `@$"..."`); a doubled quote is an escaped quote and backslashes are literal.

A C-family `CommentSyntax` includes `TripleDoubleQuoted` (together with
`DoubleQuoted`), so a `//` or `/*` inside a C-family raw string is not treated
as a comment:

```csharp
var cStyle = new CommentSyntax("//", new BlockCommentSyntax("/*", "*/"),
    StringLiteralStyle.DoubleQuoted | StringLiteralStyle.TripleDoubleQuoted);

int start = CommentOperations.FindComment(
    "string s = \"\"\"\n// not a comment\n\"\"\";", cStyle)?.SpanStart ?? -1;   // -1

string cleaned = CommentOperations.RemoveBlockComments(
    "\"\"\"a/* note */\"\"\"/* real */", cStyle);   // "\"\"\"a/* note */\"\"\""
```

## Deliberate trade-offs

- `WhitespaceConverter` computes tab columns from UTF-16 code units, so text
  containing wide or zero-width characters is approximated; use host font
  metrics when a display-accurate column is required.
- `StringTextSnapshot` materializes one line entry per line on first line access
  and caches it, so repeated `Lines` reads allocate nothing; a very large document
  pays that build cost on its first line read, so use the metadata-light `TextLineMap`
  when the richer line model is not needed.

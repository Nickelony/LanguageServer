# Nickelony.IDEKit.Core

Dependency-free text primitives, editing utilities, and host-independent
contracts for editor hosts and language tooling.

The package uses zero-based UTF-16 offsets and one-based line numbers. Line lengths and end offsets exclude line terminators. `StringTextSnapshot` recognizes LF, CRLF, and CR line endings.

The package is organized into vertical feature slices, each with its own
namespace:

- `Nickelony.IDEKit.Core.Text` — the text model: immutable snapshots, lines,
  ranges, and edit operations (`ITextSnapshot`, `StringTextSnapshot`,
  `TextRange`, `TextEditOperation`, `ITextEditTarget`, `ITextEditTargetVersion`),
  plus plain-text normalization of markup (`MarkupTextNormalizer`).
- `Nickelony.IDEKit.Core.Comments` — comment scanning, masking, removal, and
  continuation-marker detection (`CommentHelper`, `CommentSyntax`,
  `StringLiteralStyle`, `ContinuationHelper`).
- `Nickelony.IDEKit.Core.Editing` — neutral edit preparation and incremental
  editing (`TextEditKernel`, `TextIncrementalEditCalculator`, `TextLineMap`,
  `TextRangeOffsetResolver`).
- `Nickelony.IDEKit.Core.Formatting` — document formatting contracts and
  whitespace conversion (`ITextDocumentFormatter`,
  `TrimTrailingWhitespaceFormatter`, `WhiteSpaceConverter`).
- `Nickelony.IDEKit.Core.Identifiers` — identifier-extraction helpers for
  completion scenarios (`IdentifierHelper`).
- `Nickelony.IDEKit.Core.Infrastructure` — asynchronous request invalidation
  and request classification (`RequestTokenSource`,
  `SynchronousRequestAdapter`, `TextEditorRequestIdentity`,
  `TextEditorRequestOutcome`).
- `Nickelony.IDEKit.Core.Navigation` — back/forward editor navigation
  history, generic over the host's location type (`NavigationHistory<TLocation>`,
  `NavigationLocation`).
- `Nickelony.IDEKit.Core.FindReplace` — text-level find and replace
  primitives and search-result data (`FindReplaceText`, `FindingOrder`,
  `FindReplaceSource`, `FindReplaceItem`).

The IntelliSense feature contracts and payloads (completion, diagnostics,
hover, navigation, and signature help) live in the separate
`Nickelony.IDEKit.IntelliSense` package, which depends on this package for
the text primitives such as `TextRange`. The provider lifecycle (document
open/update/close/rename, cancellation, and startup/capability events) stays in
the `Nickelony.LanguageServer.Abstractions` package.

### Continuation markers

Several languages continue a statement onto the next line with a marker at the
end of the line. The marker can be a single character (PowerShell uses the
backtick) or a multi-character sequence (MATLAB uses `...`):

```powershell
Get-ChildItem `
    -Path C:\Temp
```

`ContinuationHelper.IsValidContinuation` detects such markers, ignoring a
trailing comment for languages that support one after the marker:

```csharp
bool continues = ContinuationHelper.IsValidContinuation(
    "Get-ChildItem `",
    new CommentSyntax("#", null, null, StringLiteralStyle.None),
    '`');   // true
```

The same check accepts a multi-character marker, such as MATLAB's `...`:

```csharp
bool continues = ContinuationHelper.IsValidContinuation(
    "total = 1 + 2 + ...",
    new CommentSyntax("%", null, null, StringLiteralStyle.None),
    "...");   // true
```

The same check accepts a trailing block comment before the marker when the
language's `CommentSyntax` includes block-comment delimiters:

```csharp
bool continues = ContinuationHelper.IsValidContinuation(
    "total = 1 + 2 /* carry */ ...",
    new CommentSyntax("//", "/*", "*/", StringLiteralStyle.DoubleQuoted | StringLiteralStyle.TripleDoubleQuoted),
    "...");   // true
```

### Block comments

Languages that delimit comments with an opening and closing marker, such as C's
`/* ... */` or Lua's `--[[ ... ]]`, are handled by `CommentHelper` with a
`CommentSyntax` that includes both delimiters plus the line-comment delimiter.
Block comments are recognized alongside line comments, so a block opener inside
a `//` comment is not treated as a comment. The string-literal styles are stated
explicitly; the C family passes `DoubleQuoted | TripleDoubleQuoted`, while a
language without string literals passes `None`:

```csharp
var cStyle = new CommentSyntax("//", "/*", "*/",
    StringLiteralStyle.DoubleQuoted | StringLiteralStyle.TripleDoubleQuoted);

int start = CommentHelper.FindBlockComment(
    "let x = 42; /* note */", cStyle)?.DelimiterStart ?? -1;   // 12

string cleaned = CommentHelper.RemoveBlockComments(
    "a /* c */ b", cStyle);   // "a  b" (surrounding whitespace kept)

string masked = CommentHelper.MaskBlockComments(
    "a /* c */ b", cStyle);   // same length, comment replaced by spaces
```

Nested comments, as allowed by Lua, are supported by passing
`allowNestedBlockComments: true` on the `CommentSyntax`. An unclosed comment
extends to the end of the text. Passing `null` or an empty string as the
line-comment delimiter disables line-comment awareness.

### Enumerating comment spans

`CommentHelper.EnumerateComments` returns a `CommentSpanEnumerator`, a `ref struct`
that walks every comment span in a single forward pass. Prefer the one-shot
`CommentHelper` methods (`FindComment`, `RemoveComments`, `MaskComments`,
`GetCodeRange`) unless you genuinely need to iterate the spans in order. Like all
`ref struct`s, the enumerator cannot cross an `await` boundary or be stored in a
field; keep its lifetime local to a synchronous method.

### Multi-line strings

String-aware scanning also recognizes multi-line strings, so comment delimiters
inside them are ignored. Two flags describe them:

- `TripleDoubleQuoted` — multi-line strings delimited by a run of three or more
  double quotes, as in C# raw string literals, Java text blocks, Kotlin raw
  strings, and Swift multi-line strings. The opening quote run is the delimiter;
  the string closes on a run of at least that length, so content that must
  contain a three-quote sequence uses a longer opener (C#). Backslashes are
  content, not escapes.
- `TripleSingleQuoted` — multi-line strings delimited by a run of three or more
  single quotes, as in Python docstrings.

A C-family `CommentSyntax` includes `TripleDoubleQuoted` (together with
`DoubleQuoted`), so a `//` or `/*` inside a C-family raw string is not treated
as a comment:

```csharp
var cStyle = new CommentSyntax("//", "/*", "*/",
    StringLiteralStyle.DoubleQuoted | StringLiteralStyle.TripleDoubleQuoted);

int start = CommentHelper.FindComment(
    "string s = \"\"\"\n// not a comment\n\"\"\";", cStyle)?.Start ?? -1;   // -1

string cleaned = CommentHelper.RemoveBlockComments(
    "\"\"\"a/* note */\"\"\"/* real */", cStyle);   // "\"\"\"a/* note */\"\"\""
```

`BacktickQuoted` strings also span lines, as in JavaScript template literals and
Go raw strings.

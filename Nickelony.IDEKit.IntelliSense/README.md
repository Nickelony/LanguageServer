# Nickelony.IDEKit.IntelliSense

Editor- and protocol-agnostic IntelliSense contracts, payloads, and framework-neutral host-state
records for editor hosts and language tooling. Depends only on `Nickelony.IDEKit.Core`.

[![NuGet](https://img.shields.io/nuget/v/Nickelony.IDEKit.IntelliSense.svg)](https://www.nuget.org/packages/Nickelony.IDEKit.IntelliSense)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Nickelony/IDEKit/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

This package owns the neutral IntelliSense feature contracts, payloads, and host-state
records shared by editor hosts, synchronous local providers, and asynchronous language-server
providers. It is organized into vertical feature slices, each with its own namespace:

- `Nickelony.IDEKit.IntelliSense.CodeActions` - code-action request and presentation records:
  `TextCodeActionContext` (editor state: document text, caret, normalized selection),
  `TextCodeActionRequestState` (the host-chosen request range; a `null` builder result
  vetoes the request), `TextCodeActionItem` (title, kind, preferred flag, opaque host payload), and
  `TextCodeActionControllerOptions` (request debounce policy; menu sizing is host
  policy and lives with the editor binding).
- `Nickelony.IDEKit.IntelliSense.Completion` - completion payloads and
  contracts: `TextCompletionItem`, `TextCompletionItemKind`,
  `TextCompletionTextEdit`, plus the
  session contracts (`ITextCompletionProvider`, `TextCompletionRequest`,
  `TextCompletionTrigger`, `TextCompletionFilter`, `TextCompletionItemFilter`,
  `TextCompletionSessionDecision`, `TextCompletionWordSpan`, `TextCompletionWordLocator`),
  and the shared `TextCompletionSessionKernel` that owns provider
  invocation, current-word filtering, replacement-range handling, and the open /
  filtered-empty / no-op decision (`TextCompletionSessionDecision.NoMatches` reports a word
  that filtered every candidate out), plus the host-state records `TextCompletionRequestSession`
  (the request lifetime shared by a controller pipeline and host-driven requests) and
  `TextCompletionPresentationState` (list, detail, and scheduled-request state). The
  completion edit payload uses neutral zero-based UTF-16 offset ranges (`TextRange`) plus
  the edit's replacement text (`TextCompletionTextEdit.NewText`) rather than line and
  character coordinates.
- `Nickelony.IDEKit.IntelliSense.Diagnostics` - synchronous diagnostics
  contracts and payloads: `ITextDiagnosticsProvider`, `TextDiagnosticsRequest`,
  `TextDiagnostic` (severity, message, producer `Source`/`Code` attribution, and
  offsets), plus the shared `DiagnosticHitTester` (offset/range selection in
  document order). Diagnostic hover selection policy (for example an exact-offset
  hit with a containing-line fallback) is host presentation and is composed by
  hosts from those primitives. The severity vocabulary (`TextDiagnosticSeverity`)
  lives in `Nickelony.IDEKit.Core.Diagnostics`, so producers, renderers, and hosts
  share one severity enum.
- `Nickelony.IDEKit.IntelliSense.DocumentSymbols` - document-symbol
  contracts and the neutral outline carrier: `TextDocumentSymbol`,
  `TextDocumentSymbolKind`, `ITextDocumentSymbolProvider`,
  `TextDocumentSymbolRequest`, and the projection-based `DocumentSymbolOutlineBuilder`
  with its `DocumentSymbolProjection<TItem>` selector bundle that projects flat or grouped
  item data into flat or grouped `TextDocumentSymbol` outlines. The protocol kind bridge
  lives with the protocol boundary in `Nickelony.LanguageServer.Client`.
- `Nickelony.IDEKit.IntelliSense.Hover` - hover contracts, requests, and
  payloads: `ITextHoverProvider`, `TextHoverRequest`, `TextHoverInfo`, and
  `TextHoverEvaluationState` (the host's hover evaluation input for one hovered
  offset: request decision, tooltip and diagnostic permissions, and diagnostic
  info). The `TextMarkupKind` used by hover content and completion documentation
  lives in the root `Nickelony.IDEKit.IntelliSense` namespace.
- `Nickelony.IDEKit.IntelliSense.Navigation` - definition navigation
  contracts and payloads: `ITextDefinitionProvider`, `TextDefinitionRequest`,
  `TextDefinitionLocation`, `TextDefinitionDiscriminator`. `TextDefinitionLocation`
  carries an opaque provider-defined document identifier plus zero-based target and
  selection ranges in line and character units (`TextPositionRange`).
- `Nickelony.IDEKit.IntelliSense.SemanticTokens` - style-neutral semantic
  token contracts: `TextSemanticToken`, `TextSemanticTokenTypes`,
  `TextSemanticTokenModifiers`. Tokens carry range, type, and modifiers only;
  hosts map them onto their own theme model.
- `Nickelony.IDEKit.IntelliSense.Signatures` - signature-help contracts and
  payloads: `ITextSignatureHelpProvider`, `TextSignatureHelpRequest`,
  `TextSignatureHelpContext`, `TextSignatureHelpTriggerKind`,
  `TextSignatureHelp`, `TextSignatureInformation`, `TextSignatureParameterInfo`,
  plus the controller records `TextSignatureHelpControllerOptions` (refresh timing and
  overload-cycle policy) and `TextSignatureHelpPresentationState` (visible, in-flight, and
  refresh-pending state).

Besides contracts and payloads, the package ships shared
behavioral helpers (`TextCompletionSessionKernel`, `TextCompletionFilter`,
`DocumentSymbolOutlineBuilder`, `DiagnosticHitTester`) that editor hosts and
language packages consume instead of reimplementing session, filtering, outline,
and diagnostic-selection logic, alongside the framework-neutral host-state
records listed above. Diagnostic message formatting, severity labels,
message separators, and hover selection policy are presentation decisions and
ship with editor host packages, not here. The built-in completion filter matches
the resolved filter text (which falls back to the item label when no filter text
was set) with an ordinal, case-insensitive substring test; matching algorithms
are client-defined. Insertion and edit text are never matched, and fuzzy ranking
is a host concern: a host that wants fuzzy scoring injects its own filter through
`TextCompletionItemFilter`.

The completion taxonomy is a documented superset of LSP 3.17
`CompletionItemKind`: every protocol kind (`Text` through `TypeParameter`, values
1-25) has a well-known member with the same identifier, so language providers
map protocol kinds one-to-one instead of collapsing them onto renderable
categories; the numeric bridge lives in `Nickelony.LanguageServer.Client`.
`Generic` is the presentation fallback for items whose producer cannot
supply a category (it is not a protocol kind), and `Array`, `Section`,
`Directive`, `Parameter`, and `Namespace` are library-only extensions. The type
is an open vocabulary: hosts whose editors distinguish additional categories (for
example host-specific command classes) define them with
`TextCompletionItemKind.CreateCustom`, and equality compares the normalized
identifier ordinally. Completion items also carry the protocol ordering data:
`TextCompletionItem.SortText` preserves the protocol sort text and is the
authoritative ordering key for hosts that follow protocol ordering;
`IsPreselected` mirrors the protocol `preselect` flag, and `Priority` is the
producer-derived numeric hint (higher first) for hosts whose list can only rank by
one number; producers derive it so that its ordering agrees with `SortText`.
A snippet-format item (`TextCompletionItem.InsertTextFormat`,
`TextCompletionInsertTextFormat.Snippet`) keeps its insertion or edit text
verbatim with raw tabstop syntax, and `TextSnippetExpander.Expand` turns that
text into the visible text plus a flat placeholder model (`$1`,
`${1:default}`, `${1|a,b|}`, `\$` escapes, nesting) with
`TextSnippetPlaceholder` entries ordered by ascending index (the final stop `$0`
last), then position. Variables and named placeholders
(`$TM_FILENAME`, `${TM_FILENAME}`, `${VAR:default}`) are outside that subset and
stay literal; a host that supports them resolves them around the expansion.
Tab navigation, placeholder linking, and undo
grouping stay host behavior; the library never rewrites snippet text on the
wire.
Completion requests carry a neutral `TextCompletionTrigger` (`Invoked` by
default); hosts that distinguish additional interactions define custom triggers
the same way. The kernel keeps no per-call state and no completion cache: every
decision invokes the provider again, so a host that re-runs the kernel while
typing re-queries a list the underlying protocol marked incomplete.
Incompleteness is not modeled by the shared contracts; a host that caches
decisions owns `isIncomplete` handling.
The semantic-token type and modifier names stay plain string
constants because the token vocabulary is open-ended and hosts map unknown names
onto a default presentation.

Vocabulary policy: vocabularies that identify host- or language-defined
categories are open and accept custom identifiers (`TextCompletionItemKind`,
`TextCompletionTrigger`, semantic-token type and modifier names), while
vocabularies that enumerate a protocol-defined set are closed and document their
protocol numerics as lineage (`TextMarkupKind`, `TextCompletionTag`,
`TextCompletionInsertTextFormat`, `TextDocumentSymbolKind`,
`TextSignatureHelpTriggerKind`); unrecognized values are mapped at the payload or
protocol boundary. Controller option records (`TextSignatureHelpControllerOptions`,
`TextCodeActionControllerOptions`) ship overridable framework-free defaults, while
editor-presentation policy such as menu sizing stays with the editor bindings.

All request contracts are synchronous, stateless snapshots: each request record
is immutable and carries the document snapshot text plus the feature-specific
context (an offset, a symbol name, or a symbol filter). Providers must be safe
to call from any thread and hold no document state.

The position model is deliberate. Position-addressed request records carry
zero-based UTF-16 offsets (`TextCompletionRequest`, `TextHoverRequest`,
`TextSignatureHelpRequest`), as do the code-action records (`TextCodeActionContext`,
`TextCodeActionRequestState`) for the caret, selection, and requested range. Requests that
identify content by name or filter,
and the whole-document diagnostics request, carry no position
(`TextDefinitionRequest`, `TextDocumentSymbolRequest`, `TextDiagnosticsRequest`);
the diagnostics request can additionally name its document through the optional
opaque `TextDiagnosticsRequest.DocumentId`.
Every response payload bound to the requested snapshot uses the same offsets
(for example `TextHoverInfo.Range`, document symbols, semantic tokens, and completion
text-edit ranges). The single exception
is `TextDefinitionLocation`, whose target and selection ranges use zero-based
line and character positions because a definition can name a document the provider
has not materialized; the host converts when it opens the target. A request
always travels with the exact text its offsets index, and Core's `TextLineMap`
(`GetOffset` and `GetPosition`) converts between the two families, so hosts
convert at their boundary in one place.

## Completion resolve lifecycle and merge rules

A language-server-backed provider attaches `TextCompletionItem.ResolveCallback`;
a host resolves lazily with `ResolveAsync` and merges the richer response with
`WithResolvedContent`. The normative per-field rules live on
`WithResolvedContent`, and the table below mirrors them; the merge follows the
explicit-assignment-wins convention (a never-assigned field behaves as absent,
while an explicitly assigned value wins, including a reset to the field's default):

| Field | Merge rule |
| --- | --- |
| `Detail` | resolved when non-blank |
| `Documentation` + `DocumentationKind` | resolved together when the resolved documentation is non-blank |
| `Kind` | resolved when the resolved item explicitly set one |
| `TextEdit`, `SortText` | adopted only when the current item lacks them |
| `InsertTextFormat` | resolved when the resolved item explicitly set one |
| `Tags` | union of both |
| `CommitCharacters`, `AdditionalTextEdits` | adopted only when the current item has none |
| `InsertText` | adopted when the current item never set it and has no edit payload |
| `FilterText` | adopted when the current item never set it |
| `Label`, `Priority`, `IsPreselected`, request stamps | always from the current item |

Two host-side lifecycle mechanisms cooperate: the request-lifetime session
(`TextCompletionRequestSession` over the core request coordinator) supersedes
in-flight pipeline runs, while item stamps (`RequestDocumentVersion`,
`RequestGeneration`) keep items that outlive the pipeline - for example entries of
an open completion window resolved asynchronously - attributable to the document
state they were produced for; `WithRequestContext` rebases the stamps and
`WithoutTextEdit` drops a resolved edit payload. A commit path chooses its text
per `TextCompletionTextEdit`: the edit's `NewText` when the item carries an edit,
the insertion text otherwise, with the edit's range used while it still fits the
current document.

Payload conventions:

- Value-like payloads are records (for example `TextCompletionRequest`, `TextHoverInfo`,
  `TextDefinitionRequest`, `TextDefinitionLocation`, `TextDiagnosticsRequest`,
  `TextDocumentSymbolRequest`, `TextHoverRequest`, `TextSignatureHelpRequest`) and
  record structs (for example `TextCompletionTextEdit`, `TextCompletionSessionDecision`,
  `TextCompletionWordSpan`, `DocumentSymbolProjection<TItem>`). Payload constructors
  store supplied values as-is apart from required-argument null guards, the documented
  position checks (`TextCompletionRequest`, `TextHoverRequest`, and
  `TextSignatureHelpRequest` reject an offset outside the document text,
  `TextCodeActionContext` and `TextCodeActionRequestState` reject an unordered or
  out-of-range span, `TextDiagnostic` rejects a reversed span, `TextSignatureHelpContext`
  rejects an undefined trigger kind, and `TextSemanticToken` trims and deduplicates
  modifiers), the blank-identifier rejection on the custom completion-trigger and
  item-kind factories, the symbol-name trimming on `TextDefinitionRequest`, and the word
  normalization on `TextCompletionWordSpan`.
  Request records compare their document text in equality, so they are call data rather
  than high-volume dictionary keys.
- Payloads whose construction normalizes or validates inputs beyond that stay
  classes so the normalization is applied once (for example `TextSemanticToken`,
  `TextDocumentSymbol`, `TextSignatureInformation`). `TextCompletionItem` is also a
  class and normalizes in its accessors; create it with `new TextCompletionItem("label")`
  and set optional fields with an object initializer. Required reference arguments throw
  `ArgumentNullException`.
- Four payload types use structural equality: `TextCompletionItemKind`
  and `TextCompletionTrigger` (normalized identifier), `TextSemanticToken` (range,
  type, and modifier sequence), and `TextDiagnostic` (severity, message, source, code,
  and span); hosts that need other value comparisons project the
  relevant components. A record whose member is a collection compares that member by
  reference (for example `TextSnippetExpansion.Placeholders`).
- Payload collections are owned read-only snapshots, while the shared helpers
  (`TextCompletionFilter`, `DocumentSymbolOutlineBuilder`, `DiagnosticHitTester`)
  return caller-owned lists (a filter or hit-tester result with no matches is a shared
  empty array that callers must not mutate).
- Blank or whitespace text values are generally tolerated and passed through unless a
  type documents otherwise (for example `TextCompletionItem` trims `Detail`, and trims
  `Documentation` only when `DocumentationKind` is `PlainText`, while `TextDiagnostic`
  and `TextHoverInfo` reject blank required content).

Two deliberate asymmetries: the SemanticTokens slice ships payloads and
well-known name constants but no provider contract (hosts convert their own
token models, and `Nickelony.LanguageServer.Client` provides the shared
protocol-token conversion), and DocumentSymbols ships the neutral model plus the
synchronous provider contract only - the asynchronous counterpart lives with the
language-server packages (`ILanguageServerIntelliSenseProvider.GetDocumentSymbolsAsync`
in `Nickelony.LanguageServer.Abstractions`).

Extension points are deliberately shaped: a typed discriminator
(`TextDefinitionDiscriminator`) names a known navigation target; opaque host
payloads (`TextCodeActionItem.Payload`, `TextDocumentSymbol.Data`) carry data
the library never inspects and hands back unchanged; and
`TextCompletionPresentationState.DetailContent` is `object?` because it is
rendering input a host decides how to present. New seams should prefer the typed
form when the set of values is known.

The six synchronous `IText*Provider` interfaces are the shared contract for
in-process and custom providers: a host that owns synchronous catalogs (an
editor host, or a language package that answers from local state) implements
them directly, while a language-server-backed provider wraps its asynchronous
client in a provider that projects server payloads onto the shared values. The
same split applies to the helpers above: they are the synchronous building
blocks a host composes, and a host that cannot call them synchronously (for
example a UI-bound editor) schedules the call itself.

## Adoption notes

The synchronous stack is consumed by an external adopter, Tomb Editor, in
production: its scripting packages implement the `IText*Provider` contracts with
their own coordinators, compose the shared helpers (`TextCompletionSessionKernel`,
`TextCompletionFilter`, `DocumentSymbolOutlineBuilder`, `DiagnosticHitTester`),
and rely on the completion item's request stamps and resolve lifecycle for their
asynchronous completion windows. The in-repo binding packages and the
language-server packages consume the same surface (the AvalonEdit binding hosts
the controllers, and the Lua language-server provider projects protocol payloads
onto the shared values), so the synchronous stack has consumers on both sides of
the repository boundary. API changes between preview versions are not accompanied
by compatibility layers; adopters migrate to the current surface.

## Getting started

```csharp
using Nickelony.IDEKit.Core.Text;              // StringTextSnapshot, TextRange
using Nickelony.IDEKit.IntelliSense.Completion;

// A provider implements ITextCompletionProvider and returns shared items.
var kernel = new TextCompletionSessionKernel();
var provider = new MyCompletionProvider();
TextCompletionSessionDecision decision = kernel.GetDecision(
    new StringTextSnapshot(documentText),
    caretOffset,
    provider,
    TextCompletionTrigger.Invoked);

if (decision.StartOffset is int startOffset
    && decision.EndOffset is int endOffset
    && decision.Items is { Count: > 0 } items)
{
    ShowCompletionWindow(items, startOffset, endOffset);
}
else if (decision.AllItemsFilteredOut)
{
    // The typed word filtered every candidate out; a host may dismiss a narrowed session
    // instead of keeping stale entries.
    HideCompletionWindow();
}
```

Diagnostics follow the same pattern: a provider returns `TextDiagnostic`
values, and the host selects them with `DiagnosticHitTester.GetDiagnosticsAtOffset`
or `GetDiagnosticsForRange` and formats them with its own presentation rules; a
hover tooltip that falls back from the exact offset to the containing line composes
both primitives. Signature help, hover, definition, and document symbols
expose analogous provider interfaces and request records.

## Requirements

- .NET 8.0 or later (the package targets `net8.0` and is built with nullable
  reference types enabled).
- `Nickelony.IDEKit.Core` for snapshots, ranges, and text primitives.

Install with:

```powershell
dotnet add package Nickelony.IDEKit.IntelliSense
```

## What stays outside this package

- **Framework-typed presentation surface** ships from editor binding packages such as
  `Nickelony.IDEKit.AvalonEdit.LanguageFeatures`, because it is written against one editor
  framework's types: host hooks (for example a hover tooltip sink carrying framework
  coordinates), skins, presenters, host sizing policy (for example the code-action menu
  height), and message formatting (diagnostic message composition
  and severity labels). The framework-neutral state those surfaces display (the hover
  evaluation state, the completion and signature help presentation states, and the code-action
  records) lives in this package's feature slices.
- The **asynchronous provider lifecycle** (document open/update/close/rename,
  request cancellation, startup/capability events, session state) lives in
  `Nickelony.LanguageServer.Abstractions`, which depends on this package for the
  shared payload values.
- **LSP protocol and transport** types live in
  `Nickelony.LanguageServer.Client` and stay at the protocol boundary as DTOs,
  together with the protocol kind bridges (`TextCompletionItemKindConversion`,
  `TextDocumentSymbolKindConversion`).
- **Text primitives** (snapshots, ranges, line maps, edit kernels) live in
  `Nickelony.IDEKit.Core`; this package depends on it for `TextRange` and
  related primitives.

## License

MIT © 2026 Kewin Kupilas.

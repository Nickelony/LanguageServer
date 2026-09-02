# Nickelony.IDEKit.IntelliSense

Dependency-free editor IntelliSense contracts and payloads for editor hosts and
language tooling.

[![NuGet](https://img.shields.io/nuget/v/Nickelony.IDEKit.IntelliSense.svg)](https://www.nuget.org/packages/Nickelony.IDEKit.IntelliSense)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Nickelony/IDEKit/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

This package owns the neutral, protocol-free IntelliSense feature contracts and
payload values shared by editor hosts, synchronous local providers, and
asynchronous language-server providers. It is organized into vertical feature
slices, each with its own namespace:

- `Nickelony.IDEKit.IntelliSense.Completion` - completion payloads and
  contracts: `TextCompletionItem`, `TextCompletionItemKind`,
  `TextCompletionTextEdit`, `TextCompletionItemKindJsonConverter`, plus the
  session contracts (`ITextCompletionProvider`, `TextCompletionContext`,
  `TextCompletionTrigger`, `TextCompletionFilter`, `TextCompletionSessionDecision`)
  and the shared `CompletionSessionKernel` that owns provider invocation,
  current-word filtering, replacement-range handling, and open/close/no-op
  decisions. The completion edit payload uses neutral zero-based UTF-16 offset
  ranges (`TextRange`) rather than protocol line and column coordinates.
- `Nickelony.IDEKit.IntelliSense.Diagnostics` - synchronous diagnostics
  contracts and payloads: `ITextDiagnosticsProvider`, `TextDiagnosticsRequest`,
  `TextEditorDiagnostic`, `TextEditorDiagnosticSeverity`.
- `Nickelony.IDEKit.IntelliSense.DocumentSymbols` - document-symbol
  contracts and the neutral outline carrier: `TextDocumentSymbol`,
  `TextDocumentSymbolKind`, `ITextDocumentSymbolProvider`,
  `TextDocumentSymbolRequest`, and the selector-driven `DocumentSymbolTreeBuilder`
  that projects flat or grouped node data into a nested `TextDocumentSymbol` tree.
- `Nickelony.IDEKit.IntelliSense.Hover` - hover contracts, requests, and
  payloads: `ITextHoverProvider`, `TextHoverRequest`, `TextHoverInfo`,
  `TextHoverContentKind`, `TextHoverRequestState`.
- `Nickelony.IDEKit.IntelliSense.Navigation` - definition navigation
  contracts and payloads: `ITextDefinitionProvider`, `TextDefinitionRequest`,
  `TextDefinitionLocation`, `TextDefinitionDiscriminator`.
- `Nickelony.IDEKit.IntelliSense.SemanticTokens` - style-neutral semantic
  token contracts: `TextSemanticToken`, `TextSemanticTokenTypes`,
  `TextSemanticTokenModifiers`. Tokens carry range, type, and modifiers only;
  hosts map them onto their own theme model.
- `Nickelony.IDEKit.IntelliSense.Signatures` - signature-help contracts and
  payloads: `ITextSignatureHelpProvider`, `TextSignatureHelpRequest`,
  `TextSignatureHelpInfo`, `TextSignatureParameterInfo`.

All contracts are synchronous, thread-safe, stateless snapshot contracts: each
request record is immutable and carries the document text plus a zero-based
document offset (or an engine version for diagnostics). No provider holds
document state.

## What stays outside this package

- The **asynchronous provider lifecycle** (document open/update/close/rename,
  request cancellation, startup/capability events, session state) lives in
  `Nickelony.LanguageServer.Abstractions`, which depends on this package for the
  shared payload values.
- **LSP protocol and transport** types live in
  `Nickelony.LanguageServer.Client` and stay at the protocol boundary as DTOs.
- **Text primitives** (snapshots, ranges, line maps, edit kernels) live in
  `Nickelony.IDEKit.Core`; this package depends on it for `TextRange` and
  related primitives.

## Dependencies

`Nickelony.IDEKit.Core`.

## License

MIT © 2026 Kewin Kupilas.

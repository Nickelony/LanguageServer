# Nickelony.LanguageServer.Abstractions

**Lightweight editor IntelliSense lifecycle contracts** for the [Nickelony Language Server](https://github.com/Nickelony/IDEKit) family.

[![NuGet](https://img.shields.io/nuget/v/Nickelony.LanguageServer.Abstractions.svg)](https://www.nuget.org/packages/Nickelony.LanguageServer.Abstractions)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Nickelony/IDEKit/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

This package contains the **editor-facing lifecycle contract** of the Nickelony Language Server
family - the stable seam between a text editor and any language provider. It holds the provider
lifecycle (document open/update/close/rename, request cancellation, startup/capability events,
session state) and the protocol-bound payloads. All of its types live in the single namespace
`Nickelony.LanguageServer.Abstractions`; the `CodeActions/`, `Editing/`, `Lifecycle/`, and
`Navigation/` folder structure is purely organizational. The shared, protocol-free IntelliSense payload
values (`TextDiagnostic`, `TextHoverInfo`, `TextDefinitionLocation`,
`TextSignatureHelp`, `TextCompletionItem`) and the synchronous provider request records live
in the dependency-free `Nickelony.IDEKit.IntelliSense` package under the
`Nickelony.IDEKit.IntelliSense` feature namespaces
(`Nickelony.IDEKit.IntelliSense.Diagnostics`, `...Hover`, `...Navigation`,
`...Signatures`, `...Completion`); this package references that package for those values.

## What's inside

The contract surface, grouped by IntelliSense feature. Every type in this package is in the
single `Nickelony.LanguageServer.Abstractions` namespace; rows that name a
`Nickelony.IDEKit.IntelliSense.*` namespace are shared payloads referenced from the
`Nickelony.IDEKit.IntelliSense` package:

| Area | Types / members |
|---|---|
| Provider | `ILanguageServerIntelliSenseProvider` |
| Session state | `LanguageServerProviderState` |
| Document lifecycle | `OpenDocument`, `UpdateDocument`, `CloseDocument`, `RenameDocument` |
| Completion | `TextCompletionItem`, `TextCompletionItemKind` (in `Nickelony.IDEKit.IntelliSense.Completion`) |
| Diagnostics | `TextDiagnostic` (in `Nickelony.IDEKit.IntelliSense.Diagnostics`), `TextDiagnosticSeverity` (in `Nickelony.IDEKit.Core.Diagnostics`) |
| Hover | `TextHoverInfo`, `TextMarkupKind` (in `Nickelony.IDEKit.IntelliSense.Hover`) |
| Navigation | `ITextReferencesProvider`, `TextReferenceLocation`, `TextReferenceRequest`; `TextDefinitionLocation` (in `Nickelony.IDEKit.IntelliSense.Navigation`) |
| Editing | `ITextEditProvider`, `ITextFormattingProvider`, `TextEdit`, `TextWorkspaceEdit`, `TextDocumentEdit`, `TextFormatRequest`, `TextFormattingOptions`, `TextRenameRequest` |
| Code actions | `TextCodeAction`, `TextCodeActionRequest` |
| Signatures | `TextSignatureHelp`, `TextSignatureInformation`, `TextSignatureParameterInfo` (in `Nickelony.IDEKit.IntelliSense.Signatures`) |
| Failures | `LanguageServerStartupFailure`, `WorkspaceWatcherFailure` |

Because this package is the contract seam, it is safe for **any** project to reference - an
editor that only wants to *consume* IntelliSense, or a provider that wants to *implement* it,
never needs to drag in `StreamJsonRpc` or any LSP implementation.

## Installation

```sh
dotnet add package Nickelony.LanguageServer.Abstractions
```

## Usage

Reference the contract instead of a concrete provider:

```csharp
using Nickelony.LanguageServer.Abstractions;
using Nickelony.IDEKit.IntelliSense.Completion;

// An editor consumes the contract, whatever provider backs it:
ILanguageServerIntelliSenseProvider provider = GetProvider(); // e.g. the Lua provider

provider.DiagnosticsUpdated += (_, eventArgs) =>
{
    // eventArgs carries the file path and an owned diagnostics snapshot.
    // Marshal to your UI thread before touching controls.
};

provider.OpenDocument(filePath, sourceText);

IReadOnlyList<TextCompletionItem> items =
    await provider.GetCompletionItemsAsync(filePath, sourceText, line, column);
```

Or implement the contract to provide IntelliSense for your own language - all the
`Text*` types are plain immutable records designed to be trivially constructible.

## Coordinate conventions

The contract uses zero-based LSP coordinates for its range payloads:

- **Request positions are zero-based** line and column indices (`TextReferenceRequest.Line`/
  `Column`, `TextRenameRequest.Line`/`Column`, and both ends of `TextCodeActionRequest.Range`);
  negative values are changed to zero.
- **Results and range payloads use zero-based** line and character positions in LSP units, carried
  by `Nickelony.IDEKit.Core.Text.TextPositionRange` (`TextEdit.Range`,
  `TextReferenceLocation.Range`): lines count line breaks, characters count UTF-16 code units within
  the line, and a tab counts as a single code unit. Range values are stored as supplied.
- The shared IntelliSense payloads use zero-based UTF-16 document offsets where their request
  records carry offsets (for example `TextHoverRequest.HoveredOffset`).

Hosts convert between their own caret coordinates and these bases at the boundary.

## Dependencies

`Nickelony.IDEKit.Core` (for the shared text primitives such as `TextPositionRange`) and
`Nickelony.IDEKit.IntelliSense` (for the shared `Nickelony.IDEKit.IntelliSense.*` payload values).

## License

MIT © 2026 Kewin Kupilas.

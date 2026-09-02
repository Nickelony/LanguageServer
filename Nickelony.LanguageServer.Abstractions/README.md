# Nickelony.LanguageServer.Abstractions

**Lightweight editor IntelliSense lifecycle contracts** for the [Nickelony Language Server](https://github.com/Nickelony/IDEKit) family.

[![NuGet](https://img.shields.io/nuget/v/Nickelony.LanguageServer.Abstractions.svg)](https://www.nuget.org/packages/Nickelony.LanguageServer.Abstractions)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Nickelony/IDEKit/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

This package contains the **editor-facing lifecycle contract** of the Nickelony Language Server
family - the stable seam between a text editor and any language provider. It holds the provider
lifecycle (document open/update/close/rename, request cancellation, startup/capability events,
session state) and the protocol-bound payloads. All of its types live in the single namespace
`Nickelony.LanguageServer.Abstractions`; the `Editing/`, `Navigation/`, and `Infrastructure/`
folder structure is purely organizational. The shared, protocol-free IntelliSense payload
values (`TextEditorDiagnostic`, `TextHoverInfo`, `TextDefinitionLocation`,
`TextSignatureHelpInfo`, `TextCompletionItem`) and the synchronous provider request records live
in the dependency-free `Nickelony.IDEKit.IntelliSense` package under the
`Nickelony.IDEKit.IntelliSense` feature namespaces
(`Nickelony.IDEKit.IntelliSense.Diagnostics`, `...Hover`, `...Navigation`,
`...Signatures`, `...Completion`); this package references that package for those values.

## What's inside

The contract surface, grouped by IntelliSense feature. Every type in this package is in the
single `Nickelony.LanguageServer.Abstractions` namespace; rows that name a
`Nickelony.IDEKit.IntelliSense.*` namespace are shared payloads referenced from the
`Nickelony.IDEKit.IntelliSense` package:

| Area | Types |
|---|---|
| Provider | `ILanguageServerIntelliSenseProvider` |
| Session state | `LanguageServerProviderState` |
| Document lifecycle | `OpenDocument`, `UpdateDocument`, `CloseDocument`, `RenameDocument` |
| Completion | `TextCompletionItem`, `TextCompletionItemKind` (in `Nickelony.IDEKit.IntelliSense.Completion`) |
| Diagnostics | `TextEditorDiagnostic`, `TextEditorDiagnosticSeverity` (in `Nickelony.IDEKit.IntelliSense.Diagnostics`) |
| Hover | `TextHoverInfo`, `TextHoverContentKind` (in `Nickelony.IDEKit.IntelliSense.Hover`) |
| Navigation | `ITextReferencesProvider`, `TextReferenceLocation`, `TextReferenceRequest`; `TextDefinitionLocation` (in `Nickelony.IDEKit.IntelliSense.Navigation`) |
| Editing | `ITextEditProvider`, `ITextFormattingProvider`, `TextEdit`, `TextWorkspaceEdit`, `TextDocumentEdit`, `TextDocumentRange`, `TextFormatRequest`, `TextFormattingOptions`, `TextRenameRequest` |
| Signatures | `TextSignatureHelpInfo`, `TextSignatureParameterInfo` (in `Nickelony.IDEKit.IntelliSense.Signatures`) |
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

provider.DiagnosticsUpdated += (filePath, diagnostics) =>
{
    // Marshal to your UI thread before touching controls.
};

provider.OpenDocument(filePath, sourceText);

IReadOnlyList<TextCompletionItem> items =
    await provider.GetCompletionItemsAsync(filePath, sourceText, line, column);
```

Or implement the contract to provide IntelliSense for your own language - all the
`Text*` types are plain records designed to be trivially constructible.

## Dependencies

`Nickelony.IDEKit.IntelliSense` (for the shared `Nickelony.IDEKit.IntelliSense.*` payload
values), which in turn depends on `Nickelony.IDEKit.Core` for the text primitives.

## License

MIT © 2026 Kewin Kupilas.

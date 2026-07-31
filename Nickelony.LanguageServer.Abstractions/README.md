# Nickelony.LanguageServer.Abstractions

**Lightweight, dependency-free editor IntelliSense contracts** for the [Nickelony Language Server](https://github.com/Nickelony/LanguageServer) family.

[![NuGet](https://img.shields.io/nuget/v/Nickelony.LanguageServer.Abstractions.svg)](https://www.nuget.org/packages/Nickelony.LanguageServer.Abstractions)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Nickelony/LanguageServer/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

This package contains the **editor-facing contract** of the Nickelony Language Server family -
the stable seam between a text editor and any language provider. It has **zero package
dependencies**: just one small `net8.0` assembly of interfaces and plain data types.

## What's inside

The contract surface, grouped by IntelliSense feature:

| Area | Types |
|---|---|
| Provider | `ILanguageServerIntellisenseProvider` |
| Document lifecycle | `OpenDocument`, `UpdateDocument`, `CloseDocument`, `RenameDocument` |
| Completion | `TextCompletionItem`, `TextCompletionItemKind`, `TextCompletionPosition`, `TextCompletionRange`, `TextCompletionTextEdit` |
| Diagnostics | `TextEditorDiagnostic`, `TextEditorDiagnosticSeverity` |
| Hover | `TextHoverInfo`, `TextHoverContentKind` |
| Navigation | `ITextReferencesProvider`, `TextDefinitionLocation`, `TextReferenceLocation`, `TextReferenceRequest` |
| Editing | `ITextEditProvider`, `ITextFormattingProvider`, `TextEdit`, `TextWorkspaceEdit`, `TextDocumentEdit`, `TextDocumentRange`, `TextFormatRequest`, `TextFormattingOptions`, `TextRenameRequest` |
| Signatures | `TextSignatureHelpInfo`, `TextSignatureParameterInfo` |
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
using Nickelony.LanguageServer.Abstractions.Completion;

// An editor consumes the contract, whatever provider backs it:
ILanguageServerIntellisenseProvider provider = GetProvider(); // e.g. the Lua provider

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

None. This package has no package references.

## License

MIT © 2026 Kewin Kupilas.

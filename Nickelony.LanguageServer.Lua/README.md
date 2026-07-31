# Nickelony.LanguageServer.Lua

**A lightweight Lua language-server provider** for the [Nickelony Language Server](https://github.com/Nickelony/LanguageServer) family, backed by the real [LuaLS](https://github.com/LuaLS/lua-language-server) (`lua-language-server` executable).

[![NuGet](https://img.shields.io/nuget/v/Nickelony.LanguageServer.Lua.svg)](https://www.nuget.org/packages/Nickelony.LanguageServer.Lua)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Nickelony/LanguageServer/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

This package turns [LuaLS](https://github.com/LuaLS/lua-language-server) into a drop-in
IntelliSense provider for your editor. It implements the editor contracts from
[`Nickelony.LanguageServer.Abstractions`](https://www.nuget.org/packages/Nickelony.LanguageServer.Abstractions)
on top of the [`Nickelony.LanguageServer.Client`](https://www.nuget.org/packages/Nickelony.LanguageServer.Client)
LSP client - all you do is construct the provider, open documents, and consume the callbacks.

## Features

- **Completion** - contextual item lists, including `completionItem/resolve` support.
- **Diagnostics** - publish diagnostics per document, delivered through
  `DiagnosticsUpdated` and cached for on-demand reads.
- **Hover** - rich hover content with markdown support.
- **Navigation** - go-to-definition and find-references.
- **Rename & formatting** - symbol rename with workspace edits, and document formatting
  respecting LuaLS settings.
- **Signature help** - parameter info for function calls.
- **Semantic tokens** - Lua-specific tokens (`LuaSemanticToken`) via
  `SemanticTokensUpdated`, with delta support for efficient re-coloring.
- **Workspace coordination** - tracks open documents, syncs file changes to the server, and
  re-syncs the whole workspace when the server restarts.

## Requirements

- .NET 8 (package targets `net8.0`, cross-platform).
- The **LuaLS executable** at runtime - download the `lua-language-server` binary from the
  [LuaLS releases page](https://github.com/LuaLS/lua-language-server/releases) and pass its path
  to the provider.

## Installation

```sh
dotnet add package Nickelony.LanguageServer.Lua
```

## Usage

```csharp
using Microsoft.Extensions.Logging;
using Nickelony.LanguageServer.Lua;

var provider = new LuaLanguageServerIntellisenseProvider(
    workspaceRootDirectoryPath: @"C:\my\workspace",
    serverExecutablePath: @"C:\tools\lua-language-server\lua-language-server.exe",
    logger: loggerFactory.CreateLogger<LuaLanguageServerIntellisenseProvider>());

// Callbacks may fire on background threads - marshal to your UI thread before touching controls.
provider.DiagnosticsUpdated += (filePath, diagnostics) =>
{
    // e.g. update squiggles / error list for `filePath`.
};

provider.SemanticTokensUpdated += (filePath, tokens) =>
{
    // e.g. recolor the editor for `filePath`.
};

// Track a document as the user edits it.
const string filePath = @"C:\my\workspace\main.lua";
provider.OpenDocument(filePath, sourceText);

// Then drive IntelliSense from editor commands:
var completions = await provider.GetCompletionItemsAsync(
    filePath, sourceText, line, column, triggerCharacter: '.');
var hover      = await provider.GetHoverAsync(filePath, sourceText, line, column);
var definition = await provider.GetDefinitionAsync(filePath, sourceText, line, column);
var signatures = await provider.GetSignatureHelpAsync(filePath, sourceText, line, column);
var references = await provider.GetReferencesAsync(new TextReferenceRequest(filePath, line, column));
var edits      = await provider.RenameSymbolAsync(new TextRenameRequest(filePath, line, column, "newName"));
var formatted  = await provider.FormatDocumentAsync(new TextFormatRequest(filePath, sourceText));

provider.UpdateDocument(filePath, updatedSourceText);
provider.CloseDocument(filePath);
provider.Dispose();
```

> **Note:** all `*Async` IntelliSense methods take the current document `content`, so you can
> drive them from a live editor buffer without waiting for server round-trips of edits.

## Dependencies

- `Nickelony.LanguageServer.Abstractions`
- `Nickelony.LanguageServer.Client`
- `Microsoft.Extensions.Logging.Abstractions` 8.0.x

## License

MIT © 2026 Kewin Kupilas.

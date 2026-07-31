# LanguageServer

A .NET library family for building language-server-powered editors: a StreamJsonRpc LSP client, generic editor IntelliSense contracts, and a Lua provider backed by [LuaLS](https://github.com/LuaLS/lua-language-server).

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

## What is this?

This repository is a small, focused collection of .NET libraries that make it easy to add
language-server-powered IntelliSense to a text editor. Instead of coupling an editor to a single
language server, the family is split into three layers:

| Package | Purpose |
|---|---|
| [`Nickelony.LanguageServer.Abstractions`](Nickelony.LanguageServer.Abstractions/) | Dependency-free editor IntelliSense contracts - the stable seam between an editor and any language provider. |
| [`Nickelony.LanguageServer.Client`](Nickelony.LanguageServer.Client/) | A lightweight LSP client built on StreamJsonRpc: spawns a language-server process and speaks LSP over stdio. |
| [`Nickelony.LanguageServer.Lua`](Nickelony.LanguageServer.Lua/) | A ready-to-use Lua provider that implements the editor contracts on top of the client and drives the real LuaLS executable. |

It powers Lua IntelliSense in **Tomb Editor** (`TombIDE.ScriptingStudio`).

## Features

- **Editor-first contracts** - completion, hover, definition, references, rename, formatting,
  signature help, diagnostics, and (for Lua) semantic tokens, expressed as plain editor types
  with zero dependency on any LSP implementation.
- **Real LSP transport** - process hosting, `initialize` handshake, capability negotiation,
  JSON-RPC over stdio via [StreamJsonRpc](https://github.com/microsoft/vs-streamjsonrpc).
- **Robust lifecycle** - automatic server restart on crash, transport versioning, graceful
  shutdown, and workspace re-sync after restart.
- **Document tracking** - full and incremental text-document synchronization, plus a workspace
  file watcher that forwards file changes to the server.
- **Plug-and-play Lua provider** - point it at a `lua-language-server` executable and get
  IntelliSense, diagnostics, and semantic coloring in your editor.

## Quick start

The fastest way to get going is to reference the **Lua** package and host
`LuaLanguageServerIntellisenseProvider` in your editor:

```xml
<PackageReference Include="Nickelony.LanguageServer.Lua" Version="0.1.0-preview" />
```

```csharp
using Nickelony.LanguageServer.Lua;

var provider = new LuaLanguageServerIntellisenseProvider(
    workspaceRootDirectoryPath: @"C:\my\workspace",
    serverExecutablePath: @"C:\tools\lua-language-server\lua-language-server.exe");

provider.DiagnosticsUpdated += (filePath, diagnostics) =>
{
    // Diagnostics arrive on a background thread - marshal to your UI thread here.
    // Show them in your editor's error list / squiggles.
};

provider.OpenDocument(@"C:\my\workspace\main.lua", sourceText);

var completions = await provider.GetCompletionItemsAsync(
    @"C:\my\workspace\main.lua", sourceText, line, column);
```

See the [Lua package README](Nickelony.LanguageServer.Lua/README.md) for the full example, or the
[Client](Nickelony.LanguageServer.Client/README.md) and
[Abstractions](Nickelony.LanguageServer.Abstractions/README.md) READMEs for the lower layers.

## Architecture

```
┌───────────────────────────────┐
│         Your editor           │
└───────────────┬───────────────┘
                │ uses
┌───────────────▼───────────────┐
│   Nickelony.LanguageServer    │  Lua provider (ILuaIntellisenseProvider)
│   .Lua                        │
└───────┬───────────────┬───────┘
        │               │
┌───────▼───────┐ ┌─────▼───────────────┐
│ Abstractions  │ │ Client              │  LSP over stdio (StreamJsonRpc)
│ (contracts)   │ │ (process + RPC)     │
└───────────────┘ └──────┬──────────────┘
                         │ stdio (JSON-RPC)
                 ┌───────▼──────────────┐
                 │  Language server     │  e.g. LuaLS (lua-language-server)
                 └──────────────────────┘
```

- **`Abstractions`** has **zero package dependencies** - it is a pure leaf any editor or provider
  can reference safely.
- **`Client`** depends on `Abstractions`, `StreamJsonRpc`, and
  `Microsoft.Extensions.Logging.Abstractions`.
- **`Lua`** depends on `Abstractions` + `Client` and shells out to an external server executable.

## Requirements

- .NET 8 (all packages target `net8.0`, cross-platform).
- The **Lua** package additionally needs the `lua-language-server` executable at runtime
  (download from the [LuaLS releases](https://github.com/LuaLS/lua-language-server/releases)).

## Building and testing

```sh
dotnet build Nickelony.LanguageServer.slnx
dotnet test  Nickelony.LanguageServer.slnx
```

The test suite covers the client (194 tests) and the Lua provider (136 tests + 4 integration
tests that require a local LuaLS bundle).

## Repository layout

```
Nickelony.LanguageServer.Abstractions/   editor contracts (zero dependencies)
Nickelony.LanguageServer.Client/         LSP client + protocol machinery
Nickelony.LanguageServer.Lua/            Lua provider backed by LuaLS
Tests/
  Nickelony.LanguageServer.Client.Tests/
  Nickelony.LanguageServer.Lua.Tests/
  TestSupport/                           shared test logger
```

## License

[MIT](LICENSE) © 2026 Kewin Kupilas.

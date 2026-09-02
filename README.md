# <img width="32" height="32" alt="Nickelony IDEKit" src="https://github.com/user-attachments/assets/3b8c6718-3f76-4780-9872-168eee8fab74" /> Nickelony IDEKit - .NET editor and language-server libraries

A lightweight .NET library family for building editors and IDEs: WPF-free editor primitives and IntelliSense contracts (`Nickelony.IDEKit.*`), an AvalonEdit editor adapter, and a language-server tier (`Nickelony.LanguageServer.*`) - a StreamJsonRpc LSP client plus a Lua provider backed by [LuaLS](https://github.com/LuaLS/lua-language-server).

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

## What is this?

This repository is a small, focused collection of .NET libraries that make it easy to build
editors and IDEs. It hosts two sibling families: the `Nickelony.IDEKit.*` editor toolkit (text
primitives, IntelliSense contracts, AvalonEdit adapters, key bindings, tooling) and the
`Nickelony.LanguageServer.*` family that implements the editor contracts on top of a real
language server. The language-server tier is split into three layers:

| Package | Purpose |
|---|---|
| [`Nickelony.LanguageServer.Abstractions`](Nickelony.LanguageServer.Abstractions/) | Dependency-free editor IntelliSense contracts - the stable seam between an editor and any language provider. |
| [`Nickelony.LanguageServer.Client`](Nickelony.LanguageServer.Client/) | A lightweight LSP client built on StreamJsonRpc: spawns a language-server process and speaks LSP over stdio. |
| [`Nickelony.LanguageServer.Lua`](Nickelony.LanguageServer.Lua/) | A ready-to-use Lua provider that implements the editor contracts on top of the client and drives the real LuaLS executable. |

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
`LuaLanguageServerIntelliSenseProvider` in your editor:

```xml
<PackageReference Include="Nickelony.LanguageServer.Lua" Version="1.0.0-preview.1" />
```

```csharp
using Nickelony.LanguageServer.Lua;

var provider = new LuaLanguageServerIntelliSenseProvider(
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
The [consumer integration guide](docs/ConsumerIntegration.md) documents construction,
disposal, event marshaling, document references, and cancellation behavior.

## Architecture

```
┌───────────────────────────────┐
│         Your editor           │
└───────────────┬───────────────┘
                │ uses
┌───────────────▼───────────────┐
│   Nickelony.LanguageServer    │  Lua provider (ILuaIntelliSenseProvider)
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

- **`Abstractions`** depends only on the in-repo `Nickelony.IDEKit.IntelliSense` contracts
  (themselves dependency-free) - it is the stable seam any editor or provider can reference safely.
- **`Client`** depends on `Abstractions`, `StreamJsonRpc`, and
  `Microsoft.Extensions.Logging.Abstractions`.
- **`Lua`** depends on `Abstractions` + `Client` and shells out to an external server executable.

## Requirements

- .NET 8 (all packages target `net8.0`, cross-platform).
- The **Lua** package additionally needs the `lua-language-server` executable at runtime
  (download from the [LuaLS releases](https://github.com/LuaLS/lua-language-server/releases)).

## Package tiers

The repository hosts two sibling families: the `Nickelony.IDEKit.*` editor toolkit
and the `Nickelony.LanguageServer.*` family described above, which can be consumed
independently of each other. The tiers make the dependency direction explicit:
pick the smallest tier that covers your feature, and do not assume any higher
tier is required.

| Tier | Packages | Purpose | Dependencies |
|---|---|---|---|
| **Foundations** (WPF-free) | `Nickelony.IDEKit.Core`, `Nickelony.IDEKit.IntelliSense`, `Nickelony.IDEKit.Workspace` | Text primitives, editor contracts, document authority. Reusable from any UI toolkit. | Core: none. IntelliSense: Core. Workspace: Core. |
| **Editor adapters** | `Nickelony.IDEKit.AvalonEdit`, `Nickelony.IDEKit.AvalonEdit.IntelliSense` | AvalonEdit UI bridge: editing, documents, status, and the IntelliSense controllers. | `net8.0-windows`, WPF. Both: Core. IntelliSense: `Nickelony.IDEKit.IntelliSense`. |
| **Optional integrations** | `Nickelony.IDEKit.AvalonEdit.Extras`, `Nickelony.IDEKit.KeyBindings`, `Nickelony.IDEKit.Tooling` | Leaf packages for a specific feature; adopt only what you need. | Extras: AvalonEdit + Markdig + TextMateSharp. KeyBindings: none (WPF). Tooling: none. |
| **Experimental extras** | `Nickelony.IDEKit.JsonSchema` | JSON Schema **vocabulary index**, not a validator or context-aware completion engine. | `Newtonsoft.Json.Schema` only (isolated). |
| **Language-server packages** | `Nickelony.LanguageServer.Abstractions`, `Nickelony.LanguageServer.Client`, `Nickelony.LanguageServer.Lua` | Editor IntelliSense contracts, an LSP client, and a Lua provider. | Abstractions: IDEKit.IntelliSense. Client: Abstractions + StreamJsonRpc. Lua: Abstractions + Client. |

### Tier notes and non-goals

- **Foundations are WPF-free** - Core, IntelliSense, and Workspace target
  `net8.0` and reference no WPF or AvalonEdit types. They are the migration seam
  for a future toolkit switch (for example AvaloniaEdit).
- **Document views and editor sessions are optional workspace architecture.** A
  host that only needs the editor packages does not need `Nickelony.IDEKit.Workspace`,
  `IWorkspaceDocumentView`, or the editor-session contracts. Those are
  opt-in document-authority features, not a requirement for using the editor
  adapters.
- **Base `Nickelony.IDEKit.AvalonEdit` does not depend on `Nickelony.IDEKit.Workspace`**
  (see the diagram below). The packages ship the document-view and
  editor-session contracts; the AvalonEdit-to-Workspace bridge is host code, so
  applications that need that integration combine the packages in their own host
  code.
- **`Nickelony.IDEKit.AvalonEdit.Extras` intentionally brings Markdig and
  TextMateSharp**; those focused dependencies are the point of the extras
  package and stay confined to it.
- **`Nickelony.IDEKit.JsonSchema` is experimental** and stays so until a second
  real consumer or a stable contract justifies promotion. It is not part of the
  core editor contract surface and may change outside the normal preview
  cadence. See its [README](Nickelony.IDEKit.JsonSchema/README.md) for the exact
  vocabulary-index contract.

### Dependency diagram

```
Nickelony.IDEKit.AvalonEdit.IntelliSense     (-> AvalonEdit, IntelliSense)
Nickelony.IDEKit.AvalonEdit.Extras           (-> AvalonEdit, Markdig, TextMateSharp)
        │
        ▼
Nickelony.IDEKit.AvalonEdit                  (-> Core, AvalonEdit)   no Workspace
        │
        ├──────────────────────────────────────┐
        ▼                                      ▼
Nickelony.IDEKit.IntelliSense                Nickelony.IDEKit.Workspace
        │  (-> Core)                            │  (-> Core)
        └───────────────┬──────────────────────┘
                        ▼
              Nickelony.IDEKit.Core            (WPF-free, dependency-free leaf)

Optional / isolated leaves:
  Nickelony.IDEKit.Tooling       dependency-free
  Nickelony.IDEKit.KeyBindings   WPF, standalone
  Nickelony.IDEKit.JsonSchema    -> Newtonsoft.Json.Schema only (experimental)

Language-server packages:
  Nickelony.LanguageServer.Abstractions  dependency-free leaf
  Nickelony.LanguageServer.Client        -> Abstractions, StreamJsonRpc
  Nickelony.LanguageServer.Lua           -> Abstractions, Client
```

## Building and testing

```sh
dotnet build Nickelony.LanguageServer.slnx
dotnet test  Nickelony.LanguageServer.slnx
```

The test suite covers the client and Lua provider, plus four opt-in integration tests that
require a local LuaLS bundle. Run `dotnet test` for the current counts.

## Repository layout

```
Nickelony.IDEKit.Core/                      WPF-free foundations: text primitives + editor contracts
Nickelony.IDEKit.IntelliSense/              WPF-free IntelliSense contracts and payloads
Nickelony.IDEKit.Workspace/                 WPF-free document authority, document views, editor sessions
Nickelony.IDEKit.AvalonEdit/                AvalonEdit editor adapter (no Workspace dependency)
Nickelony.IDEKit.AvalonEdit.IntelliSense/   AvalonEdit IntelliSense controllers
Nickelony.IDEKit.AvalonEdit.Extras/         Markdown tooltips (Markdig) + TextMate highlighting (TextMateSharp)
Nickelony.IDEKit.Tooling/                   External-process execution
Nickelony.IDEKit.KeyBindings/               WPF command + keyboard shortcut system
Nickelony.IDEKit.JsonSchema/                Experimental JSON Schema vocabulary index
Nickelony.LanguageServer.Abstractions/   editor contracts (zero dependencies)
Nickelony.LanguageServer.Client/         LSP client + protocol machinery
Nickelony.LanguageServer.Lua/            Lua provider backed by LuaLS
Tests/
  Nickelony.IDEKit.*.Tests/                 per-package test projects
  Nickelony.LanguageServer.Client.Tests/
  Nickelony.LanguageServer.Lua.Tests/
  TestSupport/                           shared test logger
```

## License

[MIT](LICENSE) © 2026 Kewin Kupilas.

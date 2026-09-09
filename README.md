# <img width="32" height="32" alt="Nickelony IDEKit" src="https://github.com/user-attachments/assets/3b8c6718-3f76-4780-9872-168eee8fab74" /> Nickelony IDEKit - .NET editor and language-server libraries

A lightweight .NET library family for building editors and IDEs: WPF-free editor primitives and IntelliSense contracts (`Nickelony.IDEKit.*`), an AvalonEdit editor adapter, and a language-server tier (`Nickelony.LanguageServer.*`) - a StreamJsonRpc LSP client plus a Lua provider backed by [LuaLS](https://github.com/LuaLS/lua-language-server).

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

## What is this?

This repository is a small, focused collection of .NET libraries that make it easy to build
editors and IDEs. It hosts two sibling families: the `Nickelony.IDEKit.*` editor toolkit (text
primitives, IntelliSense contracts, AvalonEdit adapters, key bindings, process execution) and
the `Nickelony.LanguageServer.*` family that implements the editor contracts on top of a real
language server. The language-server tier is split into four layers:

| Package | Purpose |
|---|---|
| [`Nickelony.LanguageServer.Abstractions`](Nickelony.LanguageServer.Abstractions/) | Editor IntelliSense contracts with no external package dependencies - the stable seam between an editor and any language provider. |
| [`Nickelony.LanguageServer.Client`](Nickelony.LanguageServer.Client/) | A lightweight LSP client built on StreamJsonRpc: spawns a language-server process and speaks LSP over stdio. |
| [`Nickelony.LanguageServer.Provider`](Nickelony.LanguageServer.Provider/) | The provider framework: lifecycle, document synchronization, workspace watching, and request machinery for language-server providers. |
| [`Nickelony.LanguageServer.Lua`](Nickelony.LanguageServer.Lua/) | A ready-to-use Lua provider that implements the editor contracts on top of the provider framework and drives the real LuaLS executable. |

## Features

- **Editor-first contracts** - completion, hover, definition, references, rename, formatting,
  signature help, document symbols, code actions, diagnostics, and (for Lua) semantic tokens,
  expressed as plain editor types with zero dependency on any LSP implementation.
- **Real LSP transport** - process hosting, `initialize` handshake, capability negotiation,
  JSON-RPC over stdio via [StreamJsonRpc](https://github.com/microsoft/vs-streamjsonrpc).
- **Robust lifecycle** - restart-on-crash at the provider layer, transport versioning, graceful
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
    workspaceRootDirectoryPaths: [@"C:\my\workspace"],
    serverExecutablePath: @"C:\tools\lua-language-server\lua-language-server.exe");

provider.DiagnosticsUpdated += (_, eventArgs) =>
{
    // Diagnostics for eventArgs.FilePath arrive on a background thread - marshal to your
    // UI thread here and show them in your editor's error list / squiggles.
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
│   Nickelony.LanguageServer    │  Lua provider (ILuaLanguageServerIntelliSenseProvider)
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
- **`Client`** depends on `Nickelony.IDEKit.Core`, `Nickelony.IDEKit.IntelliSense`, `StreamJsonRpc`,
  and `Microsoft.Extensions.Logging.Abstractions`.
- **`Provider`** depends on `Client` + `Abstractions` and supplies the provider framework:
  lifecycle, document synchronization, workspace watching, and request machinery.
- **`Lua`** depends on `Provider` + `Abstractions` + `Client` (plus the IDEKit models) and shells
  out to an external server executable.

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
| **Foundations** (WPF-free) | `Nickelony.IDEKit.Core`, `Nickelony.IDEKit.IntelliSense`, `Nickelony.IDEKit.Workspace`, `Nickelony.IDEKit.Workspace.Views` | Text primitives, editor contracts, document authority, and optional document-view coordination. Reusable from any UI toolkit. | Core: none. IntelliSense: Core. Workspace: Core. Workspace.Views: Workspace. |
| **Editor adapters** | `Nickelony.IDEKit.AvalonEdit`, `Nickelony.IDEKit.AvalonEdit.LanguageFeatures` | AvalonEdit UI bridge: editing, documents, status, and the IntelliSense controllers. | `net8.0-windows`, WPF. Both: Core. IntelliSense: `Nickelony.IDEKit.IntelliSense`. |
| **Optional integrations** | `Nickelony.IDEKit.AvalonEdit.Markdown`, `Nickelony.IDEKit.AvalonEdit.TextMate`, `Nickelony.IDEKit.KeyBindings`, `Nickelony.IDEKit.Processes` | Leaf packages for a specific feature; adopt only what you need. | Markdown: AvalonEdit + Markdig. TextMate: AvalonEdit + TextMateSharp. KeyBindings: none (WPF). Processes: none. |
| **Experimental extras** | `Nickelony.IDEKit.JsonSchema` | JSON Schema **vocabulary index**, not a validator or context-aware completion engine. | `Newtonsoft.Json.Schema` only (isolated). |
| **Language-server packages** | `Nickelony.LanguageServer.Abstractions`, `Nickelony.LanguageServer.Client`, `Nickelony.LanguageServer.Lua` | Editor IntelliSense contracts, an LSP client, and a Lua provider. | Abstractions: IDEKit.IntelliSense. Client: IDEKit.Core + IDEKit.IntelliSense + StreamJsonRpc. Lua: Abstractions + Client. |

### Tier notes and non-goals

- **Foundations are WPF-free** - Core, IntelliSense, Workspace, and Workspace.Views target
  `net8.0` and reference no WPF or AvalonEdit types. They are the migration seam
  for a future toolkit switch (for example AvaloniaEdit). The editor-generic surface a second
  binding reuses lives here: the text and edit primitives (`Core.Text`, `Core.Editing`), the
  auto-closing resolver and the line-comment planner (`Core.AutoClosing`, `Core.Comments`),
  request coordination (`Core.Requests`), the navigation identity (`Core.Navigation`), the
  line-status contracts (`Core.LineStatus`), the diagnostic vocabulary (`Core.Diagnostics`), and
  the IntelliSense contracts, payloads, kernels, and host-state records. The
  [editor-binding guide](docs/EditorBindingGuide.md) inventories what a new binding reuses and
  what it writes.
- **Document views are optional workspace architecture.** The workspace family splits document
  authority (`Nickelony.IDEKit.Workspace`, usable headless) from view coordination
  (`Nickelony.IDEKit.Workspace.Views`). A host that only needs document authority never takes
  the view contracts, and a host that only needs the editor packages does not need the workspace
  family at all.
- **Base `Nickelony.IDEKit.AvalonEdit` does not depend on the workspace packages**
  (see the diagram below). The workspace packages ship the document-view coordination; the
  AvalonEdit-to-Workspace bridge is host code, so applications that need that integration combine
  the packages in their own host code.
- **`Nickelony.IDEKit.AvalonEdit.Markdown` and
  `Nickelony.IDEKit.AvalonEdit.TextMate` intentionally bring Markdig and
  TextMateSharp**; those focused dependencies are the point of the packages
  and stay confined to the one that needs them.
- **`Nickelony.IDEKit.Processes` is batch-scoped by design**: one request runs
  one process to completion and redirected output is captured in full. There is
  no standard-input piping or streaming support; hosts that need interactive
  processes drive `System.Diagnostics.Process` directly, and the LSP transport
  lives in `Nickelony.LanguageServer.Client`.
- **`Nickelony.IDEKit.JsonSchema` is experimental** and stays so until a second
  real consumer or a stable contract justifies promotion. It is not part of the
  core editor contract surface and may change outside the normal preview
  cadence. See its [README](Nickelony.IDEKit.JsonSchema/README.md) for the exact
  vocabulary-index contract.

### Dependency diagram

```
Nickelony.IDEKit.AvalonEdit.LanguageFeatures     (-> AvalonEdit, IntelliSense)
Nickelony.IDEKit.AvalonEdit.Markdown         (-> AvalonEdit, Markdig)
Nickelony.IDEKit.AvalonEdit.TextMate         (-> AvalonEdit, TextMateSharp)
        │
        ▼
Nickelony.IDEKit.AvalonEdit                  (-> Core, AvalonEdit)   no Workspace
        │
        ├──────────────────────────────────────┐
        ▼                                      ▼
Nickelony.IDEKit.IntelliSense                Nickelony.IDEKit.Workspace
        │  (-> Core)                            │  (-> Core)
        │                                      ▼
        │                            Nickelony.IDEKit.Workspace.Views   (-> Core, Workspace)
        └───────────────┬──────────────────────┘
                        ▼
              Nickelony.IDEKit.Core            (WPF-free, dependency-free leaf)

Optional / isolated leaves:
  Nickelony.IDEKit.Processes     dependency-free
  Nickelony.IDEKit.KeyBindings   WPF, standalone
  Nickelony.IDEKit.JsonSchema    -> Newtonsoft.Json.Schema only (experimental)

Language-server packages:
  Nickelony.LanguageServer.Abstractions  -> IDEKit.IntelliSense (no external dependencies)
  Nickelony.LanguageServer.Client        -> IDEKit.Core, IDEKit.IntelliSense, StreamJsonRpc
  Nickelony.LanguageServer.Lua           -> Abstractions, Client
```

## Building and testing

```sh
dotnet build Nickelony.LanguageServer.slnx
dotnet test  Nickelony.LanguageServer.slnx
```

The test suite covers all packages, plus four opt-in integration tests that require a local
LuaLS bundle. Run `dotnet test` for the current counts.

## Repository layout

```
Nickelony.IDEKit.Core/                      WPF-free foundations: text primitives + editor contracts
Nickelony.IDEKit.IntelliSense/              WPF-free IntelliSense contracts, payloads, and host-state records
Nickelony.IDEKit.Workspace/                 WPF-free document authority + editing core (headless-friendly)
Nickelony.IDEKit.Workspace.Views/           WPF-free document-view coordination (view contract + manager)
Nickelony.IDEKit.AvalonEdit/                AvalonEdit editor adapter (no Workspace dependency)
Nickelony.IDEKit.AvalonEdit.LanguageFeatures/   AvalonEdit IntelliSense controllers
Nickelony.IDEKit.AvalonEdit.Markdown/       Markdown tooltips (Markdig)
Nickelony.IDEKit.AvalonEdit.TextMate/       TextMate highlighting (TextMateSharp)
Nickelony.IDEKit.Processes/                  Batch external-process execution
Nickelony.IDEKit.KeyBindings/               WPF command + keyboard shortcut system
Nickelony.IDEKit.JsonSchema/                Experimental JSON Schema vocabulary index
Nickelony.LanguageServer.Abstractions/   editor contracts (no external dependencies)
Nickelony.LanguageServer.Client/         LSP client + protocol machinery
Nickelony.LanguageServer.Lua/            Lua provider backed by LuaLS
Tests/
  Nickelony.IDEKit.*.Tests/                 per-package test projects
  Nickelony.LanguageServer.Abstractions.Tests/
  Nickelony.LanguageServer.Client.Tests/
  Nickelony.LanguageServer.Lua.Tests/
  TestSupport/                           shared test logger
```

## License

[MIT](LICENSE) © 2026 Kewin Kupilas.

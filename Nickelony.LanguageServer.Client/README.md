# Nickelony.LanguageServer.Client

**A lightweight LSP client for .NET**, built on [StreamJsonRpc](https://github.com/microsoft/vs-streamjsonrpc). It spawns a language-server process and speaks the Language Server Protocol over stdio.

[![NuGet](https://img.shields.io/nuget/v/Nickelony.LanguageServer.Client.svg)](https://www.nuget.org/packages/Nickelony.LanguageServer.Client)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Nickelony/LanguageServer/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

This package is the **LSP client machinery** of the [Nickelony Language Server](https://github.com/Nickelony/LanguageServer) family. It handles everything below the editor-facing contracts: hosting the server process, the `initialize` handshake, capability negotiation, JSON-RPC request/notification transport, document synchronization, and workspace file watching.

## Features

- **Process hosting** - starts the language-server executable and performs the full LSP
  initialization handshake (`LanguageServerClient.StartAsync`).
- **Typed JSON-RPC** - `SendRequestAsync<TResult>` / `SendNotificationAsync` over stdio via
  StreamJsonRpc, with a `JsonSerializer` tuned for LSP conventions.
- **Resilient sessions** - transport generation tracking, automatic restart when the server
  crashes or the transport becomes unhealthy, and workspace re-sync after restart.
- **Capability negotiation** - read negotiated capabilities such as
  `SupportsReferences`, `SupportsRename`, `SupportsFormatting`, `SupportsSemanticTokensFull`,
  `SupportsSemanticTokensDelta`, and `TextDocumentSyncKind`.
- **Document tracking** - full and incremental text-document synchronization
  (`TrackedDocumentStore`, `DocumentIncrementalEditCalculator`), didOpen/didChange/didClose,
  and file renames.
- **Workspace watching** - `WorkspaceFileWatcher` with change debouncing and batched
  `didChangeWatchedFiles` forwarding.
- **Server notifications** - `DiagnosticsPublished`, `SemanticTokensRefreshRequested`, and
  `workspace/didChangeConfiguration` with a cached settings payload.
- **Logging** - `Microsoft.Extensions.Logging.Abstractions` throughout; pass your `ILogger`
  and get structured, level-appropriate logs.

## Installation

```sh
dotnet add package Nickelony.LanguageServer.Client
```

## Usage

```csharp
using Microsoft.Extensions.Logging;
using Nickelony.LanguageServer.Client;

var client = new LanguageServerClient(
    workspaceRootDirectoryPath: @"C:\my\workspace",
    serverExecutablePath: @"C:\tools\lua-language-server\lua-language-server.exe",
    options: new LanguageServerClientOptions(settingsProvider: () => new { }),
    logger: loggerFactory.CreateLogger<LanguageServerClient>());

client.DiagnosticsPublished += diagnostics =>
{
    // Diagnostics for a tracked document were published by the server.
    // May be raised on a background thread - marshal to your UI thread.
};

bool ready = await client.StartAsync(cancellationToken);

if (ready)
{
    var result = await client.SendRequestAsync<MyResponse>(
        "textDocument/hover", hoverParams, cancellationToken);

    await client.SendNotificationAsync(
        "textDocument/didChange", changeParams, cancellationToken);
}

await client.DisposeAsync();
```

Options let you tune lifecycle timeouts and provide LSP payloads for your host:

```csharp
var options = new LanguageServerClientOptions(settingsProvider: () => new { maxPreload = 10 })
{
    InitializeTimeout       = TimeSpan.FromSeconds(20),
    ShutdownRequestTimeout  = TimeSpan.FromSeconds(3),
    DisposeWaitTimeout      = TimeSpan.FromSeconds(5),
    ClientCapabilitiesProvider = _ => new { textDocument = new { hover = true } },
    InitializationOptionsProvider = _ => new { },
};
```

> **Tip:** `LanguageServerClient` is a low-level building block. If you're looking for
> turnkey IntelliSense in your editor, prefer the
> [`Nickelony.LanguageServer.Lua`](https://www.nuget.org/packages/Nickelony.LanguageServer.Lua)
> provider, which implements the editor contracts from
> [`Nickelony.LanguageServer.Abstractions`](https://www.nuget.org/packages/Nickelony.LanguageServer.Abstractions)
> on top of this client.

## Dependencies

- `Nickelony.LanguageServer.Abstractions`
- `StreamJsonRpc` 2.25.x
- `Microsoft.Extensions.Logging.Abstractions` 8.0.3

## License

MIT © 2026 Kewin Kupilas.

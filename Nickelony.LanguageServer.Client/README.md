# Nickelony.LanguageServer.Client

**A lightweight LSP client for .NET**, built on [StreamJsonRpc](https://github.com/microsoft/vs-streamjsonrpc). It spawns a language-server process and speaks the Language Server Protocol over stdio.

[![NuGet](https://img.shields.io/nuget/v/Nickelony.LanguageServer.Client.svg)](https://www.nuget.org/packages/Nickelony.LanguageServer.Client)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Nickelony/LanguageServer/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

This package is the **LSP client machinery** of the [Nickelony Language Server](https://github.com/Nickelony/LanguageServer) family. It handles everything below the editor-facing contracts: hosting the server process, the `initialize` handshake, capability negotiation, JSON-RPC request/notification transport, the document-synchronization payload machinery, and workspace file watching.

## Features

- **Process hosting** - starts the language-server executable and performs the full LSP
  initialization handshake (`LanguageServerClient.StartAsync`).
- **Multi-root workspaces** - the constructor takes a workspace root list; every entry is
  advertised through `workspace/workspaceFolders`, and the first entry is the primary `rootUri`.
  The folder set is fixed at construction time (no `workspace/didChangeWorkspaceFolders`). An empty
  list is valid and models a folderless session: `rootUri` and `workspaceFolders` are sent as `null`.
- **Typed JSON-RPC** - `SendRequestAsync<TResult>` / `SendNotificationAsync` over stdio via
  StreamJsonRpc, with a `JsonSerializer` tuned for LSP conventions. A server error response is
  surfaced as `LanguageServerRequestRejectedException` (carrying the JSON-RPC `ErrorCode`), so a
  rejection is distinguishable from a transport failure and the transport stays ready.
- **Resilient sessions** - transport generation tracking with crash-resilient session invalidation:
  when the transport of a ready session crashes or becomes unhealthy, the client marks the transport
  unavailable (raising `TransportUnavailable`) and the host recreates the session on the next
  startup attempt, followed by workspace re-sync. Failures before a session becomes ready surface as
  `false` from `StartAsync` with the cause logged and exposed through `LastStartupException` instead.
- **Capability negotiation** - read negotiated capabilities such as
  `SupportsCompletionResolve`, `SupportsReferences`, `SupportsDocumentSymbols`, `SupportsCodeActions`,
  `SupportsRename`, `SupportsFormatting`, `SupportsHover`, `SupportsDefinition`,
  `SupportsSignatureHelp`, `SupportsSemanticTokensFull`, `SupportsSemanticTokensDelta`, and
  `TextDocumentSyncKind`.
- **Document tracking** - full and incremental text-document synchronization payloads
  (`TrackedDocumentStore`, using the IDEKit core diff helper to compute incremental edits),
  didOpen/didChange/didClose notification payloads, and rename requests; the host sends the returned
  synchronization requests over its transport. `TryClose` reports its outcome explicitly
  (`DocumentCloseResult`: `Closed`, `StillOpen`, `BusyWithRequests`, or `Untracked`), so a host can
  defer the close until outstanding request references drain. Request references can be released
  by identity (`DocumentRequestReference`), so a release still reaches a record that a rename
  rekeyed.
- **Workspace watching** - `WorkspaceFileWatcher` with change debouncing and batched change
  forwarding that the host sends as `workspace/didChangeWatchedFiles`.
- **Provider-neutral utilities** - `WorkspaceSnapshotTracker` for capturing and diffing
  workspace snapshots that a host can use in its own watcher recovery, and `SignatureLabelParser`
  for signature-help parameter labels.
- **Server events** - `DiagnosticsPublished` and `SemanticTokensRefreshRequested` raise server
  notifications; the client also pushes `workspace/didChangeConfiguration` with a cached settings payload.
- **Semantic tokens** - `SemanticTokensDecoder` decodes raw LSP semantic-token integer streams into
  typed `SemanticToken` lists; `SemanticTokensDeltaParser` normalizes full and delta responses;
  `SemanticTokenConversion.ToTextSemanticTokens(tokens, lineMap)` bridges the protocol tokens to the
  shared offset-based `TextSemanticToken` payload for editor hosts. Delta payloads are parsed into
  validated edit lists that the client never applies; the provider requests full tokens, so
  incremental edit application stays a host-side concern until a host needs it.
- **Protocol helpers** - `ProtocolRangeConversion` translates LSP payload positions and ranges into
  the shared zero-based `TextPositionRange` payloads; the `Protocol/DocumentSymbols/` family
  carries the document-symbol request and the tolerant response payloads that cover the
  hierarchical `DocumentSymbol` and flat `SymbolInformation` shapes with one type; the
  `Protocol/CodeActions/` family carries the code-action request (range plus diagnostics context)
  and the tolerant literal-action responses that keep entries with an inline edit and skip
  command-only entries.
- **Logging** - `Microsoft.Extensions.Logging.Abstractions` throughout; pass your `ILogger`
  and get structured, level-appropriate logs.

## Project structure

The package uses a **single flat namespace** (`Nickelony.LanguageServer.Client`) for its entire
public and internal surface: folders express slice ownership only, and a file keeps its namespace
when it moves between slices. The slice map:

| Folder | Contents |
|---|---|
| `Documents/` | Tracked-document synchronization: store, snapshots, change ranges, operation scheduler |
| `Interop/` | Protocol-to-editor bridges: `ProtocolRangeConversion`, `SemanticTokenConversion`, and the `ProtocolMarkupContent`/`MarkupContentReader` pair |
| `Pathing/` | `LanguageServerPaths` (path and file-URI identity helpers) |
| `Protocol/` | All LSP wire DTOs and their JSON converters, one subfolder per feature (`Common/`, `Requests/`, `Notifications/`, `Callbacks/`, `Capabilities/`, `WorkspaceEdits/`, `CodeActions/`, `Completion/`, `DocumentSymbols/`, `Hover/`, `Navigation/`, `References/`, `SignatureHelp/`, `SemanticTokens/`) |
| `SemanticTokens/` | Non-wire semantic-token model and decode machinery: `SemanticToken`, `SemanticTokensDecoder`, `SemanticTokensDeltaParser` |
| `Transport/` | Client runtime: process hosting (`ProcessJobObject`), transport session/host, RPC forwarding, capability store, options, exceptions, and subscription plumbing |
| `Workspace/` | File watching: `WorkspaceFileWatcher`, `WorkspaceSnapshotTracker`, change debouncer/accumulator |

Placement rules:

- **Wire shapes live in `Protocol/`.** Every DTO that mirrors an LSP message and every
  `JsonConverter` for one lives under `Protocol/<Feature>/`; feature logic stays in its own slice.
- **Bridges live in `Interop/`.** Every file that exposes or consumes shared IDEKit text/editor
  models lives here; those are the only IDEKit-typed public members in the package. IDEKit is also
  referenced as an implementation detail by `Documents/` (incremental edit computation),
  `SemanticTokens/` (line/character to offset math), and `Pathing/` (the shared local-path comparison
  policy), without appearing in their signatures.
- **`SemanticTokens/` contains no wire types**; the wire payloads and delta response shapes live
  in `Protocol/SemanticTokens/`.

## Installation

```sh
dotnet add package Nickelony.LanguageServer.Client
```

## Usage

```csharp
using Microsoft.Extensions.Logging;
using Nickelony.LanguageServer.Client;

var client = new LanguageServerClient(
    workspaceRootDirectoryPaths: [@"C:\my\workspace"],
    serverExecutablePath: @"C:\tools\example-language-server\example-language-server.exe",
    options: new LanguageServerClientOptions(settingsProvider: () => new { }),
    logger: loggerFactory.CreateLogger<LanguageServerClient>());

client.DiagnosticsPublished += (_, eventArgs) =>
{
    // Diagnostics for a tracked document were published by the server;
    // eventArgs.Parameters carries the payload. May be raised on a background
    // thread - marshal to your UI thread.
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
    ServerArguments = ["--log-level=warn"],
    ServerWorkingDirectory = @"C:\my\workspace",   // default: the executable's directory
    EnvironmentVariables = new Dictionary<string, string> { ["EXAMPLE_SERVER_HOME"] = @"C:\tools\example-server" },
};
```

`settingsProvider` is optional: omit the constructor argument (or pass `null`) to push an empty
settings payload.

Host-side utilities: `DocumentOperationScheduler.EnqueueGlobalAsync` and
`DocumentOperationScheduler.WaitForPerDocumentOperationsAsync` provide ordering and quiescence points for
operations your host schedules against the client, and `TryMarkTransportUnhealthy` (with
`TransportUnavailable`) is the generation-safe way to invalidate a transport you observed failing.

> **Tip:** `LanguageServerClient` is a low-level building block. If you're looking for
> turnkey IntelliSense in your editor, prefer the
> [`Nickelony.LanguageServer.Lua`](https://www.nuget.org/packages/Nickelony.LanguageServer.Lua)
> provider, which implements the editor contracts from
> [`Nickelony.LanguageServer.Abstractions`](https://www.nuget.org/packages/Nickelony.LanguageServer.Abstractions)
> on top of this client.

## Dependencies

- `StreamJsonRpc` 2.25.29
- `Microsoft.Extensions.Logging.Abstractions` 8.0.3
- `Nickelony.IDEKit.Core` - used by `TrackedDocumentStore` for incremental text edits, by the
  `Interop/` bridges (protocol ranges, semantic-token line maps), by `SemanticTokensDecoder`, and by
  `LanguageServerPaths` for the shared local-path comparison policy.
- `Nickelony.IDEKit.IntelliSense` - supplies the shared offset-based `TextSemanticToken` payload that
  `SemanticTokenConversion` bridges to, plus the `TextCompletionItemKind`/`TextDocumentSymbolKind`
  models behind the kind bridges.

Package boundary: the two IDEKit packages are the package's only external stack dependencies beyond
StreamJsonRpc and logging. A protocol-only host that does not use IDEKit still takes them
transitively; a future split of the `Interop/` slice into a separate bridge package is recorded as a
backlog item and ships only when a second, non-IDEKit host exists.

## Limitations

- Workspace folders are fixed at construction time: the roots list may be empty (a folderless session
  sends `rootUri: null` and `workspaceFolders: null`); entries are validated (null, empty, whitespace-only,
  and duplicate entries are rejected; duplicates are detected by comparing normalized paths with the
  configured local-path identity), and every entry is advertised through `workspace/workspaceFolders`
  with the first entry as the primary `rootUri`. The client does not send
  `workspace/didChangeWorkspaceFolders`.
- Positions are UTF-16 code units: the client advertises `general.positionEncodings: ["utf-16"]` and
  fails the transport startup when a server selects another encoding.
- The client is configured by default (`LanguageServerClientOptions.RequireTextDocumentSynchronization`)
  to reject servers that do not advertise full or incremental `textDocumentSync`; set the option to
  `false` to accept servers without text synchronization.
- Dynamic registration is deliberately not supported: the server-callback target logs and ignores
  `client/registerCapability` and `client/unregisterCapability`, and the initialize payload forces
  `dynamicRegistration = false` on every declared `workspace`/`textDocument` capability.
- Child-process lifetime binding uses a Windows job object; on other platforms the client relies on
  the graceful `shutdown`/`exit` teardown and forced termination instead.
- `workspace/configuration` request items are answered from the single global settings snapshot;
  a server-requested `scopeUri` for per-resource configuration is not honored.
- `WorkspaceFileWatcher` uses one `FileSystemWatcher` per watch specification with a 64 KiB buffer;
  watch filters follow the runtime's simple wildcard grammar on every platform (`*.*` and an empty
  filter are normalized to `*`), and empty or whitespace-only filters, directory aliases (`.` and
  `..`), rooted patterns, and patterns containing directory separators are rejected.
- `WorkspaceFileWatcher` retries a failed dispatch with bounded exponential backoff, then stops
  watching, drops the pending changes, and notifies the owner once; disposal is non-blocking and
  drops undelivered changes, so the recovery path is a replacement watcher whose missed changes are
  reconciled through `WorkspaceSnapshotTracker`.
- The server process and its stdio transport are fixed; there is no custom launcher or in-process/
  socket transport seam. Hosts that own the server process would have to re-implement the handshake.
- Completion snippets stay raw: the client forwards `insertTextFormat` and the snippet text
  untouched, so tabstop expansion stays the consuming host's commit-time concern.
- The settings provider is optional; without one the client pushes an empty
  `workspace/didChangeConfiguration` payload and answers `workspace/configuration` requests from an
  empty snapshot.

## License

MIT © 2026 Kewin Kupilas.

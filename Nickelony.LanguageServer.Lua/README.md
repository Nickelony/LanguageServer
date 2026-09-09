# Nickelony.LanguageServer.Lua

**A lightweight Lua language-server provider** for the [Nickelony Language Server](https://github.com/Nickelony/LanguageServer) family, backed by [LuaLS](https://github.com/LuaLS/lua-language-server) (`lua-language-server` executable).

[![NuGet](https://img.shields.io/nuget/v/Nickelony.LanguageServer.Lua.svg)](https://www.nuget.org/packages/Nickelony.LanguageServer.Lua)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Nickelony/LanguageServer/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

This package turns [LuaLS](https://github.com/LuaLS/lua-language-server) into a drop-in
IntelliSense provider for editor-class applications. It implements the language-service contracts
from [`Nickelony.LanguageServer.Abstractions`](https://www.nuget.org/packages/Nickelony.LanguageServer.Abstractions)
on top of the [`Nickelony.LanguageServer.Client`](https://www.nuget.org/packages/Nickelony.LanguageServer.Client)
LSP client, through the language-neutral
[`Nickelony.LanguageServer.Provider`](https://www.nuget.org/packages/Nickelony.LanguageServer.Provider)
framework. This package supplies the LuaLS-specific hooks and mapping. Construct the provider,
synchronize documents, and consume the callbacks.

## Features

- **Completion** - contextual item lists with a protocol-faithful kind mapping, the protocol
  `sortText`/`preselect` fields, `Priority` ordering hints that agree with the protocol `sortText`
  order, and `completionItem/resolve` support. Snippet insert texts (`insertTextFormat: 2`) pass
  through with their raw placeholder syntax and the shared `TextCompletionInsertTextFormat` marker,
  so a host expands `$1`/`${1:default}`/`${1|a,b|}` tabstops at commit time.
- **Diagnostics** - publish diagnostics per document with their server messages (trimmed; a blank
  message becomes `Unknown Lua diagnostic.`) and the protocol `source`/`code` attribution preserved,
  delivered through `DiagnosticsUpdated` and cached for on-demand reads. A diagnostic without a
  severity is presented as an error and an unknown severity as a warning (the protocol defines no
  severity default). Diagnostics are only published
  for documents the provider currently tracks; a document that was never synchronized has no content to
  map server ranges against, so its published diagnostics are dropped.
- **Hover** - rich hover content with markdown support.
- **Navigation** - go-to-definition and find-references.
- **Document symbols** - the hierarchical outline tree for a document (names, kinds, full and
  selection ranges, nested children) mapped onto the shared offset-based `TextDocumentSymbol`
  model. The client advertises `hierarchicalDocumentSymbolSupport`, so LuaLS normally answers with
  the hierarchical form; the flat `SymbolInformation` fallback is still handled.
  Document symbols are best-effort: a server that does not advertise the capability yields an
  empty outline without a request.
- **Code actions** - quick fixes and refactorings for a document range mapped into
  `TextCodeAction` values with their workspace edits; the request context carries the cached
  diagnostics reported for the range, so LuaLS's diagnostic quick fixes stay reachable, and
  command-only actions are omitted because the client does not support
  `workspace/executeCommand`. The client advertises `codeActionLiteralSupport`, so LuaLS answers
  with the literal action shape.
- **Rename & formatting** - symbol rename with workspace edits, and document formatting
  respecting LuaLS settings.
- **Signature help** - parameter info for function calls.
- **Semantic tokens** - shared `SemanticToken` values decoded from the server's full token stream and
  announced through `SemanticTokensUpdated`, with `GetSemanticTokens` as the pull-side read of the same
  cache. Refresh runs after every successful open or edit synchronization and whenever LuaLS requests
  it (`workspace/semanticTokens/refresh`, which the client advertises support for); request-driven
  synchronization does not issue an extra refresh. A failed refresh keeps the previously cached
  tokens. LuaLS does not serve delta requests, so the provider requests full payloads only.
- **Configurable settings** - override the LuaLS runtime version, library folders, disabled
  diagnostics, and semantic highlighting through `LuaLanguageServerOptions`.
  The defaults mirror the LuaLS defaults; two deliberate deviations keep the provider
  self-contained: third-party checks are disabled because a library cannot answer interactive
  prompts, and completion call snippets are enabled with `Replace` because the provider consumes
  snippet insert texts end to end. Library folders that live in the workspace can be
  declared through the workspace's `.luarc.json` (`workspace.library`); the provider watches the
  configuration file and asks LuaLS to reload it when it changes, so a folder that appears later
  is picked up automatically.
- **Workspace coordination** - tracks open documents, watches every configured workspace root
  for external changes, and re-opens tracked documents when the server restarts. A watcher
  failure is contained to its own root.

## Requirements

- .NET 8 (package targets `net8.0`, cross-platform).
- The **LuaLS executable** at runtime - download the `lua-language-server` binary from the
  [LuaLS releases page](https://github.com/LuaLS/lua-language-server/releases) and pass its path
  to the provider. Pass `serverExecutablePath: null` when no installation is available: the provider
  still constructs, reports a persistent startup failure, and every call returns its documented
  fallback value until the provider is recreated with a valid path.

## Installation

```sh
dotnet add package Nickelony.LanguageServer.Lua
```

## Usage

```csharp
using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.IntelliSense.Signatures;
using Nickelony.LanguageServer.Abstractions;
using Nickelony.LanguageServer.Lua;

// Use any absolute local path; `/home/user/workspace` on Linux/macOS is as valid as
// `C:\my\workspace` on Windows.
var workspaceRoot = @"C:\my\workspace";
var serverExecutablePath = @"C:\tools\lua-language-server\lua-language-server.exe";

var provider = new LuaLanguageServerIntelliSenseProvider(
    workspaceRootDirectoryPaths: [workspaceRoot],
    serverExecutablePath: serverExecutablePath,
    logger: loggerFactory.CreateLogger<LuaLanguageServerIntelliSenseProvider>());

// Provider settings are optional; pass an options instance to override the LuaLS defaults
// (runtime version, library folders, disabled diagnostics, semantic highlighting):
var configuredProvider = new LuaLanguageServerIntelliSenseProvider(
    workspaceRootDirectoryPaths: [workspaceRoot],
    serverExecutablePath: serverExecutablePath,
    options: new LuaLanguageServerOptions
    {
        RuntimeVersion = "LuaJIT",
        AdditionalLibraryDirectories = [Path.Combine(workspaceRoot, "vendor")],
        DisabledDiagnostics = ["undefined-global"]
    },
    logger: loggerFactory.CreateLogger<LuaLanguageServerIntelliSenseProvider>());

// Callbacks may fire on background threads - marshal to your application's synchronization
// context (if any) before touching UI objects.
provider.DiagnosticsUpdated += (_, eventArgs) =>
{
    // e.g. update the diagnostic markers for `eventArgs.FilePath`.
};

provider.SemanticTokensUpdated += (_, eventArgs) =>
{
    // e.g. render the tokens for `eventArgs.FilePath`.
};

// Track a document as the user edits it.
string filePath = Path.Combine(workspaceRoot, "main.lua");
provider.OpenDocument(filePath, sourceText);

// Then drive IntelliSense from your command handlers:
var completions = await provider.GetCompletionItemsAsync(
    filePath, sourceText, line, column, triggerCharacter: '.');
var hover      = await provider.GetHoverAsync(filePath, sourceText, line, column);
var definition = await provider.GetDefinitionAsync(filePath, sourceText, line, column);
// The optional trigger context forwards how the request was triggered (and the shown payload on
// retriggers); omit it for a position-only request.
var signatures = await provider.GetSignatureHelpAsync(
    filePath, sourceText, line, column,
    new TextSignatureHelpContext(TextSignatureHelpTriggerKind.TriggerCharacter, "("));
var references = await provider.GetReferencesAsync(new TextReferenceRequest(filePath, sourceText, line, column));
var edits      = await provider.RenameSymbolAsync(new TextRenameRequest(filePath, sourceText, line, column, "newName"));
var formatted  = await provider.FormatDocumentAsync(new TextFormatRequest(
    filePath,
    sourceText,
    new TextFormattingOptions(tabSize: 4, insertSpaces: true)));

provider.UpdateDocument(filePath, updatedSourceText);
provider.CloseDocument(filePath);
provider.Dispose();
```

For the complete construction, disposal, threading, document-reference, and cancellation
contract, see the repository's [consumer integration guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/ConsumerIntegration.md).

> **Note:** every `*Async` IntelliSense method carries the current document text (directly or in its
> request record), so you can drive them from a live document buffer without waiting for server
> round-trips of edits.

> **Document contract:** every document API takes a local file-system path (a relative path is
> anchored to the current process working directory). A path that cannot be normalized to a local
> file is ignored - the call is a no-op that returns its empty or `null` fallback, while a
> `null` path argument throws `ArgumentNullException` - and untitled or purely virtual documents
> are not supported because the language server operates on on-disk files. The `Supports*`
> capability flags become `true` after a ready session has negotiated the capability and revert
> when the transport is lost; IntelliSense requests, document opens, and document updates start the
> server lazily, while calls that do not need it (such as closing or renaming a tracked document) do not.

## Limitations

- Untitled or purely virtual documents are unsupported; all document APIs take local file paths,
  and relative paths are anchored to the current process working directory.
- Diagnostics are consumed from server pushes (`textDocument/publishDiagnostics`); a pull-only
  server does not fit the client. Diagnostics for documents that were never synchronized are
  dropped because there is no content to map server ranges against.
- Positions are fixed to UTF-16 code units; the client does not negotiate another position encoding.
- Dynamic registration is unsupported by design: the client advertises `dynamicRegistration = false`
  and ignores `client/registerCapability` notifications.
- Semantic tokens are full-payload only (LuaLS serves no delta requests); a refresh carries the whole
  token set for a document.
- Semantic tokens cached for a document reflect the version they were decoded for and can be briefly
  stale while a newer refresh is in flight; `SemanticTokensUpdated` announces each new set.
- Each workspace root gets its own watcher; a watcher failure is contained to that root and reported
  through `WorkspaceWatcherFailed`.
- Configuration watching covers `.luarc.json`/`.luarc.jsonc` files in each workspace **root only**;
  a custom configuration file supplied through LuaLS's `--configpath` flag is not watched, and the
  provider cannot pass the flag (it exposes no server-argument surface).
- Completion call snippets are requested with `callSnippet = "Replace"`; snippet items carry
  `insertTextFormat: 2` and hosts are expected to expand the `$1`/`${1:default}` tabstops at commit
  time, otherwise the raw placeholder syntax is inserted.
- Workspace folders are fixed at construction; `workspace/didChangeWorkspaceFolders` is not sent.
- A rename edit that contains resource operations (file create, rename, or delete) is dropped whole
  instead of being applied partially: the shared workspace-edit model represents text edits only.

## Dependencies

- `Nickelony.LanguageServer.Abstractions`
- `Nickelony.LanguageServer.Client`
- `Nickelony.LanguageServer.Provider`
- `Nickelony.IDEKit.IntelliSense`
- `Nickelony.IDEKit.Core`
- `Microsoft.Extensions.Logging.Abstractions` 8.0.3

`StreamJsonRpc` and the remaining client transport dependencies arrive transitively through
`Nickelony.LanguageServer.Client`.

## License

MIT © 2026 Kewin Kupilas.

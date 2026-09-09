# Nickelony.LanguageServer.Provider

**The language-neutral provider framework** of the [Nickelony Language Server](https://github.com/Nickelony/LanguageServer) family. It supplies the lifecycle, document synchronization, workspace watching, and request machinery that a language-server provider package builds on, so a new provider implements its language hooks instead of re-implementing the orchestration.

[![NuGet](https://img.shields.io/nuget/v/Nickelony.LanguageServer.Provider.svg)](https://www.nuget.org/packages/Nickelony.LanguageServer.Provider)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Nickelony/LanguageServer/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

This package is the **provider framework** of the family. It sits on the two language-server packages -
[`Nickelony.LanguageServer.Abstractions`](https://www.nuget.org/packages/Nickelony.LanguageServer.Abstractions)
(the editor-facing contracts) and
[`Nickelony.LanguageServer.Client`](https://www.nuget.org/packages/Nickelony.LanguageServer.Client)
(the LSP transport and shared primitives) - and directly references the
`Nickelony.IDEKit.IntelliSense` model the language packages implement against. A language package derives from
`LanguageServerIntelliSenseProviderBase<TDocumentState>` and implements the language-specific
hooks - watch specifications, configuration rule, settings payload, failure reports, diagnostics
handling, response parsing, and the feature members. Hosts consume the resulting concrete
provider through `ILanguageServerIntelliSenseProvider`.

## Features

- **Provider base class** - `LanguageServerIntelliSenseProviderBase<TDocumentState>` implements
  `ILanguageServerIntelliSenseProvider` end to end: availability and state, the lifecycle events,
  the document lifecycle (`OpenDocument`/`UpdateDocument`/`CloseDocument`/`RenameDocument`), the
  cached diagnostics reads, the request scaffolding behind the abstract feature members, and
  shared synchronous/asynchronous disposal.
- **Lazy startup and recovery** - one serialized startup path with fast-path re-entry, restart
  shielding, resumable tracked-document reopen after a restart, transport-generation fencing, and
  the hard-failure latch after consecutive startup failures.
- **Document synchronization** - full-content `didOpen`/`didClose` with full-or-incremental
  `didChange`, driven through the per-document scheduler and the version-fenced tracked-document
  store, with idle-document trimming that closes each evicted document through its own scheduler
  slot, rename rekeying, and per-document invalidation.
- **Workspace watching** - one watch scope per workspace root (watcher, snapshot tracker,
  recovery state, failure latch), ownership-based forwarding (each path is forwarded exactly once
  when roots nest), buffered replay of recoverable gaps, and configuration-file refresh through
  the language's settings payload.
- **Request pipeline** - per-request timeout with transport-restart hysteresis, capability
  gating before document synchronization, request dispatch after synchronization,
  caller-cancellation pass-through, and the documented fallback value for every failure mode -
  timeouts, transport loss, disposal, and server rejections (which never restart the transport).
- **Request and file-change helpers** - `LanguageServerRequestDispatcher` (per-request timeout,
  transport-generation fencing, a single retry after a transport boundary, timeout-driven restart
  hysteresis) and `WorkspaceFileChangeForwarder` (buffered replay of recoverable delivery gaps
  behind an ensure-started gate) are usable directly by a provider that composes its own
  orchestration. Disposing the forwarder drops changed batches that were not delivered yet; a
  host that composes its own orchestration reconciles missed changes through its workspace
  snapshot.
- **Tunable policy** - `LanguageServerProviderOptions` carries the request timeout, the
  timeout-restart threshold, the hard startup-failure threshold, and the tracked-idle-document
  cap, with the family defaults baked in.
- **Hooks, not forks** - the language package supplies its display name and language id, watch
  specifications, configuration-path rule, settings payload, failure reports, diagnostics
  handling, and feature members, and can refine behavior through the virtual hooks
  (`OnDocumentSynchronizedAsync`, `OnTrackedDocumentInvalidated`, `OnDocumentRenamed`,
  `OnDisposing`) or substitute watcher creation through `CreateWorkspaceFileWatcher`.
  [`Nickelony.LanguageServer.Lua`](https://www.nuget.org/packages/Nickelony.LanguageServer.Lua)
  is the reference implementation; [`docs/ProviderAuthoring.md`](https://github.com/Nickelony/LanguageServer/blob/main/docs/ProviderAuthoring.md)
  describes the authoring model end to end.

## Dependencies

- `Nickelony.LanguageServer.Abstractions`
- `Nickelony.LanguageServer.Client`
- `Nickelony.IDEKit.IntelliSense`
- `Microsoft.Extensions.Logging.Abstractions` 8.0.3

`StreamJsonRpc` and the remaining client transport dependencies arrive transitively through
`Nickelony.LanguageServer.Client`; the `Nickelony.IDEKit.Core` types used by the IntelliSense model
arrive transitively through `Nickelony.IDEKit.IntelliSense`.

## License

MIT © 2026 Kewin Kupilas.

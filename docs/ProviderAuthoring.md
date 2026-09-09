# Provider authoring guide

This guide is for implementing a language provider package on the Nickelony Language Server
family. It is the provider-side counterpart of the host-facing
[consumer integration guide](ConsumerIntegration.md). The language-neutral framework lives in the
`Nickelony.LanguageServer.Provider` package: a provider package derives from
`LanguageServerIntelliSenseProviderBase<TDocumentState>` and implements the language-specific
hooks. `Nickelony.LanguageServer.Lua` (the LuaLS-backed provider) is the verified reference
implementation of those hooks; every recipe step and trap below matches how that package
implements it.

## Package shape

A provider package sits on three framework packages:

- `Nickelony.LanguageServer.Provider` - the provider framework: the provider base class
  (`LanguageServerIntelliSenseProviderBase<TDocumentState>`), the tunables
  (`LanguageServerProviderOptions`), request dispatch with timeout and restart policy
  (`LanguageServerRequestDispatcher`), file-change forwarding with buffered replay
  (`WorkspaceFileChangeForwarder`), and the internal lifecycle, document-synchronization,
  workspace-watching, and request machinery behind the language hooks.
- `Nickelony.LanguageServer.Abstractions` - the editor-facing contracts: the provider interface
  (`ILanguageServerIntelliSenseProvider`), the lifecycle state enum
  (`LanguageServerProviderState`), the startup-failure and watcher-failure payloads, and the
  lifecycle rules that provider authors must honor.
- `Nickelony.LanguageServer.Client` - the LSP transport and the shared provider framework
  primitives: process launch and JSON-RPC hosting, the initialize handshake, the capability
  snapshot, the workspace watch primitives (`WorkspaceFileWatcher`, `WorkspaceSnapshotTracker`),
  the path helpers (`LanguageServerPaths`), the protocol payload folders, and the tracked-document
  store framework (`TrackedDocumentStore<TState>`, `TrackedDocumentState`).

The framework packages depend only on the IDEKit packages; `Client` deliberately has **no**
`Abstractions` edge (the Provider package bridges the two). A provider package references the
framework and owns:

- the composition root: one public provider class deriving from
  `LanguageServerIntelliSenseProviderBase<TDocumentState>` and implementing the base contract plus
  the abstract feature members,
- the hook implementations: display name, language id, watch specifications, configuration-file
  rule, settings payload, startup-failure reports, diagnostics handling/retrieval, and the
  virtual extension points (`OnDocumentSynchronizedAsync`, `OnTrackedDocumentInvalidated`,
  `OnDocumentRenamed`, `OnDisposing`),
- the language-specific contract additions, if any (Lua adds semantic tokens through
  `ILuaLanguageServerIntelliSenseProvider` rather than widening the base contract),
- the option type (Lua: `LuaLanguageServerOptions`) and language-specific event payloads,
- the client construction with its handshake payload factories (client capabilities,
  initialization options, settings) and the workspace conventions (watched file patterns,
  configuration-file refresh rule) fed into the hooks,
- the mapping slices: one parser partial per feature plus one provider partial per feature.

Everything else stays internal to the framework. The Lua package's public surface is exactly five
types: the provider (`LuaLanguageServerIntelliSenseProvider`), the language contract addition
(`ILuaLanguageServerIntelliSenseProvider`), the language option type (`LuaLanguageServerOptions`), the
language event payload (`SemanticTokensUpdatedEventArgs`), and the tracked-document state
(`LuaDocumentState`, public because it parameterizes the public generic base; every member of it
remains internal). Keep a new provider package that small and raise members to public only when
a host needs them.

## Recipe

### 1. Contract

Derive from `LanguageServerIntelliSenseProviderBase<TDocumentState>` (where `TDocumentState` is
the language's tracked-document state). The base implements `ILanguageServerIntelliSenseProvider`
end to end: the lifecycle surface (`IsAvailable`, `State`), the payload events
(`DiagnosticsUpdated`, `CapabilitiesChanged`, `StartupFailed`, `WorkspaceWatcherFailed`), the
document lifecycle members (`OpenDocument`, `UpdateDocument`, `CloseDocument`, `RenameDocument`),
the cached reads (`GetDiagnostics`), and the request scaffolding. The language class implements
the abstract feature members (completion, hover, definition, references, rename, formatting,
signature help, document symbols, and code actions) and any language
contract additions (Lua: `ILuaLanguageServerIntelliSenseProvider`). Each request method accepts the current
buffer text and a `CancellationToken`; the sync surface returns owned snapshots.

Several hooks run during base construction (`ProviderDisplayName`, `LanguageId`,
`CreateWatchSpecifications`, `CreateTrackedDocumentStore`), so their implementations must not
depend on derived instance state, and derived constructors must not pass state into them through
fields. The remaining hooks run lazily on the calling thread after construction completed.

### 2. Client construction and handshake

Build one `LanguageServerClient` per provider and pass it to the base constructor, which takes
ownership of it (the client is disposed with the provider):

- Pass the normalized workspace roots as a list. The first entry is the primary root
  (`rootUri`); every entry is advertised through `workspace/workspaceFolders` and answered by
  the `workspace/workspaceFolders` callback.
- Pass the server executable path; `null` is a supported configuration that reports a
  persistent `StartupFailed` instead of throwing.
- Configure `LanguageServerClientOptions`: `SettingsProvider` (a `Func<object>` that the client
  caches for `workspace/configuration` answers and refreshes through
  `workspace/didChangeConfiguration`), plus `ClientCapabilitiesProvider` and
  `InitializationOptionsProvider` (both `Func<IReadOnlyList<string>, object?>` receiving the
  normalized roots in caller order). `ServerArguments`, `EnvironmentVariables`, and the
  `InitializeTimeout`/`ShutdownRequestTimeout`/`DisposeWaitTimeout` budgets cover the process
  launch.
- Build the capability and initialization payloads in dedicated factories so tests can pin
  them (Lua: `LuaLanguageServerClientCapabilitiesFactory`,
  `LuaLanguageServerInitializationOptionsFactory`).

The payload factories may run on background transport threads; keep them thread-safe,
non-blocking, and cheap.

### 3. Lazy startup and recovery

The base never starts the server from the constructor; it starts on demand - first request,
first document open/update, first workspace-change forwarding - through one serialized startup path with a fast path
for an already-healthy client, a startup lock that makes concurrent callers share one recovery
flow, tracked-document reopen after a transport restart, and consecutive-failure counting with
the hard failure threshold from `LanguageServerProviderOptions`. The language package supplies
the failure reports (`CreateStartupFailure` for the transient/permanent split,
`CreateMissingClientFailure` for the unconfigured-client configuration) and tunes the threshold
through the options; it otherwise inherits the flow.

### 4. Document synchronization

The base maps the document lifecycle onto `textDocument/didOpen`, `textDocument/didChange`, and
`textDocument/didClose`, driven through the Client's `DocumentOperationScheduler` (per-document
serialization, latest-update coalescing, exclusive rename slots) and the language's tracked
store (`CreateTrackedDocumentStore`). The language package customizes through hooks:

- `LanguageId` for the `didOpen` language identifier (Lua sends `"lua"`).
- `OnDocumentSynchronizedAsync` for post-sync refresh work (Lua: semantic tokens); it is
  deliberately not invoked on request-driven synchronization, so each IntelliSense request does
  not issue an extra request.
- `OnTrackedDocumentInvalidated` for language state that must follow close, rename, idle
  trimming, and synchronization invalidation.
- `InvalidateTrackedDocumentSynchronization` and `GetTrackedDiagnostics` to bridge the base's
  generic store operations onto the language store (Lua: `LuaDocumentStore`).
- `HandleDiagnosticsPayload` to parse and cache a published payload for a tracked document;
  return `null` to drop it (unparsable or stale payloads).

The base chooses the change payload from the negotiated `TextDocumentSyncKind` (incremental when
accepted, full otherwise; a server that accepts neither fails loudly at that point under the
default client configuration, and under a client that does not require synchronization a change
that cannot be expressed is skipped with a warning while the next session re-establishes the full
content), keeps request-driven synchronization from triggering refreshes, and degrades to the
documented request fallback when a synchronization send fails.

### 5. Workspace watching

The base mirrors external workspace changes to the server with the Client primitives - one
watch scope per root (watcher, snapshot tracker, recovery state, failure latch, so a failure is
contained to its root and the failure message can name it), a shared
`WorkspaceFileChangeForwarder` buffering recoverable gaps and replaying them once the server is
ready again, ownership-based forwarding when roots nest (a path is normally forwarded only by the
scope whose root is its longest prefix, so overlapping watchers produce one notification; a root
whose own watcher is inactive stays covered by the delivering watcher), and snapshot
reconciliation after watcher recovery (changes that cannot be replayed unambiguously are dropped
rather than approximated).

The language package supplies:

- `CreateWatchSpecifications` - the file patterns to watch (Lua: `*.lua` recursively plus
  `.luarc.*` in the workspace root). Patterns use the `FileSystemWatcher` filter grammar,
  are matched against file names, and follow the platform's file-name casing behavior.
  Returning an empty list disables workspace watching for the provider, so a host that
  bridges file events itself can opt out without faking a pattern.
- `IsConfigurationPath` - the configuration-file predicate that triggers a settings refresh
  before the watched-files notification is forwarded.
- `CreateSettingsPayload` - the payload for the resulting `workspace/didChangeConfiguration`
  notification.

### 6. Feature slices

For every feature, keep the same three-piece slice:

1. A protocol payload family under `Nickelony.LanguageServer.Client/Protocol/` when the wire
   shape is not already modeled. Converters are tolerant by policy: malformed elements are
   skipped with a warning, missing optional pieces degrade instead of failing the whole
   response, and open protocol numerics (kinds, severities) stay representable for the provider
   to map.
2. A parser partial that maps payloads to the shared IDEKit models. Convert protocol ranges
   through the document line map; never approximate a malformed edit (a range that cannot be
   mapped is dropped, not turned into a deletion).
3. A provider partial implementing the base's abstract feature member: confirm the server
   capability, issue the request through the inherited pipeline
   (`SendDocumentRequestAsync`/`SendDocumentPositionRequestAsync`), and map the response with the parser.

### 7. Capability flags

Read the negotiated server capabilities through the inherited `Client` accessor and surface
them as `Supports*` properties (`IsAvailable && Client.Supports*`). Gate each request at dispatch
time with the client flag - the pipeline takes a `supportsRequest` delegate - and return the
feature's documented empty/fallback value when the gate fails. Lua surfaces
`SupportsReferences`, `SupportsRename`, and `SupportsFormatting` publicly (they change UI
affordances) and gates document symbols and code actions inside the pipeline. A request that is
not gated must still degrade through the fallback when the server rejects it.

### 8. Callbacks and disposal

The base owns the admission discipline and the disposal sequence: registration goes through the
admitted add path (`AddAdmittedCallback`), raises go through the subscriber enumerator that
checks admission per handler and isolates throwing handlers (logged; later subscribers still
run), fire-and-forget work goes through the background observer (`ObserveBackgroundTask`), and
disposal closes callback admission, cancels the disposal token, disposes the workspace
coordinator (watchers and forwarding buffer), cancels queued document work, and disposes the
client, idempotently. The language package:

- raises its own language events through `RaiseSubscribers`,
- overrides `OnDisposing` for language-owned teardown (Lua: detach the server's semantic-tokens
  refresh notification and cancel queued token requests),
- keeps the base's deliberate choice not to dispose the dispose token source (request paths may
  still link timeout tokens against it; see the trap below).

## Traps (learned on the reference implementation)

### Path normalization contract

- Normalize every host-supplied path at the public boundary with
  `LanguageServerPaths.TryNormalizeLocalPath`; an unusable path is a no-op or empty result,
  never a throw. Work with normalized paths internally (dictionary keys, routing, comparisons).
- Convert to protocol URIs with `LanguageServerPaths.CreateFileUri(normalizedPath)` and back
  with `TryGetLocalPath`; compare with `LanguageServerPaths.AreLocalPathsEqual` /
  `LanguageServerPaths.LocalPathComparer`, which follow the platform policy (Windows and macOS
  are case-insensitive).
- Document identity is the normalized path end to end. There is no document-to-root routing
  layer; roots only shape the initialize payload and the watch scopes.
- A case-only rename refers to the same document on case-insensitive file systems; the provider
  must treat it as a no-op instead of closing and reopening.

### Zero-based coordinates and UTF-16

- Protocol positions are zero-based line/character pairs, and characters are UTF-16 code units
  (LSP's fixed encoding; the client never negotiates another one).
- The provider model exchanges document offsets or `TextPosition` pairs; convert through
  `TextLineMap` / `ProtocolRangeConversion` and let the conversion clamp out-of-document
  coordinates instead of failing.
- Negative inputs are representable in payloads and clamped at the provider boundary
  (`SendDocumentPositionRequestAsync` clamps line and column to zero).
- Reversed ranges cannot be converted; drop them (and their containing edit/entry) rather than
  guessing, and keep the protocol numeric accessible so the mapping layer chooses the fallback.

### Snapshot ownership

- Every payload a provider publishes or caches is an owned immutable snapshot detached from
  internal state; consumers must never observe a later mutation through an earlier event.
- Tracked documents are content plus version; caches are version-fenced, so a payload parsed
  against an older snapshot cannot overwrite newer state. Diagnostics for documents the provider
  has never synchronized are dropped: without tracked content, server ranges cannot be mapped.

### Threading, callback admission, and disposal

- Callbacks may arrive on background threads. Handlers run serially for one invocation, and a
  throwing handler does not prevent later subscribers from being notified.
- Admission closes before disposal releases resources; registration after admission closes is
  ignored, and raising checks admission again per handler.
- The dispose token source is deliberately never disposed by the base:
  request paths may still link timeout tokens against it during disposal, and linking against
  the token of a disposed source throws `ObjectDisposedException` instead of surfacing the
  documented fallback value.

### Lazy startup and capability flags

- The startup state object owns the state transitions, the failure counters, and the transport
  generation fencing that makes stale transport events harmless; transitions are ignored once
  disposal starts, and the disposed state is terminal.
- Capability values are valid for one transport generation: read them through the client
  (never cache them across restarts), and raise `CapabilitiesChanged` whenever a restart or
  transport loss may have changed them. Hosts re-read `IsAvailable` and the `Supports*`
  properties on that event.
- `IsAvailable` is a projection: ready session, not disposing, client ready, startup succeeded.

### Generation invariants

Three owners fence state by transport generation, and a change to one must keep the others
consistent:

- the provider's startup state (`LanguageServerStartupState`) accepts a completed startup only
  for the generation that is still ready and rejects unavailability notifications for
  generations before it - this is the provider-state fence;
- the client's capability store publishes one immutable snapshot per generation and gates
  request results and server callbacks on the owning session still being current - this is the
  capability fence;
- the request dispatcher tracks consecutive timeouts per generation so a stale timeout cannot
  mark a replacement transport unhealthy - this is the timeout fence.

The rule they share: act on a generation only after observing that it is still the current one,
and never let an event for generation N affect generation N+1.

### Request dispatch and timeout policy

- Route feature requests through the shared dispatcher and the base's document-request
  pipeline: ensure the transport is started (capability values only exist after the
  initialize handshake), check the capability gate, acquire a temporary request reference,
  synchronize the document, send with the per-request timeout, parse, and release the
  reference in `finally` (so caller cancellation cannot leak the reference). The release is
  bound to the record itself (`DocumentRequestReference`), so a rename that rekeys the record
  cannot strand the reference. An unsupported request therefore produces no document traffic
  at all.
- Caller cancellation is the only `OperationCanceledException` that escapes to the caller
  (rethrow when the caller's token fired). Provider disposal, provider timeouts, internal
  transport failure, server rejections, and unsupported capabilities produce the documented
  fallback value instead.
- The framework defaults (inherited by Lua): 10 seconds per request, and 2 consecutive request
  timeouts mark the transport unhealthy so the next request restarts it; a successful start, and
  a server rejection, reset the tracking.

### Document reference semantics

- One `OpenDocument` call acquires one editor-open reference; pair each with one
  `CloseDocument` call, so repeated opens require repeated closes. `UpdateDocument` acquires no
  reference.
- Request paths acquire a temporary request reference and release it after completion; the
  reference is released by identity (`DocumentRequestReference`), so it still finds the record
  when a rename rekeyed it. Idle documents are trimmed beyond the configured cap
  (`LanguageServerProviderOptions.MaxTrackedIdleDocuments`; Lua: 16) with a `didClose`,
  while editor-open documents are never trimmed. A close that was deferred because a request
  reference was still active completes when that reference is released.
- Forward `didClose` only when a server session is running: starting a server just to close a
  document is wasteful and can race disposal. Closes are delivered through the document's own
  scheduler slot with a tracked-state recheck, so a close that arrives during a restart replay
  still closes a document the replay reopened instead of leaving a phantom server-open copy.

### Test strategy

- Unit slices: a fake `ILanguageServerClient` that captures notifications and answers scripted
  requests (the Client's event and dispatch surface is small enough to fake completely), an
  `InternalsVisibleTo` test-access bridge for provider internals, and polling helpers for
  callback admission/disposal timing. Pin handshake factories with payload tests and parsers
  with raw-JSON tests per feature.
- Real-server door: an `Integration/` suite that exercises the actual server executable behind
  an environment-configured archive path and reports inconclusive when no server is configured.
  Use it for emission questions the fake cannot answer (LuaLS: hierarchical document symbols),
  keep the suite spec-first, and keep the fakes authoritative for behavior.

## Server compatibility constraints

The client makes five fixed choices that a candidate language server must fit (the full
language-#2 kickoff checklist is recorded in `future-backlog-2026-09-17.md`):

- The server process working directory defaults to the executable's folder; set
  `LanguageServerClientOptions.ServerWorkingDirectory` to pin another directory (for example the
  workspace root).
- Positions are zero-based and fixed to UTF-16 code units.
- Diagnostics are push-only (`textDocument/publishDiagnostics`); pull diagnostics
  (`textDocument/diagnostic`) are not implemented.
- Dynamic registration is unsupported by design: the client advertises
  `dynamicRegistration = false` and logs and ignores `client/registerCapability` /
  `client/unregisterCapability`.
- Multi-root is fixed at construction: all roots are advertised and watched, one watch scope
  per root, and `workspace/didChangeWorkspaceFolders` is not sent.

# Consumer integration guide

This guide describes the lifecycle and threading contract a host should follow when
consuming the provider package.

## Construction and disposal

Construct the provider during host setup, subscribe to callbacks before opening documents,
and dispose it during host shutdown. The provider owns its underlying client and watcher.
Disposal is idempotent and closes callback admission before releasing those resources.

```csharp
var provider = new LuaLanguageServerIntelliSenseProvider(
    workspaceRootDirectoryPath: workspaceRoot,
    serverExecutablePath: luaLanguageServerPath,
    logger: logger);

try
{
    provider.DiagnosticsUpdated += OnDiagnosticsUpdated;
    provider.SemanticTokensUpdated += OnSemanticTokensUpdated;

    provider.OpenDocument(filePath, initialText);

    IReadOnlyList<TextCompletionItem> items =
        await provider.GetCompletionItemsAsync(filePath, initialText, line, column, cancellationToken: token);

    provider.UpdateDocument(filePath, currentText);
    provider.CloseDocument(filePath);
}
finally
{
    provider.Dispose();
}
```

Subscribe and unsubscribe symmetrically when a host owns a longer-lived provider. After
disposal begins, no new provider callback is admitted; a callback already in progress may
finish.

## Events and UI-thread marshaling

Diagnostics, semantic-token, capability, startup-failure, and workspace-watcher callbacks
may arrive on background threads. Their handlers run serially for one invocation, and a
failing handler does not prevent later subscribers from running. Marshal to the editor UI
dispatcher before touching controls, and treat each payload as an owned immutable snapshot.

```csharp
provider.DiagnosticsUpdated += (path, diagnostics) =>
{
    editorDispatcher.Post(() => errorList.Replace(path, diagnostics));
};
```

`CapabilitiesChanged` means that the host must reread `IsAvailable` and the capability
properties; it does not carry a cached capability payload.

## Document references and cancellation

Each `OpenDocument` call acquires one editor-open reference. Pair it with one
`CloseDocument` call. Repeated opens therefore require repeated closes. `UpdateDocument`
does not acquire an editor-open reference, although the provider may temporarily retain an
idle server-open document while a request reference is active.

Request methods accept the current buffer text and a `CancellationToken`:

```csharp
using var requestCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));

try
{
    var items = await provider.GetCompletionItemsAsync(
        filePath, editorText, line, column,
        cancellationToken: requestCancellation.Token);
}
catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
{
    // The caller cancelled; do not display this as an empty completion result or an error.
}
```

Caller cancellation propagates as `OperationCanceledException`. Provider disposal,
provider-enforced timeouts, internal transport failure, and unsupported capabilities use
the documented fallback result instead; they are not reported as caller cancellation.

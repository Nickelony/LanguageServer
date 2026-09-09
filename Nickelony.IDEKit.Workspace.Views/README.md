# Nickelony.IDEKit.Workspace.Views

WPF-free document-view coordination for the `Nickelony.IDEKit.Workspace` document authority:
the host view contract (`IWorkspaceDocumentView`) and the manager
(`WorkspaceDocumentManager`) that synchronizes attached views with tracked documents.
`WorkspaceDocumentManager` is split across per-operation partial files
(`WorkspaceDocumentManager.Open.cs`, `.Operations.cs`, `.DeleteGuards.cs`, `.ViewState.cs`,
`.Results.cs`, `.Lifecycle.cs`).

The package is the view slice of the workspace family, split into its own package so a host that
only needs document authority (a headless server, a tool, a service) can adopt
`Nickelony.IDEKit.Workspace` without taking the view contracts. It depends on
`Nickelony.IDEKit.Workspace` and `Nickelony.IDEKit.Core`. The sibling READMEs are the
[document authority](../Nickelony.IDEKit.Workspace/README.md) that the manager coordinates and
[Core](../Nickelony.IDEKit.Core/README.md), the text primitives the snapshot content uses.

The package covers:

- Attaching a host view to a tracked document (`OpenWithViewAsync`) and closing/unregistering it
  (`StopAsync`, `UnregisterOpenView`).
- Publishing view edits back to the document authority: the view raises
  `IWorkspaceDocumentView.ApplyRequested` and the manager applies the replacement, acknowledging
  the result to the view.
- Synchronizing views around document operations: identity changes for rename, save-as, and
  directory rename; refreshes after commit, reload, and conflict resolution; and closing views when
  their document is deleted.
- Reporting view outcomes as a composed value (`WorkspaceDocumentViewSynchronization` with
  `Synchronized`, `Blocked`, or `Unsynchronized`) next to the store result, so the document-authority
  outcome and the view outcome stay separate.

## Quick start

```csharp
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;
using Nickelony.IDEKit.Workspace.Views;

// Supplied by the host: a way to run view access on the right thread (a UI dispatcher, or the
// direct headless adapter shown here) and the host's view implementation for the document. The
// delegate must marshal the action, await it, and propagate its exceptions to the manager.
Func<Action, Task> dispatchViewAction = action =>
{
    action();
    return Task.CompletedTask;
};

string scriptDirectory = Path.Combine(Path.GetTempPath(), "workspace", "scripts");
string scriptPath = Path.Combine(scriptDirectory, "main.lua");

// A commit writes a temporary file next to the target before replacing it, so the destination
// directory must exist before the first write.
Directory.CreateDirectory(scriptDirectory);

IWorkspaceFileSystem fileSystem = new LocalWorkspaceFileSystem();
await using var store = new WorkspaceDocumentStore(fileSystem);
await using var manager = new WorkspaceDocumentManager(store, dispatchViewAction);
IWorkspaceDocumentView view = new MyDocumentView(scriptPath);

WorkspaceDocumentManagerOpenResult opened = await manager.OpenWithViewAsync(
    scriptPath,
    new WorkspaceDocumentOpenOptions(
        TextEncodingKind.Utf8,
        new TextFileFormat(TextEncodingKind.Utf8, false, TextNewlineStyle.Lf)),
    view);

if (opened.Status == WorkspaceDocumentManagerOpenStatus.Opened)
{
    WorkspaceDocumentSnapshot snapshot = opened.Snapshot!;
    WorkspaceDocumentManagerCommitResult committed = await manager.CommitAsync(
        new WorkspaceDocumentCommitRequest(
            new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
            snapshot.OnDiskStamp));
}
```

## Operation model

- **Dispatch delegate.** The manager owns no editor or UI thread. Every action that touches a view
  member is dispatched through the delegate supplied to the constructor, which must execute the
  action synchronously on the target thread and complete with its outcome; the manager consumes the
  captured results immediately afterwards and observes a fault as a view failure. A
  headless host passes a delegate that invokes the action directly (`action => { action(); return
  Task.CompletedTask; }`). The one exception is the `ApplyRequested` subscription, which the
  manager adds and removes on the calling thread.
- **Blocking policy.** Operations that could lose view state or destroy the document behind it
  consult attached view state first: delete, delete-directory, commit, reload, and conflict
  resolution report `Blocked` while a view has pending edits, a conflict, or an unsynchronized
  failure. Operations that only change the document identity or path - rename, save-as, and
  directory rename - never block on view state: the move cannot lose view edits, and the identity
  change is acknowledged afterwards, where a view update failure is reported as `Unsynchronized`.
- **Unsynchronized state.** A view whose refresh or acknowledgment failed is retained as
  unsynchronized. A successful refresh clears that state; commit retries the synchronization of a
  view whose only outstanding state is such a failure instead of blocking the commit permanently.
- **Delete guards.** Delete operations (file and directory) and directory moves ask attached views
  that implement `IWorkspaceDocumentDeleteGuardView` to enter a delete guard (a host-defined
  barrier, for example a read-only editor); a view without the capability is skipped. When a view
  rejects the guard, the operation is reported as `Blocked` before the store is reached; entered
  guards are always released, including when the operation throws.
- **Identity changes.** Rename, save-as, and directory rename ask attached views to acknowledge the
  new identity through `AcknowledgeIdentity`; a view that cannot accept the change is reported as
  `Unsynchronized` on the result, and the manager keeps reporting the view until a successful
  refresh clears it.
- **Reuse and closure.** A view that is already registered is reported as `AlreadyOpen` with the
  snapshot of the document it is attached to. A different view that duplicates a registered view id
  is `ViewInUse`. Stopping the manager (`StopAsync`/`DisposeAsync`) rejects new operations, waits
  for active ones, closes and unregisters every registered view, and releases all view-tracking
  state.

## Dependencies

`Nickelony.IDEKit.Workspace` (document authority) and `Nickelony.IDEKit.Core` - WPF-free,
toolkit-agnostic. Targets `net8.0`. The license terms are in the repository's `LICENSE` file.

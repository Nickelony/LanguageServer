# Nickelony.IDEKit.Workspace

WPF-free workspace document authority and file-system coordination built on
`Nickelony.IDEKit.Core`. The package owns logical document content, its persistence state, and the
file operations behind it; it contains no view, editor, or UI types.

The package contains four vertical slices:

- `Nickelony.IDEKit.Workspace.Documents` - the document authority: logical
  document ownership, persistence state, internal path normalization, and
  open/destination reservations (`WorkspaceDocumentStore`, split across
  per-operation partial files, and its read-only `IWorkspaceDocumentReader`
  slice, plus the `WorkspaceTextCodec` text codec). The failure vocabulary
  shared by the workspace packages (`WorkspaceOperationFailureCodes`) lives in
  the root namespace.
- `Nickelony.IDEKit.Workspace.Documents.FileSystem` - the file-system seam and
  its result vocabulary: `IWorkspaceFileSystem`, the `LocalWorkspaceFileSystem`
  default implementation, `WorkspaceFileSystemDecorator` for single-member host
  overrides, and the read/replace/move/delete result and temporary file
  contracts.
- `Nickelony.IDEKit.Workspace.Documents.Reloading` - the host-facing external
  reload pass: `FileReloadCoordinator` with its `FileReloadCallbacks` bundle.
- `Nickelony.IDEKit.Workspace.Editing` - the neutral workspace-edit application
  core (`WorkspaceEditApplier`) together with the multi-file application
  result contracts (`WorkspaceEditApplicationResult`,
  `WorkspaceEditApplicationStatus`, `WorkspaceEditTargetPreparation`,
  `WorkspaceEditTargetResult`, `WorkspaceEditTargetStatus`,
  `WorkspaceEditChangeSet`, `WorkspaceDocumentChange`).
  - Targets are identified by host-supplied `TargetId` strings, typically document file paths,
    and a prepared target carries the content and format before and after the transformation. A
    target whose content and format both match is a no-op that never reaches the store, and
    confirmed changes record both formats.
  - Use the `WorkspaceEditApplicationResult` factories (`Completed`, `PartiallyApplied`,
    `ValidationFailed`, `Canceled`) so the status, failure, and change-set members cannot
    contradict each other. Changed and unknown target ids are de-duplicated with the comparison
    supplied to the factory (the default follows the operating system; supply the store's
    `PathComparison` value so the comparison cannot drift), preserving first-occurrence order.
  - The applier applies multiple prepared changes to the same document in order by tracking the
    version each accepted replacement returns, applies the targets themselves in list order, stops
    at the first target that is not applied unless it was constructed with `continueOnFailure`, and
    accepts a cancellation token that marks the remaining targets not applied. An
    `OperationCanceledException` thrown by the replacement delegate propagates to the caller.
  - The `WorkspaceEdit` name is narrower than the Language Server Protocol's `WorkspaceEdit`: the
    applier applies whole-content replacements to existing tracked documents and does not create,
    rename, or delete resources.

Document-view coordination is a separate package, `Nickelony.IDEKit.Workspace.Views`, so a
headless consumer that only needs document authority never takes the view contracts. That package
covers opening a view on a document, applying view edits back to the document, delete guards, and
view synchronization outcomes (`IWorkspaceDocumentView`, `WorkspaceDocumentManager`).

Types are grouped by slice in shared contract files (for example
`Documents/WorkspaceDocumentMutationContracts.cs`) as a deliberate exception to the one-type-per-file
default: the contracts in each group change together and are read together.
`WorkspaceDocumentStore` is additionally split across per-operation partial files
(`WorkspaceDocumentStore.Open.cs`, `.Persistence.cs`, `.FileMoves.cs`,
`.DirectoryOperations.cs`, `.Internals.cs`, `.Lifecycle.cs`).

## Quick start

```csharp
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;

string documentDirectory = Path.Combine(Path.GetTempPath(), "workspace", "documents");
string documentPath = Path.Combine(documentDirectory, "notes.txt");

// A commit writes a temporary file next to the target before replacing it, so the destination
// directory must exist before the first write.
Directory.CreateDirectory(documentDirectory);

IWorkspaceFileSystem fileSystem = new LocalWorkspaceFileSystem();
await using var store = new WorkspaceDocumentStore(fileSystem);

WorkspaceDocumentOpenResult opened = await store.OpenAsync(
    documentPath,
    new WorkspaceDocumentOpenOptions(
        TextEncodingKind.Utf8,
        new TextFileFormat(TextEncodingKind.Utf8, false, TextNewlineStyle.Lf)));

if (opened.Status is WorkspaceDocumentOpenStatus.Opened or WorkspaceDocumentOpenStatus.AlreadyOpen)
{
    WorkspaceDocumentSnapshot snapshot = opened.Snapshot!;
    WorkspaceDocumentCommitResult committed = await store.CommitAsync(
        new WorkspaceDocumentCommitRequest(
            new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
            snapshot.OnDiskStamp));
}
```

The view-coordination quick start (attaching an editor view through `WorkspaceDocumentManager`)
lives in the [Views package README](../Nickelony.IDEKit.Workspace.Views/README.md).

## Operation model

- **Optimistic concurrency.** Requests carry a `WorkspaceDocumentRequestIdentity`
  (document key, normalized id, and expected version); disk-mutating operations also
  carry the expected `FileStamp`. Stale expectations produce result statuses such as
  `StaleDocument` or `ExternalFileConflict` instead of mutating a different state; a
  null or blank document id is an argument error.
- **Read-side slice.** `IWorkspaceDocumentReader` exposes the read-only surface,
  so consumers that only inspect document state can depend on a smaller contract
  than the full store.
- **Thread affinity.** Store operations are safe to call concurrently; the store does not
  capture the caller's context, so its continuations may resume on the thread pool. A dirty
  reload bypasses the per-document disk gate and is not registered as an active operation; it
  reports `Canceled` when the store is disposed while it runs.
- **Path identity.** Document ids are normalized full paths compared with the shared
  `Nickelony.IDEKit.Core.Pathing.LocalPathComparisonPolicy` policy (case-insensitive on Windows and macOS,
  ordinal elsewhere, exposed as `LocalPathComparisonPolicy.ForCurrentPlatform`). Only fully
  qualified paths are accepted; a relative path is invalid input because resolving it
  against the process current directory would bind document identity to ambient process
  state. The platform value is an operating-system assumption, not a probe of the volume:
  pass `LocalPathComparisonPolicy.CaseSensitive` on a case-sensitive macOS or Linux
  volume, and supply the same policy to the store, the reload
  coordinator, and the file system when the file-system semantics differ.
- **Host-specific behavior.** `LocalWorkspaceFileSystem` deletes permanently, and the
  store's delete members inherit that behavior; deleting through the store is therefore
  destructive by default. A host that needs a trash folder or the Windows shell recycle
  bin supplies its own `IWorkspaceFileSystem` decorator instead of relying on the
  library. See the delete member documentation for the contract.
- **Document scope.** Documents are path-identified. A missing path opens as a new empty
  document that can be saved there by default; pass
  `WorkspaceDocumentOpenOptions.CreateIfMissing: false` to report
  `WorkspaceDocumentOpenStatus.NotFound` instead. Saving as onto the document's own file saves
  in place and reports `SavedAs` instead of a destination collision. The package has no untitled
  or read-only document model.

## Custom file systems

`LocalWorkspaceFileSystem` is the default `IWorkspaceFileSystem`; text encoding is provided
separately by the stateless `WorkspaceTextCodec`. Hosts override individual members with
`WorkspaceFileSystemDecorator`, which forwards every member to an inner implementation:

- **Deletion.** The default deletes permanently; derive from `WorkspaceFileSystemDecorator` and
  override the delete members to route deletes through a trash or recycle-bin implementation
  before calling the inner file system.
- **Stamp cost.** Stamp capture reads and hashes the whole file (SHA-256), so large files
  and network shares pay that cost on every commit, rename, save-as, delete, reload, and
  conflict resolution. A clean no-op commit skips stamp capture because no write occurs.
  A host that needs cheaper stamps keeps one stamp computation across every stamp-producing
  member (`ReadAsync`, `CaptureStampAsync`, and the `ReplaceFileAsync` result), because the
  store compares stamps across those sources: overriding a single member mixes stamp
  vocabularies and turns cross-source comparisons into spurious external conflicts. The store
  treats any `FileStamp` value opaquely, and re-captures the stamp after a replacement when
  the replaced result does not report one.
- **Case-only renames.** Construct the file system with the same `LocalPathComparisonPolicy`
  as the store. A case-insensitive policy routes a case-only rename through an intermediate
  path because the destination resolves to the source file; a case-sensitive policy
  treats the two spellings as distinct paths.

A minimal trash decorator keeps every other member forwarding to the default file system:

```csharp
sealed class TrashFileSystem(IWorkspaceFileSystem inner) : WorkspaceFileSystemDecorator(inner)
{
    public override Task<WorkspaceFileDeleteResult> DeleteAsync(
        string path, FileStamp expectedStamp, CancellationToken cancellationToken = default)
        => HostTrash.MoveToTrash(path, expectedStamp, cancellationToken); // host trash/recycle-bin seam
}
```

## Optional architecture, not a requirement

The workspace document store is an optional part of the editor architecture. A host that only
needs the editor packages (`Nickelony.IDEKit.AvalonEdit` and related packages) does not need this
package at all, and base `Nickelony.IDEKit.AvalonEdit` deliberately does not reference it. The
AvalonEdit-to-Workspace bridge is host code, not a package; adopt this package directly when you
want document authority for your own host, and add
[Nickelony.IDEKit.Workspace.Views](../Nickelony.IDEKit.Workspace.Views/README.md) when you want
the view-coordination manager as well.

## Dependencies

`Nickelony.IDEKit.Core` only - WPF-free, toolkit-agnostic. Targets `net8.0`. The license terms are
in the repository's `LICENSE` file.

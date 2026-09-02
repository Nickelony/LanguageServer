# Nickelony.IDEKit.Workspace

WPF-free workspace document authority, filesystem coordination, document-view
coordination, and editor-session contracts built on `Nickelony.IDEKit.Core`.

The package is organized into vertical feature slices, delivered by this single
workspace assembly:

- `Nickelony.IDEKit.Workspace.Documents` — the document authority: logical
  document ownership, persistence state, filesystem coordination, path
  normalization, and open/destination reservations (`WorkspaceDocumentStore`,
  `WorkspaceDocumentPath`, `WorkspaceFileCodec`, `FileReloadCoordinator`).
- `Nickelony.IDEKit.Workspace.Editing` — the neutral workspace-edit application
  core (`WorkspaceEditApplierCore`).
- `Nickelony.IDEKit.Workspace.Views` — document views, the view manager, and the
  editor-view host and session contracts. Opening a view on a document, applying
  view edits back to the document, delete guards, and opening/reusing an editor
  view for a snapshot (`IWorkspaceDocumentView`, `WorkspaceDocumentManager`,
  `ViewBinding`, `ViewValidation`, `IEditorViewHost`, `IEditorSession`,
  `EditorSession`, `EditorSessionOptions`, `EditorSessionMode`,
  `EditorSessionOpenResult`). The session is WPF-free: it carries workspace
  document identity and mode only, and delegates view close/activate behavior
  to host callbacks.

## Optional architecture, not a requirement

Document views, editor sessions, and the workspace document store are optional
workspace architecture. A host that only needs the editor packages
(`Nickelony.IDEKit.AvalonEdit` and friends) does not need this package at all, and
base `Nickelony.IDEKit.AvalonEdit` deliberately does not reference it. The
AvalonEdit-to-Workspace bridge (the view and host contract) is host code, not a
package; adopt this package directly only when you want document authority or
editor-session semantics for your own host.

## Dependencies

`Nickelony.IDEKit.Core` only - WPF-free, toolkit-agnostic.
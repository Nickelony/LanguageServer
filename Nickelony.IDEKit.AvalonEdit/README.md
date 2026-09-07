# Nickelony.IDEKit.AvalonEdit

The first UI-coupled package in the family. It bridges the dependency-free
`Nickelony.IDEKit.Core` contracts to an AvalonEdit `TextEditor` control:
programmatic edits, line operations, bracket and quote auto-closing, caret and
zoom status, line comments, bookmarks, unsaved-change markers, offset clamping, and
diagnostic underlines.

The package targets `net8.0-windows`, uses WPF, and depends on AvalonEdit 6.x
plus `Nickelony.IDEKit.Core`. It does not depend on `Nickelony.IDEKit.Workspace`, so
consuming it never pulls the workspace document authority in transitively. It
is organized into vertical feature slices, each with its own namespace:

- `Nickelony.IDEKit.AvalonEdit.Documents` - document helpers
  (`TextDocumentExtensions.ClampOffset`, `TextSegmentFactory`) and the
  immutable `TextDocumentSnapshot`.
- `Nickelony.IDEKit.AvalonEdit.Editing` - programmatic edits and line
  operations (`TextEditorEditHelper`, `TextEditorLineOperations`,
  `AvalonEditTextEditTarget`), bracket and quote auto-closing
  (`TextAutoClosingService`), whole-document formatting
  (`TextEditorFormattingService`), and the workspace-edit selection state
  (`TextWorkspaceEditSelectionState`).
- `Nickelony.IDEKit.AvalonEdit.Navigation` - clamped caret, selection, and
  scroll-location helpers (`EditorNavigationHelper`) built on the Core
  `NavigationLocation` record, including a `TextDocument` extension that
  re-selects regex search results.
- `Nickelony.IDEKit.AvalonEdit.Comments` - line-comment transformations
  (`TextLineCommentService`) driven by an explicit Core `CommentSyntax`.
- `Nickelony.IDEKit.AvalonEdit.Bookmarks` - bookmark tracking
  (`BookmarkCoordinator`) and icon-gutter margin rendering (`BookmarkMargin`).
- `Nickelony.IDEKit.AvalonEdit.ChangeMarkers` - change-indicator gutter
  (`ChangeMarkerMargin`) over a pluggable `IChangeMarkerSource`, with
  `UnsavedChangesTracker` marking unsaved-change lines as the default source.
- `Nickelony.IDEKit.AvalonEdit.Diagnostics` - diagnostic underlines
  (`DiagnosticsRenderer`) over generic `TextDiagnosticSegment` values.
- `Nickelony.IDEKit.AvalonEdit.Editors` - editor status (`TextEditorStatusCoordinator`).

### Host integration seams

The editing helpers are AvalonEdit-only. The two host integration points - an
optional workspace `ITextEditTarget` and an optional content-changed callback -
are supplied as optional parameters, so the generic helpers stay reusable while
a host wires them exactly where it needs them:

```csharp
editor.InsertText(
    insertOffset,
    newText,
    workspaceEditTarget: editor.WorkspaceEditTarget,
    onContentChanged: () => editor.RunContentChangedWorker());
```

When no workspace target is supplied, edits are applied through an
`AvalonEditTextEditTarget` to the editor document directly, and the
content-changed callback is invoked after the direct edit when one is provided.

The bookmark coordinator is in-memory only: bookmark persistence (for example
a sidecar file) is a host concern. A host restores bookmarks by passing the
one-based line numbers to `BookmarkCoordinator.Restore` and persists the
bookmarked line numbers read from `GetBookmarkedLines`.

The change-marker margin is driven by a pluggable `IChangeMarkerSource`. The
built-in `UnsavedChangesTracker` marks the lines that differ from a recorded
baseline; a host records the baseline (for example after a load or save) by
calling `UnsavedChangesTracker.SetBaseline`, and a different source can be
supplied to render other kinds of change indicators (such as git diff lines).

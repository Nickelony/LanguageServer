# Nickelony.IDEKit.AvalonEdit

A UI-coupled package in the Nickelony IDEKit family. It bridges the dependency-free
`Nickelony.IDEKit.Core` contracts to an AvalonEdit `TextEditor` control:
programmatic edits, line operations, configurable auto-closing, navigation,
line comments, bookmarks, unsaved-change markers, offset clamping, and
diagnostic underlines.

## Getting started

Install the package from your configured feed (the library ships from a local
feed while it is in preview):

```powershell
dotnet add package Nickelony.IDEKit.AvalonEdit
```

A minimal editor setup: compose the auto-closing service, attach a change-marker
margin, and restore bookmarks from the sidecar for the document.

```csharp
using Nickelony.IDEKit.AvalonEdit.Bookmarks;
using Nickelony.IDEKit.AvalonEdit.ChangeMarkers;
using Nickelony.IDEKit.AvalonEdit.Editing;
using Nickelony.IDEKit.Core.AutoClosing;

var autoClosing = new TextAutoClosingService();
var options = TextAutoClosingOptions.Default;

editor.TextArea.TextEntering += (_, e) => autoClosing.HandleTextEntering(editor, e, options);
editor.PreviewKeyDown += (_, e) => autoClosing.HandleBackspace(editor, e, options);

var tracker = new UnsavedChangesTracker(() => editor.Document);
tracker.SetBaseline(originalText);                       // after load or save
editor.TextArea.LeftMargins.Add(new ChangeMarkerMargin(tracker));

var coordinator = new BookmarkCoordinator(() => editor.Document);
var store = new BookmarkSidecarStore(".bkmrk");
coordinator.RestoreBookmarks(store, filePath);           // on load, for the new document
```

The package targets `net8.0-windows`, uses WPF, and depends on AvalonEdit 6.x
plus `Nickelony.IDEKit.Core`. It does not depend on `Nickelony.IDEKit.Workspace`, so
consuming it never pulls the workspace document authority in transitively. The package
is organized into vertical feature slices, each with its own namespace:

- `Nickelony.IDEKit.AvalonEdit.Documents` - document helpers
  (`TextDocumentExtensions.ClampOffset`), the immutable
  `TextDocumentSnapshot` (captured on the document's owner thread, readable from
  any thread; it wraps AvalonEdit's own document snapshot and materializes line
  metadata on first access), and the incrementally updated
  `DocumentLineStateCache` for line-start parser states.
- `Nickelony.IDEKit.AvalonEdit.Editing` - programmatic edits and line
  operations (`TextEditorEditOperations`, `TextEditorLineOperations`,
  `TextEditorFirstMatchingLineOperations`, `AvalonEditTextEditTarget`) and
  whole-document formatting (`TextDocumentFormattingService`). Auto-closing
  (`TextAutoClosingService`) composes the Core `TextAutoClosingResolver` over a
  host-supplied pair list and applies its actions: one edit per typed pair
  (through the caller's edit target when one is supplied), so a single undo
  removes the inserted pair. It covers per-kind gates for the character after the
  caret, bracket-like and quote-like pair rules, selection wrapping that keeps
  the enclosed text selected, Backspace pair deletion through
  `HandleBackspace`, and per-document insertion tracking that feeds the
  resolver's provenance callback.
- `Nickelony.IDEKit.AvalonEdit.Navigation` - clamped caret, selection, and
  scroll-location operations (`TextAreaNavigationOperations`, extensions on AvalonEdit's
  `TextArea`; a host with a `TextEditor` passes `editor.TextArea`) and mouse-to-offset
  helpers (`TextEditorNavigationOperations`; the current-mouse caret move only
  applies while the mouse is over the editor), over the
  `Nickelony.IDEKit.Core.Navigation.NavigationLocation` record from Core.
- `Nickelony.IDEKit.AvalonEdit.Comments` - line-comment transformations
  (`TextLineCommentService`) driven by an explicit Core `CommentSyntax`. The edit
  computation lives in the Core `TextLineCommentPlanner` (the service captures the
  document into a snapshot and delegates); the service applies the edit to the editor
  document or a host edit target, preserves each line's original line terminator, and
  skips a transformation that would not change the selected text.
- `Nickelony.IDEKit.AvalonEdit.Bookmarks` - bookmark tracking
  (`BookmarkCoordinator` raising a `Changed` event on every explicit bookmark
  mutation: toggle, clear, and restore) and
  icon margin rendering (`BookmarkMargin`, driven by an
  `IBookmarkSource` and scaling its icons and reserved width with the editor's
  font size). Optional persistence through the `IBookmarkStore` seam:
  `BookmarkSidecarStore` saves a sidecar file through the Core `SidecarLineFile`
  writer using a host-supplied extension, and `BookmarkStoreExtensions` glue the
  coordinator to a store.
- `Nickelony.IDEKit.AvalonEdit.ChangeMarkers` - change-marker margin
  (`ChangeMarkerMargin`, scaling its marker and reserved width with the editor's
  font size) over a pluggable line-status source
  (`Nickelony.IDEKit.Core.LineStatus.ILineStatusSource`), with
  `UnsavedChangesTracker` as the ready-made source for unsaved-change lines.
  Sources that also implement `IChangeNotificationSource` (the tracker
  does) invalidate the margin automatically when they raise a change
  notification; the tracker raises one after `SetBaseline` and after every
  document edit and maintains its line view incrementally from those edits.
- `Nickelony.IDEKit.AvalonEdit.Diagnostics` - diagnostic underlines
  (`DiagnosticsRenderer`) over generic
  `Nickelony.IDEKit.Core.Diagnostics.TextDiagnosticSegment` values, themed
  through per-severity `ErrorPen`/`WarningPen`/`InformationPen`/`HintPen`
  properties. The segment and its `TextDiagnosticSeverity` are Core types, so
  producers, renderers, and hosts share one range model and one severity enum
  whose `Error`-`Hint` values mirror the Language Server Protocol numbering
  (protocol severities are still mapped explicitly; `None` is the enum default).
- `Nickelony.IDEKit.AvalonEdit.Rendering` - `LineStatusMarginBase`, the shared
  base for line-status margins that paint a marker beside the marked document
  lines their source reports (`ILineStatusSource` lives in
  `Nickelony.IDEKit.Core.LineStatus`, `IChangeNotificationSource` in
  `Nickelony.IDEKit.Core.Notifications`), plus `LineStatusIconMarginBase` on top
  of it for margins whose marker is a replaceable icon
  (`IconBrush`/`IconGeometry`) that runs a click action for the marked line;
  subclass either base to build custom margins over any line source. The namespace also
  carries the shared text-run styling contract (`ITextRunStyle`), its ready-made
  `TextRunStyle` record (foreground, bold, italic, and text decorations, with an
  `Empty` default), and the paint-time application helper (`TextRunStyleApplier`)
  used by the colorizing transformers in the `Nickelony.IDEKit.AvalonEdit.LanguageFeatures`
  (semantic tokens) and `Nickelony.IDEKit.AvalonEdit.TextMate` packages.
- `Nickelony.IDEKit.AvalonEdit.Highlighting` - code-first, regex-based
  highlighting (`RegexHighlightingDefinition`, `RegexHighlightingRule`,
  `RegexHighlightingSpan`, `RegexHighlightingStyle`). The definition covers both
  per-line rules and delimiter-based spans, so block comments and long strings
  stay highlighted across lines without an XSHD file.
- `Nickelony.IDEKit.AvalonEdit.Indentation` - a bridge that adapts an
  `IIndentationPolicy` from `Nickelony.IDEKit.Core.Indentation` to AvalonEdit's
  indentation seam (`PolicyIndentationStrategy`).

## Host integration seams

Each seam is a small wiring step; the snippets below show the intended shape.

**Programmatic edits.** The editing operations are AvalonEdit-only.
`TextEditorEditOperations` exposes an
optional `ITextEditTarget` (the Core seam for targets that own document
authority), so the generic helpers stay reusable while a host wires it exactly
where it needs it.

```csharp
editor.InsertText(insertOffset, newText, editTarget: myEditTarget);
```

`myEditTarget` stands for a host-provided target; this package neither defines
nor wraps it. A host that owns document authority supplies its own target and
updates its own state after the call when it tracks content changes.

The editor-facing services implement `ITextAutoClosingService`,
`ITextLineCommentService`, and `ITextDocumentFormattingService`, so a host that
composes services per editor can substitute its own implementations. The
package follows one rule for its API shape: fixed behavior is a static helper
class, while behavior a host may substitute per editor is an instance service
behind an interface. The auto-closing service is wired as shown in the
getting-started snippet above.

When no edit target is supplied, edits are applied through an
`AvalonEditTextEditTarget` to the editor's document directly; otherwise the edit
is applied through the target. A supplied target must satisfy the edit-target
contract documented by `TextEditorEditOperations` (same content as the editor's
document, synchronous apply, document updated before returning), because the
requested caret and selection offsets are applied to the editor's document and
clamped to its current length. A target that does not update
the document leaves the final view state to the host, which should set it after
publishing the change. `TextLineCommentService.ApplyEdit` and
`TextDocumentFormattingService.FormatDocument` expose the same optional target, so
comment transformations and whole-document formatting can also be routed through
a host-owned target; when a target is supplied, the formatter receives the
target's current content. `TextEditorLineOperations` exposes the same optional
edit target on its replacement operations; its selection and caret helpers act
on the editor directly, so a host that owns document authority must observe
those calls and keep its own content in sync. `TextAutoClosingService.HandleTextEntering`
and `HandleBackspace` also accept the optional target and apply the typed pair
(or the pair deletion) through it as one batch, so a single undo still removes
or restores the whole pair.

The bookmark coordinator is in-memory only: it tracks bookmarks as document
anchors in one document and raises `Changed` after explicit toggles, clears, and
restores. Anchors move with the text, so an edit before a bookmark keeps it on
its line. Its lifecycle around document swaps is documented on the type: reads
for a swapped-in document report no bookmarks, stale anchors are discarded on the
next mutation, and `SaveBookmarks` fails (throws) while the bookmarks are not
bound to the current document instead of persisting an empty set that would
delete the sidecar (`IsBoundToCurrentDocument` reports that state, so a host can
guard a save without a catch). `Clear` binds the coordinator to the current document first,
so a cleared set can be persisted for it. Persistence is optional: the
package ships `IBookmarkStore` with `BookmarkSidecarStore`, a host restores
bookmarks by passing one-based line numbers to `BookmarkCoordinator.Restore`
(or through `BookmarkStoreExtensions.RestoreBookmarks`, which leaves the
coordinator unchanged when the store reports a failed load), and persists the
numbers read from `GetMarkedLineNumbers` (see
`BookmarkStoreExtensions`). While the `BookmarkMargin` is attached to a text
view and the source implements `IChangeNotificationSource`, the margin
subscribes to the source's `Changed` event and invalidates itself, so
programmatic toggles and restores do not require a manual invalidation call.
With the `coordinator` and `store` from the getting-started snippet, the
guarded save reads:

```csharp
coordinator.Changed += (_, _) =>
{
    // While the bookmarks are not bound to the current document (for example after a document
    // swap and before a restore), SaveBookmarks throws instead of deleting the sidecar; guard
    // the save with IsBoundToCurrentDocument (or skip/defer it) in that window.
    if (coordinator.IsBoundToCurrentDocument)
        coordinator.SaveBookmarks(store, filePath);
};
editor.TextArea.LeftMargins.Add(new BookmarkMargin(coordinator));
```

The change-marker margin is driven by a pluggable `ILineStatusSource` from
`Nickelony.IDEKit.Core.LineStatus`. The
built-in `UnsavedChangesTracker` marks the lines that differ from a recorded
baseline (deletion-only edits mark nothing, and a final line terminator is part
of the comparison, so adding one marks the trailing empty line); a host records
the baseline (for example after a load or save) by
calling `UnsavedChangesTracker.SetBaseline`, and a different source can be
supplied to render other kinds of change markers (such as git diff lines).
Because the tracker implements `IChangeNotificationSource`, an attached
`ChangeMarkerMargin` invalidates itself after `SetBaseline`; sources that do not
raise that notification require the host to invalidate the margin after they
change. The tracker and margin wiring is the getting-started snippet above.

The diagnostic renderer is an `IBackgroundRenderer`: hosts add it to the text
view's background renderers and invalidate the text view after the diagnostic
segments or pens change (for example with `TextView.Redraw()`). It draws in the
selection layer by default; assign `DiagnosticsRenderer.Layer` before attaching
it when another layer is needed. The
`Nickelony.IDEKit.AvalonEdit.LanguageFeatures` package projects IntelliSense
diagnostics for it through `TextDiagnosticSegmentFactory`.

```csharp
// myDiagnostics is the host's IReadOnlyList<TextDiagnostic> (for example the latest diagnostics
// its IntelliSense provider published); the factory projects it on every render pass.
var renderer = TextDiagnosticSegmentFactory.CreateRenderer(() => myDiagnostics);
editor.TextArea.TextView.BackgroundRenderers.Add(renderer);
editor.TextArea.TextView.Redraw();                      // after the diagnostics change
```

Indentation bridges a Core policy onto AvalonEdit's strategy seam:

```csharp
editor.TextArea.IndentationStrategy = new PolicyIndentationStrategy(editor.Options, myPolicy);
```

Highlighting definitions rebuild lazily; refresh the editor after a rebuild:

```csharp
definition.RuleSetChanged += (_, _) => editor.SyntaxHighlighting = definition;
```

## Host-neutral boundary

"Host-owned" describes a type or authority that the consuming host supplies and
this package never creates itself. The package keeps host-supplied values as
host decisions; where it needs a starting point it ships a documented sample
default that the host overrides (the margin brushes, authored geometry, and
metrics, the diagnostic pens, and the default auto-closing pairs):

- `BookmarkSidecarStore` requires the sidecar extension to be supplied by the
  host (for example `.bkmrk`) and passes it explicitly to the Core
  `SidecarLineFile` writer.
- `NavigationLocation.IsEquivalentTo` compares file paths ordinally by default;
  hosts whose document identities are case-insensitive pass
  `StringComparison.OrdinalIgnoreCase`.
- The Core `TextAutoClosingOptions` defaults to the language-neutral pairs (parentheses,
  braces, brackets, double quotes, and single quotes); language-specific tokens
  such as angle brackets and backticks are opt-in through `TextAutoClosingPair`
  presets, and quote-like pairs are configured explicitly with
  `TextAutoClosingPair.Kind`. Auto-closing applies when the character after the
  caret is the end of the text, whitespace, or belongs to the pair kind's default
  auto-close-before preset (`TextAutoClosingOptions.DefaultBracketAutoCloseBefore`
  or `...DefaultQuoteAutoCloseBefore`); `AutoCloseBefore`
  replaces that preset when a host wants the language-configured behavior (an empty
  set admits no character besides whitespace and the end of the text).
  Skipping existing closing text and Backspace pair deletion default to
  `TextAutoClosingProvenance.Auto`; under that mode both apply only to closing text
  the service inserted and tracked itself, while hosts that apply resolutions
  through their own edit path select `Always` or `Never` instead.
- The AvalonEdit-independent contracts live in the toolkit-agnostic packages:
  `IIndentationPolicy`/`IndentationContext`, `SidecarLineFile`, and the line-comment
  planner (`TextLineCommentPlanner`) in `Nickelony.IDEKit.Core`.

The license terms are in the repository's `LICENSE` file.

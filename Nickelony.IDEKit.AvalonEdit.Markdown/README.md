# Nickelony.IDEKit.AvalonEdit.Markdown

Markdown tooltip rendering for AvalonEdit editors, built on the
**AvalonEdit** editor package and **Markdig**. The package targets
`net8.0-windows`, uses WPF, and renders through the single namespace
`Nickelony.IDEKit.AvalonEdit.Markdown`:

- Rendering: CommonMark plus pipe tables (with GFM column alignment),
  strikethrough, and autolinks through a deliberately narrow Markdig
  pipeline; other extensions (footnotes, mathematics, definition lists,
  task lists, custom containers, and the subscript, superscript, marked,
  and inserted emphasis variants) are not enabled and render as literal
  text. CommonMark soft line breaks render as spaces and hard breaks as
  line breaks. The AST is emitted directly into a WPF `FlowDocument`.
- Code blocks: fenced and indented code renders as a read-only AvalonEdit
  `TextEditor` with syntax highlighting. Resolution tries the language as a
  definition name with the supplied casing, then case-insensitively against
  the registered definition names, then as a file extension, and finally
  through the configured aliases; the default aliases map `csharp` to `.cs`
  and `json5` to `.json`, while names and extensions AvalonEdit already
  knows (for example `cs`, `js`, `python`, or `javascript`) need no alias.
  Definitions AvalonEdit does not ship (for example Lua or TypeScript) must
  be host-registered or supplied through `CustomHighlightingInstaller`.
  `CreateCodeBlockEditor` is available for embeddable code content; the
  displayed text has its line endings normalized to line feeds and trailing
  blank lines removed. Rendered blocks are measured with the editor's own
  text view, clamped to `MaxVisibleCodeBlockLines`, and scroll (or clip when
  scrolling is disabled) beyond that.
- Presentation: `CreateContent` returns tooltip-shaped elements (not
  focusable, not selectable); `CreateFlowDocument` returns the rendered
  document for hosts that want to present it in their own container.
- Theme and options: `MarkdownToolTipTheme` controls fonts, sizes, brushes,
  and spacing (numeric values are validated on assignment; blend ratios are
  clamped when rendered); `MarkdownToolTipOptions` controls scrolling, an
  `OpenHyperlink` callback, language aliases, a custom highlighting installer,
  and an optional logger. Assigned hyperlink scheme sets and alias maps are
  copied and frozen on assignment and always compared case-insensitively.
  `AllowScrolling = false` disables scrolling
  entirely - overflow is clipped at the theme limits. A hyperlink opener that
  returns `false` or throws counts as not handled and falls back to the
  operating system's default protocol handler. Plain-text fallback rendering
  is preserved for empty or unparseable content.

## Dependencies

This package intentionally brings **Markdig** (for Markdown parsing) on top
of AvalonEdit. The dependency is confined here; the dependency-free editing
helpers live in `Nickelony.IDEKit.AvalonEdit`, so a host that does not use
Markdown tooltips never receives Markdig.

### Logging

Markdown rendering accepts an optional `ILogger` through
`MarkdownToolTipOptions.Logger`. Rendering failures and hyperlink-open failures
are reported as warnings through it, and a code-block language that resolves to
no highlighting is reported at debug level. Without a logger these failures
are not reported, and rendering still falls back to plain text. Event ids are
stable within the package: `1` (markdown rendering failed), `2` (hyperlink
open failed), and `3` (unresolved code-block language).

## Design notes

- `MarkdownToolTipTheme.Default` is a platform baseline, not an integration
  with a host's color palette or editor theme; themed hosts should build a
  theme from their own palette.
- The Markdown renderer is intentionally custom instead of delegating to
  `Markdig.Wpf`: that dependency does not provide this package's tooltip
  semantics (focus and selection policy, embedded AvalonEdit code blocks,
  wheel chaining, and hyperlink gating).
- WPF does not chain the mouse wheel from a nested scroll viewer to an outer
  one, so `ToolTipScrollChaining` routes the wheel event; it can disappear if
  WPF or AvalonEdit ever chains the wheel itself. Chained scrolling uses the
  native step (three text lines per notch, or a page when the operating system
  is configured that way), and an event the viewer cannot consume is left for
  an enclosing host scroller.
- GFM table column alignment is applied to cell paragraphs; fixed layout
  values such as heading margins, list indentation, and quote padding are not
  theme properties.
- The Markdown renderer is split internally: `MarkdownToolTipRenderer` is the
  public facade, `MarkdownFlowDocumentBuilder` parses the content and builds
  the document with its blocks and inlines (including hyperlink activation),
  and `MarkdownCodeBlockFactory` creates the code-block editors and their
  measured host elements. The assets shared by all three - line-ending
  normalization and the per-theme derived brushes - live in
  `MarkdownRenderingAssets`, so the helpers no longer reach back into the
  facade. The helpers are internal, so the public surface stays
  flow-oriented.

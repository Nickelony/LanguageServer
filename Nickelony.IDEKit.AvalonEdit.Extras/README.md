# Nickelony.IDEKit.AvalonEdit.Extras

Optional AvalonEdit editor extras on top of `Nickelony.IDEKit.AvalonEdit`:
Markdown tooltip rendering and TextMate syntax-highlighting infrastructure.
The package targets `net8.0-windows`, uses WPF, and is organized into vertical
feature slices, each with its own namespace:

- `Nickelony.IDEKit.AvalonEdit.Extras.Markdown` - Markdown tooltip rendering
  built on AvalonEdit and Markdig. Parses CommonMark (plus tables,
  strikethrough, and autolinks via Markdig advanced extensions) and renders it
  directly into a WPF `FlowDocument` - no placeholder round-trips, element
  `Tag` scans, or post-processing passes. Fenced and indented code blocks
  render as read-only AvalonEdit `TextEditor` elements with syntax
  highlighting. A `MarkdownToolTipTheme` controls fonts, sizes, brushes, and
  spacing; a `MarkdownToolTipOptions` controls scrolling, hyperlink scheme
  gating, and a host-side custom syntax-highlighting hook. Plain-text fallback
  rendering is preserved for empty or unparseable content.
- `Nickelony.IDEKit.AvalonEdit.Extras.TextMate.Highlighting` - the colorizing
  transformer (`TextMateColorizingTransformer`) that translates TextMate
  tokenization into AvalonEdit visual-line runs, the incremental document
  line-list adapter (`TextMateDocumentLineList`), the resolved visual style
  (`TextMateHighlightingStyle`), the token-theme data model
  (`TextMateTokenTheme` / `TextMateTokenThemeRule`), and the theme style
  resolver (`TextMateThemeStyleResolver`) that maps token scopes to styles.

Hosts provide the token theme (`TextMateTokenTheme`) and, on a view, install
the transformer alongside a `TMModel`; the package owns no engine or
host-specific concepts.

## Dependencies

This extras package intentionally brings **Markdig** (for markdown parsing)
and **TextMateSharp** (with its grammars package) on top of AvalonEdit. Those
focused dependencies are confined here, so a host that uses neither Markdown
tooltips nor TextMate highlighting never receives them transitively.

### Logging

`TextMateThemeStyleResolver` accepts an optional
`Microsoft.Extensions.Logging.ILogger` and defaults to `NullLogger` when none
is supplied, so hosts control where malformed theme data is logged.

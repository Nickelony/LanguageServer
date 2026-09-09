# Nickelony.IDEKit.AvalonEdit.TextMate

TextMate syntax-highlighting infrastructure for AvalonEdit editors, built on
`Nickelony.IDEKit.AvalonEdit` and **TextMateSharp** (with its grammars
package). The package targets `net8.0-windows`, uses WPF, and hosts the
`Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting` namespace:

- `Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting` - the colorizing
  transformer (`TextMateColorizingTransformer`) that translates TextMate
  tokenization into AvalonEdit visual-line runs, the incremental document
  line-list adapter (`TextMateDocumentLineList`), the resolved visual style
  (`TextRunStyle`, the shared `ITextRunStyle` implementation from
  `Nickelony.IDEKit.AvalonEdit.Rendering`), the token-theme
  data model
  (`TextMateTokenTheme` / `TextMateTokenThemeRule`), and the theme style
  resolver (`TextMateThemeStyleResolver`) that maps token scopes to styles.
  The host installs the transformer on a view's `LineTransformers` and owns
  its removal; disposing the transformer only detaches its model listener.
  `TextMateHighlightingAttachment` wires the line list, model, resolver, and
  transformer to an editor in one step and disposes them in the safe order.
  Lines render uncolored until the model's background tokenizer completes
  them, and token-change notifications then coalesce into a single queued
  redraw, so the paint path never drives TextMateSharp's tokenizer directly.
  Selectors support comma-separated alternatives and space-separated
  descendant chains (for example `source.lua keyword.control`); the TextMate
  exclusion (`-`), direct-child (`>`), wildcard (`*`), priority (`L:`), and
  parenthesis operators are not supported, and selectors that use any of them
  never match and are reported through the resolver logger. Styles resolve the
  way VS Code resolves them: scope push by scope push, anchoring the
  selector's rightmost part at each scope and letting the matching rules of
  that push overwrite the accumulated attributes, most specific rule first.
  A rule with a blank scope provides the defaults that apply to every token.
  Theme values follow the TextMate data conventions: a rule foreground also
  accepts the eight-digit `#RRGGBBAA` form and the four-digit `#RGBA` form
  (both normalized to WPF order), and a present `fontStyle` value - including
  an empty string - resets the inherited traits before applying the
  recognized ones, while an absent value keeps them. Token rules carry
  foreground colors and font traits only; rule backgrounds are not represented
  and are ignored. Resolved styles are cached per resolver instance with a
  bounded, synchronized cache, so one resolver can be shared across editors
  and threads; the line list is safe to read from the tokenizer thread.

Hosts provide the token theme (`TextMateTokenTheme`) and, on a view, install
the transformer alongside a `TMModel`; the package does not bundle a tokenizer
engine or any host-specific concepts. The dependency-free first-matching-line
editing helpers live in the base `Nickelony.IDEKit.AvalonEdit` package.

## Dependencies

This package intentionally brings **TextMateSharp** (with its grammars
package) on top of AvalonEdit. The dependency is confined here; the
dependency-free editing helpers live in `Nickelony.IDEKit.AvalonEdit`, so a
host that does not use TextMate highlighting never receives TextMateSharp.

### Logging

`TextMateThemeStyleResolver` accepts an optional
`Microsoft.Extensions.Logging.ILogger` and defaults to `NullLogger` when none
is supplied, so hosts control where malformed theme data is logged. Invalid
foreground colors, unsupported selectors, and unrecognized font style traits
are reported as warnings. Event ids are stable within the package: `1`
(invalid foreground color), `2` (unsupported selector), and `3`
(unrecognized font style trait).

## Design notes

- The TextMate theme resolver is intentionally custom instead of delegating to
  `TextMateSharp.Themes`: that dependency does not provide the current VS Code
  scope matching and specificity behavior. The resolver applies the theme per
  scope push, so a deeper token scope takes precedence over a more deeply
  nested selector that matched an enclosing scope - the same precedence VS
  Code uses.
- `TextMateDocumentLineList` keeps a full snapshot of line texts so the
  tokenizer thread can read the document without touching the UI-thread
  document; large documents pay for one extra copy of their text. Changed line
  ranges are derived from the document's line geometry, so edits that split or
  form a CRLF pair keep the snapshot, the model's line-state list, and the
  document at the same line count, with every snapshot line text mirroring the
  document line including its terminator.
- The colorizing transformer never tokenizes from the paint path:
  TextMateSharp's tokenizer is not safe to drive from two threads at once, so
  lines render with the base style until the model's background pass (up to
  three seconds and 10,000 characters per line) completes them and raises the
  token-change notification that queues the redraw.
- TextMate token rules carry foreground colors and font traits only; rule
  backgrounds are not represented and are ignored.

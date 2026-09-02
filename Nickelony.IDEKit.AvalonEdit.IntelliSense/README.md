# Nickelony.IDEKit.AvalonEdit.IntelliSense

The IntelliSense UI layer of the family. It brings the hover, signature help,
definition-navigation, completion-window, and semantic-token colorizing
controllers to an AvalonEdit `TextEditor`, built on the dependency-free
`Nickelony.IDEKit.Core` and `Nickelony.IDEKit.IntelliSense` contracts
plus the `Nickelony.IDEKit.AvalonEdit` document/editing helpers.

The package targets `net8.0-windows`, uses WPF, and is organized into vertical
feature slices, each with its own namespace:

- `Nickelony.IDEKit.AvalonEdit.IntelliSense.Hover` - the hover request
  controller (`TextHoverController`) that coordinates hover-vs-diagnostic
  tooltip display.
- `Nickelony.IDEKit.AvalonEdit.IntelliSense.Signatures` - the signature
  help controller (`TextSignatureHelpController`) with debounced refresh and
  supersession, plus the `ISyntaxPreviewSource` contract.
- `Nickelony.IDEKit.AvalonEdit.IntelliSense.Navigation` - the definition
  navigation trigger controller (`TextDefinitionTriggerController`) for F12
  and Ctrl+Click.
- `Nickelony.IDEKit.AvalonEdit.IntelliSense.Completion` - the completion
  window plumbing (`CompletionWindowHost`, `CompletionWindowCoordinator`,
  `CompletionWindowToolTipAccess`, `EditorCompletionTriggerHelper`,
  `TextPopupInteractionRules`) and the completion popup controller
  (`TextCompletionController`) with request scheduling, tooltip ownership,
  and sizing.
- `Nickelony.IDEKit.AvalonEdit.IntelliSense.Presentation` - pure
  presentation-state records for hover, signature help, and completion.
- `Nickelony.IDEKit.AvalonEdit.IntelliSense.Highlighting` - the generic
  `SemanticTokensColorizer` rendering kernel with an injectable
  `ISemanticTokenStyleResolver`; hosts map token types to their own theme.

### Logging

Controllers accept an optional `Microsoft.Extensions.Logging.ILogger` and
default to `NullLogger` when none is supplied, so hosts control where request
failures are logged.

### Host integration seams

The controllers are hook-driven: they take callbacks for offset resolution,
request execution, and presentation, so a host wires them to its own editor
state and skins. `TextCompletionController` takes the editor, a
`CompletionWindowCoordinator`, and the options record; hosts keep their own
completion-item and window styling (for example a `CompletionData` type and
window-style skin) and supply item factories and display-text selectors.

# Nickelony.IDEKit.KeyBindings

WPF command and keyboard shortcut system for editors and any application with
a command surface: command catalog, key bindings, persisted overrides,
validation, and key dispatch.

The package is organized as a single cohesive binding slice, delivered by this
assembly:

- `Nickelony.IDEKit.KeyBindings` — the key binding system:
  `KeyCombo`, `CommandDescriptor<TCommandId>` and
  `CommandCatalog<TCommandId>` (defaults + remapping policy),
  `KeyBindingOverrideCollection` (persisted per-workspace overrides),
  `IKeyBindingService<TCommandId>` / `KeyBindingService<TCommandId>`
  (lookup, display, validation, apply/clear/reset), and
  `KeyBindingDispatcher<TCommandId>` (keydown -> binding lookup ->
  canExecute -> execute pipeline).

The system is WPF-native: `KeyCombo` composes `System.Windows.Input.Key` and
`ModifierKeys`, and `KeyBindingDispatcher` consumes `KeyEventArgs`. Command
identity is generic (`TCommandId`) so any command model (an enum, a string id,
etc.) can drive the catalog; persistence always uses the stable string
`SerializedId`.

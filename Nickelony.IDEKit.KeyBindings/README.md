# Nickelony.IDEKit.KeyBindings

WPF command and keyboard shortcut system for editors and any application with
a command surface: command catalog, key bindings, persisted overrides,
validation, and key dispatch.

The package is organized as a single cohesive binding slice, delivered by this
assembly:

- `Nickelony.IDEKit.KeyBindings` - the key binding system:
  `KeyCombo`, `CommandDescriptor<TCommandId>` and
  `CommandCatalog<TCommandId>` (defaults + remapping policy),
  `KeyBindingOverrideCollection` with its `KeyBindingOverrideEntry` and
  `KeyBindingSettings` entries (the XML-serializable persisted override model),
  `KeyBindingValidationResult` (validation outcome), `IKeyBindingService<TCommandId>` /
  `KeyBindingService<TCommandId>` (lookup, display text, validation, reset/reset-all), and
  `KeyBindingDispatcher<TCommandId>` (keydown -> binding lookup ->
  canExecute -> execute pipeline).

The system is WPF-native: `KeyCombo` composes `System.Windows.Input.Key` and
`ModifierKeys`, and `KeyBindingDispatcher` consumes `KeyEventArgs`. Command
identity is generic (`TCommandId`) so any command model (an enum, a string id,
etc.) can drive the catalog; persistence always uses the stable string
`SerializedId`.

## Logging

`KeyBindingService<TCommandId>` accepts an optional
`Microsoft.Extensions.Logging.ILogger` and defaults to `NullLogger` when none
is supplied, so hosts control where ignored or invalid override entries are
logged. Event ids are stable within the package: `1` (host-reserved override
ignored), `2` (all override bindings invalid), `3` (empty key name), and `4`
(invalid key name).

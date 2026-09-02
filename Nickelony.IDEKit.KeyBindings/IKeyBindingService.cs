using System.Diagnostics.CodeAnalysis;

namespace Nickelony.IDEKit.KeyBindings;

/// <summary>
/// Runtime service for key bindings.
/// Provides lookup, current bindings, display text, validation,
/// apply/clear/reset operations, and a <see cref="BindingsChanged"/> notification.
/// </summary>
public interface IKeyBindingService<TCommandId> : IDisposable
	where TCommandId : notnull
{
	/// <summary>
	/// Raised after the runtime maps are rebuilt by an apply, clear, or reset operation.
	/// Apply and clear raise this event only after the persistence callback reports success;
	/// reset and reset-all invoke the callback but do not inspect its return value.
	/// Menu, toolbar, and context-menu presentation may refresh from this notification.
	/// </summary>
	event EventHandler? BindingsChanged;

	/// <summary>
	/// Looks up the <typeparamref name="TCommandId"/> currently bound to a key combo.
	/// Returns <see langword="false"/> when no command is bound.
	/// The concrete service rejects collisions while rebuilding its maps, so this lookup
	/// never applies a first-wins rule to a collision.
	/// </summary>
	bool TryGetCommand(KeyCombo shortcut, [NotNullWhen(true)] out TCommandId? command);

	/// <summary>
	/// Returns the current bindings for a command, or an empty list when
	/// the command has no bindings (explicitly unbound) or is not catalogued.
	/// </summary>
	IReadOnlyList<KeyCombo> GetBindings(TCommandId command);

	/// <summary>
	/// Returns the display text for a command's current bindings.
	/// When the command has no bindings, returns <paramref name="fallbackDisplayText"/>.
	/// </summary>
	string GetDisplayText(TCommandId command, string fallbackDisplayText = "");

	/// <summary>
	/// Validates a proposed binding set for a command without mutating state. The result
	/// accounts for command remapping policy, repeated combos within the set, and conflicts
	/// with the current bindings of other commands.
	/// </summary>
	KeyBindingValidationResult Validate(TCommandId command, IReadOnlyList<KeyCombo> bindings);

	/// <summary>
	/// Attempts to replace a command's current binding set. Reserved, non-remappable,
	/// and duplicate proposals are rejected; conflicts are rejected unless replacement
	/// is explicitly requested.
	/// When <paramref name="replaceConflicts"/> is <see langword="true"/>, the service
	/// requests conflict replacement while constructing the persisted override snapshot;
	/// catalog defaults are not removed. The persistence callback receives the snapshot
	/// before the in-memory maps are rebuilt.
	/// </summary>
	/// <remarks>
	/// The override snapshot is persisted before the in-memory maps are updated; if
	/// persistence fails, <see cref="KeyBindingValidationResult.Conflict"/> is returned
	/// and the in-memory bindings remain unchanged.
	/// </remarks>
	KeyBindingValidationResult Apply(TCommandId command, IReadOnlyList<KeyCombo> bindings, bool replaceConflicts);

	/// <summary>
	/// Clears all bindings for a command, storing an explicit empty override.
	/// Rejected for host-reserved or non-remappable commands.
	/// </summary>
	/// <remarks>
	/// If the override snapshot cannot be persisted, <see cref="KeyBindingValidationResult.Conflict"/>
	/// is returned and the in-memory bindings remain unchanged.
	/// </remarks>
	KeyBindingValidationResult Clear(TCommandId command);

	/// <summary>
	/// Removes the override for a single command, falling back to catalog defaults.
	/// </summary>
	/// <remarks>
	/// The persistence callback is invoked, but its return value is not inspected. The
	/// in-memory override collection and runtime maps are updated and the change event is
	/// raised even when the callback reports failure.
	/// </remarks>
	void Reset(TCommandId command);

	/// <summary>
	/// Removes all overrides for the active workspace, falling back to catalog defaults.
	/// </summary>
	/// <remarks>
	/// The persistence callback is invoked, but its return value is not inspected. The
	/// in-memory override collection and runtime maps are updated and the change event is
	/// raised even when the callback reports failure.
	/// </remarks>
	void ResetAll();
}

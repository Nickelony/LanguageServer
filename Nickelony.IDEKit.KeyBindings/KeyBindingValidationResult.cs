namespace Nickelony.IDEKit.KeyBindings;

/// <summary>
/// Result of key binding validation or a binding mutation operation.
/// </summary>
public enum KeyBindingValidationResult
{
	/// <summary>The operation is valid.</summary>
	Valid,

	/// <summary>The command is host-reserved and cannot be remapped.</summary>
	Reserved,

	/// <summary>The command is unknown or is not remappable.</summary>
	NotRemappable,

	/// <summary>
	/// A proposed binding contains an invalid key, such as the
	/// <see cref="System.Windows.Input.Key.None"/> key in the <see langword="default"/>
	/// <see cref="KeyCombo"/> value. The current
	/// <see cref="KeyBindingService{TCommandId}"/> does not return this result.
	/// </summary>
	InvalidKey,

	/// <summary>The same key combo appears more than once in the proposed binding set.</summary>
	DuplicateInCommand,

	/// <summary>A key combo is already assigned to another command under the current runtime maps.</summary>
	Conflict
}

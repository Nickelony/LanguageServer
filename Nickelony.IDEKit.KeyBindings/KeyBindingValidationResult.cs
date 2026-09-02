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

	/// <summary>One or more bindings contain an invalid key, such as <see cref="System.Windows.Input.Key.None"/>. The current <see cref="KeyBindingService{TCommandId}"/> does not produce this result.</summary>
	InvalidKey,

	/// <summary>The same key combo appears more than once in the proposed binding set.</summary>
	DuplicateInCommand,

	/// <summary>A key combo is already assigned to another command under the current runtime maps.</summary>
	Conflict
}

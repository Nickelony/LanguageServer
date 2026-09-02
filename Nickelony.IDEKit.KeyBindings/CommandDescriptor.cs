namespace Nickelony.IDEKit.KeyBindings;

/// <summary>
/// Command catalog entry containing a <typeparamref name="TCommandId"/>, its stable
/// serialized identifier, default bindings, and remapping policy.
/// </summary>
public sealed class CommandDescriptor<TCommandId>
	where TCommandId : notnull
{
	/// <summary>
	/// Creates a command descriptor with its serialized identifier, remapping policy, and default bindings.
	/// </summary>
	/// <param name="command">The command identity. Catalog construction rejects the <see langword="default"/> command value.</param>
	/// <param name="serializedId">The stable serialized identifier used for persistence.</param>
	/// <param name="isRemappable">Whether the user is permitted to remap this command.</param>
	/// <param name="isHostReserved">Whether this command's key binding is reserved by the host.</param>
	/// <param name="defaultBindings">The default bindings defined by the application.</param>
	public CommandDescriptor(
		TCommandId command,
		string serializedId,
		bool isRemappable,
		bool isHostReserved,
		params KeyCombo[] defaultBindings)
	{
		Command = command;
		SerializedId = serializedId ?? throw new ArgumentNullException(nameof(serializedId));
		IsRemappable = isRemappable;
		IsHostReserved = isHostReserved;
		DefaultBindings = Array.AsReadOnly(defaultBindings ?? Array.Empty<KeyCombo>());
	}

	/// <summary>
	/// The command this descriptor represents. Catalog construction rejects the
	/// <see langword="default"/> command value, such as <c>None</c> for an enum identity.
	/// </summary>
	public TCommandId Command { get; }

	/// <summary>
	/// Stable identifier associated with this command in persisted overrides.
	/// </summary>
	public string SerializedId { get; }

	/// <summary>
	/// The default key combos used when no applicable override is loaded.
	/// </summary>
	public IReadOnlyList<KeyCombo> DefaultBindings { get; }

	/// <summary>
	/// Whether the command may be changed through the service's remapping operations.
	/// </summary>
	public bool IsRemappable { get; }

	/// <summary>
	/// Whether this command is reserved by the host (for example, Alt+F4 for Exit).
	/// Validation rejects remapping operations for reserved commands, and non-empty loaded
	/// overrides are ignored in favor of the catalog defaults.
	/// </summary>
	public bool IsHostReserved { get; }
}

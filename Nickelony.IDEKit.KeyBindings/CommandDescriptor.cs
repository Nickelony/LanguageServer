namespace Nickelony.IDEKit.KeyBindings;

/// <summary>
/// A command's catalog entry, including its <typeparamref name="TCommandId"/>, stable
/// identifier, default bindings, and remapping policy.
/// </summary>
public sealed class CommandDescriptor<TCommandId>
	where TCommandId : notnull
{
	/// <summary>
	/// Creates a descriptor with a command identity, stable identifier, remapping policy,
	/// and default bindings.
	/// </summary>
	/// <param name="command">The command identity. Catalog construction rejects the <see langword="default"/> command value.</param>
	/// <param name="serializedId">The stable serialized identifier used for persistence.</param>
	/// <param name="isRemappable">Whether the user is permitted to remap this command.</param>
	/// <param name="isHostReserved">Whether the host reserves this command and prevents it from being remapped.</param>
	/// <param name="defaultBindings">The default bindings defined by the application.</param>
	/// <exception cref="ArgumentNullException"><paramref name="serializedId"/> is <see langword="null"/>.</exception>
	public CommandDescriptor(
		TCommandId command,
		string serializedId,
		bool isRemappable,
		bool isHostReserved,
		params KeyCombo[] defaultBindings)
	{
		ArgumentNullException.ThrowIfNull(serializedId);
		ArgumentNullException.ThrowIfNull(defaultBindings);

		Command = command;
		SerializedId = serializedId;
		IsRemappable = isRemappable;
		IsHostReserved = isHostReserved;
		DefaultBindings = Array.AsReadOnly(defaultBindings);
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
	/// The default key combos used when no override applies.
	/// </summary>
	public IReadOnlyList<KeyCombo> DefaultBindings { get; }

	/// <summary>
	/// Whether the command may be changed through the service's apply or clear operations.
	/// </summary>
	public bool IsRemappable { get; }

	/// <summary>
	/// Whether this command is reserved by the host. Validation rejects apply and clear
	/// operations for reserved commands, and non-empty loaded overrides are ignored in
	/// favor of the catalog defaults.
	/// </summary>
	public bool IsHostReserved { get; }
}

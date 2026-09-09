namespace Nickelony.IDEKit.KeyBindings;

/// <summary>
/// A command's catalog entry, including its <typeparamref name="TCommandId"/>, stable
/// identifier, default bindings, and remapping policy.
/// </summary>
/// <typeparam name="TCommandId">The command identity type.</typeparam>
public sealed class CommandDescriptor<TCommandId>
	where TCommandId : notnull
{
	/// <summary>
	/// Initializes a new instance of the <see cref="CommandDescriptor{TCommandId}"/> class.
	/// </summary>
	/// <param name="command">The command identity. Catalog construction rejects the <see langword="default"/> command value.</param>
	/// <param name="serializedId">The stable serialized identifier used for persistence.</param>
	/// <param name="isRemappable">The value indicating whether the user is permitted to remap this command.</param>
	/// <param name="isHostReserved">The value indicating whether the host reserves this command and prevents it from being remapped.</param>
	/// <param name="defaultBindings">The default bindings defined by the application.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="serializedId"/> or <paramref name="defaultBindings"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="serializedId"/> is empty.</exception>
	public CommandDescriptor(
		TCommandId command,
		string serializedId,
		bool isRemappable,
		bool isHostReserved,
		params KeyCombo[] defaultBindings)
	{
		ArgumentException.ThrowIfNullOrEmpty(serializedId);
		ArgumentNullException.ThrowIfNull(defaultBindings);

		Command = command;
		SerializedId = serializedId;
		IsRemappable = isRemappable;
		IsHostReserved = isHostReserved;

		// The bindings are copied so later mutation of the caller's array cannot alter the descriptor.
		DefaultBindings = Array.AsReadOnly<KeyCombo>([.. defaultBindings]);
	}

	/// <summary>
	/// Gets the command this descriptor represents.
	/// </summary>
	/// <remarks>
	/// Catalog construction rejects the <see langword="default"/> command value, such as <c>None</c> for an
	/// enum identity.
	/// </remarks>
	public TCommandId Command { get; }

	/// <summary>
	/// Gets the stable identifier associated with this command in persisted overrides.
	/// </summary>
	public string SerializedId { get; }

	/// <summary>
	/// Gets the default key combos used when no override applies.
	/// </summary>
	public IReadOnlyList<KeyCombo> DefaultBindings { get; }

	/// <summary>
	/// Gets a value indicating whether the command may be proposed for remapping.
	/// </summary>
	/// <remarks>Validation rejects proposed binding sets for commands that are not remappable.</remarks>
	public bool IsRemappable { get; }

	/// <summary>
	/// Gets a value indicating whether this command is reserved by the host.
	/// </summary>
	/// <remarks>
	/// Validation rejects proposed binding sets for reserved commands, and every loaded override is
	/// ignored in favor of the catalog defaults, including an empty binding list that would unbind a
	/// remappable command.
	/// </remarks>
	public bool IsHostReserved { get; }
}

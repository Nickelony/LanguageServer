namespace Nickelony.IDEKit.KeyBindings;

/// <summary>
/// Catalog of commands that can participate in key bindings.
/// Each entry supplies a command identity, stable identifier, default bindings,
/// and remapping policy.
/// </summary>
/// <typeparam name="TCommandId">The command identity type.</typeparam>
public sealed class CommandCatalog<TCommandId>
	where TCommandId : notnull
{
	private readonly Dictionary<TCommandId, CommandDescriptor<TCommandId>> _descriptorsByCommand;

	/// <summary>
	/// Initializes a new instance of the <see cref="CommandCatalog{TCommandId}"/> class.
	/// </summary>
	/// <param name="descriptors">The descriptors to include in the catalog.</param>
	/// <exception cref="ArgumentNullException"><paramref name="descriptors"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// A descriptor uses the <see langword="default"/> command value, or a command identity or serialized identifier is repeated.
	/// </exception>
	public CommandCatalog(IReadOnlyList<CommandDescriptor<TCommandId>> descriptors)
	{
		ArgumentNullException.ThrowIfNull(descriptors);

		_descriptorsByCommand = new Dictionary<TCommandId, CommandDescriptor<TCommandId>>(descriptors.Count);
		var serializedIds = new HashSet<string>(StringComparer.Ordinal);

		foreach (CommandDescriptor<TCommandId> descriptor in descriptors)
		{
			if (EqualityComparer<TCommandId>.Default.Equals(descriptor.Command, default))
				throw new ArgumentException("The default command value must not be cataloged.", nameof(descriptors));

			if (_descriptorsByCommand.ContainsKey(descriptor.Command))
				throw new ArgumentException($"Duplicate command in catalog: {descriptor.Command}.", nameof(descriptors));

			if (!serializedIds.Add(descriptor.SerializedId))
				throw new ArgumentException($"Duplicate serialized ID in catalog: {descriptor.SerializedId}.", nameof(descriptors));

			_descriptorsByCommand[descriptor.Command] = descriptor;
		}
	}

	/// <summary>
	/// Gets all descriptors in the catalog.
	/// </summary>
	/// <remarks>Enumeration order is not specified.</remarks>
	public IReadOnlyCollection<CommandDescriptor<TCommandId>> Descriptors => _descriptorsByCommand.Values;

	/// <summary>
	/// Gets a descriptor by command identity.
	/// </summary>
	/// <param name="command">The command identity to resolve.</param>
	/// <returns>The matching descriptor, or <see langword="null"/> when the command is not cataloged.</returns>
	public CommandDescriptor<TCommandId>? TryGetDescriptor(TCommandId command)
	{
		_descriptorsByCommand.TryGetValue(command, out CommandDescriptor<TCommandId>? descriptor);
		return descriptor;
	}
}

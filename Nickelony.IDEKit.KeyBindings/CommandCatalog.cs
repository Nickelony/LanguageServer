namespace Nickelony.IDEKit.KeyBindings;

/// <summary>
/// Catalog of all commands that can participate in key bindings.
/// The catalog supplies command descriptors and their default bindings to the
/// binding service and other command surfaces.
/// </summary>
public sealed class CommandCatalog<TCommandId>
	where TCommandId : notnull
{
	private readonly Dictionary<TCommandId, CommandDescriptor<TCommandId>> _descriptorsByCommand;
	private readonly Dictionary<string, CommandDescriptor<TCommandId>> _descriptorsById;

	/// <summary>
	/// Creates a catalog from the supplied descriptors. Construction rejects
	/// <see langword="default"/> command values, duplicate command identities, and
	/// duplicate serialized identifiers.
	/// </summary>
	/// <param name="descriptors">The descriptors to include in the catalog.</param>
	public CommandCatalog(IReadOnlyList<CommandDescriptor<TCommandId>> descriptors)
	{
		ArgumentNullException.ThrowIfNull(descriptors);

		_descriptorsByCommand = new Dictionary<TCommandId, CommandDescriptor<TCommandId>>(descriptors.Count);
		_descriptorsById = new Dictionary<string, CommandDescriptor<TCommandId>>(descriptors.Count, StringComparer.Ordinal);

		foreach (CommandDescriptor<TCommandId> descriptor in descriptors)
		{
			if (EqualityComparer<TCommandId>.Default.Equals(descriptor.Command, default))
				throw new ArgumentException("The default command value must not be catalogued.", nameof(descriptors));

			if (_descriptorsByCommand.ContainsKey(descriptor.Command))
				throw new ArgumentException($"Duplicate command in catalog: {descriptor.Command}.", nameof(descriptors));

			if (_descriptorsById.ContainsKey(descriptor.SerializedId))
				throw new ArgumentException($"Duplicate serialized ID in catalog: {descriptor.SerializedId}.", nameof(descriptors));

			_descriptorsByCommand[descriptor.Command] = descriptor;
			_descriptorsById[descriptor.SerializedId] = descriptor;
		}
	}

	/// <summary>
	/// Gets a read-only view of all catalog entries.
	/// </summary>
	public IReadOnlyCollection<CommandDescriptor<TCommandId>> Descriptors => _descriptorsByCommand.Values;

	/// <summary>
	/// Gets a descriptor by command identity. Returns <see langword="null"/> when
	/// the command is not catalogued.
	/// </summary>
	public CommandDescriptor<TCommandId>? TryGetDescriptor(TCommandId command)
	{
		_descriptorsByCommand.TryGetValue(command, out CommandDescriptor<TCommandId>? descriptor);
		return descriptor;
	}

	/// <summary>
	/// Gets a descriptor by its stable serialized identifier. Returns <see langword="null"/>
	/// when the identifier is unknown.
	/// </summary>
	public CommandDescriptor<TCommandId>? TryGetDescriptorById(string serializedId)
	{
		_descriptorsById.TryGetValue(serializedId, out CommandDescriptor<TCommandId>? descriptor);
		return descriptor;
	}

	/// <summary>
	/// Finds duplicate use of a default key binding, including repeated bindings within
	/// one descriptor. Returns violation descriptions, or an empty list when no duplicates exist.
	/// </summary>
	public IReadOnlyList<string> ValidateNoDuplicateDefaults()
	{
		var violations = new List<string>();
		var seen = new Dictionary<KeyCombo, TCommandId>();

		foreach (CommandDescriptor<TCommandId> descriptor in _descriptorsByCommand.Values)
		{
			foreach (KeyCombo binding in descriptor.DefaultBindings)
			{
				if (seen.TryGetValue(binding, out TCommandId? existingCommand))
				{
					violations.Add(
						$"Default key binding {binding.GetDisplayText()} is used by both " +
						$"{descriptor.Command} and {existingCommand}.");
				}
				else
				{
					seen[binding] = descriptor.Command;
				}
			}
		}

		return violations;
	}
}

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;

namespace Nickelony.IDEKit.KeyBindings;

/// <summary>
/// Runtime key binding service.
/// Merges catalog defaults with loaded overrides and provides lookup, display,
/// validation, and mutation operations.
/// </summary>
/// <typeparam name="TCommandId">The command identity type.</typeparam>
public sealed class KeyBindingService<TCommandId> : IKeyBindingService<TCommandId>
	where TCommandId : notnull
{
	// Key-binding diagnostics occupy the package log event id block 1-4.
	private static readonly Action<ILogger, string, Exception?> s_logHostReservedOverrideIgnored = LoggerMessage.Define<string>(
		LogLevel.Warning,
		new EventId(1, "HostReservedOverrideIgnored"),
		"Key binding override for host-reserved command '{CommandId}' ignored. Using catalog defaults.");

	private static readonly Action<ILogger, string, Exception?> s_logAllOverrideBindingsInvalid = LoggerMessage.Define<string>(
		LogLevel.Warning,
		new EventId(2, "AllOverrideBindingsInvalid"),
		"All override bindings for command '{CommandId}' were invalid. Falling back to catalog defaults.");

	private static readonly Action<ILogger, string, Exception?> s_logEmptyKeyName = LoggerMessage.Define<string>(
		LogLevel.Warning,
		new EventId(3, "EmptyKeyName"),
		"Key binding override for '{CommandId}' has an empty key name. Skipping.");

	private static readonly Action<ILogger, string, string, Exception?> s_logInvalidKeyName = LoggerMessage.Define<string, string>(
		LogLevel.Warning,
		new EventId(4, "InvalidKeyName"),
		"Key binding override for '{CommandId}' has invalid key name '{KeyName}'. Skipping.");

	private readonly ILogger _logger;
	private readonly CommandCatalog<TCommandId> _catalog;
	private readonly Func<KeyBindingOverrideCollection, bool> _saveOverrides;
	private readonly KeyBindingOverrideCollection _appliedOverrides;

	private PublishedMaps _published;

	/// <summary>
	/// Initializes a new instance of the <see cref="KeyBindingService{TCommandId}"/> class.
	/// </summary>
	/// <param name="catalog">The command catalog that supplies descriptors and default bindings.</param>
	/// <param name="loadedOverrides">The loaded overrides for the active workspace. This instance is updated in place when a service operation changes runtime state.</param>
	/// <param name="saveOverrides">The callback that receives each proposed override snapshot and returns <see langword="true"/> when persistence succeeds. Reset operations do not inspect the result.</param>
	/// <param name="logger">The optional logger for ignored or invalid override diagnostics.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="catalog"/>, <paramref name="loadedOverrides"/>, or <paramref name="saveOverrides"/> is
	/// <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">The catalog defaults or loaded overrides produce a key combo collision.</exception>
	public KeyBindingService(
		CommandCatalog<TCommandId> catalog,
		KeyBindingOverrideCollection loadedOverrides,
		Func<KeyBindingOverrideCollection, bool> saveOverrides,
		ILogger? logger = null)
	{
		ArgumentNullException.ThrowIfNull(catalog);
		ArgumentNullException.ThrowIfNull(loadedOverrides);
		ArgumentNullException.ThrowIfNull(saveOverrides);

		_logger = logger ?? NullLogger.Instance;
		_catalog = catalog;
		_saveOverrides = saveOverrides;
		_appliedOverrides = loadedOverrides;

		_published = new(
			ImmutableDictionary<TCommandId, ImmutableArray<KeyCombo>>.Empty,
			ImmutableDictionary<KeyCombo, TCommandId>.Empty);

		Rebuild();
	}

	/// <inheritdoc cref="IKeyBindingService{TCommandId}.BindingsChanged"/>
	public event EventHandler? BindingsChanged;

	/// <inheritdoc cref="IKeyBindingService{TCommandId}.TryGetCommand"/>
	public bool TryGetCommand(KeyCombo shortcut, [NotNullWhen(true)] out TCommandId? command)
		=> _published.CommandsByKeyCombo.TryGetValue(shortcut, out command);

	/// <inheritdoc cref="IKeyBindingService{TCommandId}.GetBindings"/>
	public IReadOnlyList<KeyCombo> GetBindings(TCommandId command)
	{
		if (_published.BindingsByCommand.TryGetValue(command, out ImmutableArray<KeyCombo> bindings))
			return bindings;

		return [];
	}

	/// <inheritdoc cref="IKeyBindingService{TCommandId}.GetDisplayText"/>
	public string GetDisplayText(TCommandId command, string fallbackDisplayText = "")
	{
		ArgumentNullException.ThrowIfNull(fallbackDisplayText);

		IReadOnlyList<KeyCombo> bindings = GetBindings(command);

		if (bindings.Count == 0)
			return fallbackDisplayText;

		return string.Join(" / ", bindings.Select(b => b.GetDisplayText()));
	}

	/// <inheritdoc cref="IKeyBindingService{TCommandId}.Validate"/>
	public KeyBindingValidationResult Validate(TCommandId command, IReadOnlyList<KeyCombo> bindings)
	{
		ArgumentNullException.ThrowIfNull(bindings);

		CommandDescriptor<TCommandId>? descriptor = _catalog.TryGetDescriptor(command);

		if (descriptor is null)
			return KeyBindingValidationResult.NotRemappable;

		if (descriptor.IsHostReserved)
			return KeyBindingValidationResult.Reserved;

		if (!descriptor.IsRemappable)
			return KeyBindingValidationResult.NotRemappable;

		var seen = new HashSet<KeyCombo>();

		foreach (KeyCombo binding in bindings)
		{
			if (!seen.Add(binding))
				return KeyBindingValidationResult.DuplicateInCommand;
		}

		foreach (KeyCombo binding in bindings)
		{
			if (_published.CommandsByKeyCombo.TryGetValue(binding, out TCommandId? existingCommand) &&
				!EqualityComparer<TCommandId>.Default.Equals(existingCommand, command))
			{
				return KeyBindingValidationResult.Conflict;
			}
		}

		return KeyBindingValidationResult.Valid;
	}

	/// <inheritdoc cref="IKeyBindingService{TCommandId}.Reset"/>
	public void Reset(TCommandId command)
	{
		// Overrides are keyed by the descriptor's stable serialized identifier, so the reset must
		// resolve the same identifier instead of the command's display string.
		CommandDescriptor<TCommandId>? descriptor = _catalog.TryGetDescriptor(command);

		CommitSnapshot(BuildSnapshotWithoutOverride(descriptor?.SerializedId ?? command.ToString() ?? string.Empty));
	}

	/// <inheritdoc cref="IKeyBindingService{TCommandId}.ResetAll"/>
	public void ResetAll()
		=> CommitSnapshot(new KeyBindingOverrideCollection());

	/// <summary>
	/// Detaches every <see cref="BindingsChanged"/> subscriber. The service owns no other resources,
	/// remains usable after disposal, and repeated calls are no-ops.
	/// </summary>
	public void Dispose()
	{
		BindingsChanged = null;
	}

	private void Rebuild()
	{
		var bindingsBuilder = ImmutableDictionary.CreateBuilder<TCommandId, ImmutableArray<KeyCombo>>();
		var commandsBuilder = ImmutableDictionary.CreateBuilder<KeyCombo, TCommandId>();

		foreach (CommandDescriptor<TCommandId> descriptor in _catalog.Descriptors)
		{
			IReadOnlyList<KeyCombo> bindings = ResolveBindings(descriptor);
			bindingsBuilder[descriptor.Command] = [.. bindings];

			foreach (KeyCombo binding in bindings)
			{
				if (commandsBuilder.TryGetValue(binding, out TCommandId? existingCommand))
				{
					throw new InvalidOperationException(
						$"Key binding collision detected: {binding.GetDisplayText()} is bound to both " +
						$"{existingCommand} and {descriptor.Command}.");
				}

				commandsBuilder[binding] = descriptor.Command;
			}
		}

		_published = new(bindingsBuilder.ToImmutable(), commandsBuilder.ToImmutable());
	}

	private void RebuildAndNotify()
	{
		Rebuild();
		BindingsChanged?.Invoke(this, EventArgs.Empty);
	}

	private IReadOnlyList<KeyCombo> ResolveBindings(CommandDescriptor<TCommandId> descriptor)
	{
		KeyBindingOverrideEntry? overrideEntry = _appliedOverrides.Overrides
			.FirstOrDefault(o => string.Equals(o.CommandId, descriptor.SerializedId, StringComparison.Ordinal));

		if (overrideEntry is null)
			return descriptor.DefaultBindings;

		// A host-reserved command ignores every loaded override, including an empty binding list
		// that would otherwise explicitly unbind it.
		if (descriptor.IsHostReserved)
		{
			s_logHostReservedOverrideIgnored(_logger, descriptor.SerializedId, null);
			return descriptor.DefaultBindings;
		}

		if (overrideEntry.Bindings.Count == 0)
			return []; // An empty override explicitly unbinds the command.

		// Parse the stored key names and modifier values.

		var parsed = new List<KeyCombo>();

		foreach (KeyBindingSettings bindingSettings in overrideEntry.Bindings)
		{
			KeyCombo? parsedKey = ParseBindingSettings(bindingSettings, descriptor.SerializedId);

			if (parsedKey is not null)
				parsed.Add(parsedKey.Value);
		}

		if (parsed.Count == 0)
		{
			s_logAllOverrideBindingsInvalid(_logger, descriptor.SerializedId, null);
			return descriptor.DefaultBindings;
		}

		return parsed;
	}

	private KeyCombo? ParseBindingSettings(KeyBindingSettings settings, string commandId)
	{
		if (string.IsNullOrEmpty(settings.KeyName))
		{
			s_logEmptyKeyName(_logger, commandId, null);
			return null;
		}

		// Enum.TryParse accepts numeric strings for any value; only defined keys are bindable.
		if (!Enum.TryParse(settings.KeyName, out Key key) || key == Key.None || !Enum.IsDefined(key))
		{
			s_logInvalidKeyName(_logger, commandId, settings.KeyName, null);
			return null;
		}

		return new KeyCombo(key, (ModifierKeys)settings.Modifiers);
	}

	private void UpdateOverridesInPlace(KeyBindingOverrideCollection newOverrides)
	{
		_appliedOverrides.Overrides.Clear();
		_appliedOverrides.Version = newOverrides.Version;

		foreach (KeyBindingOverrideEntry entry in newOverrides.Overrides)
			_appliedOverrides.Overrides.Add(entry);
	}

	private KeyBindingOverrideCollection BuildSnapshotWithoutOverride(string serializedId)
	{
		var snapshot = new KeyBindingOverrideCollection();

		foreach (KeyBindingOverrideEntry existing in _appliedOverrides.Overrides)
		{
			if (!string.Equals(existing.CommandId, serializedId, StringComparison.Ordinal))
				snapshot.Overrides.Add(CloneEntry(existing));
		}

		return snapshot;
	}

	private static KeyBindingOverrideEntry CloneEntry(KeyBindingOverrideEntry entry)
	{
		var clone = new KeyBindingOverrideEntry
		{
			CommandId = entry.CommandId,
			Bindings = [.. entry.Bindings.Select(b => new KeyBindingSettings { KeyName = b.KeyName, Modifiers = b.Modifiers })]
		};

		return clone;
	}

	private void CommitSnapshot(KeyBindingOverrideCollection snapshot)
	{
		_saveOverrides(snapshot);
		UpdateOverridesInPlace(snapshot);
		RebuildAndNotify();
	}

	/// <summary>
	/// The runtime lookup maps published as one immutable snapshot, so a concurrent reader never
	/// observes the command-to-bindings map and the bindings-to-command map from different rebuilds.
	/// </summary>
	private sealed record PublishedMaps(
		ImmutableDictionary<TCommandId, ImmutableArray<KeyCombo>> BindingsByCommand,
		ImmutableDictionary<KeyCombo, TCommandId> CommandsByKeyCombo);
}

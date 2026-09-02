using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nickelony.IDEKit.KeyBindings;

/// <summary>
/// Runtime key binding service.
/// Merges catalog defaults with loaded overrides and provides lookup, display,
/// validation, and mutation operations.
/// </summary>
public sealed class KeyBindingService<TCommandId> : IKeyBindingService<TCommandId>
	where TCommandId : notnull
{
	private static readonly Action<ILogger, string, Exception?> s_logHostReservedOverrideIgnored = LoggerMessage.Define<string>(
		LogLevel.Warning,
		new EventId(0),
		"Key binding override for host-reserved command '{CommandId}' ignored. Using catalog defaults.");

	private static readonly Action<ILogger, string, Exception?> s_logAllOverrideBindingsInvalid = LoggerMessage.Define<string>(
		LogLevel.Warning,
		new EventId(0),
		"All override bindings for command '{CommandId}' were invalid. Falling back to catalog defaults.");

	private static readonly Action<ILogger, string, Exception?> s_logEmptyKeyName = LoggerMessage.Define<string>(
		LogLevel.Warning,
		new EventId(0),
		"Key binding override for '{CommandId}' has an empty key name. Skipping.");

	private static readonly Action<ILogger, string, string, Exception?> s_logInvalidKeyName = LoggerMessage.Define<string, string>(
		LogLevel.Warning,
		new EventId(0),
		"Key binding override for '{CommandId}' has invalid key name '{KeyName}'. Skipping.");

	private static readonly Action<ILogger, string, string, Exception?> s_logBindingParseFailed = LoggerMessage.Define<string, string>(
		LogLevel.Warning,
		new EventId(0),
		"Key binding override for '{CommandId}' could not be parsed: {Message}. Skipping.");

	private readonly ILogger _logger;
	private readonly CommandCatalog<TCommandId> _catalog;
	private readonly Func<KeyBindingOverrideCollection, bool> _saveOverrides;
	private readonly KeyBindingOverrideCollection _appliedOverrides;

	private ImmutableDictionary<TCommandId, ImmutableArray<KeyCombo>> _bindingsByCommand;
	private ImmutableDictionary<KeyCombo, TCommandId> _commandsByKeyCombo;

	/// <summary>
	/// Creates the service, merging the catalog defaults with the loaded overrides.
	/// </summary>
	/// <param name="catalog">The command catalog that supplies descriptors and default bindings.</param>
	/// <param name="loadedOverrides">The loaded overrides for the active workspace. The instance is updated in place when a service operation updates runtime state.</param>
	/// <param name="saveOverrides">Receives each proposed override snapshot and returns <see langword="true"/> when persistence succeeds. Apply and clear honor this result; reset operations do not.</param>
	/// <param name="logger">Optional logger for ignored or invalid override diagnostics.</param>
	public KeyBindingService(
		CommandCatalog<TCommandId> catalog,
		KeyBindingOverrideCollection loadedOverrides,
		Func<KeyBindingOverrideCollection, bool> saveOverrides,
		ILogger? logger = null)
	{
		_logger = logger ?? NullLogger.Instance;
		_catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
		_saveOverrides = saveOverrides ?? throw new ArgumentNullException(nameof(saveOverrides));
		_appliedOverrides = loadedOverrides ?? throw new ArgumentNullException(nameof(loadedOverrides));

		_bindingsByCommand = ImmutableDictionary<TCommandId, ImmutableArray<KeyCombo>>.Empty;
		_commandsByKeyCombo = ImmutableDictionary<KeyCombo, TCommandId>.Empty;

		Rebuild();
	}

	/// <inheritdoc cref="IKeyBindingService{TCommandId}.BindingsChanged"/>
	public event EventHandler? BindingsChanged;

	/// <inheritdoc cref="IKeyBindingService{TCommandId}.TryGetCommand"/>
	public bool TryGetCommand(KeyCombo shortcut, [NotNullWhen(true)] out TCommandId? command)
		=> _commandsByKeyCombo.TryGetValue(shortcut, out command);

	/// <inheritdoc cref="IKeyBindingService{TCommandId}.GetBindings"/>
	public IReadOnlyList<KeyCombo> GetBindings(TCommandId command)
	{
		if (_bindingsByCommand.TryGetValue(command, out ImmutableArray<KeyCombo> bindings))
			return bindings;

		return [];
	}

	/// <inheritdoc cref="IKeyBindingService{TCommandId}.GetDisplayText"/>
	public string GetDisplayText(TCommandId command, string fallbackDisplayText = "")
	{
		IReadOnlyList<KeyCombo> bindings = GetBindings(command);

		if (bindings.Count == 0)
			return fallbackDisplayText;

		return string.Join(" / ", bindings.Select(b => b.GetDisplayText()));
	}

	/// <inheritdoc cref="IKeyBindingService{TCommandId}.Validate"/>
	public KeyBindingValidationResult Validate(TCommandId command, IReadOnlyList<KeyCombo> bindings)
		=> ValidateInternal(command, bindings, checkConflicts: true);

	/// <inheritdoc cref="IKeyBindingService{TCommandId}.Apply"/>
	public KeyBindingValidationResult Apply(TCommandId command, IReadOnlyList<KeyCombo> bindings, bool replaceConflicts)
	{
		KeyBindingValidationResult result = ValidateInternal(command, bindings, checkConflicts: !replaceConflicts);

		if (result != KeyBindingValidationResult.Valid && result != KeyBindingValidationResult.Conflict)
			return result;

		if (result == KeyBindingValidationResult.Conflict && !replaceConflicts)
			return KeyBindingValidationResult.Conflict;

		// Build a new override snapshot from the currently applied entries.
		var newOverrides = new KeyBindingOverrideCollection();

		foreach (KeyBindingOverrideEntry existing in _appliedOverrides.Overrides)
		{
			if (!string.Equals(existing.CommandId, GetSerializedId(command), StringComparison.Ordinal))
			{
				newOverrides.Overrides.Add(new KeyBindingOverrideEntry
				{
					CommandId = existing.CommandId,
					Bindings = [.. existing.Bindings.Select(b => new KeyBindingSettings { KeyName = b.KeyName, Modifiers = b.Modifiers })]
				});
			}
		}

		// Add or replace the target command's override.
		var settingsList = bindings
			.Select(b => new KeyBindingSettings { KeyName = b.Key.ToString(), Modifiers = (int)b.Modifiers })
			.ToList();

		newOverrides.Overrides.Add(new KeyBindingOverrideEntry
		{
			CommandId = GetSerializedId(command),
			Bindings = settingsList
		});

		// Remove matching bindings from each persisted override entry.
		if (replaceConflicts)
			RemoveConflictingOverrides(newOverrides, bindings);

		// Persist before publishing the new in-memory maps.
		if (!_saveOverrides(newOverrides))
			return KeyBindingValidationResult.Conflict; // A save failure leaves runtime state unchanged.

		// Publish the persisted state in memory.
		UpdateOverridesInPlace(newOverrides);
		RebuildAndNotify();

		return KeyBindingValidationResult.Valid;
	}

	/// <inheritdoc cref="IKeyBindingService{TCommandId}.Clear"/>
	public KeyBindingValidationResult Clear(TCommandId command)
	{
		CommandDescriptor<TCommandId>? descriptor = _catalog.TryGetDescriptor(command);

		if (descriptor is null)
			return KeyBindingValidationResult.NotRemappable;

		if (descriptor.IsHostReserved)
			return KeyBindingValidationResult.Reserved;

		if (!descriptor.IsRemappable)
			return KeyBindingValidationResult.NotRemappable;

		// Build a new snapshot with an explicit empty override for this command.
		var newOverrides = new KeyBindingOverrideCollection();

		foreach (KeyBindingOverrideEntry existing in _appliedOverrides.Overrides)
		{
			if (!string.Equals(existing.CommandId, descriptor.SerializedId, StringComparison.Ordinal))
			{
				newOverrides.Overrides.Add(new KeyBindingOverrideEntry
				{
					CommandId = existing.CommandId,
					Bindings = [.. existing.Bindings.Select(b => new KeyBindingSettings { KeyName = b.KeyName, Modifiers = b.Modifiers })]
				});
			}
		}

		newOverrides.Overrides.Add(new KeyBindingOverrideEntry
		{
			CommandId = descriptor.SerializedId,
			Bindings = [] // An empty list explicitly unbinds the command.
		});

		if (!_saveOverrides(newOverrides))
			return KeyBindingValidationResult.Conflict;

		UpdateOverridesInPlace(newOverrides);
		RebuildAndNotify();

		return KeyBindingValidationResult.Valid;
	}

	/// <inheritdoc cref="IKeyBindingService{TCommandId}.Reset"/>
	public void Reset(TCommandId command)
	{
		// Build a new snapshot without the selected command's override.
		var newOverrides = new KeyBindingOverrideCollection();

		foreach (KeyBindingOverrideEntry existing in _appliedOverrides.Overrides)
		{
			if (!string.Equals(existing.CommandId, GetSerializedId(command), StringComparison.Ordinal))
			{
				newOverrides.Overrides.Add(new KeyBindingOverrideEntry
				{
					CommandId = existing.CommandId,
					Bindings = [.. existing.Bindings.Select(b => new KeyBindingSettings { KeyName = b.KeyName, Modifiers = b.Modifiers })]
				});
			}
		}

		_saveOverrides(newOverrides);
		UpdateOverridesInPlace(newOverrides);
		RebuildAndNotify();
	}

	/// <inheritdoc cref="IKeyBindingService{TCommandId}.ResetAll"/>
	public void ResetAll()
	{
		var newOverrides = new KeyBindingOverrideCollection();
		_saveOverrides(newOverrides);
		UpdateOverridesInPlace(newOverrides);
		RebuildAndNotify();
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		BindingsChanged = null;
	}

	private KeyBindingValidationResult ValidateInternal(TCommandId command, IReadOnlyList<KeyCombo> bindings, bool checkConflicts)
	{
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

		if (checkConflicts)
		{
			foreach (KeyCombo binding in bindings)
			{
				if (_commandsByKeyCombo.TryGetValue(binding, out TCommandId? existingCommand) &&
					!EqualityComparer<TCommandId>.Default.Equals(existingCommand, command))
				{
					return KeyBindingValidationResult.Conflict;
				}
			}
		}

		return KeyBindingValidationResult.Valid;
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

		_bindingsByCommand = bindingsBuilder.ToImmutable();
		_commandsByKeyCombo = commandsBuilder.ToImmutable();
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

		if (overrideEntry.Bindings.Count == 0)
			return []; // An empty override explicitly unbinds the command.

		// Validate and deserialize the stored binding entries.
		if (descriptor.IsHostReserved)
		{
			s_logHostReservedOverrideIgnored(_logger, descriptor.SerializedId, null);
			return descriptor.DefaultBindings;
		}

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

		if (!Enum.TryParse(settings.KeyName, out Key key) || key == Key.None)
		{
			s_logInvalidKeyName(_logger, commandId, settings.KeyName, null);
			return null;
		}

		try
		{
			return new KeyCombo(key, (ModifierKeys)settings.Modifiers);
		}
		catch (ArgumentException ex)
		{
			s_logBindingParseFailed(_logger, commandId, ex.Message, null);
			return null;
		}
	}

	private void UpdateOverridesInPlace(KeyBindingOverrideCollection newOverrides)
	{
		_appliedOverrides.Overrides.Clear();
		_appliedOverrides.Version = newOverrides.Version;

		foreach (KeyBindingOverrideEntry entry in newOverrides.Overrides)
			_appliedOverrides.Overrides.Add(entry);
	}

	private void RemoveConflictingOverrides(
		KeyBindingOverrideCollection newOverrides,
		IReadOnlyList<KeyCombo> newBindings)
	{
		var newBindingSet = new HashSet<KeyCombo>(newBindings);

		foreach (KeyBindingOverrideEntry entry in newOverrides.Overrides)
		{
			entry.Bindings.RemoveAll(bs =>
			{
				KeyCombo? parsed = ParseBindingSettings(bs, entry.CommandId);
				return parsed is not null && newBindingSet.Contains(parsed.Value);
			});
		}
	}

	private static string GetSerializedId(TCommandId command)
		=> command.ToString() ?? string.Empty;
}

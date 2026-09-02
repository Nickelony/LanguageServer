using System.Diagnostics.CodeAnalysis;
using System.Xml.Serialization;

namespace Nickelony.IDEKit.KeyBindings;

/// <summary>
/// Versioned, XML-serializable collection of key binding overrides.
/// A host can store an instance as part of its workspace settings document.
/// </summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "The type is the persisted collection model and its public name is part of the settings API.")]
public sealed class KeyBindingOverrideCollection
{
	/// <summary>
	/// Creates an empty override collection at schema version 1.
	/// </summary>
	public KeyBindingOverrideCollection()
	{
		Version = 1;
		Overrides = [];
	}

	/// <summary>
	/// Persisted schema version marker.
	/// </summary>
	[XmlAttribute("Version")]
	public int Version { get; set; }

	/// <summary>
	/// Per-command override entries. An empty <see cref="KeyBindingOverrideEntry.Bindings"/>
	/// list means the user explicitly unbound the command.
	/// </summary>
	[XmlElement("Command")]
	public List<KeyBindingOverrideEntry> Overrides { get; set; }
}

/// <summary>
/// A single command override entry in the persisted key binding settings.
/// </summary>
public sealed class KeyBindingOverrideEntry
{
	/// <summary>
	/// Creates an override entry with an empty command identifier and binding list.
	/// </summary>
	public KeyBindingOverrideEntry()
	{
		CommandId = string.Empty;
		Bindings = [];
	}

	/// <summary>
	/// Stable serialized command identifier used to match a catalog descriptor (for example, "Save").
	/// </summary>
	[XmlAttribute("Id")]
	public string CommandId { get; set; }

	/// <summary>
	/// The binding entries for this command.
	/// An empty list means the command is explicitly unbound.
	/// </summary>
	[XmlElement("Binding")]
	public List<KeyBindingSettings> Bindings { get; set; }
}

/// <summary>
/// A single key binding in the persisted key binding settings.
/// Never contains display text - that is calculated at runtime.
/// </summary>
public sealed class KeyBindingSettings
{
	/// <summary>
	/// Creates a binding setting with an empty key name.
	/// </summary>
	public KeyBindingSettings()
	{
		KeyName = string.Empty;
	}

	/// <summary>
	/// The <see cref="System.Windows.Input.Key"/> enum name (e.g. "S", "F9", "OemQuestion").
	/// </summary>
	[XmlAttribute("Key")]
	public string KeyName { get; set; }

	/// <summary>
	/// The numeric value of the modifier flags represented by
	/// <see cref="System.Windows.Input.ModifierKeys"/>.
	/// </summary>
	[XmlAttribute("Modifiers")]
	public int Modifiers { get; set; }
}

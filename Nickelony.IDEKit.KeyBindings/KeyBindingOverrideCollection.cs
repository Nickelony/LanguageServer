using System.Diagnostics.CodeAnalysis;
using System.Xml.Serialization;

namespace Nickelony.IDEKit.KeyBindings;

/// <summary>
/// XML-serializable collection of key binding overrides with a schema version.
/// A host can include an instance in its workspace settings document.
/// </summary>
/// <example>
/// <code><![CDATA[
/// <KeyBindingOverrideCollection Version="1">
///   <Command Id="Save">
///     <Binding Key="S" Modifiers="2" />
///   </Command>
/// </KeyBindingOverrideCollection>
/// ]]></code>
/// </example>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "The type is the persisted collection model and its public name is part of the settings API.")]
public sealed class KeyBindingOverrideCollection
{
	/// <summary>
	/// Creates an empty override collection with schema version 1.
	/// </summary>
	public KeyBindingOverrideCollection()
	{
		Version = 1;
		Overrides = [];
	}

	/// <summary>
	/// Schema version marker written as the <c>Version</c> XML attribute.
	/// </summary>
	[XmlAttribute("Version")]
	public int Version { get; set; }

	/// <summary>
	/// Per-command override entries. An empty <see cref="KeyBindingOverrideEntry.Bindings"/>
	/// list explicitly unbinds the matching command.
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
	/// Stable command identifier used to match a catalog descriptor. Matching is case-sensitive.
	/// </summary>
	[XmlAttribute("Id")]
	public string CommandId { get; set; }

	/// <summary>
	/// The binding entries for this command. An empty list explicitly unbinds the command.
	/// </summary>
	[XmlElement("Binding")]
	public List<KeyBindingSettings> Bindings { get; set; }
}

/// <summary>
/// Key and modifier values for one persisted binding.
/// Display text is calculated at runtime.
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
	/// The <see cref="System.Windows.Input.Key"/> enum name, such as <c>S</c>, <c>F9</c>, or
	/// <c>OemQuestion</c>.
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

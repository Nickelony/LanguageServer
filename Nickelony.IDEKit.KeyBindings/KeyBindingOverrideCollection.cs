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
	/// Initializes a new instance of the <see cref="KeyBindingOverrideCollection"/> class.
	/// </summary>
	public KeyBindingOverrideCollection()
	{
		Version = 1;
		Overrides = [];
	}

	/// <summary>
	/// Gets or sets the schema version marker written as the <c>Version</c> XML attribute.
	/// </summary>
	[XmlAttribute("Version")]
	public int Version { get; set; }

	/// <summary>
	/// Gets or sets the per-command override entries.
	/// </summary>
	/// <remarks>An empty <see cref="KeyBindingOverrideEntry.Bindings"/> list explicitly unbinds the matching command.</remarks>
	[XmlElement("Command")]
	public List<KeyBindingOverrideEntry> Overrides { get; set; }
}

/// <summary>
/// A single command override entry in the persisted key binding settings.
/// </summary>
public sealed class KeyBindingOverrideEntry
{
	/// <summary>
	/// Initializes a new instance of the <see cref="KeyBindingOverrideEntry"/> class.
	/// </summary>
	public KeyBindingOverrideEntry()
	{
		CommandId = string.Empty;
		Bindings = [];
	}

	/// <summary>
	/// Gets or sets the stable command identifier used to match a catalog descriptor.
	/// </summary>
	/// <remarks>Matching is case-sensitive.</remarks>
	[XmlAttribute("Id")]
	public string CommandId { get; set; }

	/// <summary>
	/// Gets or sets the binding entries for this command.
	/// </summary>
	/// <remarks>An empty list explicitly unbinds the command.</remarks>
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
	/// Initializes a new instance of the <see cref="KeyBindingSettings"/> class.
	/// </summary>
	public KeyBindingSettings()
	{
		KeyName = string.Empty;
	}

	/// <summary>
	/// Gets or sets the <see cref="System.Windows.Input.Key"/> enum name, such as <c>S</c>, <c>F9</c>, or
	/// <c>OemQuestion</c>.
	/// </summary>
	[XmlAttribute("Key")]
	public string KeyName { get; set; }

	/// <summary>
	/// Gets or sets the numeric value of the modifier flags represented by
	/// <see cref="System.Windows.Input.ModifierKeys"/>.
	/// </summary>
	[XmlAttribute("Modifiers")]
	public int Modifiers { get; set; }
}

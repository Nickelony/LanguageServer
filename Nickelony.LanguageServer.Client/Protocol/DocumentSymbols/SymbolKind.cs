using System.Diagnostics.CodeAnalysis;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Describes the kind of a document-symbol entry, using the LSP <c>SymbolKind</c> mapping.
/// </summary>
/// <remarks>
/// The numeric values match the protocol values and are serialized as numbers. A value outside the
/// defined range stays representable as an unnamed enum value so a newer server does not invalidate a
/// response; hosts that map the kind onto a library taxonomy choose their own fallback because the
/// protocol defines no default symbol kind.
/// </remarks>
public enum SymbolKind
{
	/// <summary>
	/// A file.
	/// </summary>
	File = 1,

	/// <summary>
	/// A module.
	/// </summary>
	Module = 2,

	/// <summary>
	/// A namespace.
	/// </summary>
	Namespace = 3,

	/// <summary>
	/// A package.
	/// </summary>
	Package = 4,

	/// <summary>
	/// A class.
	/// </summary>
	Class = 5,

	/// <summary>
	/// A method.
	/// </summary>
	Method = 6,

	/// <summary>
	/// A property.
	/// </summary>
	Property = 7,

	/// <summary>
	/// A field.
	/// </summary>
	Field = 8,

	/// <summary>
	/// A constructor.
	/// </summary>
	Constructor = 9,

	/// <summary>
	/// An enum.
	/// </summary>
	Enum = 10,

	/// <summary>
	/// An interface.
	/// </summary>
	Interface = 11,

	/// <summary>
	/// A function.
	/// </summary>
	Function = 12,

	/// <summary>
	/// A variable.
	/// </summary>
	Variable = 13,

	/// <summary>
	/// A constant.
	/// </summary>
	Constant = 14,

	/// <summary>
	/// A string.
	/// </summary>
	[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "The member name mirrors the LSP SymbolKind wire contract.")]
	String = 15,

	/// <summary>
	/// A number.
	/// </summary>
	Number = 16,

	/// <summary>
	/// A boolean.
	/// </summary>
	Boolean = 17,

	/// <summary>
	/// An array.
	/// </summary>
	Array = 18,

	/// <summary>
	/// An object.
	/// </summary>
	[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "The member name mirrors the LSP SymbolKind wire contract.")]
	Object = 19,

	/// <summary>
	/// A key.
	/// </summary>
	Key = 20,

	/// <summary>
	/// A null.
	/// </summary>
	Null = 21,

	/// <summary>
	/// An enum member.
	/// </summary>
	EnumMember = 22,

	/// <summary>
	/// A struct.
	/// </summary>
	Struct = 23,

	/// <summary>
	/// An event.
	/// </summary>
	Event = 24,

	/// <summary>
	/// An operator.
	/// </summary>
	Operator = 25,

	/// <summary>
	/// A type parameter.
	/// </summary>
	TypeParameter = 26
}

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Describes the kind of a completion item, using the LSP <c>CompletionItemKind</c> mapping.
/// </summary>
/// <remarks>
/// The numeric values match the protocol values and are serialized as numbers. A value outside the
/// defined range stays representable as an unnamed enum value; the protocol defines no default kind, so
/// hosts that map the kind onto a library taxonomy choose their own fallback (the <c>Interop</c> bridge
/// maps values outside the protocol range to <c>TextCompletionItemKind.Generic</c>).
/// </remarks>
public enum CompletionItemKind
{
	/// <summary>
	/// A text snippet or plain word completion.
	/// </summary>
	Text = 1,

	/// <summary>
	/// A method.
	/// </summary>
	Method = 2,

	/// <summary>
	/// A function.
	/// </summary>
	Function = 3,

	/// <summary>
	/// A constructor.
	/// </summary>
	Constructor = 4,

	/// <summary>
	/// A field.
	/// </summary>
	Field = 5,

	/// <summary>
	/// A variable.
	/// </summary>
	Variable = 6,

	/// <summary>
	/// A class.
	/// </summary>
	Class = 7,

	/// <summary>
	/// An interface.
	/// </summary>
	Interface = 8,

	/// <summary>
	/// A module.
	/// </summary>
	Module = 9,

	/// <summary>
	/// A property.
	/// </summary>
	Property = 10,

	/// <summary>
	/// A unit.
	/// </summary>
	Unit = 11,

	/// <summary>
	/// A value.
	/// </summary>
	Value = 12,

	/// <summary>
	/// An enumeration.
	/// </summary>
	Enum = 13,

	/// <summary>
	/// A keyword.
	/// </summary>
	Keyword = 14,

	/// <summary>
	/// A snippet.
	/// </summary>
	Snippet = 15,

	/// <summary>
	/// A color.
	/// </summary>
	Color = 16,

	/// <summary>
	/// A file.
	/// </summary>
	File = 17,

	/// <summary>
	/// A reference.
	/// </summary>
	Reference = 18,

	/// <summary>
	/// A folder.
	/// </summary>
	Folder = 19,

	/// <summary>
	/// An enumeration member.
	/// </summary>
	EnumMember = 20,

	/// <summary>
	/// A constant.
	/// </summary>
	Constant = 21,

	/// <summary>
	/// A struct.
	/// </summary>
	Struct = 22,

	/// <summary>
	/// An event.
	/// </summary>
	Event = 23,

	/// <summary>
	/// An operator.
	/// </summary>
	Operator = 24,

	/// <summary>
	/// A type parameter.
	/// </summary>
	TypeParameter = 25
}

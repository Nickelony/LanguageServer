namespace Nickelony.IDEKit.Core.Identifiers;

/// <summary>
/// Determines how an identifier span is located relative to an offset.
/// </summary>
public enum IdentifierAffinity
{
	/// <summary>
	/// Locates the token that contains the offset. When the offset falls on a non-token character,
	/// the token immediately before the boundary is preferred.
	/// </summary>
	Containing,

	/// <summary>
	/// Locates the token that ends exactly at the offset (the word being typed). The span never
	/// extends past the offset.
	/// </summary>
	BeforeCaret,
}

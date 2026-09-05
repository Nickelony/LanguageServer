namespace Nickelony.IDEKit.Core.Identifiers;

/// <summary>
/// Determines how an identifier span is located relative to an offset.
/// </summary>
public enum IdentifierAffinity
{
	/// <summary>
	/// Locates the token at the clamped offset. A preceding token-start character is preferred only
	/// when the offset itself is a non-token character and the policy identifies that preceding
	/// character as a token start.
	/// </summary>
	Containing,

	/// <summary>
	/// Locates a token ending at the offset (the word being typed). The character immediately before
	/// the offset must be accepted as a token-start character, and the span never extends past it.
	/// </summary>
	BeforeCaret,
}

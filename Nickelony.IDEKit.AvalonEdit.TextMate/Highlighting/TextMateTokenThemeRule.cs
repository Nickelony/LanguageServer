namespace Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting;

/// <summary>
/// Describes the visual style associated with one or more TextMate scopes.
/// </summary>
/// <remarks>
/// <para>
/// Instances are commonly produced by deserializing host theme data; the property names and values
/// form that serialized contract and should be kept stable. Deserializers should match property names
/// case-insensitively so that VS Code-style lower-camel names (for example <c>scope</c>,
/// <c>foreground</c>, and <c>fontStyle</c>) resolve to these properties.
/// </para>
/// <para>
/// Only the foreground color and the font traits are applied; a background color is not part of this
/// shape and is ignored.
/// </para>
/// </remarks>
public sealed record TextMateTokenThemeRule
{
	/// <summary>
	/// Gets or initializes the TextMate scope selector or comma-separated selectors matched by the rule.
	/// A blank or whitespace-only scope marks the rule as a theme defaults rule: its values apply to
	/// every token, which is how VS Code theme files carry their base foreground and font style.
	/// Otherwise a selector matches a token scope when the scope equals it or extends it with a
	/// dot-separated suffix. Space-separated selectors describe descendant scopes: the rightmost part
	/// matches the innermost scope of the matched path, and the remaining parts must match enclosing
	/// scopes in order. The exclusion (<c>-</c>), direct-child (<c>&gt;</c>), wildcard (<c>*</c>),
	/// priority (<c>L:</c>), and parenthesis selector operators are not supported; selectors that use
	/// them never match and are reported through the resolver's logger.
	/// </summary>
	public string Scope { get; init; } = string.Empty;

	/// <summary>
	/// Gets or initializes the foreground color of the rule as a color string. Leave empty to avoid
	/// changing the foreground. Invalid values are ignored and reported through the resolver's logger.
	/// </summary>
	/// <remarks>
	/// The WPF color formats are accepted, as are the TextMate eight-digit form <c>#RRGGBBAA</c> and the
	/// four-digit form <c>#RGBA</c>; the alpha component is normalized to the WPF <c>#AARRGGBB</c> order
	/// before parsing.
	/// </remarks>
	public string Foreground { get; init; } = string.Empty;

	/// <summary>
	/// Gets or initializes the space-separated font traits of the rule, or <see langword="null"/> to
	/// leave inherited traits unchanged. This property matches the <c>fontStyle</c> field of TextMate
	/// and VS Code theme data.
	/// </summary>
	/// <remarks>
	/// Any present value is an explicit reset first: an empty string clears bold, italic, underline, and
	/// strikethrough, and the recognized trait names then enable individual traits. Recognized values are
	/// case-sensitive: <c>bold</c>, <c>italic</c>, <c>underline</c>, and <c>strikethrough</c>; the
	/// non-standard <c>none</c> keyword is accepted as an explicit spelling of the reset. Unrecognized
	/// values are ignored and reported through the resolver's logger.
	/// </remarks>
	public string? FontStyle { get; init; }
}

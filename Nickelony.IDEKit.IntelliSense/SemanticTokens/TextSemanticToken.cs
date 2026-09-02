using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.IntelliSense.SemanticTokens;

/// <summary>
/// A style-neutral semantic token: a typed, optionally modified span of document text.
/// </summary>
/// <remarks>
/// The token carries only semantics - a range, a type, and modifiers - never a color or style.
/// Hosts map <see cref="Type"/> and <see cref="Modifiers"/> onto their own theme model, so the
/// token can cross provider and editor boundaries without UI coupling. Offsets are zero-based
/// UTF-16 positions.
/// </remarks>
public sealed class TextSemanticToken
{
	private readonly IReadOnlyList<string> _modifiers;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextSemanticToken"/> class.
	/// </summary>
	/// <param name="range">The zero-based range the token spans.</param>
	/// <param name="type">The semantic token type (for example <see cref="TextSemanticTokenTypes.Variable"/>).</param>
	/// <param name="modifiers">The optional semantic token modifiers; an owned snapshot is taken.</param>
	/// <exception cref="ArgumentNullException"><paramref name="type"/> is <see langword="null"/>.</exception>
	public TextSemanticToken(TextRange range, string type, IReadOnlyList<string>? modifiers = null)
	{
		ArgumentNullException.ThrowIfNull(type);

		Range = range;
		Type = type;

		_modifiers = modifiers is { Count: > 0 } ? [.. modifiers] : [];
	}

	/// <summary>
	/// Gets the zero-based range the token spans.
	/// </summary>
	public TextRange Range { get; }

	/// <summary>
	/// Gets the semantic token type.
	/// </summary>
	public string Type { get; }

	/// <summary>
	/// Gets the owned immutable snapshot of semantic token modifiers.
	/// </summary>
	public IReadOnlyList<string> Modifiers => _modifiers;

	/// <summary>
	/// Determines whether the token has the specified modifier.
	/// </summary>
	/// <remarks>
	/// Modifier names are compared using ordinal, case-sensitive comparison. A <see langword="null"/>,
	/// empty, or whitespace-only name is never considered present.
	/// </remarks>
	/// <param name="modifier">
	/// The modifier name to check (for example <see cref="TextSemanticTokenModifiers.Deprecated"/>).
	/// </param>
	/// <returns>
	/// <see langword="true"/> if the modifier is present; otherwise, <see langword="false"/>.
	/// </returns>
	public bool HasModifier(string? modifier)
	{
		if (string.IsNullOrWhiteSpace(modifier) || _modifiers.Count == 0)
			return false;

		for (int i = 0; i < _modifiers.Count; i++)
		{
			if (string.Equals(_modifiers[i], modifier, StringComparison.Ordinal))
				return true;
		}

		return false;
	}
}

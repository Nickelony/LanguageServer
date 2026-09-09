namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents a single semantic token produced by a language server.
/// </summary>
public sealed class SemanticToken
{
	/// <summary>
	/// Initializes a new instance of the <see cref="SemanticToken"/> class.
	/// </summary>
	/// <param name="line">The zero-based line index.</param>
	/// <param name="character">The zero-based character index (UTF-16 code units) within the line.</param>
	/// <param name="length">The token length in UTF-16 code units.</param>
	/// <param name="type">The semantic token type.</param>
	/// <param name="modifiers">The semantic token modifiers.</param>
	/// <remarks>
	/// Negative position and length values are normalized to zero. The modifier sequence is copied and exposed as a
	/// read-only snapshot.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="type"/> or <paramref name="modifiers"/> is <see langword="null"/>.
	/// </exception>
	public SemanticToken(int line, int character, int length, string type, IReadOnlyList<string> modifiers)
		: this(line, character, length, type, modifiers, copyModifiers: true)
	{
	}

	/// <summary>
	/// Creates a token that adopts an already-frozen modifier list instead of copying it.
	/// </summary>
	/// <param name="line">The zero-based line index.</param>
	/// <param name="character">The zero-based character index (UTF-16 code units) within the line.</param>
	/// <param name="length">The token length in UTF-16 code units.</param>
	/// <param name="type">The semantic token type.</param>
	/// <param name="frozenModifiers">
	/// The modifier sequence to adopt. The caller must guarantee that it is immutable for the token's lifetime, as
	/// the decoder's per-mask lists are.
	/// </param>
	/// <returns>The created token.</returns>
	internal static SemanticToken CreateWithFrozenModifiers(int line, int character, int length, string type, IReadOnlyList<string> frozenModifiers)
		=> new(line, character, length, type, frozenModifiers, copyModifiers: false);

	private SemanticToken(int line, int character, int length, string type, IReadOnlyList<string> modifiers, bool copyModifiers)
	{
		ArgumentNullException.ThrowIfNull(type);
		ArgumentNullException.ThrowIfNull(modifiers);

		Line = Math.Max(0, line);
		Character = Math.Max(0, character);
		Length = Math.Max(0, length);
		Type = type;

		if (!copyModifiers)
			Modifiers = modifiers;
		else
			Modifiers = modifiers.Count > 0
				? Array.AsReadOnly([.. modifiers])
				: [];
	}

	/// <summary>
	/// Gets the zero-based line index containing the token.
	/// </summary>
	public int Line { get; }

	/// <summary>
	/// Gets the zero-based character index (UTF-16 code units) of the token within its line.
	/// </summary>
	public int Character { get; }

	/// <summary>
	/// Gets the token length in UTF-16 code units.
	/// </summary>
	public int Length { get; }

	/// <summary>
	/// Gets the semantic token type.
	/// </summary>
	public string Type { get; }

	/// <summary>
	/// Gets a read-only snapshot of the semantic token modifiers.
	/// </summary>
	public IReadOnlyList<string> Modifiers { get; }

	/// <summary>
	/// Determines whether the token has the specified modifier using an ordinal, case-sensitive comparison; the
	/// queried modifier is trimmed before the comparison while the stored modifiers are compared as supplied.
	/// </summary>
	/// <param name="modifier">The modifier name to check, or <see langword="null"/>.</param>
	/// <returns><see langword="true"/> if the modifier is present; otherwise, <see langword="false"/>.</returns>
	public bool HasModifier(string? modifier)
	{
		string? normalizedModifier = modifier?.Trim();

		if (string.IsNullOrEmpty(normalizedModifier) || Modifiers.Count == 0)
			return false;

		for (int i = 0; i < Modifiers.Count; i++)
		{
			if (string.Equals(Modifiers[i], normalizedModifier, StringComparison.Ordinal))
				return true;
		}

		return false;
	}
}

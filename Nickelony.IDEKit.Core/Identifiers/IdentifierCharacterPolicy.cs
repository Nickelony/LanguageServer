namespace Nickelony.IDEKit.Core.Identifiers;

/// <summary>
/// Character-level rules that define which characters belong to an identifier or token.
/// </summary>
/// <remarks>
/// The policy distinguishes characters that may begin a token from characters that may appear
/// within one, and optionally treats quotes or punctuation as token characters. The quote and
/// punctuation flags add membership on top of the predicates, so a policy can extend
/// <see cref="Default"/> without writing a custom predicate.
/// </remarks>
public sealed class IdentifierCharacterPolicy
{
	private readonly Func<char, bool> _isPartCharacter;
	private readonly Func<char, bool> _isStartCharacter;

	/// <summary>
	/// Gets the policy that accepts letters, digits, and underscores as token characters.
	/// </summary>
	public static IdentifierCharacterPolicy Default { get; } = Create(static character => char.IsLetterOrDigit(character) || character == '_');

	/// <summary>
	/// Gets a value indicating whether double and single quotes belong to a token.
	/// </summary>
	public bool IncludeQuotes { get; }

	/// <summary>
	/// Gets a value indicating whether punctuation and symbol characters belong to a token.
	/// </summary>
	public bool IncludePunctuation { get; }

	private IdentifierCharacterPolicy(Func<char, bool> isPartCharacter, Func<char, bool>? isStartCharacter, bool includeQuotes, bool includePunctuation)
	{
		_isPartCharacter = isPartCharacter;
		_isStartCharacter = isStartCharacter ?? isPartCharacter;
		IncludeQuotes = includeQuotes;
		IncludePunctuation = includePunctuation;
	}

	/// <summary>
	/// Creates a policy with the supplied character rules.
	/// </summary>
	/// <param name="isPartCharacter">Determines whether a character may appear within a token.</param>
	/// <param name="isStartCharacter">
	/// Determines whether a character may begin a token. Defaults to <paramref name="isPartCharacter"/>.
	/// </param>
	/// <param name="includeQuotes">
	/// When <see langword="true"/>, double and single quotes belong to a token in addition to the
	/// characters accepted by the predicates.
	/// </param>
	/// <param name="includePunctuation">
	/// When <see langword="true"/>, punctuation and symbol characters belong to a token in addition
	/// to the characters accepted by the predicates.
	/// </param>
	/// <returns>The created policy.</returns>
	public static IdentifierCharacterPolicy Create(
		Func<char, bool> isPartCharacter,
		Func<char, bool>? isStartCharacter = null,
		bool includeQuotes = false,
		bool includePunctuation = false)
	{
		ArgumentNullException.ThrowIfNull(isPartCharacter);

		return new IdentifierCharacterPolicy(isPartCharacter, isStartCharacter, includeQuotes, includePunctuation);
	}

	/// <summary>
	/// Determines whether the character may begin a token.
	/// </summary>
	/// <param name="character">The character to test.</param>
	/// <returns><see langword="true"/> if the character may begin a token; otherwise, <see langword="false"/>.</returns>
	public bool IsStartCharacter(char character)
		=> _isStartCharacter(character) || IsFlaggedCharacter(character);

	/// <summary>
	/// Determines whether the character may appear within a token.
	/// </summary>
	/// <param name="character">The character to test.</param>
	/// <returns><see langword="true"/> if the character may appear within a token; otherwise, <see langword="false"/>.</returns>
	public bool IsPartCharacter(char character)
		=> _isPartCharacter(character) || IsFlaggedCharacter(character);

	private bool IsFlaggedCharacter(char character)
		=> (IncludeQuotes && character is '"' or '\'')
			|| (IncludePunctuation && (char.IsPunctuation(character) || char.IsSymbol(character)));

	/// <summary>
	/// Returns a copy of this policy that also treats double and single quotes as token characters.
	/// </summary>
	/// <returns>A policy with the same rules plus quote membership.</returns>
	public IdentifierCharacterPolicy WithQuotes()
		=> new(_isPartCharacter, _isStartCharacter, includeQuotes: true, IncludePunctuation);

	/// <summary>
	/// Returns a copy of this policy that also treats punctuation and symbols as token characters.
	/// </summary>
	/// <returns>A policy with the same rules plus punctuation membership.</returns>
	public IdentifierCharacterPolicy WithPunctuation()
		=> new(_isPartCharacter, _isStartCharacter, IncludeQuotes, includePunctuation: true);
}

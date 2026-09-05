using System.Diagnostics.CodeAnalysis;

namespace Nickelony.IDEKit.Core.Themes;

/// <summary>
/// Resolves named themes from a preloaded collection, supporting name and alias lookups, a
/// configured default selection, and deterministic ordering. Themes with blank names are skipped;
/// a theme whose name matches the configured default is ordered first, and the remainder are
/// ordered by name. Names and nonblank aliases are indexed with an ordinal, case-insensitive
/// comparison; the first theme wins when lookup names collide.
/// </summary>
/// <typeparam name="TTheme">The theme type carried by the catalog.</typeparam>
public sealed class ThemeCatalog<TTheme>
{
	private readonly IReadOnlyList<TTheme> _themes;
	private readonly Dictionary<string, TTheme> _themesByLookupName;
	private readonly TTheme _defaultTheme;

	/// <summary>
	/// Initializes a new instance of the <see cref="ThemeCatalog{TTheme}"/> class.
	/// </summary>
	/// <param name="themes">The themes to include in the catalog.</param>
	/// <param name="getName">Gets the display name of a theme.</param>
	/// <param name="getAliases">Gets the additional lookup names of a theme.</param>
	/// <param name="defaultThemeName">The name or alias of the default theme selection.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="themes"/>, <paramref name="getName"/>, <paramref name="getAliases"/>, or
	/// <paramref name="defaultThemeName"/> is null.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// No theme with a nonblank name remains after filtering.
	/// </exception>
	public ThemeCatalog(
		IEnumerable<TTheme> themes,
		Func<TTheme, string> getName,
		Func<TTheme, IReadOnlyList<string>> getAliases,
		string defaultThemeName)
	{
		ArgumentNullException.ThrowIfNull(themes);
		ArgumentNullException.ThrowIfNull(getName);
		ArgumentNullException.ThrowIfNull(getAliases);
		ArgumentNullException.ThrowIfNull(defaultThemeName);

		List<TTheme> orderedThemes = themes
			.Where(theme => !string.IsNullOrWhiteSpace(getName(theme)))
			.OrderByDescending(theme => string.Equals(getName(theme), defaultThemeName, StringComparison.OrdinalIgnoreCase))
			.ThenBy(theme => getName(theme), StringComparer.OrdinalIgnoreCase)
			.ToList();

		if (orderedThemes.Count == 0)
			throw new ArgumentException("The theme catalog must contain at least one theme with a name.", nameof(themes));

		var themesByLookupName = new Dictionary<string, TTheme>(StringComparer.OrdinalIgnoreCase);

		foreach (TTheme theme in orderedThemes)
		{
			AddLookupName(themesByLookupName, getName(theme), theme);

			IReadOnlyList<string> aliases = getAliases(theme);

			if (aliases is null)
				continue;

			for (int aliasIndex = 0; aliasIndex < aliases.Count; aliasIndex++)
				AddLookupName(themesByLookupName, aliases[aliasIndex], theme);
		}

		_themes = Array.AsReadOnly([.. orderedThemes]);
		_themesByLookupName = themesByLookupName;
		_defaultTheme = themesByLookupName.TryGetValue(defaultThemeName, out TTheme? configuredTheme) && configuredTheme is not null
			? configuredTheme
			: orderedThemes[0];
	}

	/// <summary>
	/// Gets the ordered list of available themes, with a theme whose name matches the configured
	/// default first when one exists.
	/// </summary>
	public IReadOnlyList<TTheme> Themes => _themes;

	/// <summary>
	/// Gets the themes indexed by their names and aliases using a case-insensitive comparison.
	/// </summary>
	public IReadOnlyDictionary<string, TTheme> ThemesByLookupName => _themesByLookupName;

	/// <summary>
	/// Gets the theme selected by the configured default name or alias, or the first ordered theme
	/// when the configured selection does not exist.
	/// </summary>
	public TTheme DefaultTheme => _defaultTheme;

	/// <summary>
	/// Attempts to resolve a theme by its name or alias using a case-insensitive comparison.
	/// </summary>
	/// <param name="nameOrAlias">The theme name or alias to resolve.</param>
	/// <param name="theme">The resolved theme when found.</param>
	/// <returns><see langword="true"/> when a theme matched; otherwise, <see langword="false"/>.</returns>
	public bool TryGetTheme(string? nameOrAlias, [MaybeNullWhen(false)] out TTheme? theme)
	{
		if (string.IsNullOrWhiteSpace(nameOrAlias))
		{
			theme = default;
			return false;
		}

		return _themesByLookupName.TryGetValue(nameOrAlias, out theme);
	}

	/// <summary>
	/// Resolves a theme by its name or alias, returning the default theme when no match exists.
	/// </summary>
	/// <param name="nameOrAlias">The theme name or alias to resolve.</param>
	/// <returns>The resolved theme, or the default theme.</returns>
	public TTheme GetTheme(string nameOrAlias)
	{
		ArgumentNullException.ThrowIfNull(nameOrAlias);
		return TryGetTheme(nameOrAlias, out TTheme? theme) && theme is not null ? theme : DefaultTheme;
	}

	private static void AddLookupName(Dictionary<string, TTheme> themesByLookupName, string lookupName, TTheme theme)
	{
		if (string.IsNullOrWhiteSpace(lookupName) || themesByLookupName.ContainsKey(lookupName))
			return;

		themesByLookupName[lookupName] = theme;
	}
}

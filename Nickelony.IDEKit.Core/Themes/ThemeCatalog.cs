using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace Nickelony.IDEKit.Core.Themes;

/// <summary>
/// Resolves named themes from a preloaded collection, supporting name and alias lookups, a
/// configured default selection, and deterministic ordering.
/// </summary>
/// <remarks>
/// <para>
/// <see langword="null"/> themes and themes with blank names are skipped. The theme selected by the configured default
/// name or alias is ordered first, and the remainder are ordered by name. The name order is an
/// ordinal, case-insensitive order, which is culture-independent rather than display-ordered (for
/// example <c>Zulu</c> sorts before <c>&#196;rger</c>); a host that presents the names to users must
/// re-sort with a display comparer.
/// </para>
/// <para>
/// Names and non-blank aliases are indexed with an ordinal, case-insensitive comparison; the first
/// theme in the ordered list wins when lookup names collide, so only that theme is reachable by a
/// shared lookup name. Keeping names unique is the host's responsibility; duplicates are kept in
/// the list rather than rejected. Theme names are identifiers, so the comparison is deliberately
/// culture-safe and fixed; there is no comparer option.
/// </para>
/// <para>
/// The catalog is immutable after construction, so its ordered theme list and lookup dictionary can
/// be read from any thread. It is a generic name registry with no host dependencies.
/// </para>
/// </remarks>
/// <typeparam name="TTheme">The reference theme type carried by the catalog.</typeparam>
public sealed class ThemeCatalog<TTheme>
	where TTheme : class
{
	private readonly IReadOnlyList<TTheme> _themes;
	private readonly FrozenDictionary<string, TTheme> _themesByLookupName;
	private readonly TTheme _defaultTheme;

	/// <summary>
	/// Initializes a new instance of the <see cref="ThemeCatalog{TTheme}"/> class.
	/// </summary>
	/// <param name="themes">The themes to include in the catalog.</param>
	/// <param name="options">The name, alias, and default-selection readers for the catalog.</param>
	/// <exception cref="ArgumentNullException"><paramref name="themes"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="options"/> does not supply a name selector or a default theme name, or no
	/// theme with a non-blank name remains after filtering.
	/// </exception>
	public ThemeCatalog(IEnumerable<TTheme> themes, ThemeCatalogOptions<TTheme> options)
	{
		ArgumentNullException.ThrowIfNull(themes);

		if (options.GetName is null || options.DefaultThemeName is null)
			throw new ArgumentException("The options must supply both a name selector and a default theme name.", nameof(options));

		// Capture each theme's metadata once; the ordering, default selection, and lookup index all
		// consume the same names and aliases.
		var namedThemes = new List<ThemeEntry>();

		foreach (TTheme theme in themes)
		{
			// A host collection can contain null gaps; they are skipped like unnamed themes.
			if (theme is null)
				continue;

			string name = options.GetName(theme);

			if (string.IsNullOrWhiteSpace(name))
				continue;

			namedThemes.Add(new ThemeEntry(theme, name, options.GetAliases?.Invoke(theme)));
		}

		if (namedThemes.Count == 0)
			throw new ArgumentException("The theme catalog must contain at least one theme with a name.", nameof(themes));

		List<ThemeEntry> orderedThemes = [.. namedThemes.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)];
		int defaultThemeIndex = orderedThemes.FindIndex(entry => IsDefaultSelection(entry, options.DefaultThemeName));

		if (defaultThemeIndex > 0)
		{
			ThemeEntry selectedTheme = orderedThemes[defaultThemeIndex];
			orderedThemes.RemoveAt(defaultThemeIndex);
			orderedThemes.Insert(0, selectedTheme);
		}

		var themesByLookupName = new Dictionary<string, TTheme>(StringComparer.OrdinalIgnoreCase);

		foreach (ThemeEntry entry in orderedThemes)
		{
			AddLookupName(themesByLookupName, entry.Name, entry.Theme);

			if (entry.Aliases is null)
				continue;

			for (int aliasIndex = 0; aliasIndex < entry.Aliases.Count; aliasIndex++)
				AddLookupName(themesByLookupName, entry.Aliases[aliasIndex], entry.Theme);
		}

		_themes = Array.AsReadOnly([.. orderedThemes.Select(entry => entry.Theme)]);
		_themesByLookupName = themesByLookupName.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
		_defaultTheme = orderedThemes[0].Theme;
	}

	/// <summary>
	/// Gets the ordered list of available themes, with the configured default theme first when one
	/// exists.
	/// </summary>
	public IReadOnlyList<TTheme> Themes => _themes;

	/// <summary>
	/// Gets the theme selected by the configured default name or alias, or the first ordered theme
	/// when the configured selection does not exist.
	/// </summary>
	public TTheme DefaultTheme => _defaultTheme;

	/// <summary>
	/// Tries to resolve a theme by its name or alias using a case-insensitive comparison.
	/// </summary>
	/// <param name="nameOrAlias">The theme name or alias to resolve.</param>
	/// <param name="theme">The resolved theme when found; otherwise, <see langword="default"/>.</param>
	/// <returns><see langword="true"/> when a theme matched; otherwise, <see langword="false"/>.</returns>
	public bool TryGetTheme(string? nameOrAlias, [NotNullWhen(true)] out TTheme? theme)
	{
		if (string.IsNullOrWhiteSpace(nameOrAlias))
		{
			theme = default;
			return false;
		}

		// The constructor skips null themes, so a successful lookup always resolves a non-null value.
		if (_themesByLookupName.TryGetValue(nameOrAlias, out TTheme? resolved))
		{
			theme = resolved!;
			return true;
		}

		theme = default;
		return false;
	}

	/// <summary>
	/// Resolves a theme by its name or alias, returning the default theme when no match exists.
	/// </summary>
	/// <remarks>
	/// Callers that may hold a nullable or blank name should use <see cref="TryGetTheme"/>; this
	/// method throws for a <see langword="null"/> name and silently falls back to the default theme
	/// for a blank one.
	/// </remarks>
	/// <param name="nameOrAlias">The theme name or alias to resolve.</param>
	/// <returns>The resolved theme, or the default theme.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="nameOrAlias"/> is <see langword="null"/>.</exception>
	public TTheme GetTheme(string nameOrAlias)
	{
		ArgumentNullException.ThrowIfNull(nameOrAlias);
		return TryGetTheme(nameOrAlias, out TTheme? theme) ? theme : DefaultTheme;
	}

	private static bool IsDefaultSelection(ThemeEntry entry, string defaultThemeName)
	{
		if (string.Equals(entry.Name, defaultThemeName, StringComparison.OrdinalIgnoreCase))
			return true;

		if (entry.Aliases is null)
			return false;

		for (int aliasIndex = 0; aliasIndex < entry.Aliases.Count; aliasIndex++)
		{
			if (!string.IsNullOrWhiteSpace(entry.Aliases[aliasIndex])
				&& string.Equals(entry.Aliases[aliasIndex], defaultThemeName, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	private static void AddLookupName(Dictionary<string, TTheme> themesByLookupName, string lookupName, TTheme theme)
	{
		if (string.IsNullOrWhiteSpace(lookupName))
			return;

		// The first theme in the ordered list wins when lookup names collide.
		themesByLookupName.TryAdd(lookupName, theme);
	}

	/// <summary>
	/// Carries one theme's lookup metadata captured during construction, so the accessor delegates
	/// run once per theme.
	/// </summary>
	private readonly record struct ThemeEntry(TTheme Theme, string Name, IReadOnlyList<string>? Aliases);
}

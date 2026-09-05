using ICSharpCode.AvalonEdit.Highlighting;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.Highlighting;

/// <summary>
/// Provides a highlighting definition from regex-based rules.
/// The main rule set is built lazily and rebuilt when the optional cache version changes.
/// </summary>
public abstract class RegexHighlightingDefinition : IHighlightingDefinition
{
	private readonly Func<int>? _cacheVersion;
	private readonly Color _fallbackColor;

	private HighlightingRuleSet? _cachedRuleSet;
	private int _cachedVersion;

	/// <summary>
	/// Initializes a new instance of the <see cref="RegexHighlightingDefinition"/> class.
	/// </summary>
	/// <param name="name">The definition name and the name of its main rule set.</param>
	/// <param name="cacheVersion">
	/// A function that returns a cache version. The main rule set is rebuilt when the returned value changes.
	/// </param>
	/// <param name="fallbackColor">
	/// The fallback foreground color for missing or invalid rule colors. Defaults to black.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	protected RegexHighlightingDefinition(string name, Func<int>? cacheVersion = null, Color? fallbackColor = null)
	{
		ArgumentNullException.ThrowIfNull(name);

		Name = name;

		_cacheVersion = cacheVersion;
		_fallbackColor = fallbackColor ?? Colors.Black;
	}

	/// <summary>
	/// Gets the main rule set. It is built on first access and rebuilt when the configured cache version changes.
	/// </summary>
	public HighlightingRuleSet MainRuleSet
	{
		get
		{
			if (_cachedRuleSet is null || (_cacheVersion is not null && _cacheVersion() != _cachedVersion))
			{
				_cachedRuleSet = BuildRuleSet();
				_cachedVersion = _cacheVersion?.Invoke() ?? 0;
			}

			return _cachedRuleSet;
		}
	}

	/// <inheritdoc/>
	public string Name { get; }

	/// <summary>
	/// Gets an empty collection because this definition has no named colors.
	/// </summary>
	public IEnumerable<HighlightingColor> NamedHighlightingColors => [];

	/// <summary>
	/// Gets an empty property collection.
	/// </summary>
	public IDictionary<string, string> Properties => new Dictionary<string, string>();

	/// <summary>
	/// Returns <see langword="null"/> because this definition has no named colors.
	/// </summary>
	/// <param name="name">The color name to look up.</param>
	/// <returns><see langword="null"/>.</returns>
	public HighlightingColor? GetNamedColor(string name)
		=> null;

	/// <summary>
	/// Returns the main rule set when <paramref name="name"/> matches <see cref="Name"/>;
	/// otherwise, returns <see langword="null"/>.
	/// </summary>
	/// <param name="name">The rule set name to look up.</param>
	/// <returns>The main rule set for <see cref="Name"/>, or <see langword="null"/> when no match exists.</returns>
	public HighlightingRuleSet? GetNamedRuleSet(string name)
		=> name == MainRuleSet.Name ? MainRuleSet : null;

	/// <summary>
	/// Provides the rules for the main rule set.
	/// </summary>
	/// <returns>The rules used to build the main rule set.</returns>
	protected abstract IEnumerable<RegexHighlightingRule> BuildRules();

	private HighlightingRuleSet BuildRuleSet()
	{
		var ruleSet = new HighlightingRuleSet
		{
			Name = Name
		};

		foreach (RegexHighlightingRule rule in BuildRules())
		{
			ruleSet.Rules.Add(new HighlightingRule
			{
				Regex = rule.Regex,
				Color = rule.Style.ToHighlightingColor(_fallbackColor)
			});
		}

		return ruleSet;
	}
}

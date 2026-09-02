using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;

namespace Nickelony.IDEKit.AvalonEdit.Highlighting;

/// <summary>
/// An <see cref="IHighlightingDefinition"/> built from declarative <see cref="RegexHighlightingRule"/>
/// instances produced by <see cref="BuildRules"/>. The rule set is cached and can be rebuilt when an
/// optional cache version changes, which lets derived definitions invalidate the cache when the
/// source of their rules (for example a catalog) reloads.
/// </summary>
public abstract class RegexHighlightingDefinition : IHighlightingDefinition
{
	private readonly string _name;
	private readonly Func<int>? _cacheVersion;
	private readonly Color _fallbackColor;
	private HighlightingRuleSet? _cachedRuleSet;
	private int _cachedVersion;

	/// <summary>
	/// Initializes a new instance of the <see cref="RegexHighlightingDefinition"/> class.
	/// </summary>
	/// <param name="name">The name of the highlighting definition and its main rule set.</param>
	/// <param name="cacheVersion">
	/// An optional version used to invalidate the cached rule set. When supplied and the returned
	/// value changes between accesses, the rule set is rebuilt.
	/// </param>
	/// <param name="fallbackColor">
	/// The color used when a rule style color cannot be parsed; defaults to white.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
	protected RegexHighlightingDefinition(string name, Func<int>? cacheVersion = null, Color? fallbackColor = null)
	{
		ArgumentNullException.ThrowIfNull(name);

		_name = name;
		_cacheVersion = cacheVersion;
		_fallbackColor = fallbackColor ?? Colors.White;
	}

	/// <inheritdoc/>
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
	public string Name => _name;

	/// <inheritdoc/>
	public IEnumerable<HighlightingColor> NamedHighlightingColors => [];

	/// <inheritdoc/>
	public IDictionary<string, string> Properties => new Dictionary<string, string>();

	/// <inheritdoc/>
	public HighlightingColor? GetNamedColor(string name)
		=> null;

	/// <inheritdoc/>
	public HighlightingRuleSet? GetNamedRuleSet(string name)
		=> name == MainRuleSet.Name ? MainRuleSet : null;

	/// <summary>
	/// Builds the declarative rules for the main rule set.
	/// </summary>
	/// <returns>The rules that make up the main rule set.</returns>
	protected abstract IEnumerable<RegexHighlightingRule> BuildRules();

	private HighlightingRuleSet BuildRuleSet()
	{
		var ruleSet = new HighlightingRuleSet
		{
			Name = _name
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

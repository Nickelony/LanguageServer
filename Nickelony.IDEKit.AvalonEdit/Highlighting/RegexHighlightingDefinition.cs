using ICSharpCode.AvalonEdit.Highlighting;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.Highlighting;

/// <summary>
/// Provides a highlighting definition from regex-based rules.
/// </summary>
/// <remarks>
/// <para>
/// The main rule set is built lazily, is safe for concurrent access, and is rebuilt when the
/// optional cache version changes. No lock is held while the cache-version callback or
/// <see cref="BuildRules"/> runs, so accesses never block on host code: two threads that observe a
/// version change can build concurrently, and each installs its rule set with an atomic publication
/// that replaces the snapshot current at that moment. A publication that replaces a snapshot of a
/// different version raises <see cref="RuleSetChanged"/>; the first build of a definition does not.
/// </para>
/// <para>
/// A definition is created either from providers through the <see cref="Create(string, Func{IEnumerable{RegexHighlightingRule}}, Func{int}, Color?)"/>
/// factories, or by subclassing and supplying the rules and spans through <see cref="BuildRules"/> and
/// <see cref="BuildSpans"/>; both shapes feed the same lazily built, version-aware rule set.
/// </para>
/// <para>
/// If a publication loses a race against a later one, the next access observes the version mismatch
/// and rebuilds, so the served rule set converges on the current version. The version-based rebuild
/// exists for hosts whose rule data is live, for example a rule catalog that is refreshed at
/// runtime; a definition over static data passes no version source and builds once.
/// </para>
/// <para>
/// A rule style without a color leaves the foreground unset so the editor's theme color is used,
/// and font styles are overridden only when the style requests them.
/// </para>
/// <para>
/// Rules and spans are evaluated during rendering, so hosts should supply regexes with a bounded match
/// timeout; see <see cref="RegexHighlightingRule"/>. A pattern that can match empty text is rejected
/// when the rule set is built because AvalonEdit's highlight engine cannot advance past a zero-length
/// match; the probe only rejects patterns it can detect on its own, so a pattern that matches empty
/// only on text the probe lacks fails later, inside the highlight engine, during rendering, where no
/// layer above the engine catches the exception. A match timeout that fires fails the same way (a
/// <see cref="System.Text.RegularExpressions.RegexMatchTimeoutException"/> raised from the colorizer).
/// </para>
/// <para>
/// AvalonEdit scans a line from left to right and, at each position, applies the earliest match of any
/// rule; when two rules match at the same position, the rule returned first by <see cref="BuildRules"/> wins.
/// A rule never overrides or suppresses an earlier match, so hosts should order the most specific rules
/// first. A rule match can span only the current line, because the engine evaluates each line separately;
/// <see cref="BuildSpans"/> provides delimiter-based spans (block comments, long strings) that stay
/// highlighted across lines.
/// </para>
/// </remarks>
public abstract class RegexHighlightingDefinition : IHighlightingDefinition
{
	/// <summary>
	/// Stores a built rule set together with the cache version that produced it, so a reader can never
	/// observe a rule set and a version from different builds.
	/// </summary>
	/// <param name="ruleSet">The built rule set.</param>
	/// <param name="version">The cache version the rule set was built from.</param>
	private sealed class RuleSetSnapshot(HighlightingRuleSet ruleSet, int version)
	{
		public HighlightingRuleSet RuleSet { get; } = ruleSet;

		public int Version { get; } = version;
	}

	private static readonly ReadOnlyDictionary<string, string> s_emptyProperties = new(new Dictionary<string, string>());
	private static readonly ReadOnlyCollection<HighlightingColor> s_emptyNamedColors = Array.AsReadOnly(Array.Empty<HighlightingColor>());

	/// <summary>
	/// Tracks, per thread, the definitions being processed on it, whether their rule set is being
	/// built or their cache-version callback is running.
	/// The recursion guard is thread-local so a concurrent build on another thread cannot interfere
	/// with it, and it is per definition so composing from another definition remains possible.
	/// </summary>
	[ThreadStatic]
	private static HashSet<RegexHighlightingDefinition>? s_buildsInProgress;

	private readonly Func<int>? _cacheVersion;
	private readonly Color? _fallbackColor;

	private RuleSetSnapshot? _snapshot;

	/// <summary>
	/// Initializes a new instance of the <see cref="RegexHighlightingDefinition"/> class.
	/// </summary>
	/// <param name="name">The definition name and the name of its main rule set.</param>
	/// <param name="cacheVersion">
	/// A function that returns a cache version. The main rule set is rebuilt when the returned value changes.
	/// </param>
	/// <param name="fallbackColor">
	/// The fallback foreground color used when a rule color value cannot be parsed. The default
	/// <see langword="null"/> leaves the foreground unset so the editor's theme color is used.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	protected RegexHighlightingDefinition(string name, Func<int>? cacheVersion = null, Color? fallbackColor = null)
	{
		ArgumentNullException.ThrowIfNull(name);

		Name = name;

		_cacheVersion = cacheVersion;
		_fallbackColor = fallbackColor;
	}

	/// <summary>
	/// Raised after the main rule set is rebuilt because the configured cache version changed.
	/// </summary>
	/// <remarks>
	/// <para>
	/// AvalonEdit caches highlighted lines and does not observe a rebuilt rule set by itself, so a host
	/// can subscribe and refresh the editor's highlighting (for example by reinstalling the highlighter)
	/// when the event arrives. The event is raised on the rebuilding thread after the build guard is
	/// released, so a handler can access <see cref="MainRuleSet"/> and receive the freshly published rule
	/// set. The initial build of a definition does not raise the event.
	/// </para>
	/// <para>
	/// The event is raised on the thread that triggered the rebuild, so a host refreshing the editor
	/// must marshal that work to the editor's thread. A handler that throws propagates the exception out
	/// of the <see cref="MainRuleSet"/> access that triggered the rebuild.
	/// </para>
	/// </remarks>
	public event EventHandler? RuleSetChanged;

	/// <summary>
	/// Gets the main rule set.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The rule set is built on first access, is cached, and is rebuilt when the configured cache version changes.
	/// The cache-version callback is read once per access.
	/// </para>
	/// <para>
	/// A <see cref="RuleSetChanged"/> handler runs after the guard is released, so it may access the rule set
	/// without triggering this exception.
	/// </para>
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	/// The property was accessed re-entrantly from the cache version callback, or from
	/// <see cref="BuildRules"/> or <see cref="BuildSpans"/> on the building thread; the access site throws
	/// instead of recursing.
	/// </exception>
	public HighlightingRuleSet MainRuleSet
	{
		get
		{
			// The guard runs before anything else so any access during this thread's own build or
			// cache-version callback fails fast, no matter whether a snapshot already exists.
			if (s_buildsInProgress?.Contains(this) == true)
			{
				throw new InvalidOperationException(
					"The main rule set must not be accessed from the cache version callback, BuildRules, or BuildSpans; " +
					"the access would recurse instead of returning a rule set.");
			}

			RuleSetSnapshot? snapshot = Volatile.Read(ref _snapshot);

			if (_cacheVersion is null)
				return snapshot?.RuleSet ?? BuildAndPublish(version: 0);

			// The version is read once per access, so a non-idempotent callback cannot make the snapshot and
			// the rebuild decision disagree about the version. The guard is armed while the callback runs:
			// the callback reads this definition only by mistake, and the access must throw instead of
			// re-invoking the callback.
			int version;

			EnterRecursionGuard();

			try
			{
				version = _cacheVersion();
			}
			finally
			{
				ExitRecursionGuard();
			}

			if (snapshot is not null && snapshot.Version == version)
				return snapshot.RuleSet;

			return BuildAndPublish(version);
		}
	}

	/// <summary>
	/// Builds the rule set, publishes it as the new snapshot, and raises <see cref="RuleSetChanged"/> when
	/// the publication replaced a snapshot of a different version.
	/// </summary>
	/// <param name="version">The cache version the built rule set belongs to.</param>
	/// <returns>The built rule set.</returns>
	private HighlightingRuleSet BuildAndPublish(int version)
	{
		EnterRecursionGuard();

		HighlightingRuleSet ruleSet;
		bool rebuilt;

		try
		{
			ruleSet = BuildRuleSet();

			// The snapshot is published against the snapshot that is current at publication time, not against
			// the one this thread read before building: a concurrent build that published first is observed,
			// so the event reports what this publication actually replaced.
			var newSnapshot = new RuleSetSnapshot(ruleSet, version);
			RuleSetSnapshot? replaced;

			do
			{
				replaced = Volatile.Read(ref _snapshot);
				rebuilt = replaced is not null && replaced.Version != version;
			}
			while (Interlocked.CompareExchange(ref _snapshot, newSnapshot, replaced) != replaced);
		}
		finally
		{
			ExitRecursionGuard();
		}

		// The event is raised after the guard is released: the documented handler workflow reinstalls the
		// editor's highlighter, which reads MainRuleSet synchronously.
		if (rebuilt)
			RuleSetChanged?.Invoke(this, EventArgs.Empty);

		return ruleSet;
	}

	/// <summary>
	/// Marks this definition as being processed on the current thread, so a re-entrant main-rule-set
	/// access throws instead of recursing.
	/// </summary>
	/// <remarks>
	/// The guard is thread-local so a concurrent build on another thread cannot interfere with it,
	/// and per definition so composing from another definition remains possible. It is armed both
	/// while <see cref="BuildRules"/> and <see cref="BuildSpans"/> run and while the cache-version
	/// callback runs, because the callback would otherwise re-invoke itself through the property.
	/// </remarks>
	private void EnterRecursionGuard()
		=> (s_buildsInProgress ??= []).Add(this);

	/// <summary>
	/// Releases the guard armed by <see cref="EnterRecursionGuard"/> and drops the thread-local
	/// set when no definition on this thread is being processed anymore.
	/// </summary>
	private void ExitRecursionGuard()
	{
		HashSet<RegexHighlightingDefinition>? buildsInProgress = s_buildsInProgress;

		if (buildsInProgress is null)
			return;

		buildsInProgress.Remove(this);

		if (buildsInProgress.Count == 0)
			s_buildsInProgress = null;
	}

	/// <inheritdoc/>
	public string Name { get; }

	/// <summary>
	/// Gets an empty collection because this definition has no named colors.
	/// </summary>
	public IEnumerable<HighlightingColor> NamedHighlightingColors => s_emptyNamedColors;

	/// <summary>
	/// Gets an empty property collection.
	/// </summary>
	public IDictionary<string, string> Properties => s_emptyProperties;

	/// <summary>
	/// Returns <see langword="null"/> because this definition has no named colors.
	/// </summary>
	/// <param name="name">The color name to look up.</param>
	/// <returns><see langword="null"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	public HighlightingColor? GetNamedColor(string name)
	{
		ArgumentNullException.ThrowIfNull(name);
		return null;
	}

	/// <summary>
	/// Returns the main rule set when <paramref name="name"/> matches <see cref="Name"/>;
	/// otherwise, returns <see langword="null"/>.
	/// </summary>
	/// <param name="name">The rule set name to look up.</param>
	/// <returns>The main rule set for <see cref="Name"/>, or <see langword="null"/> when no match exists.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">
	/// The main rule set was accessed re-entrantly from the cache version callback, or from
	/// <see cref="BuildRules"/> or <see cref="BuildSpans"/> on the building thread; the access site throws
	/// instead of recursing.
	/// </exception>
	public HighlightingRuleSet? GetNamedRuleSet(string name)
	{
		ArgumentNullException.ThrowIfNull(name);

		// Compare the name before touching the rule set so unknown names do not build it.
		return name == Name ? MainRuleSet : null;
	}

	/// <summary>
	/// Provides the rules for the main rule set.
	/// </summary>
	/// <remarks>
	/// Invoked while the main rule set is built, so accessing <see cref="MainRuleSet"/>, or
	/// <see cref="GetNamedRuleSet(string)"/> with a name that matches <see cref="Name"/>, from this method
	/// throws <see cref="InvalidOperationException"/> instead of recursing. Other names return
	/// <see langword="null"/> without touching the rule set. Concurrent first accesses can build
	/// concurrently, so an implementation that shares host state must be thread-safe.
	/// </remarks>
	/// <returns>The rules used to build the main rule set.</returns>
	protected abstract IEnumerable<RegexHighlightingRule> BuildRules();

	/// <summary>
	/// Provides the delimiter-based spans for the main rule set.
	/// </summary>
	/// <remarks>
	/// The spans are evaluated together with the rules; see <see cref="RegexHighlightingSpan"/>.
	/// The same build-time constraints as for <see cref="BuildRules"/> apply: accessing the main rule set
	/// from this method throws <see cref="InvalidOperationException"/>, and an implementation that shares
	/// host state must be thread-safe.
	/// </remarks>
	/// <returns>The spans used to build the main rule set; the default is an empty sequence.</returns>
	protected virtual IEnumerable<RegexHighlightingSpan> BuildSpans() => [];

	/// <summary>
	/// Creates a definition from a rule provider without subclassing.
	/// </summary>
	/// <param name="name">The definition name and the name of its main rule set.</param>
	/// <param name="rulesProvider">Provides the rules, invoked while the main rule set is built.</param>
	/// <param name="cacheVersion">
	/// A function that returns a cache version. The main rule set is rebuilt when the returned value changes.
	/// </param>
	/// <param name="fallbackColor">
	/// The fallback foreground color used when a rule color value cannot be parsed. The default
	/// <see langword="null"/> leaves the foreground unset so the editor's theme color is used.
	/// </param>
	/// <returns>The created definition.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="name"/> or <paramref name="rulesProvider"/> is <see langword="null"/>.
	/// </exception>
	public static RegexHighlightingDefinition Create(
		string name,
		Func<IEnumerable<RegexHighlightingRule>> rulesProvider,
		Func<int>? cacheVersion = null,
		Color? fallbackColor = null)
	{
		ArgumentNullException.ThrowIfNull(rulesProvider);

		return new DelegateRegexHighlightingDefinition(name, rulesProvider, cacheVersion, fallbackColor);
	}

	/// <summary>
	/// Creates a definition from rule and span providers without subclassing.
	/// </summary>
	/// <param name="name">The definition name and the name of its main rule set.</param>
	/// <param name="rulesProvider">Provides the rules, invoked while the main rule set is built.</param>
	/// <param name="spansProvider">Provides the spans, invoked while the main rule set is built.</param>
	/// <param name="cacheVersion">
	/// A function that returns a cache version. The main rule set is rebuilt when the returned value changes.
	/// </param>
	/// <param name="fallbackColor">
	/// The fallback foreground color used when a rule color value cannot be parsed. The default
	/// <see langword="null"/> leaves the foreground unset so the editor's theme color is used.
	/// </param>
	/// <returns>The created definition.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="name"/>, <paramref name="rulesProvider"/>, or <paramref name="spansProvider"/> is
	/// <see langword="null"/>.
	/// </exception>
	public static RegexHighlightingDefinition Create(
		string name,
		Func<IEnumerable<RegexHighlightingRule>> rulesProvider,
		Func<IEnumerable<RegexHighlightingSpan>> spansProvider,
		Func<int>? cacheVersion = null,
		Color? fallbackColor = null)
	{
		ArgumentNullException.ThrowIfNull(rulesProvider);
		ArgumentNullException.ThrowIfNull(spansProvider);

		return new DelegateRegexHighlightingDefinition(name, rulesProvider, spansProvider, cacheVersion, fallbackColor);
	}

	private sealed class DelegateRegexHighlightingDefinition : RegexHighlightingDefinition
	{
		private readonly Func<IEnumerable<RegexHighlightingRule>> _rulesProvider;
		private readonly Func<IEnumerable<RegexHighlightingSpan>>? _spansProvider;

		public DelegateRegexHighlightingDefinition(
			string name,
			Func<IEnumerable<RegexHighlightingRule>> rulesProvider,
			Func<int>? cacheVersion,
			Color? fallbackColor)
			: this(name, rulesProvider, spansProvider: null, cacheVersion, fallbackColor)
		{ }

		public DelegateRegexHighlightingDefinition(
			string name,
			Func<IEnumerable<RegexHighlightingRule>> rulesProvider,
			Func<IEnumerable<RegexHighlightingSpan>>? spansProvider,
			Func<int>? cacheVersion,
			Color? fallbackColor)
			: base(name, cacheVersion, fallbackColor)
		{
			_rulesProvider = rulesProvider;
			_spansProvider = spansProvider;
		}

		protected override IEnumerable<RegexHighlightingRule> BuildRules() => _rulesProvider();

		protected override IEnumerable<RegexHighlightingSpan> BuildSpans()
			=> _spansProvider is null ? [] : _spansProvider();
	}

	/// <summary>
	/// Builds an AvalonEdit rule set from the definition's rules and spans, validating every pattern
	/// before it enters the set.
	/// </summary>
	/// <returns>The built rule set.</returns>
	private HighlightingRuleSet BuildRuleSet()
	{
		var ruleSet = new HighlightingRuleSet
		{
			Name = Name
		};

		foreach (RegexHighlightingRule rule in BuildRules())
		{
			ValidatePattern(rule.Pattern);

			ruleSet.Rules.Add(new HighlightingRule
			{
				Regex = rule.Pattern,
				Color = rule.Style.ToHighlightingColor(_fallbackColor)
			});
		}

		foreach (RegexHighlightingSpan span in BuildSpans())
		{
			ValidatePattern(span.Begin);

			if (span.End is not null)
				ValidatePattern(span.End);

			ruleSet.Spans.Add(new HighlightingSpan
			{
				StartExpression = span.Begin,
				EndExpression = span.End,
				SpanColor = span.SpanStyle.ToHighlightingColor(_fallbackColor),
				StartColor = span.BeginStyle?.ToHighlightingColor(_fallbackColor),
				EndColor = span.EndStyle?.ToHighlightingColor(_fallbackColor),

				// Without a dedicated begin or end style, the span color covers the delimiter matches as well.
				SpanColorIncludesStart = span.BeginStyle is null,
				SpanColorIncludesEnd = span.EndStyle is null
			});
		}

		return ruleSet;
	}

	private static readonly string[] s_emptyMatchProbes = ["", "x y", "foo bar", "0x0", "a/b"];

	/// <summary>
	/// Rejects a rule or span pattern that can match empty text.
	/// </summary>
	/// <remarks>
	/// AvalonEdit's highlight engine throws when the earliest match at a scan position is empty, because
	/// it cannot advance past a zero-length match. A short probe corpus catches patterns that match empty
	/// on common text shapes (anchors, optional repetitions, empty-capable alternations, lookarounds); a
	/// pattern that can only match empty on other text is not detected here and fails inside the engine
	/// during rendering instead.
	/// </remarks>
	/// <param name="pattern">The rule or span pattern to validate.</param>
	/// <exception cref="InvalidOperationException">The pattern can match empty text.</exception>
	private static void ValidatePattern(Regex pattern)
	{
		foreach (string probe in s_emptyMatchProbes)
		{
			foreach (ValueMatch match in pattern.EnumerateMatches(probe))
			{
				if (match.Length == 0)
				{
					throw new InvalidOperationException(
						"The highlighting pattern must not match empty text, because AvalonEdit's highlight engine " +
						$"cannot advance past a zero-length match. Pattern: {pattern}");
				}
			}
		}
	}
}

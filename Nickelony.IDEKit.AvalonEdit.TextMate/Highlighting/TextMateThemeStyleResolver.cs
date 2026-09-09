using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;
using static Nickelony.IDEKit.Infrastructure.BrushHelpers;

namespace Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting;

/// <summary>
/// Resolves TextMate token scopes into <see cref="TextRunStyle"/> instances using a
/// <see cref="TextMateTokenTheme"/>.
/// </summary>
/// <remarks>
/// <para>
/// Rules are matched with TextMate selector semantics for space-separated descendant selectors: the
/// rightmost (deepest) selector part must match the innermost scope of the path being matched, and
/// the remaining parts must match enclosing scopes in order. The exclusion (<c>-</c>), direct-child
/// (<c>&gt;</c>), wildcard (<c>*</c>), priority (<c>L:</c>), and parenthesis selector operators are
/// not supported; a selector that uses any of them never matches and is reported through the
/// constructor logger.
/// </para>
/// <para>
/// Styles are resolved the way VS Code resolves them: the theme is applied scope push by scope push.
/// For every scope of the token's stack, the scope path up to that scope is matched with the
/// rightmost selector part anchored at that innermost scope, and all matching rules of that push are
/// applied from the most specific to the least specific, where each property is taken from the first
/// rule that sets it and the resulting values overwrite the accumulated attributes. The selector
/// specificity order follows VS Code's token-theme ranking (TextMate's "Ranking Matches"): greater
/// scope depth wins first, where the depth is the number of dot-separated segments in the rightmost
/// selector part; at equal depth the parent scopes decide, compared from the deepest parent towards
/// the root, where a longer scope name wins; and a rule with more parent scopes wins the remaining
/// ties. Rules of equal specificity keep the later-listed rule, matching the way VS Code merges
/// duplicate selectors.
/// </para>
/// <para>
/// A rule with a blank or whitespace-only scope provides the defaults that apply to every token,
/// which is how VS Code theme files carry their base foreground and font style; when several such
/// rules are present, later values overwrite earlier ones for each property. Unset properties are
/// inherited from the defaults, and a property that neither the defaults nor any matching push sets
/// stays unset in the resolved style.
/// </para>
/// <para>
/// Theme values follow the TextMate data conventions. A rule foreground accepts the WPF color formats
/// and the TextMate eight-digit form <c>#RRGGBBAA</c> and four-digit form <c>#RGBA</c>, whose alpha
/// component is moved to the WPF <c>#AARRGGBB</c> order before parsing. Font traits follow the
/// <c>fontStyle</c> field semantics: an absent value leaves inherited traits unchanged, while any
/// present value - including an empty string - resets all four traits first and then enables the
/// recognized ones (<c>bold</c>, <c>italic</c>, <c>underline</c>, and <c>strikethrough</c>). The
/// non-standard <c>none</c> keyword is accepted as an explicit reset for readability. Invalid colors,
/// unsupported selectors, and unrecognized traits are ignored and reported as warnings through the
/// constructor logger. Rule backgrounds are not supported: the theme model carries foreground colors
/// and font traits only, so a background color in host theme data is ignored without a warning.
/// </para>
/// <para>
/// Resolved styles for non-empty scope sequences are cached per resolver instance. The cache is
/// bounded and is cleared when it reaches its entry limit; scope sequences are compared by value,
/// and stored keys snapshot the sequence, so a caller that modifies an assigned collection after a
/// call only makes later lookups miss the cache - it cannot corrupt a cached style. Cache access is
/// synchronized, so one resolver can be shared by several editors across threads; the resolved
/// styles themselves are immutable.
/// </para>
/// </remarks>
public sealed class TextMateThemeStyleResolver
{
	internal const int MaxCacheEntryCount = 4096;

	private static readonly TextDecorationCollection s_underlineDecorations = CloneTextDecorations(TextDecorations.Underline);
	private static readonly TextDecorationCollection s_strikethroughDecorations = CloneTextDecorations(TextDecorations.Strikethrough);

	private static readonly Action<ILogger, string, Exception?> s_invalidForegroundColorLogger = LoggerMessage.Define<string>(
		LogLevel.Warning,
		new EventId(1, "InvalidForegroundColor"),
		"Invalid foreground color '{Color}' in TextMate theme rule; the foreground is ignored.");

	private static readonly Action<ILogger, string, Exception?> s_unsupportedSelectorLogger = LoggerMessage.Define<string>(
		LogLevel.Warning,
		new EventId(2, "UnsupportedSelector"),
		"TextMate theme selector '{Selector}' uses an unsupported selector operator and will never match.");

	private static readonly Action<ILogger, string, Exception?> s_unknownFontStyleTraitLogger = LoggerMessage.Define<string>(
		LogLevel.Warning,
		new EventId(3, "UnknownFontStyleTrait"),
		"Unrecognized font style trait '{Trait}' in a TextMate theme rule; the trait is ignored.");

	private readonly ILogger _logger;

	private readonly List<ParsedThemeRule> _rules;
	private readonly Brush? _defaultForeground;
	private readonly TokenFontTraits _defaultTraits;
	private readonly ConcurrentDictionary<ScopeCacheKey, TextRunStyle> _cache = new();

	/// <summary>
	/// Initializes a new instance of the <see cref="TextMateThemeStyleResolver"/> class.
	/// </summary>
	/// <param name="theme">The token theme whose rules drive style resolution.</param>
	/// <param name="logger">
	/// An optional logger used to report invalid foreground colors, unsupported selectors, and
	/// unrecognized font style traits.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="theme"/> is <see langword="null"/>.
	/// </exception>
	public TextMateThemeStyleResolver(TextMateTokenTheme theme, ILogger? logger = null)
	{
		ArgumentNullException.ThrowIfNull(theme);

		_logger = logger ?? NullLogger.Instance;
		_rules = CreateRules(theme, out _defaultForeground, out _defaultTraits);
	}

	/// <summary>
	/// Resolves the visual style for a token's scope sequence.
	/// </summary>
	/// <param name="scopes">
	/// The token scopes to resolve, ordered from the root scope to the innermost scope.
	/// </param>
	/// <returns>
	/// The resolved style, or the shared <see cref="TextRunStyle.Empty"/> instance when
	/// <paramref name="scopes"/> is <see langword="null"/> or empty.
	/// </returns>
	/// <remarks>
	/// The method is safe to call from any thread and returns an immutable style that is shared by all
	/// tokens whose scope sequence resolves to it.
	/// </remarks>
	public TextRunStyle Resolve(IReadOnlyList<string>? scopes)
	{
		if (scopes is null || scopes.Count == 0)
			return TextRunStyle.Empty;

		var lookupKey = new ScopeCacheKey(scopes);

		if (_cache.TryGetValue(lookupKey, out TextRunStyle cachedStyle))
			return cachedStyle;

		Brush? foreground = _defaultForeground;
		bool? isBold = _defaultTraits.Bold;
		bool? isItalic = _defaultTraits.Italic;
		bool? isUnderline = _defaultTraits.Underline;
		bool? isStrikethrough = _defaultTraits.Strikethrough;

		// VS Code parity: the theme is applied scope push by scope push. For every prefix of the token's
		// scope stack, the rightmost selector part is anchored at that prefix's innermost scope, all
		// matching rules are applied from the most specific to the least specific (a property is taken
		// from the first rule that sets it), and the values overwrite the accumulated attributes, so a
		// deeper push replaces the outcome of a shallower one.
		for (int anchorIndex = 0; anchorIndex < scopes.Count; anchorIndex++)
		{
			var matches = new List<ParsedThemeRule>();

			for (int i = 0; i < _rules.Count; i++)
			{
				if (_rules[i].MatchesAnchor(scopes, anchorIndex))
					matches.Add(_rules[i]);
			}

			if (matches.Count == 0)
				continue;

			// Most specific first; rules of equal specificity keep the later-listed rule, matching the way
			// VS Code merges duplicate selectors. The secondary comparison keeps the order deterministic
			// even though List<T>.Sort is unstable.
			matches.Sort(
				static (left, right) =>
				{
					int bySpecificity = CompareSpecificity(left, right);
					return bySpecificity != 0 ? bySpecificity : right.SequenceIndex.CompareTo(left.SequenceIndex);
				});

			Brush? pushForeground = null;
			bool? pushBold = null;
			bool? pushItalic = null;
			bool? pushUnderline = null;
			bool? pushStrikethrough = null;

			for (int i = 0; i < matches.Count; i++)
			{
				ParsedThemeRule rule = matches[i];

				pushForeground ??= rule.Foreground;
				pushBold ??= rule.Traits.Bold;
				pushItalic ??= rule.Traits.Italic;
				pushUnderline ??= rule.Traits.Underline;
				pushStrikethrough ??= rule.Traits.Strikethrough;

				if (pushForeground is not null
					&& pushBold.HasValue
					&& pushItalic.HasValue
					&& pushUnderline.HasValue
					&& pushStrikethrough.HasValue)
				{
					break;
				}
			}

			if (pushForeground is not null)
				foreground = pushForeground;

			if (pushBold.HasValue)
				isBold = pushBold;

			if (pushItalic.HasValue)
				isItalic = pushItalic;

			if (pushUnderline.HasValue)
				isUnderline = pushUnderline;

			if (pushStrikethrough.HasValue)
				isStrikethrough = pushStrikethrough;
		}

		TextDecorationCollection? textDecorations = CreateTextDecorations(isUnderline ?? false, isStrikethrough ?? false);

		TextRunStyle style = new(
			foreground,
			isBold ?? false,
			isItalic ?? false,
			textDecorations);

		StoreInCache(scopes, style);
		return style;
	}

	/// <summary>
	/// Orders two matching rules by specificity, mirroring VS Code's token-theme ranking (TextMate's
	/// "Ranking Matches"): a deeper scope wins, then a longer parent scope name (compared from the
	/// deepest parent towards the root), then a higher parent count.
	/// </summary>
	private static int CompareSpecificity(ParsedThemeRule left, ParsedThemeRule right)
	{
		if (left.ScopeDepth != right.ScopeDepth)
			return right.ScopeDepth.CompareTo(left.ScopeDepth);

		int leftParentIndex = 0;
		int rightParentIndex = 0;

		while (leftParentIndex < left.ParentScopes.Length && rightParentIndex < right.ParentScopes.Length)
		{
			int lengthDifference = right.ParentScopes[rightParentIndex].Length - left.ParentScopes[leftParentIndex].Length;

			if (lengthDifference != 0)
				return lengthDifference;

			leftParentIndex++;
			rightParentIndex++;
		}

		return right.ParentScopes.Length.CompareTo(left.ParentScopes.Length);
	}

	private void StoreInCache(IReadOnlyList<string> scopes, TextRunStyle style)
	{
		// Bounds resolver memory for long sessions that resolve many distinct scope sequences. Cached
		// styles are cheap to rebuild, so dropping the whole cache is preferable to tracking usage.
		if (_cache.Count >= MaxCacheEntryCount)
			_cache.Clear();

		_cache[ScopeCacheKey.CreateForStorage(scopes)] = style;
	}

	private List<ParsedThemeRule> CreateRules(
		TextMateTokenTheme theme,
		out Brush? defaultForeground,
		out TokenFontTraits defaultTraits)
	{
		var rules = new List<ParsedThemeRule>();
		defaultForeground = null;
		defaultTraits = default;

		for (int i = 0; i < theme.Rules.Count; i++)
		{
			TextMateTokenThemeRule rawRule = theme.Rules[i];

			if (rawRule is null)
				continue;

			string[] selectors = rawRule.Scope.Split(',');
			string? foregroundValue = NormalizeThemeColor(rawRule.Foreground);
			Brush? foreground = null;

			if (!string.IsNullOrWhiteSpace(foregroundValue))
			{
				try
				{
					foreground = CreateFrozenBrush(foregroundValue);
				}
				catch (Exception exception)
				{
					s_invalidForegroundColorLogger(_logger, rawRule.Foreground, exception);
					foreground = null;
				}
			}

			ParseFontStyle(rawRule.FontStyle, out TokenFontTraits fontTraits);

			if (string.IsNullOrWhiteSpace(rawRule.Scope))
			{
				// A rule without a scope is the theme's defaults block, which VS Code theme files use to
				// carry their base foreground and font style; later values win for each property.
				if (foreground is not null)
					defaultForeground = foreground;

				if (fontTraits.Bold.HasValue)
					defaultTraits = defaultTraits with { Bold = fontTraits.Bold };

				if (fontTraits.Italic.HasValue)
					defaultTraits = defaultTraits with { Italic = fontTraits.Italic };

				if (fontTraits.Underline.HasValue)
					defaultTraits = defaultTraits with { Underline = fontTraits.Underline };

				if (fontTraits.Strikethrough.HasValue)
					defaultTraits = defaultTraits with { Strikethrough = fontTraits.Strikethrough };

				continue;
			}

			// Each comma-separated selector becomes its own rule so it carries its own specificity data.
			// Selectors that use an unsupported operator can never match, so they are reported once here
			// and dropped instead of being re-checked on every resolution.
			for (int selectorIndex = 0; selectorIndex < selectors.Length; selectorIndex++)
			{
				string selector = selectors[selectorIndex].Trim();
				string[] parts = selector.Split(' ', StringSplitOptions.RemoveEmptyEntries);

				if (parts.Length == 0)
					continue;

				if (ContainsUnsupportedOperator(parts))
				{
					s_unsupportedSelectorLogger(_logger, selector, null);
					continue;
				}

				string[] parentScopes = Array.Empty<string>();

				if (parts.Length > 1)
				{
					parentScopes = new string[parts.Length - 1];

					// Parent parts are stored deepest-first, the order the ranking compares them in.
					for (int partIndex = 0; partIndex < parentScopes.Length; partIndex++)
						parentScopes[partIndex] = parts[parts.Length - 2 - partIndex];
				}

				int scopeDepth = parts[^1].Split('.').Length;

				rules.Add(new ParsedThemeRule(
					parts,
					parentScopes,
					scopeDepth,
					rules.Count,
					foreground,
					fontTraits));
			}
		}

		return rules;
	}

	private void ParseFontStyle(string? fontStyleValue, out TokenFontTraits traits)
	{
		// An absent font-style value leaves the traits unset, so inherited formatting is kept. Any
		// present value is an explicit reset first: TextMate theme data uses an empty string to clear
		// inherited traits, and the recognized trait names then enable the individual traits.
		if (fontStyleValue is null)
		{
			traits = default;
			return;
		}

		bool isBold = false;
		bool isItalic = false;
		bool isUnderline = false;
		bool isStrikethrough = false;

		string[] parts = fontStyleValue.Split(' ', StringSplitOptions.RemoveEmptyEntries);

		for (int i = 0; i < parts.Length; i++)
		{
			string trait = parts[i].Trim();

			switch (trait)
			{
				case "bold":
					isBold = true;
					break;

				case "italic":
					isItalic = true;
					break;

				case "underline":
					isUnderline = true;
					break;

				case "strikethrough":
					isStrikethrough = true;
					break;

				case "none":
					// The reset already happened; the keyword is accepted as an explicit spelling of it.
					break;

				default:
					s_unknownFontStyleTraitLogger(_logger, trait, null);
					break;
			}
		}

		traits = new TokenFontTraits(isBold, isItalic, isUnderline, isStrikethrough);
	}

	/// <summary>
	/// Normalizes a TextMate theme color value so the WPF color parser reads it correctly. TextMate
	/// data spells eight-digit colors as <c>#RRGGBBAA</c> and four-digit colors as <c>#RGBA</c>, while
	/// WPF spells them as <c>#AARRGGBB</c> and <c>#ARGB</c>.
	/// </summary>
	private static string? NormalizeThemeColor(string? colorValue)
	{
		if (string.IsNullOrWhiteSpace(colorValue))
			return colorValue;

		string trimmed = colorValue.Trim();

		if (trimmed.Length == 9 && trimmed[0] == '#' && IsHexDigits(trimmed.AsSpan(1)))
			return string.Concat("#", trimmed.AsSpan(7, 2), trimmed.AsSpan(1, 6));

		if (trimmed.Length == 5 && trimmed[0] == '#' && IsHexDigits(trimmed.AsSpan(1)))
		{
			// #RGBA moves the alpha digit to the front and doubles every digit.
			return $"#{trimmed[4]}{trimmed[4]}{trimmed[1]}{trimmed[1]}{trimmed[2]}{trimmed[2]}{trimmed[3]}{trimmed[3]}";
		}

		return colorValue;
	}

	private static bool IsHexDigits(ReadOnlySpan<char> value)
	{
		for (int i = 0; i < value.Length; i++)
		{
			char character = value[i];

			if (!char.IsAsciiHexDigit(character))
				return false;
		}

		return true;
	}

	private static bool ContainsUnsupportedOperator(string[] parts)
	{
		for (int i = 0; i < parts.Length; i++)
		{
			string part = parts[i];

			if (part.Length == 0)
				continue;

			// The wildcard, parenthesis, and priority operators can appear in any position of a selector
			// part, while the exclusion and direct-child operators only lead one.
			if (part[0] is '-' or '>'
				|| part.Contains('*')
				|| part.Contains('(')
				|| part.Contains(')')
				|| part.StartsWith("L:", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	private static TextDecorationCollection? CreateTextDecorations(bool isUnderline, bool isStrikethrough)
	{
		if (isUnderline && isStrikethrough)
		{
			var decorations = new TextDecorationCollection();

			foreach (TextDecoration decoration in s_underlineDecorations)
				decorations.Add(decoration);

			foreach (TextDecoration decoration in s_strikethroughDecorations)
				decorations.Add(decoration);

			decorations.Freeze();
			return decorations;
		}

		if (isUnderline)
			return s_underlineDecorations;

		if (isStrikethrough)
			return s_strikethroughDecorations;

		return null;
	}

	private static TextDecorationCollection CloneTextDecorations(TextDecorationCollection source)
	{
		var clone = source.Clone();
		clone.Freeze();
		return clone;
	}

	/// <summary>
	/// Dictionary key describing a token scope sequence without allocating a combined string per lookup.
	/// Keys created for storage own a snapshot of the sequence, so later mutation of a caller-owned list
	/// cannot corrupt the cache.
	/// </summary>
	private readonly struct ScopeCacheKey : IEquatable<ScopeCacheKey>
	{
		private readonly IReadOnlyList<string> _scopes;
		private readonly int _hashCode;

		public ScopeCacheKey(IReadOnlyList<string> scopes)
			: this(scopes, ownsSnapshot: false)
		{
		}

		private ScopeCacheKey(IReadOnlyList<string> scopes, bool ownsSnapshot)
		{
			_scopes = ownsSnapshot ? [.. scopes] : scopes;
			_hashCode = ComputeHashCode(scopes);
		}

		public static ScopeCacheKey CreateForStorage(IReadOnlyList<string> scopes)
			=> new(scopes, ownsSnapshot: true);

		public bool Equals(ScopeCacheKey other)
		{
			if (_scopes.Count != other._scopes.Count)
				return false;

			for (int i = 0; i < _scopes.Count; i++)
			{
				if (!string.Equals(_scopes[i], other._scopes[i], StringComparison.Ordinal))
					return false;
			}

			return true;
		}

		public override bool Equals(object? obj)
			=> obj is ScopeCacheKey other && Equals(other);

		public override int GetHashCode()
			=> _hashCode;

		private static int ComputeHashCode(IReadOnlyList<string> scopes)
		{
			var hashCode = new HashCode();

			for (int i = 0; i < scopes.Count; i++)
				hashCode.Add(scopes[i], StringComparer.Ordinal);

			return hashCode.ToHashCode();
		}
	}

	/// <summary>
	/// Bundles the four optional font traits of a parsed theme rule; <see langword="null"/> means the
	/// trait was not set by the rule.
	/// </summary>
	private readonly record struct TokenFontTraits(bool? Bold, bool? Italic, bool? Underline, bool? Strikethrough);

	private sealed class ParsedThemeRule
	{
		private readonly string[] _selectorParts;

		public ParsedThemeRule(
			string[] selectorParts,
			string[] parentScopes,
			int scopeDepth,
			int sequenceIndex,
			Brush? foreground,
			TokenFontTraits traits)
		{
			_selectorParts = selectorParts;
			ParentScopes = parentScopes;
			ScopeDepth = scopeDepth;
			SequenceIndex = sequenceIndex;
			Foreground = foreground;
			Traits = traits;
		}

		/// <summary>
		/// Gets the parent selector parts in matching order: index 0 is the deepest parent.
		/// </summary>
		public string[] ParentScopes { get; }

		/// <summary>
		/// Gets the number of dot-separated segments in the rightmost selector part.
		/// </summary>
		public int ScopeDepth { get; }

		/// <summary>
		/// Gets the position of the parsed rule in the resolver's rule list; it breaks specificity ties
		/// towards the later rule.
		/// </summary>
		public int SequenceIndex { get; }

		public Brush? Foreground { get; }
		public TokenFontTraits Traits { get; }

		/// <summary>
		/// Determines whether the selector matches the token's scope sequence with its rightmost part
		/// anchored at the given scope: that scope must match the rightmost selector part, and the
		/// remaining parts must match enclosing scopes in order.
		/// </summary>
		/// <param name="scopes">The token scopes, ordered from the root scope to the innermost scope.</param>
		/// <param name="anchorIndex">The index of the scope that acts as the innermost scope.</param>
		/// <returns><see langword="true"/> when the selector matches the anchored scope path.</returns>
		public bool MatchesAnchor(IReadOnlyList<string> scopes, int anchorIndex)
		{
			if (!MatchesScope(_selectorParts[^1], scopes[anchorIndex]))
				return false;

			return MatchesEnclosingScopes(_selectorParts, scopes, anchorIndex);
		}

		private static bool MatchesEnclosingScopes(string[] parts, IReadOnlyList<string> scopes, int anchorIndex)
		{
			int partIndex = parts.Length - 2;

			for (int scopeIndex = anchorIndex - 1; scopeIndex >= 0 && partIndex >= 0; scopeIndex--)
			{
				if (MatchesScope(parts[partIndex], scopes[scopeIndex]))
					partIndex--;
			}

			return partIndex < 0;
		}

		private static bool MatchesScope(string selector, string scope)
		{
			if (string.Equals(scope, selector, StringComparison.Ordinal))
				return true;

			return scope.StartsWith(selector + ".", StringComparison.Ordinal);
		}
	}
}

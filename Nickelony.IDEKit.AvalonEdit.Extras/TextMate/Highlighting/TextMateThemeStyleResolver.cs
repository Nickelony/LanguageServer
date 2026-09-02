using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static Nickelony.IDEKit.Infrastructure.BrushHelpers;

namespace Nickelony.IDEKit.AvalonEdit.Extras.TextMate.Highlighting;

/// <summary>
/// Resolves TextMate token scopes into <see cref="TextMateHighlightingStyle"/> instances using a
/// <see cref="TextMateTokenTheme"/>. Resolved styles are cached for each scope sequence.
/// </summary>
public sealed class TextMateThemeStyleResolver
{
	private static readonly TextDecorationCollection s_underlineDecorations = CreateTextDecorations(TextDecorations.Underline);
	private static readonly TextDecorationCollection s_strikethroughDecorations = CreateTextDecorations(TextDecorations.Strikethrough);

	private static readonly Action<ILogger, string, Exception?> s_invalidForegroundColorLogger = LoggerMessage.Define<string>(
		LogLevel.Warning,
		new EventId(0, "InvalidForegroundColor"),
		"Invalid foreground color '{Color}' in TextMate theme rule; the foreground is ignored.");

	private readonly ILogger _logger;

	private readonly List<ParsedThemeRule> _rules;
	private readonly Dictionary<string, TextMateHighlightingStyle> _cache = new(StringComparer.Ordinal);

	/// <summary>
	/// Initializes a new instance of the <see cref="TextMateThemeStyleResolver"/> class.
	/// </summary>
	/// <param name="theme">The token theme whose rules drive style resolution.</param>
	/// <param name="logger">An optional logger for malformed theme data.</param>
	public TextMateThemeStyleResolver(TextMateTokenTheme theme, ILogger? logger = null)
	{
		ArgumentNullException.ThrowIfNull(theme);

		_logger = logger ?? NullLogger.Instance;
		_rules = CreateRules(theme);
	}

	/// <summary>
	/// Resolves the visual style for a token's scope sequence.
	/// </summary>
	/// <param name="scopes">The token scopes to resolve.</param>
	/// <returns>
	/// The resolved style, or <see cref="TextMateHighlightingStyle.Empty"/> when no scope matches a theme rule.
	/// When several rules match, more-specific scope selectors provide each formatting value first.
	/// </returns>
	public TextMateHighlightingStyle Resolve(IList<string> scopes)
	{
		if (scopes is null || scopes.Count == 0)
			return TextMateHighlightingStyle.Empty;

		string cacheKey = string.Join(" ", scopes);

		if (_cache.TryGetValue(cacheKey, out TextMateHighlightingStyle? cachedStyle) && cachedStyle is not null)
			return cachedStyle;

		Brush? foreground = null;
		bool? isBold = null;
		bool? isItalic = null;
		bool? isUnderline = null;
		bool? isStrikethrough = null;

		var matches = new List<RuleMatch>();

		for (int i = 0; i < _rules.Count; i++)
		{
			int score = _rules[i].GetMatchScore(scopes);

			if (score >= 0)
				matches.Add(new RuleMatch(_rules[i], score));
		}

		matches.Sort((left, right) => right.Score.CompareTo(left.Score));

		for (int i = 0; i < matches.Count; i++)
		{
			ParsedThemeRule rule = matches[i].Rule;

			if (foreground is null && rule.Foreground is not null)
				foreground = rule.Foreground;

			if (!isBold.HasValue && rule.IsBold.HasValue)
				isBold = rule.IsBold.Value;

			if (!isItalic.HasValue && rule.IsItalic.HasValue)
				isItalic = rule.IsItalic.Value;

			if (!isUnderline.HasValue && rule.IsUnderline.HasValue)
				isUnderline = rule.IsUnderline.Value;

			if (!isStrikethrough.HasValue && rule.IsStrikethrough.HasValue)
				isStrikethrough = rule.IsStrikethrough.Value;

			if (foreground is not null
				&& isBold.HasValue
				&& isItalic.HasValue
				&& isUnderline.HasValue
				&& isStrikethrough.HasValue)
			{
				break;
			}
		}

		TextDecorationCollection? textDecorations = CreateTextDecorations(isUnderline ?? false, isStrikethrough ?? false);

		TextMateHighlightingStyle style = new(
			foreground,
			isBold ?? false,
			isItalic ?? false,
			textDecorations);

		_cache[cacheKey] = style;
		return style;
	}

	private List<ParsedThemeRule> CreateRules(TextMateTokenTheme theme)
	{
		var rules = new List<ParsedThemeRule>();

		if (theme.Rules is null)
			return rules;

		for (int i = 0; i < theme.Rules.Count; i++)
		{
			TextMateTokenThemeRule rawRule = theme.Rules[i];

			if (rawRule is null || string.IsNullOrWhiteSpace(rawRule.Scope))
				continue;

			string[] selectors = rawRule.Scope.Split(',');

			for (int selectorIndex = 0; selectorIndex < selectors.Length; selectorIndex++)
				selectors[selectorIndex] = selectors[selectorIndex].Trim();

			Brush? foreground = null;

			if (!string.IsNullOrWhiteSpace(rawRule.Foreground))
			{
				try
				{
					foreground = CreateFrozenBrush(rawRule.Foreground);
				}
				catch (Exception exception)
				{
					s_invalidForegroundColorLogger(_logger, rawRule.Foreground, exception);
					foreground = null;
				}
			}

			ParseFontStyle(rawRule.FontStyle, out bool? isBold, out bool? isItalic, out bool? isUnderline, out bool? isStrikethrough);

			rules.Add(new ParsedThemeRule(selectors, foreground, isBold, isItalic, isUnderline, isStrikethrough));
		}

		return rules;
	}

	private static void ParseFontStyle(string fontStyleValue, out bool? isBold, out bool? isItalic, out bool? isUnderline, out bool? isStrikethrough)
	{
		isBold = null;
		isItalic = null;
		isUnderline = null;
		isStrikethrough = null;

		if (string.IsNullOrWhiteSpace(fontStyleValue))
			return;

		string[] parts = fontStyleValue.Split(' ', StringSplitOptions.RemoveEmptyEntries);

		for (int i = 0; i < parts.Length; i++)
		{
			switch (parts[i].Trim())
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
					isBold = false;
					isItalic = false;
					isUnderline = false;
					isStrikethrough = false;
					break;
			}
		}
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

	private static TextDecorationCollection CreateTextDecorations(TextDecorationCollection source)
	{
		var clone = source.Clone();
		clone.Freeze();
		return clone;
	}

	private sealed class ParsedThemeRule
	{
		private readonly string[] _selectors;

		public ParsedThemeRule(string[] selectors, Brush? foreground, bool? isBold, bool? isItalic, bool? isUnderline, bool? isStrikethrough)
		{
			_selectors = selectors;
			Foreground = foreground;
			IsBold = isBold;
			IsItalic = isItalic;
			IsUnderline = isUnderline;
			IsStrikethrough = isStrikethrough;
		}

		public Brush? Foreground { get; }
		public bool? IsBold { get; }
		public bool? IsItalic { get; }
		public bool? IsUnderline { get; }
		public bool? IsStrikethrough { get; }

		public int GetMatchScore(IList<string> scopes)
		{
			int bestScore = -1;

			for (int selectorIndex = 0; selectorIndex < _selectors.Length; selectorIndex++)
			{
				string selector = _selectors[selectorIndex];

				if (string.IsNullOrWhiteSpace(selector))
					continue;

				for (int scopeIndex = 0; scopeIndex < scopes.Count; scopeIndex++)
				{
					string scope = scopes[scopeIndex];

					if (!MatchesScope(selector, scope))
						continue;

					int selectorDepth = selector.Split('.').Length;
					int score = (selectorDepth * 1000) + (selector.Length * 10) + scope.Length;

					if (score > bestScore)
						bestScore = score;
				}
			}

			return bestScore;
		}

		private static bool MatchesScope(string selector, string scope)
		{
			if (string.Equals(scope, selector, StringComparison.Ordinal))
				return true;

			return scope.StartsWith(selector + ".", StringComparison.Ordinal);
		}
	}

	private readonly struct RuleMatch
	{
		public RuleMatch(ParsedThemeRule rule, int score)
		{
			Rule = rule;
			Score = score;
		}

		public ParsedThemeRule Rule { get; }
		public int Score { get; }
	}
}

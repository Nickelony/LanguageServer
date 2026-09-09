using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Nickelony.IDEKit.Core.FindReplace;

/// <summary>
/// Pure text-level find and replace primitives with no UI or document dependencies.
/// </summary>
/// <remarks>
/// <para>
/// The helpers reuse a bounded cache of <see cref="Regex"/> instances keyed by pattern, options,
/// and timeout, so repeated searches over one pattern do not re-parse it.
/// </para>
/// <para>
/// By default the helpers apply <see cref="Regex.InfiniteMatchTimeout"/>; hosts that search with
/// caller-supplied patterns can pass a finite <c>matchTimeout</c> to bound backtracking, which can
/// throw <see cref="RegexMatchTimeoutException"/>. For untrusted patterns, prefer adding
/// <see cref="RegexOptions.NonBacktracking"/> (.NET 7 and later) to the query options: it rejects
/// patterns the engine cannot run without backtracking instead of relying on the timeout, which
/// bounds but cannot prevent catastrophic backtracking; a pattern the engine cannot run throws
/// <see cref="NotSupportedException"/> when the regex is constructed.
/// </para>
/// <para>
/// The helpers define their own search direction - next from a start offset, previous ending
/// at or before an end offset, and match enumeration in document order - so
/// <see cref="RegexOptions.RightToLeft"/> is rejected with an <see cref="ArgumentException"/>
/// instead of silently changing what the helpers return.
/// </para>
/// <para>
/// Every helper that takes a pattern, options, and timeout also has an overload taking a
/// <see cref="TextSearchQuery"/> that bundles the three, so callers can carry one query object
/// through search, count, and replace operations.
/// </para>
/// <para>
/// The helpers operate on regular-expression patterns. <see cref="BuildPattern"/> is the entry
/// point for literal (non-regular-expression) find text: it escapes the text so one code path
/// serves both modes with identical match semantics. A literal fast path built on <c>IndexOf</c>
/// would be faster for find-as-you-type, but it would duplicate the offset and replacement logic;
/// the single-engine design is deliberate.
/// </para>
/// </remarks>
public static class FindReplaceText
{
	/// <summary>
	/// An empty pattern means no search text; this pattern never matches, so callers get no matches
	/// instead of a match at every position.
	/// </summary>
	private static readonly Regex s_neverMatchRegex = new("(?!)", RegexOptions.None, Regex.InfiniteMatchTimeout);

	/// <summary>
	/// Builds a regex pattern from the find text and search options.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A regular expression is grouped when <paramref name="matchWholeWord"/> is set, so that every
	/// alternative keeps both word boundaries.
	/// </para>
	/// <para>
	/// The word-boundary form uses <c>\b</c>, which anchors on word characters (letters, digits, and
	/// underscore). Find text that starts or ends with a non-word character (for example <c>-foo</c>)
	/// does not match after a space, and can match inside a larger token; a token-accurate whole-word
	/// rule needs the caller's identifier policy, which this neutral primitive does not know.
	/// </para>
	/// </remarks>
	/// <param name="findText">The text to find.</param>
	/// <param name="useRegex">Whether the find text is already a regular expression.</param>
	/// <param name="matchWholeWord">Whether to add a regular-expression word boundary before and after the pattern.</param>
	/// <returns>The built pattern, or an empty string when the find text is empty.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="findText"/> is <see langword="null"/>.</exception>
	public static string BuildPattern(string findText, bool useRegex, bool matchWholeWord)
	{
		ArgumentNullException.ThrowIfNull(findText);

		if (string.IsNullOrEmpty(findText))
			return string.Empty;

		string pattern = useRegex ? findText : Regex.Escape(findText);

		if (matchWholeWord)
		{
			// Group regular expressions so every alternative keeps both word boundaries; escaped
			// literal text cannot contain a top-level alternation and needs no grouping.
			pattern = useRegex ? @"\b(?:" + pattern + @")\b" : @"\b" + pattern + @"\b";
		}

		return pattern;
	}

	/// <summary>
	/// Builds <see cref="RegexOptions"/> from the case-sensitive flag.
	/// </summary>
	/// <remarks>
	/// Case-insensitive matching adds <see cref="RegexOptions.CultureInvariant"/>, so the result does
	/// not depend on the current culture (for example the Turkish dotted and dotless I). Callers that
	/// build their own options should add the flag themselves for culture-independent matching.
	/// </remarks>
	/// <param name="caseSensitive">Whether matching should distinguish letter case.</param>
	/// <returns>The regex options for the requested case sensitivity.</returns>
	public static RegexOptions BuildRegexOptions(bool caseSensitive)
		=> caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

	/// <summary>
	/// Returns the number of matches of <paramref name="pattern"/> in <paramref name="text"/>.
	/// </summary>
	/// <param name="text">The text to search.</param>
	/// <param name="pattern">The regular expression pattern.</param>
	/// <param name="options">The options used for matching.</param>
	/// <param name="matchTimeout">
	/// The maximum time for a single match attempt; see the class remarks for the timeout contract.
	/// </param>
	/// <returns>The number of matches, or zero when the pattern is empty.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="text"/> or <paramref name="pattern"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="pattern"/> is not a valid regular expression.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="matchTimeout"/> is negative (except for <see cref="Regex.InfiniteMatchTimeout"/>) or exceeds
	/// the maximum timeout accepted by <see cref="Regex"/>.
	/// </exception>
	/// <exception cref="RegexMatchTimeoutException">A match attempt exceeds a finite <paramref name="matchTimeout"/>.</exception>
	public static int CountMatches(string text, string pattern, RegexOptions options, TimeSpan matchTimeout = default)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(pattern);

		ValidateMatchTimeout(matchTimeout);

		return TryGetSearchRegex(pattern, options, matchTimeout, out Regex? regex) ? regex.Count(text) : 0;
	}

	/// <summary>
	/// Returns the number of matches of the query pattern in <paramref name="text"/>. Shorthand for
	/// <see cref="CountMatches(string, string, RegexOptions, TimeSpan)"/> with the query's pattern,
	/// options, and match timeout.
	/// </summary>
	/// <param name="text">The text to search.</param>
	/// <param name="query">The search pattern, match options, and per-match timeout.</param>
	/// <returns>The number of matches, or zero when the pattern is empty.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="text"/> is <see langword="null"/>, or the query pattern is
	/// <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">The query pattern is not a valid regular expression.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The query match timeout is negative (except for <see cref="Regex.InfiniteMatchTimeout"/>) or exceeds
	/// the maximum timeout accepted by <see cref="Regex"/>.
	/// </exception>
	/// <exception cref="RegexMatchTimeoutException">A match attempt exceeds a finite query match timeout.</exception>
	public static int CountMatches(string text, TextSearchQuery query)
		=> CountMatches(text, query.Pattern, query.Options, query.MatchTimeout);

	/// <summary>
	/// Returns all matches of <paramref name="pattern"/> in <paramref name="text"/>.
	/// </summary>
	/// <remarks>
	/// The returned collection is evaluated lazily while it is enumerated, so a finite
	/// <paramref name="matchTimeout"/> is enforced during enumeration rather than by this call.
	/// </remarks>
	/// <param name="text">The text to search.</param>
	/// <param name="pattern">The regular expression pattern.</param>
	/// <param name="options">The options used for matching.</param>
	/// <param name="matchTimeout">
	/// The maximum time for a single match attempt, or <see cref="TimeSpan.Zero"/> (the default) to
	/// use <see cref="Regex.InfiniteMatchTimeout"/>.
	/// </param>
	/// <returns>The matches in document order, or an empty collection when the pattern is empty.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="text"/> or <paramref name="pattern"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="pattern"/> is not a valid regular expression.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="matchTimeout"/> is negative (except for <see cref="Regex.InfiniteMatchTimeout"/>) or exceeds
	/// the maximum timeout accepted by <see cref="Regex"/>.
	/// </exception>
	/// <exception cref="RegexMatchTimeoutException">A match attempt exceeds a finite <paramref name="matchTimeout"/> while the returned collection is enumerated.</exception>
	public static MatchCollection FindAllMatches(string text, string pattern, RegexOptions options, TimeSpan matchTimeout = default)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(pattern);

		ValidateMatchTimeout(matchTimeout);

		if (!TryGetSearchRegex(pattern, options, matchTimeout, out Regex? regex))
			return s_neverMatchRegex.Matches(text);

		return regex.Matches(text);
	}

	/// <summary>
	/// Returns all matches of the query pattern in <paramref name="text"/>. Shorthand for
	/// <see cref="FindAllMatches(string, string, RegexOptions, TimeSpan)"/> with the query's pattern,
	/// options, and match timeout.
	/// </summary>
	/// <remarks>
	/// The returned collection is evaluated lazily while it is enumerated, so a finite
	/// <paramref name="query"/> match timeout is enforced during enumeration rather than by this call.
	/// </remarks>
	/// <param name="text">The text to search.</param>
	/// <param name="query">The search pattern, match options, and per-match timeout.</param>
	/// <returns>The matches in document order, or an empty collection when the pattern is empty.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="text"/> is <see langword="null"/>, or the query pattern is
	/// <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">The query pattern is not a valid regular expression.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The query match timeout is negative (except for <see cref="Regex.InfiniteMatchTimeout"/>) or exceeds
	/// the maximum timeout accepted by <see cref="Regex"/>.
	/// </exception>
	/// <exception cref="RegexMatchTimeoutException">A match attempt exceeds a finite query match timeout while the returned collection is enumerated.</exception>
	public static MatchCollection FindAllMatches(string text, TextSearchQuery query)
		=> FindAllMatches(text, query.Pattern, query.Options, query.MatchTimeout);

	/// <summary>
	/// Finds the first match that starts at or after <paramref name="startOffset"/>.
	/// </summary>
	/// <remarks>
	/// Matches are located in the full document text, so regular-expression anchors and boundary
	/// assertions (<c>^</c>, <c>$</c>, <c>\A</c>, <c>\z</c>, and <c>\b</c>) resolve against the
	/// document rather than against the searched range. The returned match carries absolute document
	/// offsets in its <c>Index</c> property.
	/// </remarks>
	/// <param name="text">The full document text.</param>
	/// <param name="startOffset">
	/// The zero-based document offset at which to start searching; matches that begin earlier are not
	/// considered.
	/// </param>
	/// <param name="query">The search pattern, match options, and per-match timeout.</param>
	/// <returns>
	/// The first match that starts at or after <paramref name="startOffset"/>, or
	/// <see langword="null"/> when there is none.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="text"/> is <see langword="null"/>, or the query pattern is
	/// <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="startOffset"/> is outside the document text, or the query match timeout is
	/// negative (except for <see cref="Regex.InfiniteMatchTimeout"/>) or exceeds the maximum timeout
	/// accepted by <see cref="Regex"/>.
	/// </exception>
	/// <exception cref="ArgumentException">The query pattern is not a valid regular expression.</exception>
	/// <exception cref="RegexMatchTimeoutException">A match attempt exceeds a finite query match timeout.</exception>
	public static Match? FindNextMatch(string text, int startOffset, TextSearchQuery query)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(query.Pattern);

		ValidateMatchTimeout(query.MatchTimeout);

		ArgumentOutOfRangeException.ThrowIfNegative(startOffset);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(startOffset, text.Length);

		if (!TryGetSearchRegex(query.Pattern, query.Options, query.MatchTimeout, out Regex? regex))
			return null;

		// Regex.Match reports a failed search as a non-successful match object, which is normalized
		// to null so both find helpers share the same "no match" shape.
		Match match = regex.Match(text, startOffset);

		return match.Success ? match : null;
	}

	/// <summary>
	/// Finds the first match that starts at or after <paramref name="startOffset"/>.
	/// </summary>
	/// <remarks>
	/// The match semantics are those of <see cref="FindNextMatch(string, int, TextSearchQuery)"/>.
	/// </remarks>
	/// <param name="text">The full document text.</param>
	/// <param name="startOffset">The zero-based document offset at which the search starts.</param>
	/// <param name="pattern">The regular expression pattern.</param>
	/// <param name="options">The options used for matching.</param>
	/// <param name="matchTimeout">
	/// The maximum time for a single match attempt; see the class remarks for the timeout contract.
	/// </param>
	/// <returns>
	/// The first match that starts at or after <paramref name="startOffset"/>, or
	/// <see langword="null"/> when there is none.
	/// </returns>
	public static Match? FindNextMatch(string text, int startOffset, string pattern, RegexOptions options, TimeSpan matchTimeout = default)
		=> FindNextMatch(text, startOffset, new TextSearchQuery(pattern, options, matchTimeout));

	/// <summary>
	/// Finds the last match that ends at or before <paramref name="endOffset"/>.
	/// </summary>
	/// <remarks>
	/// Matches are located in the full document text, so regular-expression anchors and boundary
	/// assertions (<c>^</c>, <c>$</c>, <c>\A</c>, <c>\z</c>, and <c>\b</c>) resolve against the
	/// document rather than against the searched range. The returned match carries absolute document
	/// offsets in its <c>Index</c> property. The scan starts at the beginning of the document and stops
	/// at the first match that reaches beyond <paramref name="endOffset"/>, so the document tail is
	/// not evaluated but the prefix is re-scanned on every call; hosts that navigate through many
	/// matches should cache the results of
	/// <see cref="FindAllMatches(string, string, RegexOptions, TimeSpan)"/> instead.
	/// </remarks>
	/// <param name="text">The full document text.</param>
	/// <param name="endOffset">
	/// The zero-based document offset the match must not reach beyond; matches that end later are not
	/// considered.
	/// </param>
	/// <param name="query">The search pattern, match options, and per-match timeout.</param>
	/// <returns>
	/// The last match that ends at or before <paramref name="endOffset"/>, or <see langword="null"/>
	/// when there is none.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="text"/> is <see langword="null"/>, or the query pattern is
	/// <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="endOffset"/> is outside the document text, or the query match timeout is
	/// negative (except for <see cref="Regex.InfiniteMatchTimeout"/>) or exceeds the maximum timeout
	/// accepted by <see cref="Regex"/>.
	/// </exception>
	/// <exception cref="ArgumentException">The query pattern is not a valid regular expression.</exception>
	/// <exception cref="RegexMatchTimeoutException">A match attempt exceeds a finite query match timeout.</exception>
	public static Match? FindPreviousMatch(string text, int endOffset, TextSearchQuery query)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(query.Pattern);

		ValidateMatchTimeout(query.MatchTimeout);

		ArgumentOutOfRangeException.ThrowIfNegative(endOffset);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(endOffset, text.Length);

		if (!TryGetSearchRegex(query.Pattern, query.Options, query.MatchTimeout, out Regex? regex))
			return null;

		Match? previousMatch = null;

		// Matches are enumerated in document order and do not overlap, so the first match that
		// reaches beyond the requested end also ends the scan.
		foreach (Match match in regex.Matches(text))
		{
			if (match.Index + match.Length > endOffset)
				break;

			previousMatch = match;
		}

		return previousMatch;
	}

	/// <summary>
	/// Finds the last match that ends at or before <paramref name="endOffset"/>.
	/// </summary>
	/// <remarks>
	/// The match semantics are those of <see cref="FindPreviousMatch(string, int, TextSearchQuery)"/>.
	/// </remarks>
	/// <param name="text">The full document text.</param>
	/// <param name="endOffset">
	/// The zero-based document offset the match must not reach beyond; matches that end later are not
	/// considered.
	/// </param>
	/// <param name="pattern">The regular expression pattern.</param>
	/// <param name="options">The options used for matching.</param>
	/// <param name="matchTimeout">
	/// The maximum time for a single match attempt; see the class remarks for the timeout contract.
	/// </param>
	/// <returns>
	/// The last match that ends at or before <paramref name="endOffset"/>, or <see langword="null"/>
	/// when there is none.
	/// </returns>
	public static Match? FindPreviousMatch(string text, int endOffset, string pattern, RegexOptions options, TimeSpan matchTimeout = default)
		=> FindPreviousMatch(text, endOffset, new TextSearchQuery(pattern, options, matchTimeout));

	/// <summary>
	/// Replaces all matches of <paramref name="pattern"/> in <paramref name="text"/>
	/// with <paramref name="replacement"/>.
	/// </summary>
	/// <param name="text">The text to transform.</param>
	/// <param name="pattern">The regular expression pattern.</param>
	/// <param name="replacement">
	/// The replacement pattern. Because the method delegates to
	/// <see cref="Regex.Replace(string, string)"/>, substitution tokens such as <c>$1</c> (captured
	/// group) and <c>$$</c> (literal <c>$</c>) are interpreted; they are not escaped automatically.
	/// </param>
	/// <param name="options">The options used for matching.</param>
	/// <param name="matchTimeout">
	/// The maximum time for a single match attempt; see the class remarks for the timeout contract.
	/// </param>
	/// <returns>The transformed text, or the original text when the pattern is empty.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="text"/>, <paramref name="pattern"/>, or <paramref name="replacement"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="pattern"/> is not a valid regular expression.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="matchTimeout"/> is negative (except for <see cref="Regex.InfiniteMatchTimeout"/>) or exceeds
	/// the maximum timeout accepted by <see cref="Regex"/>.
	/// </exception>
	/// <exception cref="RegexMatchTimeoutException">A match attempt exceeds a finite <paramref name="matchTimeout"/>.</exception>
	public static string ReplaceAll(string text, string pattern, string replacement, RegexOptions options, TimeSpan matchTimeout = default)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(pattern);
		ArgumentNullException.ThrowIfNull(replacement);

		ValidateMatchTimeout(matchTimeout);

		if (!TryGetSearchRegex(pattern, options, matchTimeout, out Regex? regex))
			return text;

		return regex.Replace(text, replacement);
	}

	/// <summary>
	/// Replaces all matches of the query pattern in <paramref name="text"/> with
	/// <paramref name="replacement"/>. Shorthand for
	/// <see cref="ReplaceAll(string, string, string, RegexOptions, TimeSpan)"/> with the query's
	/// pattern, options, and match timeout.
	/// </summary>
	/// <param name="text">The text to transform.</param>
	/// <param name="query">The search pattern, match options, and per-match timeout.</param>
	/// <param name="replacement">
	/// The replacement pattern. Because the method delegates to
	/// <see cref="Regex.Replace(string, string)"/>, substitution tokens such as <c>$1</c> (captured
	/// group) and <c>$$</c> (literal <c>$</c>) are interpreted; they are not escaped automatically.
	/// </param>
	/// <returns>The transformed text, or the original text when the pattern is empty.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="text"/> or <paramref name="replacement"/> is <see langword="null"/>, or the
	/// query pattern is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">The query pattern is not a valid regular expression.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The query match timeout is negative (except for <see cref="Regex.InfiniteMatchTimeout"/>) or exceeds
	/// the maximum timeout accepted by <see cref="Regex"/>.
	/// </exception>
	/// <exception cref="RegexMatchTimeoutException">A match attempt exceeds a finite query match timeout.</exception>
	public static string ReplaceAll(string text, TextSearchQuery query, string replacement)
		=> ReplaceAll(text, query.Pattern, replacement, query.Options, query.MatchTimeout);

	/// <summary>
	/// Gets the cached regex for a search, or reports that the pattern is empty. An empty pattern
	/// means no search text, so every helper treats it as "no search" and reports its own empty
	/// result instead of matching at every position.
	/// </summary>
	/// <param name="pattern">The regular expression pattern.</param>
	/// <param name="options">The options used for matching.</param>
	/// <param name="matchTimeout">The per-match timeout.</param>
	/// <param name="regex">Receives the cached regex when the pattern is not empty.</param>
	/// <returns><see langword="true"/> when the pattern is not empty and a regex is available.</returns>
	private static bool TryGetSearchRegex(string pattern, RegexOptions options, TimeSpan matchTimeout, [NotNullWhen(true)] out Regex? regex)
	{
		if (pattern.Length == 0)
		{
			regex = null;
			return false;
		}

		// The helpers report their matches in a fixed direction, so a right-to-left regex would
		// silently violate their documented contracts: a "next" match could start before the start
		// offset, and match enumeration would run in reverse document order.
		if ((options & RegexOptions.RightToLeft) != 0)
		{
			throw new ArgumentException(
				"These helpers define their own search direction, so RegexOptions.RightToLeft is not supported.",
				nameof(options));
		}

		regex = RegexCache.CreateRegex(pattern, options, matchTimeout);
		return true;
	}

	/// <summary>
	/// Rejects a negative timeout so a caller error is not silently coerced into an unbounded search;
	/// <see cref="Regex.InfiniteMatchTimeout"/> (-1 ms) is the documented unbounded value and is
	/// accepted. A timeout above the maximum accepted by <see cref="Regex"/> is rejected by the
	/// <see cref="Regex"/> constructor.
	/// </summary>
	private static void ValidateMatchTimeout(TimeSpan matchTimeout)
	{
		if (matchTimeout < TimeSpan.Zero && matchTimeout != Regex.InfiniteMatchTimeout)
			throw new ArgumentOutOfRangeException(nameof(matchTimeout), matchTimeout, "The match timeout must not be negative, except for Regex.InfiniteMatchTimeout.");
	}
}

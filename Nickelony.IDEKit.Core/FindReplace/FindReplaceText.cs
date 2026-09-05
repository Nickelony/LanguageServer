using System.Text.RegularExpressions;

namespace Nickelony.IDEKit.Core.FindReplace;

/// <summary>
/// Pure text-level find and replace primitives with no UI or document dependencies.
/// </summary>
public static class FindReplaceText
{
	/// <summary>
	/// Builds a regex pattern from the find text and search options.
	/// </summary>
	/// <param name="findText">The text to find.</param>
	/// <param name="useRegex">Whether the find text is already a regular expression.</param>
	/// <param name="matchWholeWord">Whether to add a regular-expression word boundary before and after the pattern.</param>
	/// <returns>The built pattern, or an empty string when the find text is blank.</returns>
	public static string BuildPattern(string findText, bool useRegex, bool matchWholeWord)
	{
		ArgumentNullException.ThrowIfNull(findText);

		if (string.IsNullOrEmpty(findText))
			return string.Empty;

		string pattern = useRegex ? findText : Regex.Escape(findText);

		if (matchWholeWord)
			pattern = @"\b" + pattern + @"\b";

		return pattern;
	}

	/// <summary>
	/// Builds <see cref="RegexOptions"/> from the case-sensitive flag.
	/// </summary>
	/// <param name="caseSensitive">Whether matching should distinguish letter case.</param>
	/// <returns>The regex options for the requested case sensitivity.</returns>
	public static RegexOptions BuildRegexOptions(bool caseSensitive)
		=> caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;

	/// <summary>
	/// Returns the number of matches of <paramref name="pattern"/> in <paramref name="text"/>.
	/// </summary>
	/// <param name="text">The text to search.</param>
	/// <param name="pattern">The regular expression pattern.</param>
	/// <param name="options">The options used for matching.</param>
	/// <returns>The number of matches.</returns>
	public static int CountMatches(string text, string pattern, RegexOptions options)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(pattern);

		return string.IsNullOrEmpty(pattern) ? 0 : Regex.Matches(text, pattern, options).Count;
	}

	/// <summary>
	/// Returns all matches of <paramref name="pattern"/> in <paramref name="text"/>.
	/// </summary>
	/// <param name="text">The text to search.</param>
	/// <param name="pattern">The regular expression pattern.</param>
	/// <param name="options">The options used for matching.</param>
	/// <returns>The matches in document order.</returns>
	public static MatchCollection FindAllMatches(string text, string pattern, RegexOptions options)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(pattern);

		return Regex.Matches(text, pattern, options);
	}

	/// <summary>
	/// Returns the text before the given <paramref name="selectionStartIndex"/>.
	/// Used to find the previous match relative to the current selection.
	/// </summary>
	/// <param name="documentText">The full document text.</param>
	/// <param name="selectionStartIndex">The zero-based start of the current selection.</param>
	/// <returns>The document prefix before the selection.</returns>
	public static string GetTextBeforeSelection(string documentText, int selectionStartIndex)
	{
		ArgumentNullException.ThrowIfNull(documentText);
		return documentText.Substring(0, selectionStartIndex);
	}

	/// <summary>
	/// Returns the text after the given <paramref name="selectionEndIndex"/>.
	/// Used to find the next match relative to the current selection.
	/// </summary>
	/// <param name="documentText">The full document text.</param>
	/// <param name="selectionEndIndex">The zero-based end of the current selection.</param>
	/// <returns>The document suffix after the selection.</returns>
	public static string GetTextAfterSelection(string documentText, int selectionEndIndex)
	{
		ArgumentNullException.ThrowIfNull(documentText);
		return documentText.Substring(selectionEndIndex);
	}

	/// <summary>
	/// Finds matches in the text section before or after the selection, depending on
	/// <paramref name="order"/>.
	/// </summary>
	/// <param name="order">The direction in which to search.</param>
	/// <param name="documentText">The full document text.</param>
	/// <param name="selectionStart">The zero-based start of the current selection.</param>
	/// <param name="selectionLength">The selection length in UTF-16 code units.</param>
	/// <param name="pattern">The regular expression pattern.</param>
	/// <param name="options">The options used for matching.</param>
	/// <returns>The matches in the selected document section.</returns>
	public static MatchCollection GetMatchesFromSection(
		FindingOrder order,
		string documentText,
		int selectionStart,
		int selectionLength,
		string pattern,
		RegexOptions options)
	{
		ArgumentNullException.ThrowIfNull(documentText);
		ArgumentNullException.ThrowIfNull(pattern);

		return order switch
		{
			FindingOrder.Previous => FindAllMatches(
				GetTextBeforeSelection(documentText, selectionStart), pattern, options),
			FindingOrder.Next => FindAllMatches(
				GetTextAfterSelection(documentText, selectionStart + selectionLength), pattern, options),
			_ => throw new ArgumentOutOfRangeException(nameof(order))
		};
	}

	/// <summary>
	/// Returns the last match in a collection (for upward/previous search).
	/// </summary>
	/// <param name="matches">The matches to inspect.</param>
	/// <returns>The last match, or <see langword="null"/> when the collection is empty.</returns>
	public static Match? GetLastMatch(MatchCollection matches)
	{
		ArgumentNullException.ThrowIfNull(matches);
		return matches.Count > 0 ? matches[matches.Count - 1] : null;
	}

	/// <summary>
	/// Returns the first match in a collection (for downward/next search).
	/// </summary>
	/// <param name="matches">The matches to inspect.</param>
	/// <returns>The first match, or <see langword="null"/> when the collection is empty.</returns>
	public static Match? GetFirstMatch(MatchCollection matches)
	{
		ArgumentNullException.ThrowIfNull(matches);
		return matches.Count > 0 ? matches[0] : null;
	}

	/// <summary>
	/// Computes the document-level offset of a match found in the text-after-selection section.
	/// The <paramref name="cutStringLength"/> is the length of the text before the section.
	/// </summary>
	/// <param name="cutStringLength">The zero-based document offset where the section begins.</param>
	/// <param name="match">The match found in that section.</param>
	/// <returns>The zero-based document offset of the match.</returns>
	public static int GetAbsoluteMatchOffset(int cutStringLength, Match match)
	{
		ArgumentNullException.ThrowIfNull(match);
		return cutStringLength + match.Index;
	}

	/// <summary>
	/// Replaces all matches of <paramref name="pattern"/> in <paramref name="text"/>
	/// with <paramref name="replacement"/>.
	/// </summary>
	/// <param name="text">The text to transform.</param>
	/// <param name="pattern">The regular expression pattern.</param>
	/// <param name="replacement">The replacement text.</param>
	/// <param name="options">The options used for matching.</param>
	/// <returns>The transformed text.</returns>
	public static string ReplaceAll(string text, string pattern, string replacement, RegexOptions options)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(pattern);
		ArgumentNullException.ThrowIfNull(replacement);

		return Regex.Replace(text, pattern, replacement, options);
	}
}

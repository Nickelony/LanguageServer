using Nickelony.IDEKit.Core.Identifiers;

namespace Nickelony.IDEKit.IntelliSense.Completion;

/// <summary>
/// Provides completion-item filtering based on the word being typed at the request point.
/// </summary>
public static class TextCompletionFilter
{
	/// <summary>
	/// Reduces a completion item set to the items matching the word being typed at the request point.
	/// </summary>
	/// <remarks>
	/// Matching is case-insensitive and tolerant: an item is kept when its insertion text contains the
	/// typed word. An empty word (for example on a fresh line or via Ctrl+Space) keeps all items.
	/// </remarks>
	/// <param name="items">The candidate completion items.</param>
	/// <param name="context">The completion request context.</param>
	/// <returns>The filtered completion items.</returns>
	public static IReadOnlyList<TextCompletionItem> FilterByCurrentWord(
		IReadOnlyList<TextCompletionItem> items,
		TextCompletionContext context)
	{
		ArgumentNullException.ThrowIfNull(items);
		ArgumentNullException.ThrowIfNull(context);

		return FilterByWord(items, IdentifierHelper.GetPrefix(context.DocumentText, context.CaretOffset));
	}

	/// <summary>
	/// Reduces a completion item set to the items matching the supplied word.
	/// </summary>
	/// <remarks>
	/// Matching is case-insensitive and tolerant: an item is kept when its insertion text contains the
	/// typed word. An empty word keeps all items.
	/// </remarks>
	/// <param name="items">The candidate completion items.</param>
	/// <param name="word">The word being typed.</param>
	/// <returns>The filtered completion items.</returns>
	public static IReadOnlyList<TextCompletionItem> FilterByWord(IReadOnlyList<TextCompletionItem> items, string word)
	{
		ArgumentNullException.ThrowIfNull(items);
		ArgumentNullException.ThrowIfNull(word);

		if (word.Length == 0)
			return [.. items];

		return [.. items.Where(item => MatchesWord(item, word))];
	}

	private static bool MatchesWord(TextCompletionItem item, string word)
		=> item.InsertText.Contains(word, StringComparison.OrdinalIgnoreCase);
}

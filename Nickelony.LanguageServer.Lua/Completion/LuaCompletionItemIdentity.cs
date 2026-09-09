using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.LanguageServer.Lua;

/// <summary>
/// Captures the completion fields that define whether two parsed Lua completion items should be treated as duplicates.
/// </summary>
/// <param name="Label">The completion label shown to the user.</param>
/// <param name="InsertText">The text inserted when the completion is accepted.</param>
/// <param name="FilterText">The filter text used during completion matching.</param>
/// <param name="Detail">The normalized detail text.</param>
/// <param name="Documentation">The normalized documentation text.</param>
/// <param name="Kind">The resolved shared completion kind.</param>
/// <param name="TextEdit">The parsed completion text edit, or <see langword="null"/> when the item has none.</param>
/// <param name="TagsKey">The canonical key of the item's annotations (empty when it has none).</param>
/// <param name="CommitCharactersKey">The canonical key of the item's commit characters (empty when it has none).</param>
/// <param name="AdditionalTextEditsKey">The canonical key of the item's secondary edits (empty when it has none).</param>
internal readonly record struct LuaCompletionItemIdentity(
	string Label,
	string InsertText,
	string FilterText,
	string Detail,
	string Documentation,
	TextCompletionItemKind Kind,
	TextCompletionTextEdit? TextEdit,
	string TagsKey,
	string CommitCharactersKey,
	string AdditionalTextEditsKey)
{
	/// <summary>
	/// Creates a duplicate-detection identity from a parsed completion item.
	/// </summary>
	/// <param name="item">The parsed completion item.</param>
	/// <returns>The normalized identity.</returns>
	internal static LuaCompletionItemIdentity Create(TextCompletionItem item) => new(
		item.Label,
		item.InsertText,
		item.FilterText,
		item.Detail ?? string.Empty,
		item.Documentation ?? string.Empty,
		item.Kind,
		item.TextEdit,
		CreateTagsKey(item.Tags),
		string.Join("\u001f", item.CommitCharacters),
		CreateAdditionalTextEditsKey(item.AdditionalTextEdits));

	/// <summary>
	/// Builds the canonical key for a tag list; the parser emits a deterministic order, so the key
	/// can preserve list order.
	/// </summary>
	private static string CreateTagsKey(IReadOnlyList<TextCompletionTag> tags)
		=> tags.Count == 0 ? string.Empty : string.Join("\u001f", tags);

	/// <summary>
	/// Builds the canonical key for the secondary edits: every replacement range and text, so two
	/// items that commit different side effects are not treated as duplicates.
	/// </summary>
	private static string CreateAdditionalTextEditsKey(IReadOnlyList<TextCompletionTextEdit> edits)
	{
		if (edits.Count == 0)
			return string.Empty;

		return string.Join("\u001f", edits.Select(static edit =>
			$"{edit.ReplacementRange.Offset}:{edit.ReplacementRange.Length}:{edit.NewText}"));
	}
}

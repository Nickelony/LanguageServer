using Nickelony.IDEKit.Core.Text;
using System.Diagnostics.CodeAnalysis;

namespace Nickelony.LanguageServer.Lua;

/// <summary>
/// Converts protocol text edits from workspace-edit and formatting responses into shared <see cref="TextEdit"/> entries.
/// </summary>
/// <remarks>
/// Edits that cannot be mapped to a document range, or that carry no replacement text, are skipped rather
/// than approximated: a malformed payload must not silently turn into a deletion.
/// </remarks>
internal static class WorkspaceEditConversion
{
	/// <summary>
	/// Appends the convertible edits from <paramref name="edits"/> to <paramref name="textEdits"/>.
	/// </summary>
	/// <param name="edits">The protocol edits to convert, or <see langword="null"/> when unavailable.</param>
	/// <param name="textEdits">The destination list that receives the converted edits.</param>
	internal static void AppendTextEdits(IReadOnlyList<TextEditPayload>? edits, List<TextEdit> textEdits)
	{
		if (edits is null)
			return;

		for (int i = 0; i < edits.Count; i++)
		{
			if (TryParseTextEdit(edits[i], out TextEdit? textEdit))
				textEdits.Add(textEdit);
		}
	}

	/// <summary>
	/// Tries to convert a single protocol edit into a shared text edit.
	/// </summary>
	/// <param name="edit">The protocol edit to convert.</param>
	/// <param name="textEdit">The converted edit when successful.</param>
	/// <returns>
	/// <see langword="true"/> when the edit has a valid range and replacement text; otherwise, <see langword="false"/>.
	/// </returns>
	private static bool TryParseTextEdit(TextEditPayload edit, [NotNullWhen(true)] out TextEdit? textEdit)
	{
		textEdit = null;

		if (!ProtocolRangeConversion.TryGetTextPositionRange(edit.Range, out TextPositionRange range)
			|| edit.NewText is null)
		{
			return false;
		}

		textEdit = new TextEdit(range, edit.NewText);
		return true;
	}

	/// <summary>
	/// Gets the edit bucket for <paramref name="filePath"/>, creating it when absent.
	/// </summary>
	/// <param name="editsByFile">The per-file edit buckets.</param>
	/// <param name="filePath">The normalized target file path.</param>
	/// <returns>The bucket that receives the converted edits for the file.</returns>
	internal static List<TextEdit> GetOrCreateTextEditBucket(Dictionary<string, List<TextEdit>> editsByFile, string filePath)
	{
		if (!editsByFile.TryGetValue(filePath, out List<TextEdit>? textEdits))
		{
			textEdits = [];
			editsByFile[filePath] = textEdits;
		}

		return textEdits;
	}
}

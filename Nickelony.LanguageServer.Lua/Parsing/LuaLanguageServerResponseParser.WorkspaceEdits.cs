namespace Nickelony.LanguageServer.Lua;

internal static partial class LuaLanguageServerResponseParser
{
	/// <summary>
	/// Parses a workspace edit from a LuaLS rename or code-action response.
	/// </summary>
	/// <remarks>
	/// LSP defines <c>changes</c> and <c>documentChanges</c> as alternative representations of the same
	/// edit set, so <c>documentChanges</c> takes precedence; a list that yields no usable edit falls back
	/// to a populated <c>changes</c> map instead of discarding the edit. An unresolvable URI or an
	/// unsupported resource operation fails the whole response closed.
	/// </remarks>
	/// <param name="response">The workspace edit response payload, or <see langword="null"/> when unavailable.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for no logging.</param>
	/// <returns>
	/// The parsed workspace edit, or <see langword="null"/> when no valid edits are present or the response contains an
	/// unsupported resource operation.
	/// </returns>
	internal static TextWorkspaceEdit? ParseWorkspaceEdit(WorkspaceEditResponse? response, ILogger? logger = null)
	{
		if (response is null)
			return null;

		var editsByFile = new Dictionary<string, List<TextEdit>>(LanguageServerPaths.LocalPathComparer);

		// documentChanges takes precedence; it fails closed for resource operations and unresolvable URIs.
		if (response.Value.DocumentChanges is not null
			&& !ParseDocumentChanges(response.Value.DocumentChanges, editsByFile, logger))
		{
			return null;
		}

		// A documentChanges list that produced no usable edit - because it is absent, empty, or every
		// edit was skipped - is treated as absent so it cannot silently discard a populated changes map.
		if (!HasUsableEdit(editsByFile)
			&& (!ParseChangeMap(response.Value.Changes, editsByFile, logger) || !HasUsableEdit(editsByFile)))
		{
			return null;
		}

		var documentEdits = new List<TextDocumentEdit>(editsByFile.Count);

		foreach ((string filePath, List<TextEdit> textEdits) in editsByFile)
		{
			if (textEdits.Count == 0)
				continue;

			documentEdits.Add(new TextDocumentEdit(filePath, textEdits));
		}

		return documentEdits.Count == 0
			? null
			: new TextWorkspaceEdit(documentEdits);
	}

	private static bool HasUsableEdit(Dictionary<string, List<TextEdit>> editsByFile)
	{
		foreach (List<TextEdit> textEdits in editsByFile.Values)
		{
			if (textEdits.Count > 0)
				return true;
		}

		return false;
	}

	private static bool ParseChangeMap(IReadOnlyDictionary<string, IReadOnlyList<TextEditPayload>?>? changes,
		Dictionary<string, List<TextEdit>> editsByFile, ILogger? logger)
	{
		// A null map is an absent representation, not a failure.
		if (changes is null)
			return true;

		foreach ((string uri, IReadOnlyList<TextEditPayload>? edits) in changes)
		{
			if (!LanguageServerPaths.TryGetLocalPath(uri, out string filePath))
			{
				logger?.LogWarning(
					"Ignoring Lua workspace edit because a change-map URI could not be resolved to a local file path (uri: '{Uri}').",
					uri);

				return false; // Fail closed so an unresolvable target cannot produce a partial rename.
			}

			List<TextEdit> textEdits = WorkspaceEditConversion.GetOrCreateTextEditBucket(editsByFile, filePath);
			WorkspaceEditConversion.AppendTextEdits(edits, textEdits);
		}

		return true;
	}

	private static bool ParseDocumentChanges(IReadOnlyList<WorkspaceDocumentChangePayload>? documentChanges,
		Dictionary<string, List<TextEdit>> editsByFile, ILogger? logger)
	{
		if (documentChanges is null)
			return true;

		for (int i = 0; i < documentChanges.Count; i++)
		{
			WorkspaceDocumentChangePayload documentChange = documentChanges[i];

			if (documentChange.IsResourceOperation)
			{
				logger?.LogWarning(
					"Ignoring Lua workspace edit because it contains unsupported resource operation '{Kind}' (uri: '{Uri}', oldUri: '{OldUri}', newUri: '{NewUri}').",
					documentChange.Kind,
					documentChange.Uri ?? string.Empty,
					documentChange.OldUri ?? string.Empty,
					documentChange.NewUri ?? string.Empty);

				return false; // Resource operations cannot be represented by the shared edit model (see TextWorkspaceEdit), so fail closed.
			}

			if (!LanguageServerPaths.TryGetLocalPath(documentChange.TextDocument?.Uri, out string filePath))
			{
				logger?.LogWarning(
					"Ignoring Lua workspace edit because a document-change URI could not be resolved to a local file path (uri: '{Uri}').",
					documentChange.TextDocument?.Uri ?? string.Empty);

				return false; // Fail closed so an unresolvable target cannot produce a partial rename.
			}

			List<TextEdit> textEdits = WorkspaceEditConversion.GetOrCreateTextEditBucket(editsByFile, filePath);
			WorkspaceEditConversion.AppendTextEdits(documentChange.Edits, textEdits);
		}

		return true;
	}
}

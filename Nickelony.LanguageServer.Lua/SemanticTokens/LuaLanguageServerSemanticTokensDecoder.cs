using Nickelony.IDEKit.Core.Editing;

namespace Nickelony.LanguageServer.Lua;

/// <summary>
/// Decodes a raw LuaLS semantic token integer stream from a server response into the typed
/// <see cref="LuaSemanticToken"/> list expected by the editor's colorizer.
/// </summary>
internal static class LuaLanguageServerSemanticTokensDecoder
{
	private static readonly IReadOnlyList<string> s_emptyModifiers = [];

	/// <summary>
	/// Decodes a raw semantic token integer stream into the typed token objects expected by the editor.
	/// </summary>
	/// <param name="data">The raw LSP semantic token integer stream.</param>
	/// <param name="document">The document snapshot associated with the token stream.</param>
	/// <param name="tokenTypes">The semantic token types advertised by the server.</param>
	/// <param name="tokenModifiers">The semantic token modifiers advertised by the server.</param>
	/// <returns>The decoded tokens; malformed tuples and tokens outside the document are ignored or clamped.</returns>
	internal static IReadOnlyList<LuaSemanticToken> Decode(int[] data, DocumentSnapshot? document,
		IReadOnlyList<string>? tokenTypes, IReadOnlyList<string>? tokenModifiers)
	{
		if (data.Length == 0 || document is null || tokenTypes is null || tokenTypes.Count == 0)
			return [];

		TextLineMap lineMap = TextLineMap.Build(document.Content);
		var semanticTokens = new List<LuaSemanticToken>(data.Length / 5);
		Dictionary<int, IReadOnlyList<string>>? modifierCache = null;

		int line = 0;
		int character = 0;

		for (int tupleStart = 0; tupleStart + 4 < data.Length; tupleStart += 5)
		{
			int deltaLine = data[tupleStart];
			int deltaCharacter = data[tupleStart + 1];
			int length = data[tupleStart + 2];
			int tokenTypeIndex = data[tupleStart + 3];
			int modifierMask = data[tupleStart + 4];

			line += deltaLine;
			character = deltaLine == 0 ? character + deltaCharacter : deltaCharacter;

			if (line < 0 || line >= lineMap.LineCount || tokenTypeIndex < 0 || tokenTypeIndex >= tokenTypes.Count)
				continue;

			int lineLength = lineMap.GetLineLength(line);
			int safeCharacter = Math.Max(0, Math.Min(character, lineLength));
			int safeLength = Math.Max(0, Math.Min(length, lineLength - safeCharacter));

			if (safeLength == 0)
				continue;

			semanticTokens.Add(new LuaSemanticToken(
				line,
				safeCharacter,
				safeLength,
				tokenTypes[tokenTypeIndex],
				GetOrAddModifiers(ref modifierCache, modifierMask, tokenModifiers)));
		}

		return semanticTokens;
	}

	private static IReadOnlyList<string> GetOrAddModifiers(
		ref Dictionary<int, IReadOnlyList<string>>? cache,
		int modifierMask,
		IReadOnlyList<string>? tokenModifiers)
	{
		if (modifierMask == 0 || tokenModifiers is null || tokenModifiers.Count == 0)
			return s_emptyModifiers;

		cache ??= [];

		if (cache.TryGetValue(modifierMask, out IReadOnlyList<string>? cached))
			return cached;

		var modifiers = new List<string>();

		for (int bitIndex = 0; bitIndex < tokenModifiers.Count; bitIndex++)
		{
			if ((modifierMask & (1 << bitIndex)) != 0)
				modifiers.Add(tokenModifiers[bitIndex]);
		}

		cache[modifierMask] = modifiers;
		return modifiers;
	}
}

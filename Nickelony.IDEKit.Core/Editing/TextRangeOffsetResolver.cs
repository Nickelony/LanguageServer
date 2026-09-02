namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Resolves stable offset ranges from line-map positions, including fallback anchors for empty or
/// otherwise unusable ranges.
/// </summary>
public static class TextRangeOffsetResolver
{
	/// <summary>
	/// Resolves start and end offsets for a range within a document.
	/// </summary>
	/// <param name="lineMap">The line map for the document.</param>
	/// <param name="startLineIndex">The zero-based start line index.</param>
	/// <param name="startCharacter">The zero-based start character.</param>
	/// <param name="endLineIndex">The zero-based end line index.</param>
	/// <param name="endCharacter">The zero-based end character.</param>
	/// <param name="startOffset">Receives the resolved start offset.</param>
	/// <param name="endOffset">Receives the resolved end offset.</param>
	/// <returns><see langword="true"/> when a usable offset range could be resolved.</returns>
	public static bool TryResolveOffsets(TextLineMap lineMap,
		int startLineIndex, int startCharacter, int endLineIndex, int endCharacter,
		out int startOffset, out int endOffset)
	{
		startOffset = 0;
		endOffset = 0;

		if (lineMap.LineCount == 0)
			return false;

		startOffset = lineMap.GetOffset(startLineIndex, startCharacter);
		endOffset = lineMap.GetOffset(endLineIndex, endCharacter);

		if (endOffset > startOffset)
			return true;

		string lineText = lineMap.GetLineText(startLineIndex);
		int lineStartOffset = lineMap.GetLineStartOffset(startLineIndex);

		if (string.IsNullOrEmpty(lineText))
			return TryGetEmptyLineFallbackOffsets(lineMap, startLineIndex, out startOffset, out endOffset);

		int safeCharacter = Math.Max(0, Math.Min(startCharacter, Math.Max(0, lineText.Length - 1)));

		if (TryGetWordBounds(lineText, safeCharacter, out int wordStart, out int wordEnd))
		{
			startOffset = lineStartOffset + wordStart;
			endOffset = lineStartOffset + wordEnd;
			return endOffset > startOffset;
		}

		int trimmedStart = 0;
		int trimmedEnd = lineText.Length;

		while (trimmedStart < trimmedEnd && char.IsWhiteSpace(lineText[trimmedStart]))
			trimmedStart++;

		while (trimmedEnd > trimmedStart && char.IsWhiteSpace(lineText[trimmedEnd - 1]))
			trimmedEnd--;

		if (trimmedEnd > trimmedStart)
		{
			startOffset = lineStartOffset + trimmedStart;
			endOffset = lineStartOffset + trimmedEnd;

			return true;
		}

		startOffset = lineStartOffset + safeCharacter;
		endOffset = Math.Min(startOffset + 1, lineMap.TextLength);
		return endOffset > startOffset;
	}

	private static bool TryGetEmptyLineFallbackOffsets(TextLineMap lineMap, int lineIndex,
		out int startOffset, out int endOffset)
	{
		startOffset = 0;
		endOffset = 0;

		for (int nextLineIndex = lineIndex + 1; nextLineIndex < lineMap.LineCount; nextLineIndex++)
		{
			if (lineMap.GetLineLength(nextLineIndex) == 0)
				continue;

			startOffset = lineMap.GetLineStartOffset(nextLineIndex);
			endOffset = Math.Min(startOffset + 1, lineMap.TextLength);
			return endOffset > startOffset;
		}

		for (int previousLineIndex = lineIndex - 1; previousLineIndex >= 0; previousLineIndex--)
		{
			int previousLineLength = lineMap.GetLineLength(previousLineIndex);

			if (previousLineLength == 0)
				continue;

			startOffset = lineMap.GetLineStartOffset(previousLineIndex) + previousLineLength - 1;
			endOffset = Math.Min(startOffset + 1, lineMap.TextLength);
			return endOffset > startOffset;
		}

		return false;
	}

	private static bool TryGetWordBounds(string lineText, int index, out int wordStart, out int wordEnd)
	{
		wordStart = 0;
		wordEnd = 0;

		if (string.IsNullOrEmpty(lineText))
			return false;

		int safeIndex = Math.Max(0, Math.Min(index, lineText.Length - 1));

		if (!IsRangeAnchorCharacter(lineText[safeIndex]) && safeIndex > 0 && IsRangeAnchorCharacter(lineText[safeIndex - 1]))
			safeIndex--;

		while (safeIndex < lineText.Length && !IsRangeAnchorCharacter(lineText[safeIndex]))
		{
			safeIndex++;

			if (safeIndex >= lineText.Length)
				return false;
		}

		wordStart = safeIndex;
		wordEnd = safeIndex;

		while (wordStart > 0 && IsRangeAnchorCharacter(lineText[wordStart - 1]))
			wordStart--;

		while (wordEnd < lineText.Length && IsRangeAnchorCharacter(lineText[wordEnd]))
			wordEnd++;

		return wordEnd > wordStart;
	}

	private static bool IsRangeAnchorCharacter(char character)
		=> char.IsLetterOrDigit(character) || character == '_' || character == '.' || character == ':' || character == '\'' || character == '"';
}

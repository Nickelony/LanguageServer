using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Identifiers;

/// <summary>
/// Provides identifier-extraction utilities for code-completion scenarios.
/// </summary>
public static class IdentifierHelper
{
	/// <summary>
	/// Gets the identifier prefix immediately before the specified caret offset.
	/// </summary>
	/// <remarks>
	/// This is the word being typed at the caret and is typically used to filter or
	/// narrow completion candidates before showing IntelliSense suggestions.
	/// </remarks>
	/// <param name="value">The text to inspect.</param>
	/// <param name="caretOffset">The zero-based caret offset.</param>
	/// <returns>The contiguous sequence of letters, digits, or underscores before the caret.</returns>
	/// <example>
	/// <code>
	/// string text = "if CONST_";
	/// string prefix = IdentifierHelper.GetPrefix(text, text.Length); // "CONST_"
	/// </code>
	/// </example>
	public static string GetPrefix(string value, int caretOffset)
	{
		ArgumentNullException.ThrowIfNull(value);
		return GetPrefix(value, caretOffset, IsIdentifierCharacter);
	}

	/// <summary>
	/// Gets the identifier prefix immediately before the specified caret offset, using a custom
	/// rule for which characters belong to an identifier.
	/// </summary>
	/// <remarks>
	/// This is the word being typed at the caret and is typically used to filter or
	/// narrow completion candidates before showing IntelliSense suggestions.
	/// </remarks>
	/// <param name="value">The text to inspect.</param>
	/// <param name="caretOffset">The zero-based caret offset.</param>
	/// <param name="isIdentifierCharacter">Determines whether a character may appear within an identifier.</param>
	/// <returns>The contiguous sequence of characters accepted by <paramref name="isIdentifierCharacter"/> before the caret.</returns>
	/// <example>
	/// JavaScript allows <c>$</c> within identifiers:
	/// <code>
	/// string text = "let $el = 1";
	/// string prefix = IdentifierHelper.GetPrefix(text, 7, static c => char.IsLetterOrDigit(c) || c is '_' or '$'); // "$el"
	/// </code>
	/// </example>
	public static string GetPrefix(string value, int caretOffset, Func<char, bool> isIdentifierCharacter)
	{
		ArgumentNullException.ThrowIfNull(value);
		ArgumentNullException.ThrowIfNull(isIdentifierCharacter);

		if (string.IsNullOrEmpty(value) || caretOffset <= 0 || caretOffset > value.Length)
			return string.Empty;

		int start = caretOffset;

		while (start > 0 && isIdentifierCharacter(value[start - 1]))
			start--;

		return value[start..caretOffset];
	}

	/// <summary>
	/// Attempts to locate an identifier or token span using the specified offset as a probe.
	/// </summary>
	/// <remarks>
	/// The offset is clamped to the document bounds. With
	/// <see cref="IdentifierAffinity.Containing"/> (the default), the span is found by probing the
	/// clamped offset; a preceding character is considered only for a non-token probe and only when
	/// that preceding character is a token start. With <see cref="IdentifierAffinity.BeforeCaret"/>,
	/// the returned span ends exactly at the offset and never includes text after it, matching the
	/// word-being-typed semantics used by completion filtering. Policies whose start-character rule
	/// is narrower than their part-character rule do not resolve a continuation character as a span
	/// start.
	/// </remarks>
	/// <param name="snapshot">The text to inspect.</param>
	/// <param name="offset">The zero-based offset to probe.</param>
	/// <param name="policy">The character rules that define token membership.</param>
	/// <param name="affinity">How the span is located relative to <paramref name="offset"/>.</param>
	/// <returns>
	/// The resolved token span, or <see langword="null"/> when the probe does not identify a token.
	/// </returns>
	public static TextRange? TryGetContainingSpan(
		ITextSnapshot snapshot,
		int offset,
		IdentifierCharacterPolicy policy,
		IdentifierAffinity affinity = IdentifierAffinity.Containing)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(policy);

		if (snapshot.TextLength == 0)
			return null;

		int clampedOffset = Math.Clamp(offset, 0, snapshot.TextLength);

		if (affinity == IdentifierAffinity.BeforeCaret)
		{
			if (clampedOffset == 0)
				return null;

			int probe = clampedOffset - 1;

			if (!policy.IsStartCharacter(snapshot.GetCharAt(probe)))
				return null;

			int start = probe;

			while (start > 0 && policy.IsPartCharacter(snapshot.GetCharAt(start - 1)))
				start--;

			return new TextRange(start, clampedOffset - start);
		}

		int containingProbe = clampedOffset;

		if (containingProbe >= snapshot.TextLength)
			containingProbe = snapshot.TextLength - 1;

		if (containingProbe > 0
			&& !policy.IsPartCharacter(snapshot.GetCharAt(containingProbe))
			&& policy.IsStartCharacter(snapshot.GetCharAt(containingProbe - 1)))
		{
			containingProbe--;
		}

		if (!policy.IsStartCharacter(snapshot.GetCharAt(containingProbe)))
			return null;

		int wordStart = containingProbe;

		while (wordStart > 0 && policy.IsPartCharacter(snapshot.GetCharAt(wordStart - 1)))
			wordStart--;

		int wordEnd = containingProbe + 1;

		while (wordEnd < snapshot.TextLength && policy.IsPartCharacter(snapshot.GetCharAt(wordEnd)))
			wordEnd++;

		return wordEnd > wordStart ? new TextRange(wordStart, wordEnd - wordStart) : null;
	}

	private static bool IsIdentifierCharacter(char character)
		=> char.IsLetterOrDigit(character) || character == '_';
}

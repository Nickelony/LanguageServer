using Nickelony.IDEKit.Core.Text;
using System.Diagnostics.CodeAnalysis;

namespace Nickelony.IDEKit.Core.AutoClosing;

/// <summary>
/// Resolves the auto-closing action for text entered at a caret position: inserting a closing text,
/// skipping an existing closing text, or wrapping a selection. Also resolves the pair that a
/// pair deletion (Backspace) removes.
/// </summary>
/// <remarks>
/// <para>
/// The resolver is the editor-neutral core of auto-closing and is stateless: an editor binding
/// composes it with its own input handling, read-only policy, document edits, and provenance
/// tracking. The pairs from <see cref="TextAutoClosingOptions.Pairs"/> are evaluated in order; the
/// first matching pair wins.
/// </para>
/// <para>
/// The leading-token skip of a multi-character closing text intentionally extends the convention of
/// mainstream desktop editors, which apply it to single-character closings only. A quote-like token
/// preceded by the configured escape character belongs to an escape sequence, so it is neither
/// skipped nor treated as an existing closing text, and pair deletion does not remove an escaped
/// pair either.
/// </para>
/// <para>
/// The unmatched-closer scan (a bracket-like pair is not inserted in front of an existing closing
/// text later on the caret's line) reads raw text: it has no string or comment awareness. The scan
/// applies per pair through <see cref="TextAutoClosingPair.CheckUnmatchedClosingText"/> and is
/// skipped entirely when <see cref="TextAutoClosingOptions.AutoCloseUnconditionally"/> is set.
/// </para>
/// <para>
/// Resolution reads only <see cref="ITextSnapshot.TextLength"/> and
/// <see cref="ITextSnapshot.GetCharAt(int)"/> and finds the caret's line end with a terminator scan,
/// so it never materializes snapshot line metadata; a snapshot implementation with lazy line storage
/// stays lazy on the typing path.
/// </para>
/// <para>
/// The tracking callback is how insertion provenance reaches the resolver: it reports whether the
/// position holds a closing text that tracked auto-closing inserted. A <see langword="null"/>
/// callback means nothing is tracked, so <see cref="TextAutoClosingProvenance.Auto"/> applies to
/// nothing - matching the documented "no skips in Auto mode without tracking" semantics.
/// </para>
/// </remarks>
public static class TextAutoClosingResolver
{
	/// <summary>
	/// Tries to resolve an auto-closing action for <see cref="TextAutoClosingRequest.InputText"/> at the
	/// request's caret offset.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Resolution considers the caret position and the text around it:
	/// </para>
	/// <list type="bullet">
	/// <item>an opening token is not doubled in front of its closing text, and typing a pair's closing
	/// token over existing closing text can skip that text (see
	/// <see cref="TextAutoClosingOptions.OvertypeMode"/>);</item>
	/// <item>an unmatched closing text later on the caret's line suppresses the insert of a bracket-like
	/// pair that keeps <see cref="TextAutoClosingPair.CheckUnmatchedClosingText"/> enabled;</item>
	/// <item>the character after the caret must be the end of the text, whitespace, or an allowed character
	/// (see <see cref="TextAutoClosingOptions.AutoCloseBefore"/> and the kind's preset);</item>
	/// <item>a quote-like token preceded by the configured escape character belongs to an escape sequence,
	/// so it is not skipped;</item>
	/// <item>a quote-like pair can be suppressed after a word character (see
	/// <see cref="TextAutoClosingPair.SuppressAfterWordCharacter"/>), and typing the token directly after
	/// the same token is left to normal text input so quote runs can be built.</item>
	/// </list>
	/// <para>
	/// An opening token is recognized by completion at the caret: the entered text must end the token,
	/// and the token's remaining characters must already end the text before the caret, so a
	/// multi-character opener resolves when its final character is typed (see
	/// <see cref="TextAutoClosingPair"/>). Closing text matches exactly, or by its leading token when it
	/// spans multiple characters (so typing <c>}</c> skips an existing <c>},</c>). Input that matches
	/// neither resolves no action and the method returns <see langword="false"/>. With
	/// <see cref="TextAutoClosingOptions.AutoCloseUnconditionally"/> set, the character-after-the-caret,
	/// unmatched-closer, and word-character gates are skipped, so a matching opening token resolves the
	/// insert unless it is doubled or escaped.
	/// </para>
	/// <para>
	/// With <see cref="TextAutoClosingRequest.IsWrappingSelection"/> set, the collapsed-caret gates are
	/// bypassed: a pair that opts into wrapping resolves an insert action for its opening token even when
	/// the doubling, word-character, or character-after-the-caret gates would decline a collapsed-caret
	/// insert, and a skip of existing closing text is never resolved for a wrapping request.
	/// </para>
	/// </remarks>
	/// <param name="request">The resolution inputs.</param>
	/// <param name="action">
	/// The resolved action when the method returns <see langword="true"/>; otherwise, the
	/// <see langword="default"/> action, whose kind is <see cref="TextAutoClosingActionKind.None"/>.
	/// </param>
	/// <returns><see langword="true"/> when an action applies; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException">
	/// <see cref="TextAutoClosingRequest.Snapshot"/>, <see cref="TextAutoClosingRequest.InputText"/>, or
	/// <see cref="TextAutoClosingRequest.Options"/> is <see langword="null"/>.
	/// </exception>
	public static bool TryResolveAction(in TextAutoClosingRequest request, out TextAutoClosingAction action)
	{
		ArgumentNullException.ThrowIfNull(request.Snapshot);
		ArgumentNullException.ThrowIfNull(request.InputText);
		ArgumentNullException.ThrowIfNull(request.Options);

		action = default;

		if (request.InputText.Length == 0)
			return false;

		// Offsets outside the snapshot are clamped before the pair gates inspect the surrounding text.
		TextAutoClosingRequest context = request with
		{
			CaretOffset = Math.Clamp(request.CaretOffset, 0, request.Snapshot.TextLength)
		};

		IReadOnlyList<TextAutoClosingPair> pairs = context.Options.Pairs;

		for (int index = 0; index < pairs.Count; index++)
		{
			if (TryResolvePairAction(pairs[index], in context, out action))
				return true;
		}

		return false;
	}

	/// <summary>
	/// Tries to resolve the auto-closing pair around <paramref name="caretOffset"/> whose opening token
	/// ends at the caret and whose closing text starts there, for pair deletion.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The pairs from <see cref="TextAutoClosingOptions.Pairs"/> are evaluated in order; the first pair
	/// whose opening token ends at the caret and whose closing text starts there wins. A pair with an
	/// empty opening or closing text is ignored (see <see cref="TextAutoClosingPair"/>).
	/// </para>
	/// <para>
	/// A quote-like pair whose opening token or closing text is directly preceded by an odd number of
	/// escape characters is part of an escape sequence and is not deleted as a pair.
	/// </para>
	/// <para>
	/// Whether an existing closing text qualifies follows <see cref="TextAutoClosingOptions.DeleteMode"/>:
	/// <see cref="TextAutoClosingProvenance.Auto"/> deletes a pair only when
	/// <paramref name="isTrackedClosingText"/> reports its closing text as tracked auto-closing, so a
	/// loaded pair whose closing text was not tracked is left to the editor.
	/// </para>
	/// </remarks>
	/// <param name="snapshot">The snapshot of the text containing the caret.</param>
	/// <param name="caretOffset">
	/// The zero-based caret offset. Offsets outside the snapshot are clamped to its bounds.
	/// </param>
	/// <param name="options">The auto-closing configuration.</param>
	/// <param name="isTrackedClosingText">
	/// The insertion-tracking callback that reports whether tracked auto-closing inserted the closing
	/// text starting at the supplied offset, or <see langword="null"/> when nothing is tracked.
	/// </param>
	/// <param name="pair">
	/// The resolved pair when the method returns <see langword="true"/>; otherwise, <see langword="null"/>.
	/// </param>
	/// <returns><see langword="true"/> when a pair applies; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="snapshot"/> or <paramref name="options"/> is <see langword="null"/>.
	/// </exception>
	public static bool TryResolvePairDeletion(
		ITextSnapshot snapshot,
		int caretOffset,
		TextAutoClosingOptions options,
		Func<int, bool>? isTrackedClosingText,
		[NotNullWhen(true)] out TextAutoClosingPair? pair)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(options);

		pair = null;
		caretOffset = Math.Clamp(caretOffset, 0, snapshot.TextLength);

		IReadOnlyList<TextAutoClosingPair> pairs = options.Pairs;

		for (int index = 0; index < pairs.Count; index++)
		{
			TextAutoClosingPair candidate = pairs[index];
			string openingToken = candidate.Open;
			string closingString = candidate.Close;

			if (string.IsNullOrEmpty(openingToken) || string.IsNullOrEmpty(closingString))
				continue;

			if (!MatchesAt(snapshot, caretOffset - openingToken.Length, openingToken)
				|| !MatchesAt(snapshot, caretOffset, closingString))
			{
				continue;
			}

			// An escaped quote-like token is part of an escape sequence, so it is not a pair member;
			// both the opening token's position and the closing text at the caret are checked.
			if (candidate.Kind == TextAutoClosingPairKind.Quote
				&& (IsEscapedAt(snapshot, caretOffset - openingToken.Length, options.EscapeCharacter)
					|| IsEscapedAt(snapshot, caretOffset, options.EscapeCharacter)))
			{
				continue;
			}

			if (!AppliesToExistingClosingText(options.DeleteMode, caretOffset, isTrackedClosingText))
				continue;

			pair = candidate;
			return true;
		}

		return false;
	}

	/// <summary>
	/// Resolves the action for one pair: inserting the closing text, skipping an existing closing
	/// text, or wrapping a selection.
	/// </summary>
	/// <param name="pair">The pair to evaluate.</param>
	/// <param name="context">The resolution inputs shared by the pair evaluators.</param>
	/// <param name="action">The resolved action when a pair applies; otherwise, the default action.</param>
	/// <returns><see langword="true"/> when the pair produced an action; otherwise, <see langword="false"/>.</returns>
	private static bool TryResolvePairAction(
		TextAutoClosingPair pair,
		in TextAutoClosingRequest context,
		out TextAutoClosingAction action)
	{
		action = default;

		string openingToken = pair.Open;
		string closingString = pair.Close;

		// A pair without an opening or closing text cannot contribute an action.
		if (string.IsNullOrEmpty(openingToken) || string.IsNullOrEmpty(closingString))
			return false;

		if (MatchesOpeningToken(context, openingToken))
			return TryResolveOpeningToken(pair, in context, openingToken, closingString, out action);

		// Typing the closing text, or the leading token of a multi-character closing text, can skip the
		// configured closing text when it is already at the caret. Skip provenance decides whether a
		// manually written closing text is skipped as well. As in the opening-token path, an escaped
		// quote-like token belongs to an escape sequence and is not skipped, and a wrapping request
		// never resolves a skip: the typed token replaces the selection instead.
		if (!context.IsWrappingSelection
			&& IsClosingInput(context.InputText, closingString)
			&& (pair.Kind != TextAutoClosingPairKind.Quote || !IsEscaped(context))
			&& MatchesAt(context.Snapshot, context.CaretOffset, closingString)
			&& AppliesToExistingClosingText(context.Options.OvertypeMode, context.CaretOffset, context.IsTrackedClosingText))
		{
			action = TextAutoClosingAction.CreateSkip(closingString);
			return true;
		}

		return false;
	}

	/// <summary>
	/// Resolves the action for an input that matches the pair's opening token: wrapping a selection,
	/// skipping an existing closing text, or inserting the closing text after the gate checks.
	/// </summary>
	/// <param name="pair">The pair being resolved.</param>
	/// <param name="context">The resolution inputs shared by the pair evaluators.</param>
	/// <param name="openingToken">The pair's opening token.</param>
	/// <param name="closingString">The pair's closing text.</param>
	/// <param name="action">The resolved action when the pair applies; otherwise, the default action.</param>
	/// <returns><see langword="true"/> when the pair produced an action; otherwise, <see langword="false"/>.</returns>
	private static bool TryResolveOpeningToken(
		TextAutoClosingPair pair,
		in TextAutoClosingRequest context,
		string openingToken,
		string closingString,
		out TextAutoClosingAction action)
	{
		action = default;

		if (context.IsWrappingSelection)
		{
			// Typing over a selection replaces it unless the pair opts into wrapping it.
			if (!pair.WrapSelection)
				return false;

			action = TextAutoClosingAction.CreateInsert(closingString);
			return true;
		}

		// A quote-like pair can be skipped as a whole, is not doubled, and can be suppressed after a word
		// character.
		if (pair.Kind == TextAutoClosingPairKind.Quote)
		{
			// A quote preceded by an escape character belongs to an escape sequence, so it is not skipped.
			if (!IsEscaped(context)
				&& MatchesAt(context.Snapshot, context.CaretOffset, closingString)
				&& AppliesToExistingClosingText(context.Options.OvertypeMode, context.CaretOffset, context.IsTrackedClosingText))
			{
				action = TextAutoClosingAction.CreateSkip(closingString);
				return true;
			}

			// Let normal text input add the character when the same token immediately precedes the caret.
			if (MatchesAt(context.Snapshot, context.CaretOffset - openingToken.Length, openingToken))
				return false;

			// Quotes usually do not auto-close after a word character; backticks usually keep this disabled.
			if (!context.Options.AutoCloseUnconditionally
				&& pair.SuppressAfterWordCharacter
				&& IsWordCharacterBeforeCaret(context))
			{
				return false;
			}
		}
		// Typing an opening token in front of its closing text must not duplicate that text; typing '('
		// before ')' leaves a single pair. The scan reads raw text, so a pair can disable it when the
		// host already tokenizes (see TextAutoClosingPair.CheckUnmatchedClosingText).
		else if (pair.CheckUnmatchedClosingText
			&& !context.Options.AutoCloseUnconditionally
			&& HasUnmatchedClosingTextAfter(context, openingToken, closingString))
		{
			return false;
		}

		// Auto-closing applies only when the character after the caret is the end of the text,
		// whitespace, or an allowed character for the pair's kind.
		if (!ShouldAutoCloseBefore(context, pair.Kind))
			return false;

		action = TextAutoClosingAction.CreateInsert(closingString);
		return true;
	}

	/// <summary>
	/// Determines whether the entered text completes the opening token at the caret: the input must
	/// end the token, and the token's remaining characters must match the text ending just before the
	/// caret, so a multi-character opener resolves when its final character is typed.
	/// </summary>
	/// <param name="context">The resolution inputs shared by the pair evaluators.</param>
	/// <param name="openingToken">The pair's opening token.</param>
	/// <returns><see langword="true"/> when the entered text completes the opening token.</returns>
	private static bool MatchesOpeningToken(in TextAutoClosingRequest context, string openingToken)
	{
		string inputText = context.InputText;

		if (inputText.Length == 0 || inputText.Length > openingToken.Length)
			return false;

		if (!openingToken.EndsWith(inputText, StringComparison.Ordinal))
			return false;

		int precedingLength = openingToken.Length - inputText.Length;

		return precedingLength == 0
			|| MatchesAt(context.Snapshot, context.CaretOffset - precedingLength, openingToken[..precedingLength]);
	}

	/// <summary>
	/// Determines whether the input matches the closing text or its leading token.
	/// </summary>
	/// <param name="inputText">The text being entered.</param>
	/// <param name="closingString">The pair's closing text.</param>
	/// <returns><see langword="true"/> when the input matches the closing text or its leading token.</returns>
	private static bool IsClosingInput(string inputText, string closingString)
	{
		if (string.Equals(inputText, closingString, StringComparison.Ordinal))
			return true;

		// A multi-character closing text is also skipped by typing its leading token, matching how
		// single-character closings behave (for example typing '}' skips an auto-comma '},').
		return closingString.Length > 1
			&& inputText.Length == 1
			&& inputText[0] == closingString[0];
	}

	/// <summary>
	/// Determines whether <paramref name="expectedText"/> matches the snapshot text at
	/// <paramref name="offset"/>. An empty text or an offset outside the snapshot matches nothing.
	/// </summary>
	/// <param name="snapshot">The snapshot to inspect.</param>
	/// <param name="offset">The zero-based offset to compare at.</param>
	/// <param name="expectedText">The text to match.</param>
	/// <returns><see langword="true"/> when the text matches at the offset; otherwise, <see langword="false"/>.</returns>
	private static bool MatchesAt(ITextSnapshot snapshot, int offset, string expectedText)
	{
		if (expectedText.Length == 0 || offset < 0 || offset + expectedText.Length > snapshot.TextLength)
			return false;

		for (int index = 0; index < expectedText.Length; index++)
		{
			if (snapshot.GetCharAt(offset + index) != expectedText[index])
				return false;
		}

		return true;
	}

	/// <summary>
	/// Determines whether the token starting at <paramref name="tokenStart"/> is escaped by an odd
	/// run of the configured escape character directly before it.
	/// </summary>
	/// <param name="snapshot">The snapshot to inspect.</param>
	/// <param name="tokenStart">The offset at which the token starts.</param>
	/// <param name="escapeCharacter">The configured escape character, or <see langword="null"/> when escape handling is disabled.</param>
	/// <returns><see langword="true"/> when the token is escaped.</returns>
	private static bool IsEscapedAt(ITextSnapshot snapshot, int tokenStart, char? escapeCharacter)
	{
		if (escapeCharacter is not char escape)
			return false;

		int escapes = 0;
		int position = tokenStart;

		while (position > 0 && snapshot.GetCharAt(position - 1) == escape)
		{
			escapes++;
			position--;
		}

		return (escapes & 1) != 0;
	}

	/// <summary>
	/// Determines whether the token at the caret is escaped, so it belongs to an escape sequence
	/// instead of an auto-closing token.
	/// </summary>
	/// <param name="context">The resolution inputs shared by the pair evaluators.</param>
	/// <returns><see langword="true"/> when the token at the caret is escaped.</returns>
	private static bool IsEscaped(in TextAutoClosingRequest context)
		=> IsEscapedAt(context.Snapshot, context.CaretOffset, context.Options.EscapeCharacter);

	/// <summary>
	/// Determines whether the caret's line contains a closing text that no opening token after the caret
	/// matches, so inserting the closing text would duplicate an existing one. The scan reads raw text:
	/// it has no string or comment awareness, so a closing text inside a string or comment counts as
	/// existing content.
	/// </summary>
	/// <param name="context">The resolution inputs shared by the pair evaluators.</param>
	/// <param name="openingToken">The pair's opening token.</param>
	/// <param name="closingString">The pair's closing text.</param>
	/// <returns><see langword="true"/> when an unmatched closing text follows on the caret's line.</returns>
	private static bool HasUnmatchedClosingTextAfter(
		in TextAutoClosingRequest context,
		string openingToken,
		string closingString)
	{
		int position = context.CaretOffset;
		int lineEndOffset = FindLineEndOffset(context.Snapshot, position);
		int depth = 0;

		while (position < lineEndOffset)
		{
			if (MatchesAt(context.Snapshot, position, openingToken))
			{
				depth++;
				position += openingToken.Length;
			}
			else if (MatchesAt(context.Snapshot, position, closingString))
			{
				if (depth == 0)
					return true;

				depth--;
				position += closingString.Length;
			}
			else
			{
				position++;
			}
		}

		return false;
	}

	/// <summary>
	/// Finds the end of the line containing <paramref name="offset"/>, exclusive of the line terminator,
	/// with a terminator scan that never materializes line metadata.
	/// </summary>
	/// <remarks>
	/// The scan matches document-line semantics: a position on a terminator belongs to the preceding
	/// line, so a position on the LF of a CRLF pair scans the line that ends at the CR.
	/// </remarks>
	/// <param name="snapshot">The snapshot to inspect.</param>
	/// <param name="offset">The zero-based offset inside the snapshot.</param>
	/// <returns>The zero-based offset at which the line's terminator starts, or the text end.</returns>
	private static int FindLineEndOffset(ITextSnapshot snapshot, int offset)
	{
		if (offset > 0 && offset < snapshot.TextLength
			&& LineTerminators.IsCrLfPair(snapshot.GetCharAt(offset - 1), snapshot.GetCharAt(offset)))
		{
			offset--;
		}

		while (offset < snapshot.TextLength && !LineTerminators.IsTerminator(snapshot.GetCharAt(offset)))
			offset++;

		return offset;
	}

	/// <summary>
	/// Determines whether auto-closing applies for the character after the caret: the end of the text,
	/// whitespace, or a character the pair's kind allows before the caret.
	/// </summary>
	/// <param name="context">The resolution inputs shared by the pair evaluators.</param>
	/// <param name="kind">The pair's resolution kind.</param>
	/// <returns><see langword="true"/> when the character after the caret allows auto-closing.</returns>
	private static bool ShouldAutoCloseBefore(in TextAutoClosingRequest context, TextAutoClosingPairKind kind)
	{
		if (context.Options.AutoCloseUnconditionally || context.CaretOffset >= context.Snapshot.TextLength)
			return true;

		char nextCharacter = context.Snapshot.GetCharAt(context.CaretOffset);

		if (char.IsWhiteSpace(nextCharacter))
			return true;

		// A configured set replaces the kind's default set; an empty set therefore admits no character
		// besides whitespace and the end of the text.
		if (context.Options.AutoCloseBefore is { } autoCloseBefore)
			return autoCloseBefore.Contains(nextCharacter);

		return (kind == TextAutoClosingPairKind.Quote
			? TextAutoClosingOptions.DefaultQuoteAutoCloseBefore
			: TextAutoClosingOptions.DefaultBracketAutoCloseBefore).Contains(nextCharacter);
	}

	/// <summary>
	/// Determines whether the character immediately before the caret counts as a word character under the
	/// configured identifier policy.
	/// </summary>
	/// <param name="context">The resolution inputs shared by the pair evaluators.</param>
	/// <returns><see langword="true"/> when the preceding character is a word character.</returns>
	private static bool IsWordCharacterBeforeCaret(in TextAutoClosingRequest context)
		=> context.CaretOffset > 0
			&& context.Options.IdentifierPolicy.IsPartCharacter(context.Snapshot.GetCharAt(context.CaretOffset - 1));

	/// <summary>
	/// Applies a provenance mode to the closing text at the caret.
	/// </summary>
	/// <param name="mode">The provenance mode to apply.</param>
	/// <param name="offset">The offset of the closing text at the caret.</param>
	/// <param name="isTrackedClosingText">The insertion-tracking callback, if any.</param>
	/// <returns><see langword="true"/> when the mode applies to the closing text.</returns>
	private static bool AppliesToExistingClosingText(
		TextAutoClosingProvenance mode,
		int offset,
		Func<int, bool>? isTrackedClosingText)
		=> mode switch
		{
			TextAutoClosingProvenance.Always => true,
			TextAutoClosingProvenance.Never => false,
			_ => isTrackedClosingText is { } isTracked && isTracked(offset)
		};
}

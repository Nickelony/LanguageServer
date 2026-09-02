using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Document;

namespace Nickelony.IDEKit.AvalonEdit.Editing;

/// <summary>
/// Applies bracket, quote, and backtick auto-closing and overtyping over an AvalonEdit text editor.
/// </summary>
/// <remarks>
/// The service is created once per editor as part of the editor service composition and
/// intentionally keeps its members instance, even though it holds no state.
/// </remarks>
[SuppressMessage(
	"Performance",
	"CA1822:MarkMembersAsStatic",
	Justification = "The service is created per editor as part of the editor service composition.")]
public sealed class TextAutoClosingService
{
	/// <summary>
	/// Tries to resolve an auto-closing action for <paramref name="inputText"/> at the caret.
	/// </summary>
	/// <param name="document">The document the caret belongs to.</param>
	/// <param name="caretOffset">The zero-based caret offset.</param>
	/// <param name="inputText">The text the user is about to enter.</param>
	/// <param name="options">The auto-closing configuration to apply.</param>
	/// <param name="action">The resolved action when one applies.</param>
	/// <returns><see langword="true"/> when an auto-closing action applies; otherwise, <see langword="false"/>.</returns>
	public bool TryGetAction(
		TextDocument document,
		int caretOffset,
		string? inputText,
		TextAutoClosingOptions options,
		out TextAutoClosingAction action)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentNullException.ThrowIfNull(options);

		action = default;

		if (string.IsNullOrEmpty(inputText))
			return false;

		if (TryGetBracketAction(
			inputText,
			openingToken: "(",
			closingToken: ")",
			isEnabled: options.AutoClosingParentheses,
			closingString: options.ParenthesesClosingString,
			document,
			caretOffset,
			out action))
		{
			return true;
		}

		if (TryGetBracketAction(
			inputText,
			openingToken: "{",
			closingToken: "}",
			isEnabled: options.AutoClosingBraces,
			closingString: options.BracesClosingString,
			document,
			caretOffset,
			out action))
		{
			return true;
		}

		if (TryGetBracketAction(
			inputText,
			openingToken: "[",
			closingToken: "]",
			isEnabled: options.AutoClosingBrackets,
			closingString: options.BracketsClosingString,
			document,
			caretOffset,
			out action))
		{
			return true;
		}

		if (TryGetQuoteAction(
			inputText,
			token: "\"",
			isEnabled: options.AutoClosingDoubleQuotes,
			closingString: options.DoubleQuotesClosingString,
			document,
			caretOffset,
			suppressAfterWordCharacter: true,
			out action))
		{
			return true;
		}

		if (TryGetQuoteAction(
			inputText,
			token: "'",
			isEnabled: options.AutoClosingSingleQuotes,
			closingString: options.SingleQuotesClosingString,
			document,
			caretOffset,
			suppressAfterWordCharacter: true,
			out action))
		{
			return true;
		}

		return TryGetQuoteAction(
			inputText,
			token: "`",
			isEnabled: options.AutoClosingBackticks,
			closingString: options.BackticksClosingString,
			document,
			caretOffset,
			suppressAfterWordCharacter: false,
			out action);
	}

	/// <summary>
	/// Handles text entering by applying the matching auto-closing action, if any.
	/// </summary>
	/// <remarks>
	/// An insert action updates the editor and leaves the event available for the normal text-input
	/// pipeline. A skip action advances over the existing closing text and marks the event handled.
	/// </remarks>
	/// <param name="editor">The editor receiving the text.</param>
	/// <param name="e">The text-composition event being handled.</param>
	/// <param name="options">The auto-closing configuration to apply.</param>
	/// <param name="onElementSkipped">The callback invoked when an existing closing element is skipped.</param>
	public void HandleTextEntering(
		ICSharpCode.AvalonEdit.TextEditor editor,
		TextCompositionEventArgs e,
		TextAutoClosingOptions options,
		Action<string>? onElementSkipped = null)
	{
		if (!TryGetAction(editor.Document, editor.CaretOffset, e.Text, options, out TextAutoClosingAction action))
			return;

		ApplyAction(editor, e, action, onElementSkipped);
	}

	private static void ApplyAction(
		ICSharpCode.AvalonEdit.TextEditor editor,
		TextCompositionEventArgs e,
		TextAutoClosingAction action,
		Action<string>? onElementSkipped)
	{
		switch (action.Kind)
		{
			case TextAutoClosingActionKind.InsertClosingElement:
				editor.SelectedText += action.Element;
				editor.CaretOffset -= action.Element.Length;
				editor.SelectionStart = editor.CaretOffset;
				editor.SelectionLength = 0;
				break;

			case TextAutoClosingActionKind.SkipExistingClosingElement:
				editor.CaretOffset += action.Element.Length;
				e.Handled = true;
				onElementSkipped?.Invoke(action.Element);
				break;
		}
	}

	/// <summary>
	/// Resolves an action for a bracket pair whose opening and closing tokens differ.
	/// </summary>
	/// <remarks>
	/// The closing token is skipped together with the rest of the closing string when it is
	/// present at the caret, so multi-character closing strings (e.g. <c>},</c>) are consumed as a unit.
	/// </remarks>
	private static bool TryGetBracketAction(
		string inputText,
		string openingToken,
		string closingToken,
		bool isEnabled,
		string closingString,
		TextDocument document,
		int caretOffset,
		out TextAutoClosingAction action)
	{
		action = default;

		if (!isEnabled || string.IsNullOrEmpty(closingString))
			return false;

		if (inputText == openingToken)
		{
			action = TextAutoClosingAction.CreateInsert(closingString);
			return true;
		}

		if ((inputText == closingToken || inputText == closingString)
			&& IsStringAtCaret(document, caretOffset, closingString))
		{
			action = TextAutoClosingAction.CreateSkip(closingString);
			return true;
		}

		return false;
	}

	/// <summary>
	/// Resolves an action for a quote-like token whose opening and closing characters are identical.
	/// </summary>
	/// <remarks>
	/// Mirrors VS Code's auto-closing behavior: the token is overtyped when it is already at the caret,
	/// inserted as a raw character when already inside a run of the same token (which is what allows
	/// building <c>"""</c>), suppressed after word characters for quotes, and only otherwise auto-closed.
	/// </remarks>
	private static bool TryGetQuoteAction(
		string inputText,
		string token,
		bool isEnabled,
		string closingString,
		TextDocument document,
		int caretOffset,
		bool suppressAfterWordCharacter,
		out TextAutoClosingAction action)
	{
		action = default;

		if (!isEnabled || string.IsNullOrEmpty(closingString))
			return false;

		if (inputText != token)
			return false;

		// Overtype: the token at the caret is an existing closing token, so move past it
		// instead of inserting (typing between "" moves the caret to the end).
		if (IsCharAtCaret(document, caretOffset, token))
		{
			action = TextAutoClosingAction.CreateSkip(token);
			return true;
		}

		// Already inside a run of the same token (e.g. at the end of ""): the typed character
		// is inserted as-is, which is what allows building """.
		if (IsCharBeforeCaret(document, caretOffset, token))
			return false;

		// Quotes do not auto-close after a word character because the typed quote is closing a
		// string. Backticks are exempt (see VS Code issue #61070).
		if (suppressAfterWordCharacter && IsWordCharacterBeforeCaret(document, caretOffset))
			return false;

		action = TextAutoClosingAction.CreateInsert(closingString);
		return true;
	}

	private static bool IsCharAtCaret(TextDocument document, int caretOffset, string token)
	{
		return caretOffset < document.TextLength
			&& !string.IsNullOrEmpty(token)
			&& document.GetCharAt(caretOffset) == token[0];
	}

	private static bool IsStringAtCaret(TextDocument document, int caretOffset, string token)
	{
		if (string.IsNullOrEmpty(token) || caretOffset + token.Length > document.TextLength)
			return false;

		for (int i = 0; i < token.Length; i++)
		{
			if (document.GetCharAt(caretOffset + i) != token[i])
				return false;
		}

		return true;
	}

	private static bool IsCharBeforeCaret(TextDocument document, int caretOffset, string token)
	{
		return caretOffset > 0
			&& !string.IsNullOrEmpty(token)
			&& document.GetCharAt(caretOffset - 1) == token[0];
	}

	private static bool IsWordCharacterBeforeCaret(TextDocument document, int caretOffset)
		=> caretOffset > 0 && IsWordCharacter(document.GetCharAt(caretOffset - 1));

	private static bool IsWordCharacter(char c)
		=> char.IsLetterOrDigit(c) || c == '_';
}

/// <summary>
/// Configures bracket, quote, and backtick auto-closing.
/// </summary>
/// <remarks>
/// An empty closing string disables the corresponding action at resolution time. The values are not
/// validated against their opening tokens, so hosts are responsible for supplying compatible pairs.
/// </remarks>
/// <param name="AutoClosingParentheses">Whether parentheses are auto-closed.</param>
/// <param name="AutoClosingBraces">Whether braces are auto-closed.</param>
/// <param name="AutoClosingBrackets">Whether brackets are auto-closed.</param>
/// <param name="AutoClosingDoubleQuotes">Whether double quotes are auto-closed.</param>
/// <param name="AutoClosingSingleQuotes">Whether single quotes are auto-closed.</param>
/// <param name="AutoClosingBackticks">Whether backticks are auto-closed.</param>
/// <param name="ParenthesesClosingString">The closing text inserted after an opening parenthesis.</param>
/// <param name="BracesClosingString">The closing text inserted after an opening brace.</param>
/// <param name="BracketsClosingString">The closing text inserted after an opening bracket.</param>
/// <param name="DoubleQuotesClosingString">The closing text inserted after an opening double quote.</param>
/// <param name="SingleQuotesClosingString">The closing text inserted after an opening single quote.</param>
/// <param name="BackticksClosingString">The closing text inserted after an opening backtick.</param>
public sealed record TextAutoClosingOptions(
	bool AutoClosingParentheses,
	bool AutoClosingBraces,
	bool AutoClosingBrackets,
	bool AutoClosingDoubleQuotes,
	bool AutoClosingSingleQuotes,
	bool AutoClosingBackticks,
	string ParenthesesClosingString,
	string BracesClosingString,
	string BracketsClosingString,
	string DoubleQuotesClosingString,
	string SingleQuotesClosingString,
	string BackticksClosingString);

/// <summary>
/// Describes one auto-closing action to apply for an input token.
/// </summary>
/// <param name="Kind">The kind of auto-closing action.</param>
/// <param name="Element">The closing text to insert or skip.</param>
public readonly record struct TextAutoClosingAction(TextAutoClosingActionKind Kind, string Element)
{
	/// <summary>
	/// Creates an action that inserts the given closing element.
	/// </summary>
	/// <param name="element">The closing element to insert.</param>
	/// <returns>The insert action.</returns>
	public static TextAutoClosingAction CreateInsert(string element)
		=> new(TextAutoClosingActionKind.InsertClosingElement, element);

	/// <summary>
	/// Creates an action that skips the existing closing element at the caret.
	/// </summary>
	/// <param name="element">The closing element to skip.</param>
	/// <returns>The skip action.</returns>
	public static TextAutoClosingAction CreateSkip(string element)
		=> new(TextAutoClosingActionKind.SkipExistingClosingElement, element);
}

/// <summary>
/// Identifies the kind of auto-closing action to apply.
/// </summary>
public enum TextAutoClosingActionKind
{
	/// <summary>
	/// Inserts the configured closing element after the caret.
	/// </summary>
	InsertClosingElement,

	/// <summary>
	/// Skips an existing closing element at the caret.
	/// </summary>
	SkipExistingClosingElement
}

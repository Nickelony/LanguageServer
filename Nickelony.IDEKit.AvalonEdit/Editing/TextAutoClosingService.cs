using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;

namespace Nickelony.IDEKit.AvalonEdit.Editing;

/// <summary>
/// Provides bracket, quote, and backtick auto-closing and overtyping for an AvalonEdit <see cref="TextEditor"/>.
/// </summary>
[SuppressMessage(
	"Performance",
	"CA1822:MarkMembersAsStatic",
	Justification = "The service is created per editor as part of the editor service composition.")]
public sealed class TextAutoClosingService
{
	/// <summary>
	/// Tries to resolve an auto-closing action for <paramref name="inputText"/> at the caret.
	/// </summary>
	/// <param name="document">The document containing the caret.</param>
	/// <param name="caretOffset">The zero-based caret offset.</param>
	/// <param name="inputText">The text being entered.</param>
	/// <param name="options">The auto-closing configuration.</param>
	/// <param name="action">
	/// The resolved action when the method returns <see langword="true"/>;
	/// otherwise, the <see langword="default"/> action.
	/// </param>
	/// <returns><see langword="true"/> when an action applies; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="document"/>, <paramref name="inputText"/>, or <paramref name="options"/> is <see langword="null"/>.
	/// </exception>
	public bool TryGetAction(
		TextDocument document,
		int caretOffset,
		string inputText,
		TextAutoClosingOptions options,
		out TextAutoClosingAction action)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentNullException.ThrowIfNull(inputText);
		ArgumentNullException.ThrowIfNull(options);

		action = default;

		if (inputText.Length == 0)
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
	/// Applies the auto-closing action (if any) for text entering the editor.
	/// </summary>
	/// <remarks>
	/// An insert action updates the editor without handling the event, allowing normal text input to continue.
	/// A skip action moves past existing closing text, marks the event handled, and invokes the callback when supplied.
	/// </remarks>
	/// <param name="editor">The editor receiving the text.</param>
	/// <param name="e">The text-composition event being handled.</param>
	/// <param name="options">The auto-closing configuration.</param>
	/// <param name="onElementSkipped">An optional callback invoked when a closing element is skipped.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="editor"/>, <paramref name="e"/>, or <paramref name="options"/> is <see langword="null"/>.
	/// </exception>
	public void HandleTextEntering(
		TextEditor editor,
		TextCompositionEventArgs e,
		TextAutoClosingOptions options,
		Action<string>? onElementSkipped = null)
	{
		ArgumentNullException.ThrowIfNull(editor);
		ArgumentNullException.ThrowIfNull(e);
		ArgumentNullException.ThrowIfNull(options);

		if (!TryGetAction(editor.Document, editor.CaretOffset, e.Text, options, out TextAutoClosingAction action))
			return;

		ApplyAction(editor, e, action, onElementSkipped);
	}

	private static void ApplyAction(
		TextEditor editor,
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
	/// Resolves an action for a bracket pair with distinct opening and closing tokens.
	/// </summary>
	/// <remarks>
	/// Typing either the closing token or the full closing string skips the configured closing string
	/// when it is already at the caret.
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
	/// Resolves an action for a quote-like token with the same opening and closing character.
	/// </summary>
	/// <remarks>
	/// A token already at the caret is skipped. When the preceding character is the same token, the
	/// typed character is left to normal input so runs such as <c>"""</c> can be built.
	/// Quote auto-closing is suppressed after a letter, digit, or underscore; backticks bypass that check.
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

		// Skip an existing quote-like token at the caret instead of inserting another one.
		if (IsCharAtCaret(document, caretOffset, token))
		{
			action = TextAutoClosingAction.CreateSkip(token);
			return true;
		}

		// Let normal text input add a token when the same token immediately precedes the caret.
		if (IsCharBeforeCaret(document, caretOffset, token))
			return false;

		// Quotes do not auto-close after a letter, digit, or underscore; backticks bypass this check.
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
/// Configures bracket, quote, and backtick auto-closing and overtyping.
/// </summary>
/// <remarks>
/// An empty closing string disables auto-closing and overtyping for its pair.
/// Closing strings are not validated against their opening tokens,
/// so hosts must provide compatible pairs.
/// </remarks>
/// <param name="AutoClosingParentheses">Whether to auto-close and overtype parentheses.</param>
/// <param name="AutoClosingBraces">Whether to auto-close and overtype braces.</param>
/// <param name="AutoClosingBrackets">Whether to auto-close and overtype brackets.</param>
/// <param name="AutoClosingDoubleQuotes">Whether to auto-close and overtype double quotes.</param>
/// <param name="AutoClosingSingleQuotes">Whether to auto-close and overtype single quotes.</param>
/// <param name="AutoClosingBackticks">Whether to auto-close and overtype backticks.</param>
/// <param name="ParenthesesClosingString">The closing text to insert or skip for parentheses.</param>
/// <param name="BracesClosingString">The closing text to insert or skip for braces.</param>
/// <param name="BracketsClosingString">The closing text to insert or skip for brackets.</param>
/// <param name="DoubleQuotesClosingString">The closing text inserted for double quotes.</param>
/// <param name="SingleQuotesClosingString">The closing text inserted for single quotes.</param>
/// <param name="BackticksClosingString">The closing text inserted for backticks.</param>
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
/// Describes an auto-closing action.
/// </summary>
/// <param name="Kind">The kind of auto-closing action.</param>
/// <param name="Element">The text to insert or skip.</param>
public readonly record struct TextAutoClosingAction(TextAutoClosingActionKind Kind, string Element)
{
	/// <summary>
	/// Creates an insert action for the specified closing text.
	/// </summary>
	/// <param name="element">The text to insert.</param>
	/// <returns>The insert action.</returns>
	public static TextAutoClosingAction CreateInsert(string element)
		=> new(TextAutoClosingActionKind.InsertClosingElement, element);

	/// <summary>
	/// Creates a skip action for the specified closing text.
	/// </summary>
	/// <param name="element">The text to skip.</param>
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
	/// Inserts the closing element.
	/// </summary>
	InsertClosingElement,

	/// <summary>
	/// Skips an existing closing element.
	/// </summary>
	SkipExistingClosingElement
}

using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Editing;
using System.Windows.Input;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

[TestClass]
public sealed class TextAutoClosingServiceTests
{
	private static readonly TextAutoClosingOptions s_options = new(
		AutoClosingParentheses: true,
		AutoClosingBraces: true,
		AutoClosingBrackets: true,
		AutoClosingDoubleQuotes: true,
		AutoClosingSingleQuotes: true,
		AutoClosingBackticks: true,
		ParenthesesClosingString: ")",
		BracesClosingString: "}",
		BracketsClosingString: "]",
		DoubleQuotesClosingString: "\"",
		SingleQuotesClosingString: "'",
		BackticksClosingString: "`");

	private readonly TextAutoClosingService _service = new();

	[TestMethod]
	public void TryGetAction_OpeningParenthesis_ReturnsInsertAction()
	{
		var document = new TextDocument("ab");

		bool resolved = _service.TryGetAction(document, 1, "(", s_options, out TextAutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingElement, action.Kind);
		Assert.AreEqual(")", action.Element);
	}

	[TestMethod]
	public void TryGetAction_ClosingParenthesisBeforeExisting_ReturnsSkipAction()
	{
		var document = new TextDocument("a)b");

		bool resolved = _service.TryGetAction(document, 1, ")", s_options, out TextAutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.SkipExistingClosingElement, action.Kind);
		Assert.AreEqual(")", action.Element);
	}

	[TestMethod]
	public void TryGetAction_DisabledToken_ReturnsFalse()
	{
		var document = new TextDocument("ab");
		var options = new TextAutoClosingOptions(
			false, false, false, false, false, false, ")", "}", "]", "\"", "'", "`");

		bool resolved = _service.TryGetAction(document, 1, "(", options, out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryGetAction_DoubleQuoteBeforeExistingQuote_ReturnsSkipAction()
	{
		var document = new TextDocument("a\"b");

		bool resolved = _service.TryGetAction(document, 1, "\"", s_options, out TextAutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.SkipExistingClosingElement, action.Kind);
		Assert.AreEqual("\"", action.Element);
	}

	[TestMethod]
	public void TryGetAction_OpeningDoubleQuote_ReturnsInsertAction()
	{
		var document = new TextDocument("x = ");

		bool resolved = _service.TryGetAction(document, 4, "\"", s_options, out TextAutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingElement, action.Kind);
		Assert.AreEqual("\"", action.Element);
	}

	[TestMethod]
	public void TryGetAction_OpeningBacktick_ReturnsInsertAction()
	{
		var document = new TextDocument("ab");

		bool resolved = _service.TryGetAction(document, 1, "`", s_options, out TextAutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingElement, action.Kind);
		Assert.AreEqual("`", action.Element);
	}

	[TestMethod]
	public void TryGetAction_BacktickBeforeExistingBacktick_ReturnsSkipAction()
	{
		var document = new TextDocument("a`b");

		bool resolved = _service.TryGetAction(document, 1, "`", s_options, out TextAutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.SkipExistingClosingElement, action.Kind);
		Assert.AreEqual("`", action.Element);
	}

	[TestMethod]
	public void TryGetAction_DoubleQuoteAfterWordCharacter_ReturnsFalse()
	{
		var document = new TextDocument("ab");

		bool resolved = _service.TryGetAction(document, 2, "\"", s_options, out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryGetAction_DoubleQuoteAtEndOfPair_ReturnsFalse()
	{
		var document = new TextDocument("\"\"");

		bool resolved = _service.TryGetAction(document, 2, "\"", s_options, out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryGetAction_BacktickAfterWordCharacter_ReturnsInsertAction()
	{
		var document = new TextDocument("ab");

		bool resolved = _service.TryGetAction(document, 2, "`", s_options, out TextAutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingElement, action.Kind);
		Assert.AreEqual("`", action.Element);
	}

	[TestMethod]
	public void TryGetAction_ClosingBraceWithoutExisting_ReturnsFalse()
	{
		var document = new TextDocument("ab");

		bool resolved = _service.TryGetAction(document, 1, "}", s_options, out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryGetAction_TypingClosingStringWithTrailingComma_SkipsWholeClosingString()
	{
		var document = new TextDocument("{},");
		var options = CreateOptions(bracesClosingString: "},");

		bool resolved = _service.TryGetAction(document, 1, "},", options, out TextAutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.SkipExistingClosingElement, action.Kind);
		Assert.AreEqual("},", action.Element);
	}

	[TestMethod]
	public void TryGetAction_TypingClosingTokenWithTrailingComma_SkipsWholeClosingString()
	{
		var document = new TextDocument("{},");
		var options = CreateOptions(bracesClosingString: "},");

		bool resolved = _service.TryGetAction(document, 1, "}", options, out TextAutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.SkipExistingClosingElement, action.Kind);
		Assert.AreEqual("},", action.Element);
	}

	[TestMethod]
	public void TryGetAction_EmptyInput_ReturnsFalse()
	{
		var document = new TextDocument("ab");

		bool resolved = _service.TryGetAction(document, 1, string.Empty, s_options, out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryGetAction_NullDocument_Throws()
	{
		TextDocument document = null!;

		Assert.ThrowsExactly<ArgumentNullException>(
			() => _service.TryGetAction(document, 0, "(", s_options, out _));
	}

	[TestMethod]
	public void TryGetAction_NullOptions_Throws()
	{
		var document = new TextDocument("ab");

		Assert.ThrowsExactly<ArgumentNullException>(
			() => _service.TryGetAction(document, 0, "(", null!, out _));
	}

	[TestMethod]
	public void HandleTextEntering_InsertAction_InsertsClosingElementAndPositionsCaret()
	{
		STATestHelper.RunInSTA(() =>
		{
			var editor = new ICSharpCode.AvalonEdit.TextEditor { Document = new TextDocument("ab") };
			editor.CaretOffset = 1;
			var e = CreateTextCompositionArgs(editor, "(");

			_service.HandleTextEntering(editor, e, s_options);

			Assert.AreEqual("a)b", editor.Text);
			Assert.AreEqual(1, editor.CaretOffset);
		});
	}

	[TestMethod]
	public void HandleTextEntering_SkipAction_AdvancesCaretAndRaisesSkipped()
	{
		STATestHelper.RunInSTA(() =>
		{
			var editor = new ICSharpCode.AvalonEdit.TextEditor { Document = new TextDocument("a)b") };
			editor.CaretOffset = 1;
			var e = CreateTextCompositionArgs(editor, ")");
			string? skippedElement = null;

			_service.HandleTextEntering(editor, e, s_options, element => skippedElement = element);

			Assert.AreEqual(2, editor.CaretOffset);
			Assert.IsTrue(e.Handled);
			Assert.AreEqual(")", skippedElement);
			Assert.AreEqual("a)b", editor.Text);
		});
	}

	[TestMethod]
	public void HandleTextEntering_NoAction_LeavesDocumentUnchanged()
	{
		STATestHelper.RunInSTA(() =>
		{
			var editor = new ICSharpCode.AvalonEdit.TextEditor { Document = new TextDocument("ab") };
			editor.CaretOffset = 1;
			var e = CreateTextCompositionArgs(editor, "x");

			_service.HandleTextEntering(editor, e, s_options);

			Assert.AreEqual("ab", editor.Text);
			Assert.AreEqual(1, editor.CaretOffset);
		});
	}

	[TestMethod]
	public void HandleTextEntering_SkipMultiCharacterClosingString_AdvancesPastWholeClosingString()
	{
		STATestHelper.RunInSTA(() =>
		{
			var editor = new ICSharpCode.AvalonEdit.TextEditor { Document = new TextDocument("{},") };
			editor.CaretOffset = 1;
			var e = CreateTextCompositionArgs(editor, "}");
			var options = CreateOptions(bracesClosingString: "},");

			_service.HandleTextEntering(editor, e, options);

			Assert.AreEqual("{},", editor.Text);
			Assert.AreEqual(3, editor.CaretOffset);
			Assert.IsTrue(e.Handled);
		});
	}

	private static TextCompositionEventArgs CreateTextCompositionArgs(ICSharpCode.AvalonEdit.TextEditor editor, string text)
	{
		var composition = new TextComposition(InputManager.Current, editor, text);
		var args = new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition);
		args.RoutedEvent = TextCompositionManager.TextInputEvent;
		return args;
	}

	private static TextAutoClosingOptions CreateOptions(string bracesClosingString) => new(
		AutoClosingParentheses: true,
		AutoClosingBraces: true,
		AutoClosingBrackets: true,
		AutoClosingDoubleQuotes: true,
		AutoClosingSingleQuotes: true,
		AutoClosingBackticks: true,
		ParenthesesClosingString: ")",
		BracesClosingString: bracesClosingString,
		BracketsClosingString: "]",
		DoubleQuotesClosingString: "\"",
		SingleQuotesClosingString: "'",
		BackticksClosingString: "`");
}

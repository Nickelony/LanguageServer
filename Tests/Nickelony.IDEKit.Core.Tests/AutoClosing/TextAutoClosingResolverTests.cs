namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Verifies the editor-neutral auto-closing resolution over text snapshots: the gate pipeline, the
/// provenance callback, the selection-wrap path, and the pair-deletion query.
/// </summary>
[TestClass]
public sealed class TextAutoClosingResolverTests
{
	private static readonly TextAutoClosingOptions s_options = TextAutoClosingOptions.Default;

	private static readonly TextAutoClosingOptions s_alwaysOptions = TextAutoClosingOptions.Default with
	{
		OvertypeMode = TextAutoClosingProvenance.Always,
		DeleteMode = TextAutoClosingProvenance.Always
	};

	[TestMethod]
	[DataRow("(", ")", DisplayName = "Parenthesis")]
	[DataRow("{", "}", DisplayName = "Brace")]
	[DataRow("[", "]", DisplayName = "Bracket")]
	public void TryResolveAction_OpeningToken_ReturnsInsertAction(string input, string closingText)
	{
		// The caret sits at the end of the text, where the character-after-the-caret gate allows auto-closing.
		bool resolved = Resolve("ab", 2, input, out TextAutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual(closingText, action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_OpeningAngleBracket_WhenOptedIn_ReturnsInsertAction()
	{
		TextAutoClosingOptions options = CreateOptions(TextAutoClosingPair.AngleBrackets);

		bool resolved = Resolve("ab", 2, "<", out TextAutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual(">", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_OpeningAngleBracket_NotInDefaultOptions_ReturnsFalse()
	{
		// The caret sits at the end of the text, so option membership is the only gate left: the test
		// fails if the preset is added to the defaults.
		bool resolved = Resolve("ab", 2, "<", out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_OutOfRangeCaretOffset_IsClampedToDocumentBounds()
	{
		const string text = ")a";
		(int Offset, string Input, bool Resolved, TextAutoClosingActionKind Kind, string? ClosingText)[] cases =
		[
			// Offsets below the document clamp to offset 0, where the closing parenthesis is at the caret.
			(int.MinValue, ")", true, TextAutoClosingActionKind.SkipExistingClosingText, ")"),
			(-1, ")", true, TextAutoClosingActionKind.SkipExistingClosingText, ")"),

			// Offsets beyond the document clamp to its end, where nothing follows the caret.
			(text.Length, ")", false, TextAutoClosingActionKind.None, null),
			(int.MaxValue, ")", false, TextAutoClosingActionKind.None, null),

			// An opening parenthesis inserts the closing string at the end boundary. At the start
			// boundary the closing parenthesis already at the caret suppresses the duplicate pair.
			(int.MinValue, "(", false, TextAutoClosingActionKind.None, null),
			(int.MaxValue, "(", true, TextAutoClosingActionKind.InsertClosingText, ")"),
		];

		foreach ((int offset, string input, bool expectedResolved, TextAutoClosingActionKind expectedKind, string? expectedClosingText) in cases)
		{
			// The mode is Always because the skip rows describe resolution shapes, not provenance.
			bool resolved = Resolve(text, offset, input, out TextAutoClosingAction action, s_alwaysOptions);

			Assert.AreEqual(expectedResolved, resolved, $"Resolution mismatch for '{input}' at offset {offset}.");
			Assert.AreEqual(expectedKind, action.Kind, $"Action kind mismatch for '{input}' at offset {offset}.");
			Assert.AreEqual(expectedClosingText, action.ClosingText, $"Closing text mismatch for '{input}' at offset {offset}.");
		}
	}

	[TestMethod]
	[DataRow("a)b", ")", DisplayName = "ClosingParenthesis")]
	[DataRow("[]", "]", DisplayName = "ClosingBracketAtExistingBracket")]
	[DataRow("{}", "}", DisplayName = "ClosingBraceAtExistingBrace")]
	public void TryResolveAction_ClosingTokenBeforeExisting_WithAlwaysOvertype_ReturnsSkipAction(string documentText, string input)
	{
		bool resolved = Resolve(documentText, 1, input, out TextAutoClosingAction action, s_alwaysOptions);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.SkipExistingClosingText, action.Kind);
		Assert.AreEqual(input, action.ClosingText);
	}

	[TestMethod]
	[DataRow("a)b", ")", DisplayName = "ClosingParenthesis")]
	[DataRow("[]", "]", DisplayName = "ClosingBracketAtExistingBracket")]
	public void TryResolveAction_ClosingTokenBeforeUntrackedExisting_AutoOvertype_ReturnsFalse(string documentText, string input)
	{
		// Nothing is tracked (the callback is null), so the default Auto provenance declines the skip
		// and normal text input applies.
		bool resolved = Resolve(documentText, 1, input, out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_ClosingTokenBeforeExisting_WithNeverOvertype_ReturnsFalse()
	{
		// Never turns the overtype path off entirely, so the closing token is normal text input even
		// when the closing text sits at the caret.
		var options = TextAutoClosingOptions.Default with { OvertypeMode = TextAutoClosingProvenance.Never };

		bool resolved = Resolve("a)b", 1, ")", out _, options);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_TrackedClosingText_WithAutoOvertype_ReturnsSkipAction()
	{
		// The callback reports the closing text at the caret as tracked, so the default Auto mode skips it.
		bool resolved = Resolve(
			"a)b",
			1,
			")",
			out TextAutoClosingAction action,
			isTrackedClosingText: static offset => offset == 1);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.SkipExistingClosingText, action.Kind);
		Assert.AreEqual(")", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_TrackedAtDifferentOffset_WithAutoOvertype_ReturnsFalse()
	{
		// A tracked closing text elsewhere must not enable the skip at the caret.
		bool resolved = Resolve(
			"a)b",
			1,
			")",
			out _,
			isTrackedClosingText: static offset => offset == 0);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_ClosingBraceWithoutExisting_ReturnsFalse()
	{
		bool resolved = Resolve("ab", 1, "}", out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_DoubleQuoteBeforeExistingQuote_WithAlwaysOvertype_ReturnsSkipAction()
	{
		bool resolved = Resolve("a\"b", 1, "\"", out TextAutoClosingAction action, s_alwaysOptions);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.SkipExistingClosingText, action.Kind);
		Assert.AreEqual("\"", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_OpeningDoubleQuote_ReturnsInsertAction()
	{
		bool resolved = Resolve("x = ", 4, "\"", out TextAutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual("\"", action.ClosingText);
	}

	[TestMethod]
	[DataRow("ab", DisplayName = "WordCharacter")]
	[DataRow("12", DisplayName = "Digit")]
	[DataRow("a_", DisplayName = "Underscore")]
	public void TryResolveAction_DoubleQuoteAfterWordCharacterDigitOrUnderscore_ReturnsFalse(string documentText)
	{
		bool resolved = Resolve(documentText, 2, "\"", out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_DoubleQuoteAfterSeparator_ReturnsInsertAction()
	{
		bool resolved = Resolve("a = ", 4, "\"", out TextAutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
	}

	[TestMethod]
	public void TryResolveAction_DoubleQuotePrecededBySameToken_ReturnsFalse()
	{
		// The doubling rule lets normal input build runs such as '"""'.
		bool resolved = Resolve("\"", 1, "\"", out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_DoubleQuoteAtEndOfPair_ReturnsFalse()
	{
		bool resolved = Resolve("\"\"", 2, "\"", out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_OpeningSingleQuote_ReturnsInsertAction()
	{
		bool resolved = Resolve("x = ", 4, "'", out TextAutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual("'", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_SingleQuoteBeforeExistingQuote_WithAlwaysOvertype_ReturnsSkipAction()
	{
		bool resolved = Resolve("a'b", 1, "'", out TextAutoClosingAction action, s_alwaysOptions);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.SkipExistingClosingText, action.Kind);
		Assert.AreEqual("'", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_SingleQuoteAfterWordCharacter_ReturnsFalse()
	{
		bool resolved = Resolve("ab", 2, "'", out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_Backtick_NotInDefaultOptions_ReturnsFalse()
	{
		// The caret sits at the end of the text, so option membership is the only gate left: the test
		// fails if the preset is added to the defaults.
		bool resolved = Resolve("ab", 2, "`", out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_OpeningBacktick_WhenOptedIn_ReturnsInsertAction()
	{
		TextAutoClosingOptions options = CreateOptions(TextAutoClosingPair.Backticks);

		bool resolved = Resolve("ab", 2, "`", out TextAutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual("`", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_BacktickBeforeExistingBacktick_WhenOptedIn_WithAlwaysOvertype_ReturnsSkipAction()
	{
		TextAutoClosingOptions options = CreateOptions(TextAutoClosingPair.Backticks) with
		{
			OvertypeMode = TextAutoClosingProvenance.Always
		};

		bool resolved = Resolve("a`b", 1, "`", out TextAutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.SkipExistingClosingText, action.Kind);
		Assert.AreEqual("`", action.ClosingText);
	}

	[TestMethod]
	[DataRow("},", DisplayName = "WholeClosingString")]
	[DataRow("}", DisplayName = "LeadingTokenOnly")]
	public void TryResolveAction_TypingClosingTextWithTrailingComma_WithAlwaysOvertype_SkipsWholeClosingString(string input)
	{
		TextAutoClosingOptions options = CreateOptions(new TextAutoClosingPair("{", "},")) with
		{
			OvertypeMode = TextAutoClosingProvenance.Always
		};

		bool resolved = Resolve("{},", 1, input, out TextAutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.SkipExistingClosingText, action.Kind);
		Assert.AreEqual("},", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_TypingNonLeadingPartOfClosingString_ReturnsFalse()
	{
		TextAutoClosingOptions options = CreateOptions(new TextAutoClosingPair("{", "},"));

		bool resolved = Resolve("{},", 1, ",", out _, options);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_MultiCharacterClosingString_PartialMatchAtCaret_DoesNotSkip()
	{
		// Only one of the two closing characters follows the caret, so the skip must not resolve.
		TextAutoClosingOptions options = CreateOptions(new TextAutoClosingPair("{", "}}"));

		bool resolved = Resolve("{}", 1, "}", out _, options);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_MultiCharacterQuoteClosingString_WithAlwaysOvertype_SkipsWholeClosingString()
	{
		TextAutoClosingOptions options = CreateOptions(
			new TextAutoClosingPair("\"", "\"\"") { Kind = TextAutoClosingPairKind.Quote }) with
		{
			OvertypeMode = TextAutoClosingProvenance.Always
		};

		bool resolved = Resolve("\"\"rest", 0, "\"", out TextAutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.SkipExistingClosingText, action.Kind);
		Assert.AreEqual("\"\"", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_BeforeCharacterOutsideTheBracketSet_ReturnsFalse()
	{
		// The default bracket set replaced the former "any non-word character" rule, matching the fixed sets
		// mainstream desktop editors use.
		foreach (char nextCharacter in "(-@")
		{
			bool resolved = Resolve(nextCharacter.ToString(), 0, "(", out _);

			Assert.IsFalse(resolved, $"Expected no auto-closing before '{nextCharacter}'.");
		}
	}

	[TestMethod]
	public void TryResolveAction_BracketBeforeQuoteCharacter_ReturnsInsertAction()
	{
		// The default bracket set includes the quote characters.
		bool resolved = Resolve("\"", 0, "(", out TextAutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
	}

	[TestMethod]
	public void TryResolveAction_BracketKindPair_DoesNotSuppressAfterWordCharacter()
	{
		// Suppression after a word character is a quote rule; an explicit Bracket kind ignores the flag.
		TextAutoClosingOptions options = CreateOptions(
			new TextAutoClosingPair("*", "*") { SuppressAfterWordCharacter = true });

		bool resolved = Resolve("ab", 2, "*", out TextAutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
	}

	[TestMethod]
	public void TryResolveAction_QuoteKindPair_SuppressesAfterWordCharacter()
	{
		TextAutoClosingOptions options = CreateOptions(new TextAutoClosingPair("*", "*")
		{
			Kind = TextAutoClosingPairKind.Quote,
			SuppressAfterWordCharacter = true
		});

		bool resolved = Resolve("ab", 2, "*", out _, options);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_DoubleQuoteAfterWordCharacter_WithCustomIdentifierPolicy_ReturnsInsertAction()
	{
		TextAutoClosingOptions options = TextAutoClosingOptions.Default with
		{
			IdentifierPolicy = IdentifierCharacterPolicy.Create(static character => char.IsDigit(character))
		};

		// 'b' is a word character under the default policy but not under the custom one.
		bool resolved = Resolve("ab", 2, "\"", out TextAutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
	}

	[TestMethod]
	public void TryResolveAction_QuotePrecededByCustomEscapeCharacter_IsNotSkipped()
	{
		TextAutoClosingOptions options = TextAutoClosingOptions.Default with
		{
			OvertypeMode = TextAutoClosingProvenance.Always,
			EscapeCharacter = '#'
		};

		// '#' escapes the quote before it, so the existing closing text must not be skipped.
		bool resolved = Resolve("a#\"", 2, "\"", out _, options);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_QuotePrecededByFormerEscapeCharacter_WithEscapeDisabled_IsSkipped()
	{
		TextAutoClosingOptions options = TextAutoClosingOptions.Default with
		{
			OvertypeMode = TextAutoClosingProvenance.Always,
			EscapeCharacter = null
		};

		bool resolved = Resolve("a#\"", 2, "\"", out TextAutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.SkipExistingClosingText, action.Kind);
	}

	[TestMethod]
	public void TryResolveAction_MultiCharacterOpeningToken_MatchesWholeInput()
	{
		TextAutoClosingOptions options = CreateOptions(new TextAutoClosingPair("<<", ">>"));

		bool resolved = Resolve("ab", 2, "<<", out TextAutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual(">>", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_EmptyClosingTextPair_IsIgnored()
	{
		TextAutoClosingOptions options = CreateOptions(new TextAutoClosingPair("(", string.Empty));

		bool resolved = Resolve("ab", 1, "(", out _, options);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_EmptyOpeningTextPair_IsIgnored()
	{
		TextAutoClosingOptions options = CreateOptions(new TextAutoClosingPair(string.Empty, ")"));

		bool resolved = Resolve("ab", 1, "(", out _, options);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_SameOpeningTokenOnTwoPairs_FirstPairWins()
	{
		TextAutoClosingOptions options = CreateOptions(
			new TextAutoClosingPair("(", "first"),
			new TextAutoClosingPair("(", "second"));

		bool resolved = Resolve("ab", 2, "(", out TextAutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual("first", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_ClosingTokenBeforeCaret_StillInsertsClosingElement()
	{
		// An existing closing text is skipped only when it starts at the caret; the same token before
		// the caret is unrelated text, so typing the opening token inserts the closing text.
		TextAutoClosingOptions options = CreateOptions(TextAutoClosingPair.Brackets);

		bool resolved = Resolve("] b", 1, "[", out TextAutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual("]", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_ClosingTokenOfOtherPair_ReturnsFalse()
	{
		// A closing token is only meaningful to its own pair, and a pair that is not part of the
		// options cannot contribute an action, so typing the closing token of another pair does nothing.
		TextAutoClosingOptions options = CreateOptions(TextAutoClosingPair.Brackets);

		bool resolved = Resolve("]b", 1, ")", out TextAutoClosingAction action, options);

		Assert.IsFalse(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.None, action.Kind);
	}

	[TestMethod]
	public void TryResolveAction_EmptyInput_ReturnsFalse()
	{
		bool resolved = Resolve("ab", 1, string.Empty, out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_NoMatch_ReturnsNoneKind()
	{
		bool resolved = Resolve("ab", 1, "x", out TextAutoClosingAction action);

		Assert.IsFalse(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.None, action.Kind);
		Assert.IsNull(action.ClosingText);
	}

	[TestMethod]
	[DataRow("ab", DisplayName = "WordCharacter")]
	[DataRow("a1", DisplayName = "Digit")]
	public void TryResolveAction_OpeningTokenBeforeWordCharacter_ReturnsFalse(string documentText)
	{
		// Auto-closing is suppressed when a letter, digit, or underscore follows the caret.
		bool resolved = Resolve(documentText, 1, "(", out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	[DataRow("a b", 1, DisplayName = "Whitespace")]
	[DataRow("ab", 2, DisplayName = "EndOfText")]
	public void TryResolveAction_OpeningTokenBeforeWhitespaceOrEndOfText_ReturnsInsertAction(string documentText, int caretOffset)
	{
		bool resolved = Resolve(documentText, caretOffset, "(", out TextAutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
	}

	[TestMethod]
	public void TryResolveAction_OpeningTokenBeforePunctuation_ReturnsInsertAction()
	{
		// Punctuation is not a word character, so the default gate allows auto-closing.
		bool resolved = Resolve("x,y", 1, "(", out TextAutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual(")", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_AutoCloseBeforeOption_AllowsListedWordCharacter()
	{
		var options = new TextAutoClosingOptions
		{
			Pairs = [TextAutoClosingPair.Parentheses],
			AutoCloseBefore = "b"
		};

		bool resolved = Resolve("ab", 1, "(", out TextAutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual(")", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_OpeningTokenBeforeExistingClosingText_ReturnsFalse()
	{
		// Typing '(' in front of ')' must not duplicate the closing text.
		bool resolved = Resolve(")", 0, "(", out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_OpeningTokenBeforeUnmatchedClosingText_ReturnsFalse()
	{
		// The closing parenthesis later on the caret's line has no opening token after the caret, so
		// inserting a closing text would duplicate it.
		bool resolved = Resolve("ab)c", 2, "(", out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_OpeningTokenAfterMatchedPairOnTheLine_ReturnsInsertAction()
	{
		// The depth scan must not treat a balanced pair after the caret as a duplicate closing text:
		// the '(' raises the depth and its ')' lowers it back to zero.
		bool resolved = Resolve("f() g()", 3, "(", out TextAutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual(")", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_OpeningTokenAfterMatchedPairWithLaterUnmatchedClosingText_ReturnsFalse()
	{
		// The scan walks a balanced pair first and then finds a closing text whose depth is zero, so the
		// action must be declined even though the character after the caret would pass the gate.
		bool resolved = Resolve("x (a) )", 1, "(", out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	[DataRow("ab\ncd)", DisplayName = "Lf")]
	[DataRow("ab\r\ncd)", DisplayName = "CrLf")]
	public void TryResolveAction_UnmatchedClosingTextOnTheNextLine_ReturnsInsertAction(string text)
	{
		// The unmatched-closing scan stops at the caret's line end, so a closing text on the next line
		// does not suppress the insert.
		bool resolved = Resolve(text, 2, "(", out TextAutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
	}

	[TestMethod]
	public void TryResolveAction_UnmatchedClosingTextOnTheSameLineBeforeCrLf_ReturnsFalse()
	{
		// The scan covers the caret's line up to the terminator, which starts at the CR of a CRLF pair.
		bool resolved = Resolve("ab)\r\ncd", 2, "(", out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_CaretBetweenCrLfPair_ScansThePrecedingLineOnly()
	{
		// A position on the LF of a CRLF pair belongs to the preceding line, whose end is the CR: the
		// closing text on the next line must not suppress the insert.
		bool resolved = Resolve("ab\r\ncd)", 3, "(", out TextAutoClosingAction action);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
	}

	[TestMethod]
	public void TryResolveAction_IsWrappingSelection_WithWrappingPair_ReturnsInsertAction()
	{
		bool resolved = Resolve("abcd", 3, "(", out TextAutoClosingAction action, wrappingSelection: true);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual(")", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_IsWrappingSelection_WithWrapDisabledPair_ReturnsFalse()
	{
		TextAutoClosingOptions options = CreateOptions(new TextAutoClosingPair("(", ")") { WrapSelection = false });

		bool resolved = Resolve("abcd", 3, "(", out _, options, wrappingSelection: true);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_IsWrappingSelection_BypassesWordCharacterSuppression()
	{
		// Wrapping bypasses the collapsed-caret gates: the quote is inserted even after a word character.
		bool resolved = Resolve("ab", 2, "\"", out TextAutoClosingAction action, wrappingSelection: true);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
	}

	[TestMethod]
	public void TryResolvePairDeletion_LoadedPair_WithAlwaysDelete_ReturnsThePair()
	{
		bool resolved = TextAutoClosingResolver.TryResolvePairDeletion(
			new StringTextSnapshot("()"),
			1,
			s_alwaysOptions,
			isTrackedClosingText: null,
			out TextAutoClosingPair? pair);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingPair.Parentheses, pair);
	}

	[TestMethod]
	public void TryResolvePairDeletion_LoadedPair_WithAutoDelete_ReturnsFalse()
	{
		// Nothing is tracked, so the default Auto delete mode leaves the loaded pair to the editor.
		bool resolved = TextAutoClosingResolver.TryResolvePairDeletion(
			new StringTextSnapshot("()"),
			1,
			s_options,
			isTrackedClosingText: null,
			out TextAutoClosingPair? pair);

		Assert.IsFalse(resolved);
		Assert.IsNull(pair);
	}

	[TestMethod]
	public void TryResolvePairDeletion_TrackedClosingText_WithAutoDelete_ReturnsThePair()
	{
		bool resolved = TextAutoClosingResolver.TryResolvePairDeletion(
			new StringTextSnapshot("()"),
			1,
			s_options,
			isTrackedClosingText: static offset => offset == 1,
			out TextAutoClosingPair? pair);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingPair.Parentheses, pair);
	}

	[TestMethod]
	public void TryResolvePairDeletion_MultiCharacterClosingText_ReturnsThePair()
	{
		TextAutoClosingOptions options = CreateOptions(new TextAutoClosingPair("(", "},")) with
		{
			DeleteMode = TextAutoClosingProvenance.Always
		};

		bool resolved = TextAutoClosingResolver.TryResolvePairDeletion(
			new StringTextSnapshot("(},"),
			1,
			options,
			isTrackedClosingText: null,
			out TextAutoClosingPair? pair);

		Assert.IsTrue(resolved);
		Assert.IsNotNull(pair);
		Assert.AreEqual("(", pair.Open);
		Assert.AreEqual("},", pair.Close);
	}

	[TestMethod]
	public void TryResolvePairDeletion_NotBetweenAPair_ReturnsFalse()
	{
		bool resolved = TextAutoClosingResolver.TryResolvePairDeletion(
			new StringTextSnapshot("ab"),
			1,
			s_alwaysOptions,
			isTrackedClosingText: null,
			out _);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_MultiCharacterClosingTextTyped_WhenEscaped_IsNotSkipped()
	{
		TextAutoClosingOptions options = CreateOptions(
			new TextAutoClosingPair("\"", "\"\"") { Kind = TextAutoClosingPairKind.Quote }) with
		{
			OvertypeMode = TextAutoClosingProvenance.Always
		};

		// The caret sits after one escape character (the default backslash), so the quote run at the
		// caret belongs to an escape sequence and must not be skipped.
		bool resolved = Resolve("a\\\"\"", 2, "\"\"", out _, options);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_MultiCharacterClosingTextTyped_WhenEvenlyEscaped_IsSkipped()
	{
		TextAutoClosingOptions options = CreateOptions(
			new TextAutoClosingPair("\"", "\"\"") { Kind = TextAutoClosingPairKind.Quote }) with
		{
			OvertypeMode = TextAutoClosingProvenance.Always
		};

		// Two escape characters before the caret form an escaped backslash, so the quote run is not
		// escaped and the skip applies (escape parity).
		bool resolved = Resolve("a\\\\\"\"", 3, "\"\"", out TextAutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.SkipExistingClosingText, action.Kind);
	}

	[TestMethod]
	public void TryResolveAction_AsymmetricQuoteClosingTyped_WhenEscaped_IsNotSkipped()
	{
		TextAutoClosingOptions options = CreateOptions(
			new TextAutoClosingPair("\u00AB", "\u00BB") { Kind = TextAutoClosingPairKind.Quote }) with
		{
			OvertypeMode = TextAutoClosingProvenance.Always
		};

		// The closing quote at the caret is preceded by the escape character, so it is not skipped.
		bool resolved = Resolve("a\\\u00BB", 2, "\u00BB", out _, options);

		Assert.IsFalse(resolved);
	}

	[TestMethod]
	public void TryResolveAction_QuoteTypedAfterDefaultEscapeCharacter_InsertsInsteadOfSkipping()
	{
		TextAutoClosingOptions options = TextAutoClosingOptions.Default with
		{
			OvertypeMode = TextAutoClosingProvenance.Always
		};

		// One backslash (the default escape character) precedes the caret, so the quote is escaped:
		// it is neither skipped nor treated as existing closing text, and normal insertion applies
		// (the caret sits at the end of the text, where the character-after-the-caret gate admits it).
		bool resolved = Resolve("a\\", 2, "\"", out TextAutoClosingAction action, options);

		Assert.IsTrue(resolved);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, action.Kind);
		Assert.AreEqual("\"", action.ClosingText);
	}

	[TestMethod]
	public void TryResolveAction_EmptyAutoCloseBeforeSet_AdmitsOnlyWhitespaceAndTheEndOfText()
	{
		TextAutoClosingOptions options = TextAutoClosingOptions.Default with { AutoCloseBefore = string.Empty };

		// A configured set replaces the kind's preset, so an empty set admits no character.
		Assert.IsFalse(Resolve("(a", 1, "(", out _, options));

		// Whitespace and the end of the text are always admitted.
		bool resolvedAfterWhitespace = Resolve("( a", 1, "(", out TextAutoClosingAction afterWhitespace, options);
		Assert.IsTrue(resolvedAfterWhitespace);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, afterWhitespace.Kind);

		bool resolvedAtEnd = Resolve("(", 1, "(", out TextAutoClosingAction atEnd, options);
		Assert.IsTrue(resolvedAtEnd);
		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, atEnd.Kind);
	}

	[TestMethod]
	public void TryResolvePairDeletion_LoadedPair_WithNeverDelete_ReturnsFalse()
	{
		TextAutoClosingOptions options = TextAutoClosingOptions.Default with
		{
			DeleteMode = TextAutoClosingProvenance.Never
		};

		// Even with a tracked closing text, the Never delete mode leaves Backspace to the editor.
		bool resolved = TextAutoClosingResolver.TryResolvePairDeletion(
			new StringTextSnapshot("()"),
			1,
			options,
			isTrackedClosingText: static offset => offset == 1,
			out TextAutoClosingPair? pair);

		Assert.IsFalse(resolved);
		Assert.IsNull(pair);
	}

	private static bool Resolve(
		string text,
		int caretOffset,
		string input,
		out TextAutoClosingAction action,
		TextAutoClosingOptions? options = null,
		Func<int, bool>? isTrackedClosingText = null,
		bool wrappingSelection = false)
		=> TextAutoClosingResolver.TryResolveAction(
			new TextAutoClosingRequest(
				new StringTextSnapshot(text),
				caretOffset,
				input,
				options ?? s_options)
			{
				IsWrappingSelection = wrappingSelection,
				IsTrackedClosingText = isTrackedClosingText
			},
			out action);

	private static TextAutoClosingOptions CreateOptions(params TextAutoClosingPair[] pairs)
		=> new() { Pairs = pairs };
}

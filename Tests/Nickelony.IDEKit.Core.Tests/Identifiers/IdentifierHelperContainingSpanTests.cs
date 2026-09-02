using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Identifiers.Tests;

/// <summary>
/// Tests for <see cref="IdentifierHelper.TryGetContainingSpan"/> with default and custom
/// token-character rules.
/// </summary>
[TestClass]
public sealed class IdentifierHelperContainingSpanTests
{
	// ClassicScript token rules: delimiters and line breaks end a token.
	private static readonly IdentifierCharacterPolicy s_classicScriptWordPolicy = IdentifierCharacterPolicy.Create(
		static c => c is not (',' or '=' or ';' or '+' or '-' or '*' or '/' or '(' or ')' or '\r' or '\n'));

	// TRX token rules: whitespace, structural delimiters, and ':' end a token, while
	// double quotes remain part of the token.
	private static readonly IdentifierCharacterPolicy s_trxWordPolicy = IdentifierCharacterPolicy.Create(
		static c => c is not (' ' or ',' or '{' or '}' or '[' or ']' or ':' or '\t' or '\n' or '\r'));

	// ------------------------------------------------------------------
	// Custom delimiter rules (Containing)
	// ------------------------------------------------------------------

	[TestMethod]
	public void ContainingSpan_ClassicScript_CommandKeyAtLineStart()
	{
		var snapshot = new StringTextSnapshot("Legend= 42");

		TextRange? range = IdentifierHelper.TryGetContainingSpan(snapshot, 0, s_classicScriptWordPolicy);

		Assert.AreEqual(new TextRange(0, 6), range);
	}

	[TestMethod]
	public void ContainingSpan_ClassicScript_FirstArgumentAfterEquals()
	{
		var snapshot = new StringTextSnapshot("Legend= 42");

		TextRange? range = IdentifierHelper.TryGetContainingSpan(snapshot, 8, s_classicScriptWordPolicy);

		Assert.IsNotNull(range);
		Assert.AreEqual(new TextRange(7, 3), range);
		Assert.AreEqual(" 42", snapshot.GetText(range.Value.Offset, range.Value.Length));
	}

	[TestMethod]
	public void ContainingSpan_ClassicScript_SecondArgumentAfterComma()
	{
		var snapshot = new StringTextSnapshot("Customize= CUST_BAR, 5");

		TextRange? range = IdentifierHelper.TryGetContainingSpan(snapshot, 22, s_classicScriptWordPolicy);

		Assert.AreEqual(new TextRange(20, 2), range);
	}

	[TestMethod]
	public void ContainingSpan_ClassicScript_MnemonicConstant()
	{
		var snapshot = new StringTextSnapshot("Customize= CUST_BAR, 5");

		TextRange? range = IdentifierHelper.TryGetContainingSpan(snapshot, 12, s_classicScriptWordPolicy);

		Assert.AreEqual(new TextRange(10, 9), range);
	}

	[TestMethod]
	public void ContainingSpan_ClassicScript_HexValueIncludesDollarSign()
	{
		var snapshot = new StringTextSnapshot("Legend= $1A2F");

		TextRange? range = IdentifierHelper.TryGetContainingSpan(snapshot, 8, s_classicScriptWordPolicy);

		Assert.IsNotNull(range);
		Assert.AreEqual(new TextRange(7, 6), range);
		Assert.AreEqual("$1A2F", snapshot.GetText(range.Value.Offset, range.Value.Length).Trim());
	}

	[TestMethod]
	public void ContainingSpan_ClassicScript_SemicolonIsForwardDelimiter()
	{
		var snapshot = new StringTextSnapshot("Legend= \"hello;world\"");

		TextRange? range = IdentifierHelper.TryGetContainingSpan(snapshot, 9, s_classicScriptWordPolicy);

		Assert.IsNotNull(range);
		Assert.AreEqual(new TextRange(7, 7), range);
		Assert.AreEqual("\"hello", snapshot.GetText(range.Value.Offset, range.Value.Length).Trim());
	}

	[TestMethod]
	public void ContainingSpan_ClassicScript_WhitespaceOnlySpansWhitespace()
	{
		var snapshot = new StringTextSnapshot("   ");

		TextRange? range = IdentifierHelper.TryGetContainingSpan(snapshot, 1, s_classicScriptWordPolicy);

		Assert.AreEqual(new TextRange(0, 3), range);
	}

	[TestMethod]
	public void ContainingSpan_ClassicScript_DoesNotCrossLineBoundary()
	{
		var snapshot = new StringTextSnapshot("Line1\nLegend= 42");

		TextRange? range = IdentifierHelper.TryGetContainingSpan(snapshot, 7, s_classicScriptWordPolicy);

		Assert.AreEqual(new TextRange(6, 6), range);
	}

	// ------------------------------------------------------------------
	// Default token rules (Containing)
	// ------------------------------------------------------------------

	[TestMethod]
	public void ContainingSpan_Default_InsideIdentifier()
	{
		var snapshot = new StringTextSnapshot("local targetValue = 1");

		TextRange? range = IdentifierHelper.TryGetContainingSpan(snapshot, 9, IdentifierCharacterPolicy.Default);

		Assert.AreEqual(new TextRange(6, 11), range);
	}

	[TestMethod]
	public void ContainingSpan_Default_StepsBackToWordBeforeBoundary()
	{
		var snapshot = new StringTextSnapshot("player.");

		TextRange? range = IdentifierHelper.TryGetContainingSpan(snapshot, snapshot.TextLength, IdentifierCharacterPolicy.Default);

		Assert.AreEqual(new TextRange(0, 6), range);
	}

	[TestMethod]
	public void ContainingSpan_Default_MiddleBoundaryPrefersWordBeforeDot()
	{
		var snapshot = new StringTextSnapshot("player.a");

		TextRange? range = IdentifierHelper.TryGetContainingSpan(snapshot, 6, IdentifierCharacterPolicy.Default);

		Assert.AreEqual(new TextRange(0, 6), range);
	}

	[TestMethod]
	public void ContainingSpan_Default_CaretAtEndOfIdentifier()
	{
		var snapshot = new StringTextSnapshot("return targetValue");

		TextRange? range = IdentifierHelper.TryGetContainingSpan(snapshot, snapshot.TextLength, IdentifierCharacterPolicy.Default);

		Assert.AreEqual(new TextRange(7, 11), range);
	}

	[TestMethod]
	public void ContainingSpan_Default_OffsetPastEndClampsToLastWord()
	{
		var snapshot = new StringTextSnapshot("return targetValue");

		TextRange? range = IdentifierHelper.TryGetContainingSpan(snapshot, 999, IdentifierCharacterPolicy.Default);

		Assert.AreEqual(new TextRange(7, 11), range);
	}

	[TestMethod]
	public void ContainingSpan_Default_GameFlowSectionName()
	{
		var snapshot = new StringTextSnapshot("LEVEL: Foo");

		TextRange? range = IdentifierHelper.TryGetContainingSpan(snapshot, 2, IdentifierCharacterPolicy.Default);

		Assert.AreEqual(new TextRange(0, 5), range);
	}

	[TestMethod]
	public void ContainingSpan_Default_GameFlowValueName()
	{
		var snapshot = new StringTextSnapshot("LEVEL: Foo");

		TextRange? range = IdentifierHelper.TryGetContainingSpan(snapshot, 8, IdentifierCharacterPolicy.Default);

		Assert.AreEqual(new TextRange(7, 3), range);
	}

	[TestMethod]
	public void ContainingSpan_Default_LeadingWhitespaceReturnsNull()
	{
		var snapshot = new StringTextSnapshot("   foo");

		TextRange? range = IdentifierHelper.TryGetContainingSpan(snapshot, 0, IdentifierCharacterPolicy.Default);

		Assert.IsNull(range);
	}

	[TestMethod]
	public void ContainingSpan_Default_EmptyDocumentReturnsNull()
	{
		var snapshot = new StringTextSnapshot(string.Empty);

		TextRange? range = IdentifierHelper.TryGetContainingSpan(snapshot, 0, IdentifierCharacterPolicy.Default);

		Assert.IsNull(range);
	}

	[TestMethod]
	public void ContainingSpan_Default_SingleWord()
	{
		var snapshot = new StringTextSnapshot("abc");

		TextRange? range = IdentifierHelper.TryGetContainingSpan(snapshot, 1, IdentifierCharacterPolicy.Default);

		Assert.AreEqual(new TextRange(0, 3), range);
	}

	// ------------------------------------------------------------------
	// Custom delimiter rules (BeforeCaret)
	// ------------------------------------------------------------------

	[TestMethod]
	public void ContainingSpan_Trx_WordBeingTypedEndsAtCaret()
	{
		var snapshot = new StringTextSnapshot("alpha beta");

		TextRange? range = IdentifierHelper.TryGetContainingSpan(snapshot, 5, s_trxWordPolicy, IdentifierAffinity.BeforeCaret);

		Assert.AreEqual(new TextRange(0, 5), range);
	}

	[TestMethod]
	public void ContainingSpan_Trx_WordAfterSpaceAtEnd()
	{
		var snapshot = new StringTextSnapshot("alpha beta");

		TextRange? range = IdentifierHelper.TryGetContainingSpan(snapshot, snapshot.TextLength, s_trxWordPolicy, IdentifierAffinity.BeforeCaret);

		Assert.AreEqual(new TextRange(6, 4), range);
	}

	[TestMethod]
	public void ContainingSpan_Trx_AtDocumentStartReturnsNull()
	{
		var snapshot = new StringTextSnapshot("alpha beta");

		TextRange? range = IdentifierHelper.TryGetContainingSpan(snapshot, 0, s_trxWordPolicy, IdentifierAffinity.BeforeCaret);

		Assert.IsNull(range);
	}

	[TestMethod]
	public void ContainingSpan_Trx_FreshQuoteReturnsQuoteOnly()
	{
		var snapshot = new StringTextSnapshot("{\"");

		TextRange? range = IdentifierHelper.TryGetContainingSpan(snapshot, snapshot.TextLength, s_trxWordPolicy, IdentifierAffinity.BeforeCaret);

		Assert.AreEqual(new TextRange(1, 1), range);
	}

	[TestMethod]
	public void ContainingSpan_Trx_QuoteIncludedInsideWord()
	{
		var snapshot = new StringTextSnapshot("{\"abc");

		TextRange? range = IdentifierHelper.TryGetContainingSpan(snapshot, 4, s_trxWordPolicy, IdentifierAffinity.BeforeCaret);

		Assert.AreEqual(new TextRange(1, 3), range);
	}

	[TestMethod]
	public void ContainingSpan_Trx_StringValueIncludingQuote()
	{
		var snapshot = new StringTextSnapshot("{\"key\": \"va");

		TextRange? range = IdentifierHelper.TryGetContainingSpan(snapshot, snapshot.TextLength, s_trxWordPolicy, IdentifierAffinity.BeforeCaret);

		Assert.AreEqual(new TextRange(8, 3), range);
	}
}

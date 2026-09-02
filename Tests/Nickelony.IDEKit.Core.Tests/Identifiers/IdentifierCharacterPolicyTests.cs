namespace Nickelony.IDEKit.Core.Identifiers.Tests;

[TestClass]
public sealed class IdentifierCharacterPolicyTests
{
	[TestMethod]
	public void Default_AcceptsLettersDigitsAndUnderscore()
	{
		Assert.IsTrue(IdentifierCharacterPolicy.Default.IsPartCharacter('a'));
		Assert.IsTrue(IdentifierCharacterPolicy.Default.IsPartCharacter('Z'));
		Assert.IsTrue(IdentifierCharacterPolicy.Default.IsPartCharacter('4'));
		Assert.IsTrue(IdentifierCharacterPolicy.Default.IsPartCharacter('_'));
	}

	[TestMethod]
	public void Default_RejectsPunctuationWhitespaceAndQuotes()
	{
		Assert.IsFalse(IdentifierCharacterPolicy.Default.IsPartCharacter('.'));
		Assert.IsFalse(IdentifierCharacterPolicy.Default.IsPartCharacter(':'));
		Assert.IsFalse(IdentifierCharacterPolicy.Default.IsPartCharacter(' '));
		Assert.IsFalse(IdentifierCharacterPolicy.Default.IsPartCharacter('"'));
		Assert.IsFalse(IdentifierCharacterPolicy.Default.IsPartCharacter('\''));
	}

	[TestMethod]
	public void Default_AcceptsLetterAndRejectsPunctuationAsStart()
	{
		Assert.IsTrue(IdentifierCharacterPolicy.Default.IsStartCharacter('x'));
		Assert.IsFalse(IdentifierCharacterPolicy.Default.IsStartCharacter('.'));
	}

	[TestMethod]
	public void Create_WithCustomPredicate_AppliesPredicate()
	{
		IdentifierCharacterPolicy policy = IdentifierCharacterPolicy.Create(static c => c is 'a' or 'b');

		Assert.IsTrue(policy.IsPartCharacter('a'));
		Assert.IsTrue(policy.IsPartCharacter('b'));
		Assert.IsFalse(policy.IsPartCharacter('c'));
	}

	[TestMethod]
	public void Create_IsStartCharacterDefaultsToPartCharacter()
	{
		IdentifierCharacterPolicy policy = IdentifierCharacterPolicy.Create(static c => c == 'a');

		Assert.IsTrue(policy.IsStartCharacter('a'));
		Assert.IsFalse(policy.IsStartCharacter('b'));
	}

	[TestMethod]
	public void Create_WithSeparateStartPredicate_UsesIt()
	{
		IdentifierCharacterPolicy policy = IdentifierCharacterPolicy.Create(
			static c => char.IsLetterOrDigit(c),
			static c => char.IsLetter(c));

		Assert.IsTrue(policy.IsPartCharacter('4'));
		Assert.IsTrue(policy.IsStartCharacter('x'));
		Assert.IsFalse(policy.IsStartCharacter('4'));
	}

	[TestMethod]
	public void Create_NullPartPredicate_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => IdentifierCharacterPolicy.Create(null!));
	}

	[TestMethod]
	public void WithQuotes_AddsQuoteMembership()
	{
		IdentifierCharacterPolicy policy = IdentifierCharacterPolicy.Default.WithQuotes();

		Assert.IsTrue(policy.IncludeQuotes);
		Assert.IsTrue(policy.IsPartCharacter('"'));
		Assert.IsTrue(policy.IsPartCharacter('\''));
		Assert.IsTrue(policy.IsStartCharacter('"'));
		Assert.IsFalse(policy.IsPartCharacter(':'));
	}

	[TestMethod]
	public void WithPunctuation_AddsPunctuationAndSymbolMembership()
	{
		IdentifierCharacterPolicy policy = IdentifierCharacterPolicy.Default.WithPunctuation();

		Assert.IsTrue(policy.IncludePunctuation);
		Assert.IsTrue(policy.IsPartCharacter(':'));
		Assert.IsTrue(policy.IsPartCharacter('$'));
		Assert.IsTrue(policy.IsPartCharacter('.'));
		Assert.IsFalse(policy.IsPartCharacter(' '));
	}
}

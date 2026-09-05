namespace Nickelony.IDEKit.Core.Identifiers.Tests;

[TestClass]
public sealed class IdentifierHelperTests
{
	[TestMethod]
	public void GetPrefix_ReturnsIdentifierPrefixAtCaret()
	{
		const string text = "if CONST_";

		string prefix = IdentifierHelper.GetPrefix(text, text.Length);

		Assert.AreEqual("CONST_", prefix);
	}

	[TestMethod]
	public void GetPrefix_DefaultStopsAtDollarSign()
	{
		const string text = "let $el";

		string prefix = IdentifierHelper.GetPrefix(text, text.Length);

		Assert.AreEqual("el", prefix);
	}

	[TestMethod]
	public void GetPrefix_CustomPredicateIncludesDollarSign()
	{
		const string text = "let $el";

		string prefix = IdentifierHelper.GetPrefix(text, text.Length, static c => char.IsLetterOrDigit(c) || c is '_' or '$');

		Assert.AreEqual("$el", prefix);
	}

	[TestMethod]
	public void GetPrefix_CustomPredicateIncludesPrime()
	{
		const string text = "map'";

		string prefix = IdentifierHelper.GetPrefix(text, text.Length, static c => char.IsLetterOrDigit(c) || c is '_' or '\'');

		Assert.AreEqual("map'", prefix);
	}

	[TestMethod]
	public void GetPrefix_PastEndOrAtStart_ReturnsEmpty()
	{
		Assert.AreEqual(string.Empty, IdentifierHelper.GetPrefix("Beta", 5));
		Assert.AreEqual(string.Empty, IdentifierHelper.GetPrefix("Beta", 0));
	}

	[TestMethod]
	public void GetPrefix_EmptyValue_ReturnsEmpty()
	{
		Assert.AreEqual(string.Empty, IdentifierHelper.GetPrefix(string.Empty, 0));
	}
}

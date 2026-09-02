namespace Nickelony.IDEKit.Core.Infrastructure.Tests;

[TestClass]
public sealed class TextEditorRequestContractTests
{
	[TestMethod]
	public void TextEditorRequestIdentity_DefaultValuesAreNullOrZero()
	{
		var identity = default(TextEditorRequestIdentity);

		Assert.IsNull(identity.LogicalDocumentId);
		Assert.AreEqual(0, identity.DocumentVersion);
		Assert.AreEqual(0, identity.SessionGeneration);
	}

	[TestMethod]
	public void TextEditorRequestIdentity_IsValueEqual()
	{
		var first = new TextEditorRequestIdentity("doc", 3, 7);
		var second = new TextEditorRequestIdentity("doc", 3, 7);

		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}

	[TestMethod]
	public void TextEditorRequestOutcome_HasExpectedNumericValues()
	{
		// Keep the numeric values explicit so accidental changes are detected.
#pragma warning disable MSTEST0032
		Assert.AreEqual(0, (int)TextEditorRequestOutcome.Completed);
		Assert.AreEqual(1, (int)TextEditorRequestOutcome.Cancelled);
		Assert.AreEqual(2, (int)TextEditorRequestOutcome.Superseded);
		Assert.AreEqual(3, (int)TextEditorRequestOutcome.Stale);
		Assert.AreEqual(4, (int)TextEditorRequestOutcome.Failed);
#pragma warning restore MSTEST0032
	}
}

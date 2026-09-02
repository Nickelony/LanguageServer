namespace Nickelony.IDEKit.Core.Text.Tests;

[TestClass]
public sealed class MarkupTextNormalizerTests
{
	[TestMethod]
	public void NormalizeForPlainText_PreservesInlineBackticks()
	{
		string? normalized = MarkupTextNormalizer.NormalizeForPlainText("Call `value` before `other`.");

		Assert.AreEqual("Call `value` before `other`.", normalized);
	}

	[TestMethod]
	public void NormalizeForPlainText_StripsFenceLinesButPreservesCodeContent()
	{
		string? normalized = MarkupTextNormalizer.NormalizeForPlainText(
			"Summary\n```lua\nlocal value = 1\n```\nTail");

		Assert.AreEqual(
			$"Summary{Environment.NewLine}local value = 1{Environment.NewLine}Tail",
			normalized);
	}

	[TestMethod]
	public void NormalizeForPlainText_BlankInputReturnsNull()
	{
		Assert.IsNull(MarkupTextNormalizer.NormalizeForPlainText(null));
		Assert.IsNull(MarkupTextNormalizer.NormalizeForPlainText("   "));
	}

	[TestMethod]
	public void NormalizeForPlainText_OnlyFenceLinesReturnsNull()
	{
		string? normalized = MarkupTextNormalizer.NormalizeForPlainText("```\n```");

		Assert.IsNull(normalized);
	}
}

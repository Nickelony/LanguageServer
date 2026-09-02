namespace Nickelony.IDEKit.Core.Editing.Tests;

[TestClass]
public sealed class TextIncrementalEditCalculatorTests
{
	[TestMethod]
	public void Compute_CollapsesUnchangedPrefixAndSuffixIntoMinimalRangeEdit()
	{
		const string oldText = "local foo = 1\nlocal bar = 2\n";
		const string newText = "local foo = 1\nlocal baz = 2\n";

		TextIncrementalChange change = TextIncrementalEditCalculator.Compute(oldText, newText);

		Assert.AreEqual(22, change.Range.Offset);
		Assert.AreEqual(1, change.Range.Length);
		Assert.AreEqual("z", change.NewText);
	}

	[TestMethod]
	public void Compute_HandlesPureInsertionAtEndOfFile()
	{
		const string oldText = "local foo = 1\n";
		const string newText = "local foo = 1\nlocal bar = 2\n";

		TextIncrementalChange change = TextIncrementalEditCalculator.Compute(oldText, newText);

		Assert.AreEqual(oldText.Length, change.Range.Offset);
		Assert.AreEqual(0, change.Range.Length);
		Assert.AreEqual("local bar = 2\n", change.NewText);
	}

	[TestMethod]
	public void Compute_NoChange_ProducesEmptyRange()
	{
		const string text = "print('hi')\n";

		TextIncrementalChange change = TextIncrementalEditCalculator.Compute(text, text);

		Assert.AreEqual(0, change.Range.Length);
		Assert.AreEqual(text.Length, change.Range.Offset);
		Assert.AreEqual(string.Empty, change.NewText);
	}
}

using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.IDEKit.IntelliSense.Tests.Completion;

/// <summary>
/// Tests for the <see cref="TextCompletionContext"/> validation rules.
/// </summary>
[TestClass]
public sealed class TextCompletionContextTests
{
	[TestMethod]
	public void Default_Context_IsNull()
	{
		TextCompletionContext? context = default;

		Assert.IsNull(context);
	}

	[TestMethod]
	public void Constructor_NegativeCaretOffset_Throws()
		=> Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextCompletionContext("text", -1));

	[TestMethod]
	public void Constructor_CaretOffsetBeyondTextLength_Throws()
		=> Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextCompletionContext("text", 5));

	[TestMethod]
	public void Constructor_ArgumentIndexLessThanMinusOne_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => new TextCompletionContext("text", 0, TextCompletionTrigger.Contextual, -2));
	}

	[TestMethod]
	public void Constructor_MinusOneArgumentIndex_IsValidSentinel()
	{
		var context = new TextCompletionContext("text", 0, TextCompletionTrigger.Contextual, -1);

		Assert.AreEqual(-1, context.ArgumentIndex);
	}

	[TestMethod]
	public void Constructor_ZeroArgumentIndex_IsFirstArgument()
	{
		var context = new TextCompletionContext("text", 0, TextCompletionTrigger.Contextual, 0);

		Assert.AreEqual(0, context.ArgumentIndex);
	}

	[TestMethod]
	public void Constructor_DefaultArguments_UseExpectedValues()
	{
		var context = new TextCompletionContext("text", 2);

		Assert.AreEqual("text", context.DocumentText);
		Assert.AreEqual(2, context.CaretOffset);
		Assert.AreEqual(TextCompletionTrigger.Automatic, context.Trigger);
		Assert.AreEqual(-1, context.ArgumentIndex);
	}
}

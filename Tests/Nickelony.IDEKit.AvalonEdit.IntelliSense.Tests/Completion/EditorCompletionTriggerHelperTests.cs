using Nickelony.IDEKit.AvalonEdit.IntelliSense.Completion;

namespace Nickelony.IDEKit.AvalonEdit.IntelliSense.Tests;

[TestClass]
public class EditorCompletionTriggerHelperTests
{
	[TestMethod]
	public void IsCtrlSpaceInput_ReturnsTrueOnlyForCtrlSpace()
	{
		Assert.IsTrue(EditorCompletionTriggerHelper.IsCtrlSpaceInput(" ", true));
		Assert.IsTrue(EditorCompletionTriggerHelper.IsCtrlSpaceInput(" ", true));
		Assert.IsFalse(EditorCompletionTriggerHelper.IsCtrlSpaceInput("a", true));
		Assert.IsFalse(EditorCompletionTriggerHelper.IsCtrlSpaceInput(" ", false));
	}

	[TestMethod]
	public void IsSingleCharacterLine_HandlesGeneralAndPredicateChecks()
	{
		Assert.IsTrue(EditorCompletionTriggerHelper.IsSingleCharacterLine("a"));
		Assert.IsFalse(EditorCompletionTriggerHelper.IsSingleCharacterLine(string.Empty));
		Assert.IsFalse(EditorCompletionTriggerHelper.IsSingleCharacterLine("ab"));
		Assert.IsFalse(EditorCompletionTriggerHelper.IsSingleCharacterLine(null));

		Assert.IsTrue(EditorCompletionTriggerHelper.IsSingleCharacterLine("a", char.IsLetter));
		Assert.IsFalse(EditorCompletionTriggerHelper.IsSingleCharacterLine("1", char.IsLetter));
	}
}

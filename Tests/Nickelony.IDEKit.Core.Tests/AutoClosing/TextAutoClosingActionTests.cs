namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Verifies the <see cref="TextAutoClosingAction"/> factories.
/// </summary>
[TestClass]
public sealed class TextAutoClosingActionTests
{
	[TestMethod]
	public void ActionFactories_NullClosingText_ThrowArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => TextAutoClosingAction.CreateInsert(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => TextAutoClosingAction.CreateSkip(null!));
	}
}

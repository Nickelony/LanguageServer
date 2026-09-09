using Nickelony.IDEKit.IntelliSense.CodeActions;

namespace Nickelony.IDEKit.IntelliSense.Tests.CodeActions;

/// <summary>
/// Pins the documented defaults and validation of the neutral code-action controller options.
/// </summary>
[TestClass]
public sealed class TextCodeActionControllerOptionsTests
{
	[TestMethod]
	public void Default_PinsTheTwoHundredFiftyMillisecondRequestDebounce()
		=> Assert.AreEqual(TimeSpan.FromMilliseconds(250.0), TextCodeActionControllerOptions.Default.RequestDebounceDelay);

	[TestMethod]
	public void RequestDebounceDelay_Negative_Throws()
		=> Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => TextCodeActionControllerOptions.Default with { RequestDebounceDelay = TimeSpan.FromMilliseconds(-1.0) });

	[TestMethod]
	public void RequestDebounceDelay_Zero_IsAccepted()
	{
		TextCodeActionControllerOptions options = TextCodeActionControllerOptions.Default with
		{
			RequestDebounceDelay = TimeSpan.Zero
		};

		Assert.AreEqual(TimeSpan.Zero, options.RequestDebounceDelay);
	}
}

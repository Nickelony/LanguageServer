using Nickelony.IDEKit.IntelliSense.Signatures;

namespace Nickelony.IDEKit.IntelliSense.Tests.Signatures;

/// <summary>
/// Pins the documented defaults and validation of the signature help controller options.
/// </summary>
[TestClass]
public sealed class TextSignatureHelpControllerOptionsTests
{
	[TestMethod]
	public void Default_PinsTheFiftyMillisecondRefreshDebounce()
	{
		// The documented default is pinned so the README, the XML docs, and the option value cannot drift apart.
		Assert.AreEqual(TimeSpan.FromMilliseconds(50.0), TextSignatureHelpControllerOptions.Default.RefreshDebounceDelay);
	}

	[TestMethod]
	public void RefreshDebounceDelay_Negative_Throws()
		=> Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => TextSignatureHelpControllerOptions.Default with { RefreshDebounceDelay = TimeSpan.FromMilliseconds(-1.0) });

	[TestMethod]
	public void Default_PinsCyclicOverloadNavigation()
		=> Assert.IsTrue(TextSignatureHelpControllerOptions.Default.Cycle);
}

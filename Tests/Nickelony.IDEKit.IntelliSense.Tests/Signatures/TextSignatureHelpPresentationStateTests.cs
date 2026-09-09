using Nickelony.IDEKit.IntelliSense.Signatures;

namespace Nickelony.IDEKit.IntelliSense.Tests.Signatures;

/// <summary>
/// Verifies the derived conveniences of the signature help presentation-state record.
/// </summary>
[TestClass]
public sealed class TextSignatureHelpPresentationStateTests
{
	[TestMethod]
	public void Empty_ReportsNothingVisibleOrPending()
	{
		TextSignatureHelpPresentationState state = TextSignatureHelpPresentationState.Empty;

		Assert.IsNull(state.SignatureHelp);
		Assert.IsFalse(state.IsVisible);
		Assert.IsFalse(state.IsRequestInFlight);
		Assert.IsFalse(state.IsRefreshPending);
		Assert.IsFalse(state.IsPresentationVisibleOrRequestPending);
	}

	[TestMethod]
	[DataRow(true, false, false)]
	[DataRow(false, true, false)]
	[DataRow(false, false, true)]
	public void IsPresentationVisibleOrRequestPending_CoversVisibilityRequestAndRefresh(
		bool isVisible,
		bool isRequestInFlight,
		bool isRefreshPending)
	{
		var state = new TextSignatureHelpPresentationState(null, isVisible, isRequestInFlight, isRefreshPending);

		// Unlike the completion record, the signature help union includes an in-flight request and a
		// pending refresh.
		Assert.IsTrue(state.IsPresentationVisibleOrRequestPending);
	}
}

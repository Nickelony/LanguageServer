using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.IDEKit.IntelliSense.Tests.Completion;

/// <summary>
/// Verifies the derived conveniences of the completion presentation-state record.
/// </summary>
[TestClass]
public sealed class TextCompletionPresentationStateTests
{
	[TestMethod]
	public void Empty_ReportsNothingVisibleOrScheduled()
	{
		TextCompletionPresentationState state = TextCompletionPresentationState.Empty;

		Assert.IsFalse(state.IsListVisible);
		Assert.IsFalse(state.IsRequestScheduled);
		Assert.IsFalse(state.IsDetailVisible);
		Assert.IsNull(state.DetailContent);
		Assert.IsFalse(state.IsPresentationVisibleOrRequestScheduled);
	}

	[TestMethod]
	[DataRow(true, false, false)]
	[DataRow(false, true, false)]
	[DataRow(false, false, true)]
	public void IsPresentationVisibleOrRequestScheduled_CoversListDetailAndScheduledRequest(
		bool isListVisible,
		bool isRequestScheduled,
		bool isDetailVisible)
	{
		var state = new TextCompletionPresentationState(isListVisible, isRequestScheduled, isDetailVisible, null);

		// Each of the three presentation triggers alone reports the presentation visible or a request scheduled.
		Assert.IsTrue(state.IsPresentationVisibleOrRequestScheduled);
	}
}

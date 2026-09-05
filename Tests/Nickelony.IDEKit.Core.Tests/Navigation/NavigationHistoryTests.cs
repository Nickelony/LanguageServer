namespace Nickelony.IDEKit.Core.Navigation.Tests;

[TestClass]
public sealed class NavigationHistoryTests
{
	private const int MinimumCaretMoveDistance = 32;
	private const int MinimumSelectionMoveDistance = 8;

	private static NavigationHistory<NavigationLocation> CreateHistory()
		=> new(static (previous, current) => previous.IsEquivalentTo(current), IsMeaningfulChange);

	private static bool IsMeaningfulChange(NavigationLocation previous, NavigationLocation current)
	{
		if (!string.Equals(previous.FilePath, current.FilePath, StringComparison.OrdinalIgnoreCase))
			return true;

		if (previous.SelectionLength != current.SelectionLength)
			return true;

		if (previous.SelectionLength > 0 || current.SelectionLength > 0)
			return Math.Abs(previous.SelectionStart - current.SelectionStart) >= MinimumSelectionMoveDistance;

		return Math.Abs(previous.CaretOffset - current.CaretOffset) >= MinimumCaretMoveDistance;
	}

	private static NavigationLocation Location(string filePath, int caretOffset)
		=> new(filePath, caretOffset, caretOffset, 0, null);

	[TestMethod]
	public void Observe_FirstLocation_CreatesNoNavigationEntries()
	{
		var history = CreateHistory();

		history.Observe(Location("a.lua", 0));

		Assert.IsFalse(history.CanNavigateBack);
		Assert.IsFalse(history.CanNavigateForward);
	}

	[TestMethod]
	public void Observe_SmallCaretMove_DoesNotRecord()
	{
		var history = CreateHistory();

		history.Observe(Location("a.lua", 0));
		history.Observe(Location("a.lua", 10));

		Assert.IsFalse(history.CanNavigateBack);
	}

	[TestMethod]
	public void Observe_LargeCaretMove_RecordsPrevious()
	{
		var history = CreateHistory();

		history.Observe(Location("a.lua", 0));
		history.Observe(Location("a.lua", 100));

		Assert.IsTrue(history.CanNavigateBack);
		Assert.IsTrue(history.TryNavigateBack(Location("a.lua", 100), out NavigationLocation target));
		Assert.AreEqual(0, target.CaretOffset);
	}

	[TestMethod]
	public void Observe_FileChangeIsAlwaysMeaningful()
	{
		var history = CreateHistory();

		history.Observe(Location("a.lua", 0));
		history.Observe(Location("b.lua", 5));

		Assert.IsTrue(history.CanNavigateBack);
	}

	[TestMethod]
	public void Observe_NewLocationClearsForwardStack()
	{
		var history = CreateHistory();

		history.Observe(Location("a.lua", 0));
		history.Observe(Location("a.lua", 100));
		history.TryNavigateBack(Location("a.lua", 100), out _);

		Assert.IsTrue(history.CanNavigateForward);

		history.Observe(Location("a.lua", 200));

		Assert.IsFalse(history.CanNavigateForward);
	}

	[TestMethod]
	public void SuppressRecording_UpdatesCurrentWithoutRecording()
	{
		var history = CreateHistory();

		history.Observe(Location("a.lua", 0));
		history.Observe(Location("a.lua", 100));

		using (history.SuppressRecording())
		{
			history.Observe(Location("a.lua", 300));
		}

		Assert.IsTrue(history.CanNavigateBack);
		Assert.IsTrue(history.TryNavigateBack(Location("a.lua", 300), out NavigationLocation target));
		Assert.AreEqual(0, target.CaretOffset);
	}

	[TestMethod]
	public void RecordProgrammaticJump_PushesCurrentAndClearsForward()
	{
		var history = CreateHistory();

		history.Observe(Location("a.lua", 0));
		history.Observe(Location("a.lua", 100));
		history.TryNavigateBack(Location("a.lua", 100), out _);

		history.RecordProgrammaticJump(
			Location("a.lua", 100),
			Location("b.lua", 50));

		Assert.IsFalse(history.CanNavigateForward);
		Assert.IsTrue(history.CanNavigateBack);
		Assert.IsTrue(history.TryNavigateBack(Location("b.lua", 50), out NavigationLocation target));
		Assert.AreEqual(100, target.CaretOffset);
	}

	[TestMethod]
	public void RecordProgrammaticJump_EquivalentLocations_DoesNotRecord()
	{
		var history = CreateHistory();

		history.Observe(Location("a.lua", 0));

		history.RecordProgrammaticJump(Location("a.lua", 0), Location("a.lua", 0));

		Assert.IsFalse(history.CanNavigateBack);
	}

	[TestMethod]
	public void TryNavigateForward_RestoresLaterPosition()
	{
		var history = CreateHistory();

		history.Observe(Location("a.lua", 0));
		history.Observe(Location("a.lua", 100));

		Assert.IsTrue(history.TryNavigateBack(Location("a.lua", 100), out _));
		Assert.IsTrue(history.CanNavigateForward);

		Assert.IsTrue(history.TryNavigateForward(Location("a.lua", 0), out NavigationLocation forwardTarget));
		Assert.AreEqual(100, forwardTarget.CaretOffset);
		Assert.IsFalse(history.CanNavigateForward);
	}

	[TestMethod]
	public void TryNavigate_EmptyHistory_ReturnsFalse()
	{
		var history = CreateHistory();

		Assert.IsFalse(history.TryNavigateBack(Location("a.lua", 0), out _));
		Assert.IsFalse(history.TryNavigateForward(Location("a.lua", 0), out _));
	}

	[TestMethod]
	public void Observe_ConsecutiveLargeMoves_RecordsEachPreviousPosition()
	{
		var history = CreateHistory();

		history.Observe(Location("a.lua", 0));
		history.Observe(Location("a.lua", 100));
		history.Observe(Location("a.lua", 200));
		history.Observe(Location("a.lua", 300));

		Assert.IsTrue(history.TryNavigateBack(Location("a.lua", 300), out NavigationLocation first));
		Assert.AreEqual(200, first.CaretOffset);

		Assert.IsTrue(history.TryNavigateBack(Location("a.lua", 200), out NavigationLocation second));
		Assert.AreEqual(100, second.CaretOffset);

		Assert.IsTrue(history.TryNavigateBack(Location("a.lua", 100), out NavigationLocation third));
		Assert.AreEqual(0, third.CaretOffset);
	}

	[TestMethod]
	public void SetCurrentLocation_DoesNotRecord()
	{
		var history = CreateHistory();

		history.SetCurrentLocation(Location("a.lua", 0));

		Assert.IsFalse(history.CanNavigateBack);
	}
}

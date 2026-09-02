using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Editing.Tests;

[TestClass]
public sealed class IncrementalLineStateCacheTests
{
	private int _transitionInvocationCount;

	private int CountBrackets(string lineText, int state)
		=> state + lineText.Count(character => character == '[') - lineText.Count(character => character == ']');

	private int CountingTransition(string lineText, int state)
	{
		_transitionInvocationCount++;
		return CountBrackets(lineText, state);
	}

	[TestMethod]
	public void GetLineStartState_ComputesStatesFromSnapshot()
	{
		var snapshot = new StringTextSnapshot("a[b\nc\nd]e\nf");
		var cache = new IncrementalLineStateCache<int>(snapshot, CountBrackets);

		Assert.AreEqual(0, cache.GetLineStartState(1));
		Assert.AreEqual(1, cache.GetLineStartState(2));
		Assert.AreEqual(1, cache.GetLineStartState(3));
		Assert.AreEqual(0, cache.GetLineStartState(4));
		Assert.AreEqual(0, cache.GetLineStartState(5));
	}

	[TestMethod]
	public void GetLineStartState_ClampsOutOfRangeLineNumbers()
	{
		var cache = new IncrementalLineStateCache<int>(new StringTextSnapshot("a\nb"), CountBrackets);

		Assert.AreEqual(0, cache.GetLineStartState(-5));
		Assert.AreEqual(0, cache.GetLineStartState(0));
		Assert.AreEqual(0, cache.GetLineStartState(99));
	}

	[TestMethod]
	public void GetLineStartState_EmptyDocument_ReturnsDefault()
	{
		var cache = new IncrementalLineStateCache<int>(new StringTextSnapshot(string.Empty), CountBrackets);

		Assert.AreEqual(0, cache.GetLineStartState(5));
	}

	[TestMethod]
	public void ApplyChange_EditOnFirstLine_RecomputesFollowingState()
	{
		var cache = new IncrementalLineStateCache<int>(new StringTextSnapshot("[a\nb"), CountBrackets);
		Assert.AreEqual(1, cache.GetLineStartState(2));

		cache.ApplyChange(new StringTextSnapshot("[[a\nb"), new TextIncrementalChange(new TextRange(0, 0), "["));

		Assert.AreEqual(2, cache.GetLineStartState(2));
	}

	[TestMethod]
	public void ApplyChange_EditOnMiddleLine_PreservesPrefixAndRecomputesRemainder()
	{
		var cache = new IncrementalLineStateCache<int>(new StringTextSnapshot("a\n[b\nc\nd]"), CountBrackets);

		Assert.AreEqual(0, cache.GetLineStartState(2));
		Assert.AreEqual(1, cache.GetLineStartState(3));
		Assert.AreEqual(1, cache.GetLineStartState(4));

		cache.ApplyChange(new StringTextSnapshot("a\n[b\n[c\nd]"), new TextIncrementalChange(new TextRange(5, 0), "["));

		Assert.AreEqual(0, cache.GetLineStartState(2));
		Assert.AreEqual(1, cache.GetLineStartState(3));
		Assert.AreEqual(2, cache.GetLineStartState(4));
	}

	[TestMethod]
	public void ApplyChange_EditOnLastLine_RecomputesOnlyLastLineStartState()
	{
		var snapshot = new StringTextSnapshot("a\nb\n[c\nd\ne");
		var cache = new IncrementalLineStateCache<int>(snapshot, CountingTransition);

		Assert.AreEqual(1, cache.GetLineStartState(5));
		Assert.AreEqual(4, _transitionInvocationCount);

		cache.ApplyChange(new StringTextSnapshot("a\nb\n[c\nd\ne]"), new TextIncrementalChange(new TextRange(10, 0), "]"));

		Assert.AreEqual(1, cache.GetLineStartState(5));
		Assert.AreEqual(5, _transitionInvocationCount);
	}

	[TestMethod]
	public void ApplyChange_MultilineReplacement_RecomputesFromFirstAffectedLine()
	{
		var cache = new IncrementalLineStateCache<int>(new StringTextSnapshot("a\n[b\nc\nd]\ne"), CountBrackets);

		Assert.AreEqual(1, cache.GetLineStartState(3));
		Assert.AreEqual(0, cache.GetLineStartState(5));
		Assert.AreEqual(0, cache.GetLineStartState(6));

		cache.ApplyChange(new StringTextSnapshot("a\n[b\nX\nY\ne"), new TextIncrementalChange(new TextRange(5, 4), "X\nY"));

		Assert.AreEqual(1, cache.GetLineStartState(3));
		Assert.AreEqual(1, cache.GetLineStartState(5));
		Assert.AreEqual(1, cache.GetLineStartState(6));
	}

	[TestMethod]
	public void ApplyChange_EditOnLaterLine_DoesNotRecomputeUnchangedPrefix()
	{
		var snapshot = new StringTextSnapshot("a\n[b\nc\nd]");
		var cache = new IncrementalLineStateCache<int>(snapshot, CountingTransition);

		Assert.AreEqual(1, cache.GetLineStartState(4));
		Assert.AreEqual(3, _transitionInvocationCount);

		cache.ApplyChange(new StringTextSnapshot("a\n[b\n[c\nd]"), new TextIncrementalChange(new TextRange(5, 0), "["));

		Assert.AreEqual(0, cache.GetLineStartState(2));
		Assert.AreEqual(3, _transitionInvocationCount);

		Assert.AreEqual(2, cache.GetLineStartState(4));
		Assert.AreEqual(5, _transitionInvocationCount);
	}

	[TestMethod]
	public void GetLineStartState_ConcurrentReads_ReturnConsistentStates()
	{
		var snapshot = new StringTextSnapshot("a\n[b\nc\nd]\ne\n[f\ng\nh]");
		var cache = new IncrementalLineStateCache<int>(snapshot, CountBrackets);

		int expectedState = cache.GetLineStartState(8);
		var observed = new int[256];

		Parallel.For(0, observed.Length, i => observed[i] = cache.GetLineStartState(8));

		Assert.IsTrue(observed.All(state => state == expectedState));
		Assert.AreEqual(1, expectedState);
	}

	[TestMethod]
	public void Constructor_NullSnapshot_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new IncrementalLineStateCache<int>(null!, CountBrackets));
	}

	[TestMethod]
	public void Constructor_NullTransition_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new IncrementalLineStateCache<int>(new StringTextSnapshot("a"), null!));
	}

	[TestMethod]
	public void ApplyChange_NullSnapshot_Throws()
	{
		var cache = new IncrementalLineStateCache<int>(new StringTextSnapshot("a"), CountBrackets);

		Assert.ThrowsExactly<ArgumentNullException>(() => cache.ApplyChange(null!, new TextIncrementalChange(new TextRange(0, 0), "b")));
	}
}

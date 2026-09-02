using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.ChangeMarkers;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

[TestClass]
public sealed class UnsavedChangesTrackerTests
{
	[TestMethod]
	public void NoChanges_ReturnsNoMarkedLines()
	{
		var document = new TextDocument("alpha\r\nbeta\r\ngamma");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("alpha\r\nbeta\r\ngamma");

		AssertLineNumbers(tracker, []);
	}

	[TestMethod]
	public void ModifiedLine_IsMarked()
	{
		var document = new TextDocument("alpha\r\nBETA\r\ngamma");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("alpha\r\nbeta\r\ngamma");

		AssertLineNumbers(tracker, [2]);
	}

	[TestMethod]
	public void InsertedLines_AreMarked_AndFollowingLinesAreNot()
	{
		var document = new TextDocument("alpha\r\ninserted\r\nanother\r\nbeta\r\ngamma");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("alpha\r\nbeta\r\ngamma");

		AssertLineNumbers(tracker, [2, 3]);
	}

	[TestMethod]
	public void AppendedLines_AreMarked()
	{
		var document = new TextDocument("alpha\r\nbeta\r\ngamma\r\ndelta");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("alpha\r\nbeta");

		AssertLineNumbers(tracker, [3, 4]);
	}

	[TestMethod]
	public void DeletedLine_LeavesRemainingLinesUnmarked()
	{
		var document = new TextDocument("alpha\r\ngamma");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("alpha\r\nbeta\r\ngamma");

		AssertLineNumbers(tracker, []);
	}

	[TestMethod]
	public void DeletedLastLine_LeavesRemainingLinesUnmarked()
	{
		var document = new TextDocument("alpha\r\nbeta");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("alpha\r\nbeta\r\ngamma");

		AssertLineNumbers(tracker, []);
	}

	[TestMethod]
	public void EmptyBaseline_MarksEveryLine()
	{
		var document = new TextDocument("alpha\r\nbeta");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("");

		AssertLineNumbers(tracker, [1, 2]);
	}

	[TestMethod]
	public void SetBaseline_ToCurrentText_ClearsMarkedLines()
	{
		var document = new TextDocument("alpha\r\nBETA\r\ngamma");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("alpha\r\nbeta\r\ngamma");
		tracker.SetBaseline(document.Text);

		AssertLineNumbers(tracker, []);
	}

	[TestMethod]
	public void ReplacedLines_InTheMiddle_AreMarked()
	{
		var document = new TextDocument("alpha\r\nfirst\r\nsecond\r\ndelta");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("alpha\r\nbeta\r\ngamma\r\ndelta");

		AssertLineNumbers(tracker, [2, 3]);
	}

	[TestMethod]
	public void LineEndings_AreNormalized_BeforeComparing()
	{
		var document = new TextDocument("alpha\nbeta\ngamma");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("alpha\r\nbeta\r\ngamma");

		AssertLineNumbers(tracker, []);
	}

	[TestMethod]
	public void MarkedLines_AreSortedByLineNumber()
	{
		var document = new TextDocument("one\r\nchanged\r\nthree\r\nchanged2\r\nfive");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("one\r\ntwo\r\nthree\r\nfour\r\nfive");

		IReadOnlyList<DocumentLine> lines = tracker.GetMarkedLines();

		Assert.AreEqual(2, lines.Count);
		Assert.AreEqual(2, lines[0].LineNumber);
		Assert.AreEqual(4, lines[1].LineNumber);
	}

	[TestMethod]
	public void EmptyBaseline_WithSingleLineDocument_MarksThatLine()
	{
		var document = new TextDocument("alpha");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("");

		AssertLineNumbers(tracker, [1]);
	}

	private static UnsavedChangesTracker CreateTracker(TextDocument document)
		=> new(() => document);

	private static void AssertLineNumbers(UnsavedChangesTracker tracker, int[] expected)
	{
		int[] actual = tracker.GetMarkedLines().Select(line => line.LineNumber).ToArray();
		CollectionAssert.AreEqual(expected, actual);
	}
}

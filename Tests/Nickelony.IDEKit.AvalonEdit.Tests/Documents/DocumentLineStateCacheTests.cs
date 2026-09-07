using ICSharpCode.AvalonEdit.Document;

namespace Nickelony.IDEKit.AvalonEdit.Documents.Tests;

[TestClass]
public sealed class DocumentLineStateCacheTests
{
	private static int CountBrackets(string lineText, int state)
		=> state + lineText.Count(character => character == '[') - lineText.Count(character => character == ']');

	[TestMethod]
	public void GetLineStartState_TracksEditsOnFirstLine()
	{
		var document = new TextDocument("a[b\nc\nd]e");
		using var cache = new DocumentLineStateCache<int>(document, CountBrackets);

		Assert.AreEqual(1, cache.GetLineStartState(2));
		Assert.AreEqual(1, cache.GetLineStartState(3));

		document.Insert(0, "[");

		Assert.AreEqual(2, cache.GetLineStartState(2));
	}

	[TestMethod]
	public void GetLineStartState_TracksMultilineReplacement()
	{
		var document = new TextDocument("a\n[b\nc\nd]\ne");
		using var cache = new DocumentLineStateCache<int>(document, CountBrackets);

		Assert.AreEqual(1, cache.GetLineStartState(3));
		Assert.AreEqual(0, cache.GetLineStartState(5));
		Assert.AreEqual(0, cache.GetLineStartState(6));

		document.Replace(5, 4, "[x\ny");

		Assert.AreEqual(1, cache.GetLineStartState(3));
		Assert.AreEqual(2, cache.GetLineStartState(4));
		Assert.AreEqual(2, cache.GetLineStartState(5));
		Assert.AreEqual(2, cache.GetLineStartState(6));
	}

	[TestMethod]
	public void GetLineStartState_EditOnLaterLine_PreservesEarlierStates()
	{
		var document = new TextDocument("a\n[b\nc\nd]");
		using var cache = new DocumentLineStateCache<int>(document, CountBrackets);

		Assert.AreEqual(0, cache.GetLineStartState(2));
		Assert.AreEqual(1, cache.GetLineStartState(3));

		document.Insert(5, "[");

		Assert.AreEqual(0, cache.GetLineStartState(2));
		Assert.AreEqual(1, cache.GetLineStartState(3));
		Assert.AreEqual(2, cache.GetLineStartState(4));
	}

	[TestMethod]
	public void Dispose_StopsTrackingDocumentEdits()
	{
		var document = new TextDocument("a\n[b");
		var cache = new DocumentLineStateCache<int>(document, CountBrackets);

		Assert.AreEqual(0, cache.GetLineStartState(2));

		cache.Dispose();
		document.Insert(0, "[");

		Assert.AreEqual(0, cache.GetLineStartState(2));
	}
}

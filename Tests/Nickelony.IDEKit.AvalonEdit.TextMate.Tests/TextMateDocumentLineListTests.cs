using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting;
using TextMateSharp.Grammars;
using TextMateSharp.Model;
using TextMateSharp.Registry;

namespace Nickelony.IDEKit.AvalonEdit.TextMate.Tests;

[STATestClass]
public sealed class TextMateDocumentLineListTests
{
	[TestMethod]
	[DataRow("a\r\nb", 2, 1, null, "a\r|b", DisplayName = "RemoveLfFromCrLfPair")]
	[DataRow("a\r\nb", 1, 1, null, "a\n|b", DisplayName = "RemoveCrFromCrLfPair")]
	[DataRow("a\nb", 1, 0, "\r", "a\r\n|b", DisplayName = "InsertCrBeforeLf")]
	[DataRow("a\r\nb", 2, 1, "\r", "a\r|\r|b", DisplayName = "ReplaceLfWithCr")]
	[DataRow("a\nb", 1, 1, null, "ab", DisplayName = "RemoveWholeTerminator")]
	[DataRow("alpha\r\nbeta", 5, 2, "\n", "alpha\n|beta", DisplayName = "CollapseCrLfToLf")]
	[DataRow("alpha\nbeta", 5, 1, "\r\n", "alpha\r\n|beta", DisplayName = "ExpandLfToCrLf")]
	[DataRow("ab", 2, 0, "\r\n", "ab\r\n|", DisplayName = "AppendCrLfAtEnd")]
	[DataRow("a\rb", 2, 0, "\n", "a\r\n|b", DisplayName = "InsertLfAfterLoneCr")]
	public void DocumentLineList_NewlineBoundaryEdits_KeepSnapshotInSync(
		string originalText,
		int offset,
		int removalLength,
		string? insertedText,
		string expectedLines)
	{
		var document = new TextDocument(originalText);
		var lineList = new TextMateDocumentLineList(document);

		try
		{
			if (removalLength == 0)
				document.Insert(offset, insertedText ?? string.Empty);
			else if (insertedText is null)
				document.Remove(offset, removalLength);
			else
				document.Replace(offset, removalLength, insertedText);

			Assert.AreEqual(document.LineCount, lineList.GetNumberOfLines());
			CollectionAssert.AreEqual(expectedLines.Split('|'), GetSnapshotLines(lineList), DescribeSnapshot(lineList));
		}
		finally
		{
			lineList.Dispose();
		}
	}

	[TestMethod]
	[DataRow("a\r\nb", 2, 1, null, DisplayName = "RemoveLfFromCrLfPair")]
	[DataRow("a\r\nb", 1, 1, null, DisplayName = "RemoveCrFromCrLfPair")]
	[DataRow("a\nb", 1, 0, "\r", DisplayName = "InsertCrBeforeLf")]
	[DataRow("a\r\nb", 2, 1, "\r", DisplayName = "ReplaceLfWithCr")]
	[DataRow("a\rb", 2, 0, "\n", DisplayName = "InsertLfAfterLoneCr")]
	public void DocumentLineList_NewlineBoundaryEditsWithModel_DoNotThrow(
		string originalText,
		int offset,
		int removalLength,
		string? insertedText)
	{
		var document = new TextDocument(originalText);
		var lineList = new TextMateDocumentLineList(document);
		var model = new TMModel(lineList);

		try
		{
			if (removalLength == 0)
				document.Insert(offset, insertedText ?? string.Empty);
			else if (insertedText is null)
				document.Remove(offset, removalLength);
			else
				document.Replace(offset, removalLength, insertedText);

			Assert.AreEqual(document.LineCount, lineList.GetNumberOfLines());
		}
		finally
		{
			model.Dispose();
			lineList.Dispose();
		}
	}

	[TestMethod]
	public void DocumentLineList_CrLfPairEdit_InvalidatesOnlyTheChangedLine()
	{
		var document = new TextDocument("a\r\nb");
		var lineList = new TextMateDocumentLineList(document);
		var model = new TMModel(lineList);

		try
		{
			for (int i = 0; i < lineList.GetNumberOfLines(); i++)
				lineList.Get(i).IsInvalid = false;

			// Remove the line feed of the CRLF pair: only the first line's terminator changes, so only
			// the first line must be re-tokenized.
			document.Remove(2, 1);

			Assert.IsTrue(lineList.Get(0).IsInvalid);
			Assert.IsFalse(lineList.Get(1).IsInvalid);
		}
		finally
		{
			model.Dispose();
			lineList.Dispose();
		}
	}

	[TestMethod]
	public void DocumentLineList_EditAfterCrLfPairEdit_DoesNotPoisonLaterEdits()
	{
		var document = new TextDocument("a\r\nb");
		var lineList = new TextMateDocumentLineList(document);
		var model = new TMModel(lineList);

		try
		{
			// Removing the carriage return leaves the document without a break between the first line's
			// text and the second line; a following whole-document replacement must still succeed.
			document.Remove(1, 1);
			document.Text = "x";

			Assert.AreEqual(document.LineCount, lineList.GetNumberOfLines());
			CollectionAssert.AreEqual(new[] { "x" }, GetSnapshotLines(lineList));
		}
		finally
		{
			model.Dispose();
			lineList.Dispose();
		}
	}

	[TestMethod]
	public void DocumentLineList_WholeTextAssignment_KeepsSnapshotInSync()
	{
		var document = new TextDocument("a\r\nb\r\nc");
		var lineList = new TextMateDocumentLineList(document);

		try
		{
			document.Text = "alpha\nbeta";
			Assert.AreEqual(document.LineCount, lineList.GetNumberOfLines());
			CollectionAssert.AreEqual(new[] { "alpha\n", "beta" }, GetSnapshotLines(lineList));

			document.Text = document.Text.Replace("\n", "\r\n", StringComparison.Ordinal);
			Assert.AreEqual(document.LineCount, lineList.GetNumberOfLines());
			CollectionAssert.AreEqual(new[] { "alpha\r\n", "beta" }, GetSnapshotLines(lineList));

			document.Text = "x\r\n";
			Assert.AreEqual(document.LineCount, lineList.GetNumberOfLines());
			CollectionAssert.AreEqual(new[] { "x\r\n", "" }, GetSnapshotLines(lineList));

			document.Text = string.Empty;
			Assert.AreEqual(1, lineList.GetNumberOfLines());
			CollectionAssert.AreEqual(new[] { "" }, GetSnapshotLines(lineList));
		}
		finally
		{
			lineList.Dispose();
		}
	}

	[TestMethod]
	public void DocumentLineList_MultiLineRemoval_KeepsSnapshotInSync()
	{
		var document = new TextDocument("alpha\r\nbeta\r\ngamma");
		var lineList = new TextMateDocumentLineList(document);

		try
		{
			document.Remove(7, "beta\r\ngamma".Length);

			Assert.AreEqual(document.LineCount, lineList.GetNumberOfLines());
			CollectionAssert.AreEqual(new[] { "alpha\r\n", "" }, GetSnapshotLines(lineList));
		}
		finally
		{
			lineList.Dispose();
		}
	}

	[TestMethod]
	public void DocumentLineList_TracksInsertedAndReplacedLines()
	{
		var document = new TextDocument("alpha\r\nbeta");
		var lineList = new TextMateDocumentLineList(document);

		try
		{
			CollectionAssert.AreEqual(new[] { "alpha\r\n", "beta" }, GetSnapshotLines(lineList));

			document.Insert(document.TextLength, "\r\ngamma");

			CollectionAssert.AreEqual(new[] { "alpha\r\n", "beta\r\n", "gamma" }, GetSnapshotLines(lineList));
			Assert.AreEqual(3, lineList.GetNumberOfLines());

			int replacementOffset = document.Text.IndexOf("beta\r\ngamma", StringComparison.Ordinal);
			document.Replace(replacementOffset, "beta\r\ngamma".Length, "delta");

			CollectionAssert.AreEqual(new[] { "alpha\r\n", "delta" }, GetSnapshotLines(lineList));
			Assert.AreEqual(2, lineList.GetNumberOfLines());
		}
		finally
		{
			lineList.Dispose();
		}
	}

	[TestMethod]
	public void DocumentLineList_MultiLineReplacement_InvalidatesWholeChangedRegion()
	{
		var document = new TextDocument("a\nb\nc\nd\ne");
		var lineList = new TextMateDocumentLineList(document);
		var model = new TMModel(lineList);

		try
		{
			// Simulate a fully tokenized document, then replace two lines with three.
			for (int i = 0; i < lineList.GetNumberOfLines(); i++)
				lineList.Get(i).IsInvalid = false;

			document.Replace(2, "b\nc".Length, "B\nC\nD");

			// The whole inserted region must be marked for re-tokenization: a new line can land on the
			// slot of a removed line, and when its predecessor's end state coincides with the stale end
			// state, the model's forward walk alone would keep that line's stale tokens.
			Assert.IsTrue(lineList.Get(1).IsInvalid);
			Assert.IsTrue(lineList.Get(2).IsInvalid);
			Assert.IsTrue(lineList.Get(3).IsInvalid);

			// Lines outside the changed region keep their tokenization state.
			Assert.IsFalse(lineList.Get(0).IsInvalid);
			Assert.IsFalse(lineList.Get(4).IsInvalid);
			Assert.IsFalse(lineList.Get(5).IsInvalid);
		}
		finally
		{
			model.Dispose();
			lineList.Dispose();
		}
	}

	[TestMethod]
	public void DocumentLineList_MultiLineReplacement_RetokenizesReplacementLine()
	{
		var document = new TextDocument("x = 1\ny = 2\n-- old comment\nz = 3\ne = 4");
		var lineList = new TextMateDocumentLineList(document);
		var model = new TMModel(lineList);

		try
		{
			var registryOptions = new RegistryOptions(ThemeName.DarkPlus);
			model.SetGrammar(new Registry(registryOptions).LoadGrammar(registryOptions.GetScopeByLanguageId("lua")));

			TokenizeLines(model, lineList);

			// Replace two lines with three. The critical slot is the third replacement line: it lands on
			// the slot that previously described the removed comment line, and the preceding line ends in
			// the same state, so a model that only invalidates the first changed line can leave the stale
			// comment tokens in place. (The invalid-flag regression test pins the same defect without
			// depending on tokenizer timing.)
			document.Replace(6, "y = 2\n-- old comment".Length, "Y = 2\nC = 3\nlocal d = 1");

			TokenizeLines(model, lineList);

			List<string> scopes = GetLineScopes(model, 3);

			Assert.IsTrue(
				scopes.Any(scope => scope.Contains("keyword", StringComparison.Ordinal)),
				"Expected the third replacement line to be tokenized as code.");
			Assert.IsFalse(
				scopes.Any(scope => scope.Contains("comment", StringComparison.Ordinal)),
				"The third replacement line must not keep tokens from the removed comment line.");
		}
		finally
		{
			model.Dispose();
			lineList.Dispose();
		}
	}

	[TestMethod]
	public void DocumentLineList_Undo_RestoresSnapshot()
	{
		var document = new TextDocument("alpha\r\nbeta");
		var lineList = new TextMateDocumentLineList(document);

		try
		{
			document.Insert(document.TextLength, "\r\ngamma");
			Assert.AreEqual(3, lineList.GetNumberOfLines());

			document.UndoStack.Undo();

			CollectionAssert.AreEqual(new[] { "alpha\r\n", "beta" }, GetSnapshotLines(lineList));
			Assert.AreEqual(2, lineList.GetNumberOfLines());
		}
		finally
		{
			lineList.Dispose();
		}
	}

	private static void TokenizeLines(TMModel model, TextMateDocumentLineList lineList)
	{
		// The test must not call ForceTokenization while the model's own tokenizer thread is running:
		// TextMateSharp's Tokenizer is not safe to drive from two threads at once, and concurrent calls
		// can produce corrupted tokens (the source of this test's flakiness). Instead, wait for the
		// model's background pass to settle. Lines the model leaves invalid because its per-line budget
		// was exceeded are re-queued by the model itself, so the wait converges; a stale-but-valid line
		// (the defect this test pins) settles immediately and fails the assertion below.
		// The wait doubles TextMateSharp's three-second per-line budget as a CI margin; a settled model
		// returns immediately.
		for (int attempt = 0; attempt < 1200; attempt++)
		{
			bool settled = true;

			for (int i = 0; i < lineList.GetNumberOfLines(); i++)
			{
				if (model.GetLineTokens(i) is null || model.IsLineInvalid(i))
				{
					settled = false;
					break;
				}
			}

			if (settled)
				return;

			Thread.Sleep(5);
		}

		Assert.Fail($"The model did not finish tokenizing the document ({lineList.GetNumberOfLines()} lines).");
	}

	private static List<string> GetLineScopes(TMModel model, int lineIndex)
	{
		List<TMToken> tokens = model.GetLineTokens(lineIndex)
			?? throw new AssertFailedException($"Line {lineIndex} has no tokens.");

		return [.. tokens.SelectMany(token => token.Scopes).Distinct(StringComparer.Ordinal)];
	}

	private static string[] GetSnapshotLines(TextMateDocumentLineList lineList)
		=> [.. Enumerable.Range(0, lineList.GetNumberOfLines())
			.Select(index => lineList.GetLineTextIncludingTerminators(index).ToString())];

	private static string DescribeSnapshot(TextMateDocumentLineList lineList)
		=> "snapshot=[" + string.Join(
			", ",
			GetSnapshotLines(lineList).Select(static line => "'" + line.Replace("\r", "\\r").Replace("\n", "\\n") + "'")) + "]";
}

using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Extras.TextMate.Highlighting;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.Extras.Tests;

/// <summary>
/// Tests line tracking through document changes and theme foreground colors from selectors.
/// </summary>
[TestClass]
public class TextMateHighlightingTests
{
	[TestMethod]
	public void GetChangeInfo_ReportsOneRemovedAndTwoInsertedLinesForCrLf()
	{
		STATestHelper.RunInSTA(() =>
		{
			(int startLineIndex, int removedLineCount, int insertedLineCount) = ApplyChangeAndGetInfo("alpha", document => document.Insert(5, "\r\nbeta"));

			Assert.AreEqual(0, startLineIndex);
			Assert.AreEqual(1, removedLineCount);
			Assert.AreEqual(2, insertedLineCount);
		});
	}

	[TestMethod]
	public void GetChangeInfo_ReportsOneRemovedAndTwoInsertedLinesForLf()
	{
		STATestHelper.RunInSTA(() =>
		{
			(int startLineIndex, int removedLineCount, int insertedLineCount) = ApplyChangeAndGetInfo("alpha", document => document.Insert(5, "\nbeta"));

			Assert.AreEqual(0, startLineIndex);
			Assert.AreEqual(1, removedLineCount);
			Assert.AreEqual(2, insertedLineCount);
		});
	}

	[TestMethod]
	public void GetChangeInfo_ReportsOneRemovedAndTwoInsertedLinesForLoneCr()
	{
		STATestHelper.RunInSTA(() =>
		{
			(int startLineIndex, int removedLineCount, int insertedLineCount) = ApplyChangeAndGetInfo("alpha", document => document.Insert(5, "\rbeta"));

			Assert.AreEqual(0, startLineIndex);
			Assert.AreEqual(1, removedLineCount);
			Assert.AreEqual(2, insertedLineCount);
		});
	}

	[TestMethod]
	public void Resolve_MatchesCommaSeparatedSelectors()
	{
		STATestHelper.RunInSTA(() =>
		{
			var resolver = new TextMateThemeStyleResolver(CreateTheme(
				new TextMateTokenThemeRule
				{
					Scope = "entity.name.function, support.function, support.function.library, support.function.any-method",
					Foreground = "#4271AE"
				}));

			TextMateHighlightingStyle style = resolver.Resolve(["source.lua", "support.function.library.lua"]);

			Assert.AreEqual("#FF4271AE", GetForegroundColor(style));
		});
	}

	[TestMethod]
	public void Resolve_PrefersMoreSpecificSelectorsOverBroaderMatches()
	{
		STATestHelper.RunInSTA(() =>
		{
			var resolver = new TextMateThemeStyleResolver(CreateTheme(
				new TextMateTokenThemeRule
				{
					Scope = "entity.name.class, support.class, support.type, support.variable, variable.language.self",
					Foreground = "#66CCCC"
				},
				new TextMateTokenThemeRule
				{
					Scope = "entity.other.attribute, support.type.property-name",
					Foreground = "#D7B8FF"
				}));

			TextMateHighlightingStyle style = resolver.Resolve(["source.lua", "support.type.property-name.lua"]);

			Assert.AreEqual("#FFD7B8FF", GetForegroundColor(style));
		});
	}

	[TestMethod]
	public void DocumentLineList_TracksInsertedAndReplacedLines()
	{
		STATestHelper.RunInSTA(() =>
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
		});
	}

	private static TextMateTokenTheme CreateTheme(params TextMateTokenThemeRule[] rules)
		=> new() { Rules = rules };

	private static (int StartLineIndex, int RemovedLineCount, int InsertedLineCount) ApplyChangeAndGetInfo(string originalText, Action<TextDocument> changeAction)
	{
		var document = new TextDocument(originalText);
		DocumentChangeEventArgs? change = null;

		document.Changed += (_, args) => change = args;
		changeAction(document);

		if (change is null)
			throw new InvalidOperationException("The change action did not modify the document.");

		return TextMateDocumentLineList.GetChangeInfo(document, change);
	}

	private static string[] GetSnapshotLines(TextMateDocumentLineList lineList)
		=> [.. WPFTestHost.GetPrivateField<List<string>>(lineList, "_lineTexts")];

	private static string GetForegroundColor(TextMateHighlightingStyle style)
	{
		Assert.IsNotNull(style.Foreground);
		return ((SolidColorBrush)style.Foreground).Color.ToString();
	}
}

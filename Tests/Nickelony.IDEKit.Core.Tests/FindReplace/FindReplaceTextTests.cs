using System.Text.RegularExpressions;

namespace Nickelony.IDEKit.Core.FindReplace.Tests;

[TestClass]
public sealed class FindReplaceTextTests
{
	[TestMethod]
	public void BuildPattern_LiteralTextIsEscaped()
	{
		Assert.AreEqual(@"a\.b", FindReplaceText.BuildPattern("a.b", useRegex: false, matchWholeWord: false));
	}

	[TestMethod]
	public void BuildPattern_RegexPassesThrough()
	{
		Assert.AreEqual("a.b", FindReplaceText.BuildPattern("a.b", useRegex: true, matchWholeWord: false));
	}

	[TestMethod]
	public void BuildPattern_WholeWordWrapsPatternInBoundaries()
	{
		Assert.AreEqual(@"\bword\b", FindReplaceText.BuildPattern("word", useRegex: false, matchWholeWord: true));
	}

	[TestMethod]
	public void BuildPattern_BlankFindTextReturnsEmpty()
	{
		Assert.AreEqual(string.Empty, FindReplaceText.BuildPattern(string.Empty, useRegex: false, matchWholeWord: false));
	}

	[TestMethod]
	public void BuildRegexOptions_IgnoresCaseUnlessRequested()
	{
		Assert.AreEqual(RegexOptions.None, FindReplaceText.BuildRegexOptions(caseSensitive: true));
		Assert.AreEqual(RegexOptions.IgnoreCase, FindReplaceText.BuildRegexOptions(caseSensitive: false));
	}

	[TestMethod]
	public void CountMatches_CountsCaseInsensitively()
	{
		Assert.AreEqual(2, FindReplaceText.CountMatches("aaa bbb aaa", "aaa", RegexOptions.IgnoreCase));
	}

	[TestMethod]
	public void CountMatches_EmptyPatternReturnsZero()
	{
		Assert.AreEqual(0, FindReplaceText.CountMatches("aaa", string.Empty, RegexOptions.None));
	}

	[TestMethod]
	public void FindAllMatches_ReturnsEveryMatch()
	{
		MatchCollection matches = FindReplaceText.FindAllMatches("aaa bbb aaa", "aaa", RegexOptions.None);

		Assert.AreEqual(2, matches.Count);
	}

	[TestMethod]
	public void GetTextBeforeSelection_ReturnsPrefix()
	{
		Assert.AreEqual("aaa ", FindReplaceText.GetTextBeforeSelection("aaa bbb", 4));
	}

	[TestMethod]
	public void GetTextAfterSelection_ReturnsSuffix()
	{
		Assert.AreEqual("bbb", FindReplaceText.GetTextAfterSelection("aaa bbb", 4));
	}

	[TestMethod]
	public void GetMatchesFromSection_ReturnsMatchesInRequestedSection()
	{
		const string text = "aaa bbb aaa ccc";
		var options = RegexOptions.None;

		MatchCollection previous = FindReplaceText.GetMatchesFromSection(
			FindingOrder.Previous, text, selectionStart: 4, selectionLength: 0, "aaa", options);

		Assert.AreEqual(1, previous.Count);
		Assert.AreEqual(0, previous[0].Index);

		MatchCollection next = FindReplaceText.GetMatchesFromSection(
			FindingOrder.Next, text, selectionStart: 4, selectionLength: 0, "aaa", options);

		Assert.AreEqual(1, next.Count);
		Assert.AreEqual(4, next[0].Index);
	}

	[TestMethod]
	public void GetMatchesFromSection_UnknownOrderThrows()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			FindReplaceText.GetMatchesFromSection(
				(FindingOrder)99, "aaa", 0, 0, "aaa", RegexOptions.None));
	}

	[TestMethod]
	public void GetLastAndFirstMatch_HandleEmptyCollections()
	{
		MatchCollection empty = FindReplaceText.FindAllMatches("bbb", "aaa", RegexOptions.None);

		Assert.IsNull(FindReplaceText.GetLastMatch(empty));
		Assert.IsNull(FindReplaceText.GetFirstMatch(empty));

		MatchCollection matches = FindReplaceText.FindAllMatches("aaa bbb aaa", "aaa", RegexOptions.None);

		Assert.AreEqual(8, FindReplaceText.GetLastMatch(matches)!.Index);
		Assert.AreEqual(0, FindReplaceText.GetFirstMatch(matches)!.Index);
	}

	[TestMethod]
	public void GetAbsoluteMatchOffset_AddsSectionStartToMatchIndex()
	{
		Match match = Regex.Match("bbb aaa ccc", "aaa");

		Assert.AreEqual(8, FindReplaceText.GetAbsoluteMatchOffset(4, match));
	}

	[TestMethod]
	public void ReplaceAll_ReplacesEveryMatch()
	{
		Assert.AreEqual(
			"xx bbb xx",
			FindReplaceText.ReplaceAll("aaa bbb aaa", "aaa", "xx", RegexOptions.None));
	}

	[TestMethod]
	public void FindReplaceSource_DefaultsToEmptyName()
	{
		var source = new FindReplaceSource();

		Assert.AreEqual(string.Empty, source.Name);
		Assert.AreEqual(0, source.Count);
	}

	[TestMethod]
	public void FindReplaceSource_CarriesNameAndItems()
	{
		var source = new FindReplaceSource("script.lua")
		{
			new FindReplaceItem(3, "local value = 1", "value", 0)
		};

		Assert.AreEqual("script.lua", source.Name);
		Assert.AreEqual(1, source.Count);
		Assert.AreEqual(3, source[0].LineNumber);
		Assert.AreEqual("local value = 1", source[0].LineText);
		Assert.AreEqual("value", source[0].MatchSegmentText);
		Assert.AreEqual(0, source[0].MatchSegmentIndex);
	}
}

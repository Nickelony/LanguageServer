using ICSharpCode.AvalonEdit.Highlighting;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.Highlighting.Tests;

[TestClass]
public sealed class RegexHighlightingDefinitionTests
{
	private sealed class TestDefinition : RegexHighlightingDefinition
	{
		private readonly Func<IEnumerable<RegexHighlightingRule>> _buildRules;

		public TestDefinition(
			string name,
			Func<IEnumerable<RegexHighlightingRule>> buildRules,
			Func<int>? cacheVersion = null,
			Color? fallbackColor = null)
			: base(name, cacheVersion, fallbackColor)
		{
			_buildRules = buildRules;
		}

		protected override IEnumerable<RegexHighlightingRule> BuildRules()
			=> _buildRules();
	}

	private static RegexHighlightingRule Rule(string pattern, string? htmlColor = null, bool isBold = false, bool isItalic = false)
		=> new(new Regex(pattern), new RegexHighlightingStyle(htmlColor, isBold, isItalic));

	[TestMethod]
	public void MainRuleSet_IsBuiltLazilyAndCached()
	{
		int buildCount = 0;

		var definition = new TestDefinition(
			"Test Rules",
			() =>
			{
				buildCount++;
				return [Rule(";.*$")];
			});

		Assert.AreEqual(0, buildCount);

		HighlightingRuleSet first = definition.MainRuleSet;

		Assert.AreEqual(1, buildCount);
		Assert.AreSame(first, definition.MainRuleSet);
		Assert.AreEqual(1, buildCount);
	}

	[TestMethod]
	public void MainRuleSet_AppliesRegexAndStyle()
	{
		var definition = new TestDefinition(
			"Test Rules",
			() => [new RegexHighlightingRule(new Regex(";.*$"), new RegexHighlightingStyle("#FF0000", IsBold: true, IsItalic: true))]);

		HighlightingRule rule = definition.MainRuleSet.Rules.Single();

		Assert.IsTrue(rule.Regex.IsMatch("; comment"));
		Assert.AreEqual(Colors.Red, rule.Color.Foreground.GetColor(null));
		Assert.AreEqual(FontWeights.Bold, rule.Color.FontWeight);
		Assert.AreEqual(FontStyles.Italic, rule.Color.FontStyle);
	}

	[TestMethod]
	public void MainRuleSet_MalformedColor_FallsBackToBlack()
	{
		var definition = new TestDefinition(
			"Test Rules",
			() => [new RegexHighlightingRule(new Regex("x"), new RegexHighlightingStyle("not-a-color"))]);

		Assert.AreEqual(Colors.Black, definition.MainRuleSet.Rules[0].Color.Foreground.GetColor(null));
	}

	[TestMethod]
	public void MainRuleSet_MalformedColor_UsesSuppliedFallback()
	{
		var definition = new TestDefinition(
			"Test Rules",
			() => [new RegexHighlightingRule(new Regex("x"), new RegexHighlightingStyle("not-a-color"))],
			fallbackColor: Colors.Blue);

		Assert.AreEqual(Colors.Blue, definition.MainRuleSet.Rules[0].Color.Foreground.GetColor(null));
	}

	[TestMethod]
	public void MainRuleSet_EmptyColorValue_FallsBack()
	{
		var definition = new TestDefinition(
			"Test Rules",
			() => [new RegexHighlightingRule(new Regex("x"), new RegexHighlightingStyle(string.Empty))]);

		Assert.AreEqual(Colors.Black, definition.MainRuleSet.Rules[0].Color.Foreground.GetColor(null));
	}

	[TestMethod]
	public void MainRuleSet_RebuildsWhenCacheVersionChanges()
	{
		int version = 1;

		var definition = new TestDefinition(
			"Test Rules",
			() => [Rule("x")],
			cacheVersion: () => version);

		HighlightingRuleSet first = definition.MainRuleSet;

		Assert.AreSame(first, definition.MainRuleSet);

		version = 2;

		HighlightingRuleSet rebuilt = definition.MainRuleSet;

		Assert.AreNotSame(first, rebuilt);
		Assert.AreSame(rebuilt, definition.MainRuleSet);
	}

	[TestMethod]
	public void MainRuleSet_EmptyRules_YieldsEmptyRuleSet()
	{
		var definition = new TestDefinition("Test Rules", () => []);
		Assert.IsEmpty(definition.MainRuleSet.Rules);
	}

	[TestMethod]
	public void GetNamedRuleSet_MatchingName_ReturnsMainRuleSet()
	{
		var definition = new TestDefinition("Test Rules", () => [Rule("x")]);
		Assert.AreSame(definition.MainRuleSet, definition.GetNamedRuleSet("Test Rules"));
	}

	[TestMethod]
	public void GetNamedRuleSet_UnknownName_ReturnsNull()
	{
		var definition = new TestDefinition("Test Rules", () => [Rule("x")]);
		Assert.IsNull(definition.GetNamedRuleSet("DoesNotExist"));
	}
}

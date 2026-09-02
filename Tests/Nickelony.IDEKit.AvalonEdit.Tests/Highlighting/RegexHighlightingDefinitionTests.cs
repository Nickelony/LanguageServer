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
	public void MainRuleSet_MalformedColor_FallsBackToWhite()
	{
		var definition = new TestDefinition(
			"Test Rules",
			() => [new RegexHighlightingRule(new Regex("x"), new RegexHighlightingStyle("not-a-color"))]);

		Assert.AreEqual(Colors.White, definition.MainRuleSet.Rules[0].Color.Foreground.GetColor(null));
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

		Assert.AreEqual(Colors.White, definition.MainRuleSet.Rules[0].Color.Foreground.GetColor(null));
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

		Assert.AreEqual(0, definition.MainRuleSet.Rules.Count);
	}

	[TestMethod]
	public void HighlightingContract_AllIHighlightingDefinitionMembersAreUsable()
	{
		var definition = new TestDefinition(
			"Test Rules",
			() => [Rule("x")]);

		Assert.AreEqual("Test Rules", definition.Name);
		Assert.IsNotNull(definition.MainRuleSet);
		Assert.AreEqual("Test Rules", definition.MainRuleSet.Name);
		Assert.IsFalse(definition.NamedHighlightingColors.Any());
		Assert.IsNotNull(definition.Properties);
		Assert.AreEqual(0, definition.Properties.Count);
		Assert.IsNull(definition.GetNamedColor("anything"));
		Assert.IsNull(definition.GetNamedRuleSet("DoesNotExist"));
		Assert.IsNotNull(definition.GetNamedRuleSet(definition.Name));
	}

	[TestMethod]
	public void Constructor_NullName_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new TestDefinition(null!, () => []));
	}
}

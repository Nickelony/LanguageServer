using Nickelony.IDEKit.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting;
using static Nickelony.IDEKit.AvalonEdit.TextMate.Tests.TextMateThemeTestHelpers;

namespace Nickelony.IDEKit.AvalonEdit.TextMate.Tests;

[STATestClass]
public sealed class TextMateThemeStyleResolverSelectorTests
{
	[TestMethod]
	public void Resolve_MatchesCommaSeparatedSelectors()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule
			{
				Scope = "entity.name.function, support.function, support.function.library, support.function.any-method",
				Foreground = "#4271AE"
			}));

		TextRunStyle style = resolver.Resolve(["source.lua", "support.function.library.lua"]);

		Assert.AreEqual("#FF4271AE", GetForegroundColor(style));
	}

	[TestMethod]
	public void Resolve_MatchesDescendantSelectorAgainstEnclosingScope()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule
			{
				Scope = "source.lua keyword.control",
				Foreground = "#CC99CC"
			}));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.AreEqual("#FFCC99CC", GetForegroundColor(style));
	}

	[TestMethod]
	public void Resolve_DescendantSelector_RequiresEnclosingScopeOrder()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule
			{
				Scope = "keyword.control source.lua",
				Foreground = "#CC99CC"
			}));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsFalse(style.HasFormatting);
	}

	[TestMethod]
	public void Resolve_DescendantSelector_BeatsBroaderSinglePartSelector()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#111111" },
			new TextMateTokenThemeRule { Scope = "source.lua keyword.control", Foreground = "#222222" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.AreEqual("#FF222222", GetForegroundColor(style));
	}

	[TestMethod]
	public void Resolve_UnsupportedSelectorOperators_DoNotMatch()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "source.lua - comment", Foreground = "#111111" },
			new TextMateTokenThemeRule { Scope = "source.lua > keyword", Foreground = "#222222" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsFalse(style.HasFormatting);
	}

	[TestMethod]
	public void Resolve_DescendantSelector_MatchesAcrossMultipleEnclosingScopes()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "source.lua meta.function keyword", Foreground = "#AAAAAA" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "meta.function.lua", "keyword.control.lua"]);

		Assert.AreEqual("#FFAAAAAA", GetForegroundColor(style));
	}

	[TestMethod]
	public void Resolve_DescendantSelector_ThreePartsOutOfOrder_DoNotMatch()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "meta.function source.lua keyword", Foreground = "#AAAAAA" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "meta.function.lua", "keyword.control.lua"]);

		Assert.IsFalse(style.HasFormatting);
	}

	[TestMethod]
	public void Resolve_DescendantSelector_MultipleCandidateAnchors_Match()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "source.lua keyword", Foreground = "#AAAAAA" }));

		// The rightmost part matches two scopes; every candidate must be evaluated so the most
		// specific matching anchor can win.
		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.operator.lua", "meta.embedded.lua", "keyword.control.lua"]);

		Assert.AreEqual("#FFAAAAAA", GetForegroundColor(style));
	}

	[TestMethod]
	public void Resolve_UnsupportedSelector_LogsWarning()
	{
		var logger = new CapturingLogger();
		var resolver = new TextMateThemeStyleResolver(
			CreateTheme(new TextMateTokenThemeRule { Scope = "source.lua - comment", Foreground = "#111111" }),
			logger);

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsFalse(style.HasFormatting);
		Assert.IsTrue(logger.Messages.Any(message => message.Contains("source.lua - comment", StringComparison.Ordinal)));
	}

	[TestMethod]
	public void Resolve_ParenthesisSelector_LogsWarning()
	{
		var logger = new CapturingLogger();
		var resolver = new TextMateThemeStyleResolver(
			CreateTheme(new TextMateTokenThemeRule { Scope = "source.lua (keyword)", Foreground = "#111111" }),
			logger);

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsFalse(style.HasFormatting);
		Assert.IsTrue(logger.Messages.Any(message => message.Contains("(keyword)", StringComparison.Ordinal)));
	}

	[TestMethod]
	public void Resolve_WildcardAndPrioritySelectors_DoNotMatchAndLogWarning()
	{
		var logger = new CapturingLogger();
		var resolver = new TextMateThemeStyleResolver(
			CreateTheme(
				new TextMateTokenThemeRule { Scope = "source.lua *", Foreground = "#111111" },
				new TextMateTokenThemeRule { Scope = "L:keyword", Foreground = "#222222" }),
			logger);

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsFalse(style.HasFormatting);
		Assert.IsTrue(logger.Messages.Any(message => message.Contains("source.lua *", StringComparison.Ordinal)));
		Assert.IsTrue(logger.Messages.Any(message => message.Contains("L:keyword", StringComparison.Ordinal)));
	}

	[TestMethod]
	public void Resolve_MixedSelector_DropsUnsupportedPartAndKeepsSupportedPart()
	{
		var logger = new CapturingLogger();
		var resolver = new TextMateThemeStyleResolver(
			CreateTheme(new TextMateTokenThemeRule { Scope = "keyword, source.lua > x", Foreground = "#123456" }),
			logger);

		// The supported half of a comma-separated selector still applies; the unsupported half is
		// reported once at construction and never re-checked during resolution.
		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.AreEqual("#FF123456", GetForegroundColor(style));
		Assert.IsTrue(logger.Messages.Any(message => message.Contains("source.lua > x", StringComparison.Ordinal)));
	}

	[TestMethod]
	public void Resolve_EmbeddedOperatorSelectors_LogWarningAndDoNotMatch()
	{
		var logger = new CapturingLogger();
		var resolver = new TextMateThemeStyleResolver(
			CreateTheme(
				new TextMateTokenThemeRule { Scope = "keyword(foo)", Foreground = "#111111" },
				new TextMateTokenThemeRule { Scope = "scope*", Foreground = "#222222" },
				new TextMateTokenThemeRule { Scope = "l:keyword.control", Foreground = "#333333" }),
			logger);

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsFalse(style.HasFormatting);
		Assert.IsTrue(logger.Messages.Any(message => message.Contains("keyword(foo)", StringComparison.Ordinal)));
		Assert.IsTrue(logger.Messages.Any(message => message.Contains("scope*", StringComparison.Ordinal)));
		Assert.IsTrue(logger.Messages.Any(message => message.Contains("l:keyword.control", StringComparison.Ordinal)));
	}
}

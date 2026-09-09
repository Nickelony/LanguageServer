using Nickelony.IDEKit.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting;
using System.Windows.Media;
using static Nickelony.IDEKit.AvalonEdit.TextMate.Tests.TextMateThemeTestHelpers;

namespace Nickelony.IDEKit.AvalonEdit.TextMate.Tests;

[STATestClass]
public sealed class TextMateThemeStyleResolverStyleTests
{
	[TestMethod]
	public void Resolve_UnknownFontStyleTrait_LogsWarningAndKeepsKnownTraits()
	{
		var logger = new CapturingLogger();
		var resolver = new TextMateThemeStyleResolver(
			CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", FontStyle = "bold sparkle" }),
			logger);

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsTrue(style.IsBold);
		Assert.IsTrue(logger.Messages.Any(message => message.Contains("sparkle", StringComparison.Ordinal)));
	}

	[TestMethod]
	public void Resolve_PresentFontStyle_ResetsInheritedTraits()
	{
		// A present font-style value is an explicit reset, including the empty string that VS Code and
		// TextMate theme data use to clear inherited traits. "none" is accepted as a readable spelling.
		foreach (string resetValue in new[] { string.Empty, "   ", "none" })
		{
			var resolver = new TextMateThemeStyleResolver(CreateTheme(
				new TextMateTokenThemeRule { Scope = "keyword", FontStyle = "bold italic underline strikethrough" },
				new TextMateTokenThemeRule { Scope = "keyword.control", FontStyle = resetValue }));

			TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

			Assert.IsFalse(style.IsBold, $"'{resetValue}' must reset bold.");
			Assert.IsFalse(style.IsItalic, $"'{resetValue}' must reset italic.");
			Assert.IsNull(style.TextDecorations, $"'{resetValue}' must clear the underline and strikethrough decorations.");
		}
	}

	[TestMethod]
	public void Resolve_AbsentFontStyle_KeepsInheritedTraits()
	{
		// An absent font-style value (null) leaves inherited traits alone, so a more specific rule that
		// only sets a foreground keeps the bold trait of the broader rule.
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "keyword", FontStyle = "bold" },
			new TextMateTokenThemeRule { Scope = "keyword.control", Foreground = "#123456" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsTrue(style.IsBold);
		Assert.AreEqual("#FF123456", GetForegroundColor(style));
	}

	[TestMethod]
	public void Resolve_EightDigitHexColor_IsParsedAsRrggbbaa()
	{
		// TextMate theme data spells eight-digit colors as #RRGGBBAA; the resolver normalizes the alpha
		// component to the WPF order instead of letting the WPF parser read it as #AARRGGBB.
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#E7C0C0FF" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.AreEqual(Color.FromArgb(0xFF, 0xE7, 0xC0, 0xC0), ((SolidColorBrush)style.Foreground!).Color);
	}

	[TestMethod]
	public void Resolve_EightDigitHexColorWithAlpha_PreservesAlpha()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#FF000080" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.AreEqual(Color.FromArgb(0x80, 0xFF, 0x00, 0x00), ((SolidColorBrush)style.Foreground!).Color);
	}

	[TestMethod]
	public void Resolve_CacheOverflow_ClearsAndStillResolves()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#123456" }));

		TextRunStyle cachedStyle = resolver.Resolve(["source.lua", "keyword"]);

		// Fill the bounded cache past its entry limit so the overflow clear path runs.
		for (int i = 0; i <= TextMateThemeStyleResolver.MaxCacheEntryCount; i++)
			resolver.Resolve(["source.lua", $"scope{i}", "keyword"]);

		TextRunStyle refreshedStyle = resolver.Resolve(["source.lua", "keyword"]);

		// The styles are values, so a rebuilt entry is indistinguishable from the cached one; the
		// observable contract after an overflow clear is that the scopes still resolve correctly.
		Assert.AreEqual("#FF123456", GetForegroundColor(refreshedStyle));
		Assert.AreEqual(cachedStyle, refreshedStyle);
	}

	[TestMethod]
	public void Resolve_FromMultipleThreads_ReturnsConsistentStyles()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#123456" },
			new TextMateTokenThemeRule { Scope = "string", Foreground = "#654321" }));

		string[] expectedColors = ["#FF123456", "#FF654321"];

		Parallel.For(0, 1000, index =>
		{
			int caseIndex = index % 2;
			TextRunStyle style = caseIndex == 0
				? resolver.Resolve(["source.lua", "keyword.control.lua"])
				: resolver.Resolve(["source.lua", "string.quoted.lua"]);

			Assert.AreEqual(expectedColors[caseIndex], GetForegroundColor(style));
		});
	}

	[TestMethod]
	public void Resolve_FourDigitHexColor_IsParsedAsRgba()
	{
		// TextMate theme data spells four-digit colors as #RGBA; the resolver moves the alpha digit to
		// the WPF order and doubles every digit.
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#F00A" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.AreEqual(Color.FromArgb(0xAA, 0xFF, 0x00, 0x00), ((SolidColorBrush)style.Foreground!).Color);
	}

	[TestMethod]
	public void Resolve_FontStyleFlags_ApplyBoldItalicUnderlineAndStrikethrough()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule
			{
				Scope = "keyword",
				FontStyle = "bold italic underline strikethrough"
			}));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsTrue(style.IsBold);
		Assert.IsTrue(style.IsItalic);
		Assert.IsNotNull(style.TextDecorations);
		Assert.AreEqual(2, style.TextDecorations.Count);
	}

	[TestMethod]
	public void Resolve_NoneFontStyle_ProducesNoFormatting()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule
			{
				Scope = "keyword",
				FontStyle = "none"
			}));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsFalse(style.HasFormatting);
		Assert.IsFalse(style.IsBold);
		Assert.IsFalse(style.IsItalic);
		Assert.IsNull(style.TextDecorations);
	}

	[TestMethod]
	public void Resolve_InvalidForegroundColor_LogsWarningAndIgnoresForeground()
	{
		var logger = new CapturingLogger();
		var resolver = new TextMateThemeStyleResolver(
			CreateTheme(new TextMateTokenThemeRule
			{
				Scope = "keyword",
				Foreground = "not-a-color"
			}),
			logger);

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsNull(style.Foreground);
		Assert.IsTrue(logger.Messages.Any(message => message.Contains("Invalid foreground color", StringComparison.Ordinal)));
	}

	[TestMethod]
	public void Resolve_EmptyScopes_ReturnsSharedEmptyStyle()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme());

		Assert.AreEqual(TextRunStyle.Empty, resolver.Resolve(null));
		Assert.AreEqual(TextRunStyle.Empty, resolver.Resolve([]));
	}

	[TestMethod]
	public void Resolve_SameScopesTwice_ReturnsTheCachedStyleValues()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule
			{
				Scope = "keyword",
				Foreground = "#123456"
			}));

		TextRunStyle first = resolver.Resolve(["source.lua", "keyword.control.lua"]);
		TextRunStyle second = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		// The resolver caches by scope sequence; with value-type styles the cached entry is observed by
		// value equality rather than by reference identity.
		Assert.AreEqual(first, second);
	}

	[TestMethod]
	public void Rules_AssignedCollection_IsCopied()
	{
		var rules = new List<TextMateTokenThemeRule>
		{
			new() { Scope = "keyword", Foreground = "#123456" }
		};
		var theme = new TextMateTokenTheme { Rules = rules };

		rules.Add(new TextMateTokenThemeRule { Scope = "string", Foreground = "#654321" });

		Assert.AreEqual(1, theme.Rules.Count);
	}
}

using ICSharpCode.AvalonEdit.Highlighting;
using Nickelony.IDEKit.AvalonEdit.Highlighting;
using System.Windows;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

[TestClass]
public sealed class RegexHighlightingStyleTests
{
	[TestMethod]
	public void ToHighlightingColor_BoldOnly_SetsWeightWithoutStyle()
	{
		HighlightingColor color = new RegexHighlightingStyle("#FF0000", IsBold: true).ToHighlightingColor(Colors.Black);

		Assert.AreEqual(Colors.Red, color.Foreground!.GetColor(null));
		Assert.AreEqual(FontWeights.Bold, color.FontWeight);
		Assert.IsNull(color.FontStyle);
	}

	[TestMethod]
	public void ToHighlightingColor_ItalicOnly_SetsStyleWithoutWeight()
	{
		HighlightingColor color = new RegexHighlightingStyle("#FF0000", IsItalic: true).ToHighlightingColor(Colors.Black);

		Assert.AreEqual(Colors.Red, color.Foreground!.GetColor(null));
		Assert.IsNull(color.FontWeight);
		Assert.AreEqual(FontStyles.Italic, color.FontStyle);
	}

	[TestMethod]
	public void ToHighlightingColor_NamedColor_IsParsed()
	{
		HighlightingColor color = new RegexHighlightingStyle("Red").ToHighlightingColor(Colors.Black);

		Assert.AreEqual(Colors.Red, color.Foreground!.GetColor(null));
	}

	[TestMethod]
	public void ToHighlightingColor_ArgbAlpha_IsPreserved()
	{
		HighlightingColor color = new RegexHighlightingStyle("#80FF0000").ToHighlightingColor(Colors.Black);

		// The eight-digit form carries an alpha channel; it must survive the conversion instead of
		// being treated as an opaque color.
		Assert.AreEqual(Color.FromArgb(0x80, 0xFF, 0x00, 0x00), color.Foreground!.GetColor(null));
	}
}

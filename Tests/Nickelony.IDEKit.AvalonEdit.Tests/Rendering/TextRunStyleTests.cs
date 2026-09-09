using Nickelony.IDEKit.AvalonEdit.Rendering;
using System.Windows;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

[STATestClass]
public sealed class TextRunStyleTests
{
	[TestMethod]
	public void Empty_RequestsNoFormatting()
	{
		Assert.IsFalse(TextRunStyle.Empty.HasFormatting);
		Assert.IsNull(TextRunStyle.Empty.Foreground);
		Assert.IsFalse(TextRunStyle.Empty.IsBold);
		Assert.IsFalse(TextRunStyle.Empty.IsItalic);
		Assert.IsNull(TextRunStyle.Empty.TextDecorations);
	}

	[TestMethod]
	public void HasFormatting_ReflectsAnyRequestedFormatting()
	{
		Assert.IsTrue(new TextRunStyle(Brushes.Red, false, false, null).HasFormatting);
		Assert.IsTrue(new TextRunStyle(null, true, false, null).HasFormatting);
		Assert.IsTrue(new TextRunStyle(null, false, true, null).HasFormatting);
		Assert.IsTrue(new TextRunStyle(null, false, false, TextDecorations.Strikethrough).HasFormatting);
		Assert.IsFalse(new TextRunStyle(null, false, false, null).HasFormatting);
	}

	[TestMethod]
	public void HasFormatting_EmptyTextDecorationCollection_RequestsNoFormatting()
	{
		// A non-null but empty collection requests nothing, so it must not count as formatting.
		Assert.IsFalse(new TextRunStyle(null, false, false, new TextDecorationCollection()).HasFormatting);
	}

	[TestMethod]
	public void CreateTypeface_AppliesBoldAndItalicAndPreservesBaseStyle()
	{
		var baseTypeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Regular, FontStretches.Normal);

		Typeface bold = new TextRunStyle(null, IsBold: true, IsItalic: false, null).CreateTypeface(baseTypeface);
		Typeface italic = new TextRunStyle(null, IsBold: false, IsItalic: true, null).CreateTypeface(baseTypeface);

		Assert.AreEqual(FontWeights.Bold, bold.Weight);
		Assert.AreEqual(FontStyles.Normal, bold.Style);
		Assert.AreEqual(FontWeights.Regular, italic.Weight);
		Assert.AreEqual(FontStyles.Italic, italic.Style);
	}

	[TestMethod]
	public void CreateTypeface_NullBaseTypeface_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => TextRunStyle.Empty.CreateTypeface(null!));
	}
}

using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using System.Windows;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

[STATestClass]
public sealed class TextRunStyleApplierTests
{
	private sealed record TestRunStyle(
		Brush? Foreground,
		bool IsBold,
		bool IsItalic,
		TextDecorationCollection? TextDecorations) : ITextRunStyle
	{
		public bool HasFormatting => Foreground is not null || IsBold || IsItalic || TextDecorations is { Count: > 0 };
	}

	[TestMethod]
	public void Apply_WithStructStyle_AppliesForegroundAndFontTraits()
	{
		// The struct style is applied through the generic overload: the empty style applies nothing, and
		// the populated style applies its foreground and font traits.
		TextEditor emptyEditor = WPFTestHost.CreateEditor("value = 1");
		emptyEditor.TextArea.TextView.LineTransformers.Add(new StructStyleApplyingTransformer(TextRunStyle.Empty));

		using (HostWindow emptyHostWindow = WPFTestHost.ShowInHostWindow(emptyEditor))
		{
			VisualLineElementTextRunProperties emptyProperties = GetFirstElementProperties(emptyEditor);

			Assert.AreEqual(FontWeights.Normal, emptyProperties.Typeface.Weight);
			Assert.AreEqual(FontStyles.Normal, emptyProperties.Typeface.Style);
		}

		TextEditor editor = WPFTestHost.CreateEditor("value = 1");
		editor.TextArea.TextView.LineTransformers.Add(
			new StructStyleApplyingTransformer(new TextRunStyle(Brushes.Red, IsBold: true, IsItalic: false, null)));

		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		VisualLineElementTextRunProperties properties = GetFirstElementProperties(editor);

		Assert.AreEqual(Brushes.Red, properties.ForegroundBrush);
		Assert.AreEqual(FontWeights.Bold, properties.Typeface.Weight);
	}

	private sealed class StructStyleApplyingTransformer(TextRunStyle style) : DocumentColorizingTransformer
	{
		protected override void ColorizeLine(DocumentLine line)
		{
			if (line.LineNumber != 1)
				return;

			ChangeLinePart(line.Offset, line.EndOffset, element => TextRunStyleApplier.Apply(element, style));
		}
	}

	private sealed class StyleApplyingTransformer(ITextRunStyle style, Typeface? typeface) : DocumentColorizingTransformer
	{
		protected override void ColorizeLine(DocumentLine line)
		{
			if (line.LineNumber != 1)
				return;

			if (typeface is null)
				ChangeLinePart(line.Offset, line.EndOffset, element => TextRunStyleApplier.Apply(element, style));
			else
				ChangeLinePart(line.Offset, line.EndOffset, element => TextRunStyleApplier.Apply(element, style, typeface));
		}
	}

	private sealed class SequentialStyleApplyingTransformer(ITextRunStyle first, ITextRunStyle second) : DocumentColorizingTransformer
	{
		protected override void ColorizeLine(DocumentLine line)
		{
			if (line.LineNumber != 1)
				return;

			ChangeLinePart(line.Offset, line.EndOffset, element =>
			{
				TextRunStyleApplier.Apply(element, first);
				TextRunStyleApplier.Apply(element, second);
			});
		}
	}

	[TestMethod]
	public void Apply_WithForeground_SetsForegroundBrush()
	{
		TextEditor editor = CreateEditorWithStyle(new TestRunStyle(Brushes.Red, false, false, null));
		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		Assert.AreEqual(Brushes.Red, GetFirstElementProperties(editor).ForegroundBrush);
	}

	[TestMethod]
	public void Apply_WithBoldAndItalic_AppliesBothFontTraits()
	{
		TextEditor editor = CreateEditorWithStyle(new TestRunStyle(null, true, true, null));
		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		VisualLineElementTextRunProperties properties = GetFirstElementProperties(editor);

		Assert.AreEqual(FontWeights.Bold, properties.Typeface.Weight);
		Assert.AreEqual(FontStyles.Italic, properties.Typeface.Style);
	}

	[TestMethod]
	public void Apply_WithBoldOnly_PreservesBaseFontStyle()
	{
		TextEditor editor = CreateEditorWithStyle(new TestRunStyle(null, true, false, null));
		editor.FontStyle = FontStyles.Oblique;

		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		VisualLineElementTextRunProperties properties = GetFirstElementProperties(editor);

		Assert.AreEqual(FontWeights.Bold, properties.Typeface.Weight);
		Assert.AreEqual(FontStyles.Oblique, properties.Typeface.Style);
	}

	[TestMethod]
	public void Apply_WithoutFontTraits_PreservesElementTypeface()
	{
		TextEditor editor = CreateEditorWithStyle(new TestRunStyle(Brushes.Red, false, false, null));

		// A non-default base style makes the assertion meaningful: an unconditional typeface overwrite
		// would replace the oblique style with the typeface default.
		editor.FontStyle = FontStyles.Oblique;

		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		VisualLineElementTextRunProperties properties = GetFirstElementProperties(editor);

		Assert.AreEqual(Brushes.Red, properties.ForegroundBrush);
		Assert.AreEqual(FontWeights.Normal, properties.Typeface.Weight);
		Assert.AreEqual(FontStyles.Oblique, properties.Typeface.Style);
	}

	[TestMethod]
	public void Apply_WithTextDecorations_AppliesDecorations()
	{
		TextEditor editor = CreateEditorWithStyle(new TestRunStyle(null, false, false, TextDecorations.Strikethrough));
		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		TextDecorationCollection? decorations = GetFirstElementProperties(editor).TextDecorations;

		Assert.IsNotNull(decorations);
		Assert.HasCount(1, decorations);
		Assert.AreEqual(TextDecorationLocation.Strikethrough, decorations[0].Location);
	}

	[TestMethod]
	public void Apply_WithTextDecorations_UnionsWithExistingDecorations()
	{
		TextEditor editor = WPFTestHost.CreateEditor("value = 1");
		editor.TextArea.TextView.LineTransformers.Add(new SequentialStyleApplyingTransformer(
			new TestRunStyle(null, false, false, TextDecorations.Underline),
			new TestRunStyle(null, false, false, TextDecorations.Strikethrough)));

		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		TextDecorationCollection? decorations = GetFirstElementProperties(editor).TextDecorations;

		Assert.IsNotNull(decorations);

		// AvalonEdit's SetTextDecorations unions with the decorations already present, so the second
		// apply must not discard the underline the first apply set.
		Assert.IsTrue(decorations.Any(decoration => decoration.Location == TextDecorationLocation.Underline));
		Assert.IsTrue(decorations.Any(decoration => decoration.Location == TextDecorationLocation.Strikethrough));
	}

	[TestMethod]
	public void Apply_WithEmptyTextDecorationCollection_LeavesDecorationsUnchanged()
	{
		TextEditor editor = CreateEditorWithStyle(new TestRunStyle(null, false, false, new TextDecorationCollection()));
		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		TextDecorationCollection? decorations = GetFirstElementProperties(editor).TextDecorations;

		// A non-null but empty collection requests nothing, so the applier must not touch the element's
		// existing decoration state.
		Assert.IsNull(decorations);
	}

	[TestMethod]
	public void Apply_WithProvidedTypeface_UsesTheProvidedTypefaceForFontTraits()
	{
		var providedTypeface = new Typeface(new FontFamily("Courier New"), FontStyles.Italic, FontWeights.Bold, FontStretches.Condensed);

		TextEditor editor = CreateEditorWithStyle(new TestRunStyle(null, true, false, null), providedTypeface);
		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		VisualLineElementTextRunProperties properties = GetFirstElementProperties(editor);

		Assert.AreEqual("Courier New", properties.Typeface.FontFamily.Source);
		Assert.AreEqual(FontWeights.Bold, properties.Typeface.Weight);
		Assert.AreEqual(FontStyles.Italic, properties.Typeface.Style);
		Assert.AreEqual(FontStretches.Condensed, properties.Typeface.Stretch);
	}

	[TestMethod]
	public void Apply_WithProvidedTypeface_WhenStyleHasNoFontTraits_PreservesElementTypeface()
	{
		var providedTypeface = new Typeface(new FontFamily("Courier New"), FontStyles.Italic, FontWeights.Bold, FontStretches.Condensed);

		TextEditor editor = CreateEditorWithStyle(new TestRunStyle(Brushes.Red, false, false, null), providedTypeface);

		// A controlled base family and stretch make the assertion exact: the provided typeface must not
		// be applied, and the element typeface must survive field by field.
		editor.FontFamily = new FontFamily("Times New Roman");
		editor.FontStretch = FontStretches.Expanded;

		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		VisualLineElementTextRunProperties properties = GetFirstElementProperties(editor);

		Assert.AreEqual(Brushes.Red, properties.ForegroundBrush);
		Assert.AreEqual("Times New Roman", properties.Typeface.FontFamily.Source);
		Assert.AreEqual(FontWeights.Normal, properties.Typeface.Weight);
		Assert.AreEqual(FontStretches.Expanded, properties.Typeface.Stretch);
	}

	[TestMethod]
	public void Apply_WithNullElement_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(
			() => TextRunStyleApplier.Apply(null!, new TestRunStyle(Brushes.Red, false, false, null)));
	}

	[TestMethod]
	public void Apply_WithNullStyle_Throws()
	{
		TextEditor editor = WPFTestHost.CreateEditor("value = 1");
		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		VisualLineElement element = GetFirstElement(editor);

		Assert.ThrowsExactly<ArgumentNullException>(() => TextRunStyleApplier.Apply(element, null!));
	}

	[TestMethod]
	public void Apply_WithNullTypeface_Throws()
	{
		TextEditor editor = WPFTestHost.CreateEditor("value = 1");
		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		VisualLineElement element = GetFirstElement(editor);

		Assert.ThrowsExactly<ArgumentNullException>(
			() => TextRunStyleApplier.Apply(element, new TestRunStyle(Brushes.Red, false, false, null), null!));
	}

	[TestMethod]
	public void CreateTypeface_WithBoldAndItalic_AppliesTraitsAndPreservesBaseFamilyAndStretch()
	{
		var baseTypeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Regular, FontStretches.Condensed);

		Typeface bold = TextRunStyleApplier.CreateTypeface(baseTypeface, new TestRunStyle(null, true, false, null));
		Typeface italic = TextRunStyleApplier.CreateTypeface(baseTypeface, new TestRunStyle(null, false, true, null));

		Assert.AreEqual(FontWeights.Bold, bold.Weight);
		Assert.AreEqual(FontStyles.Normal, bold.Style);
		Assert.AreEqual(FontWeights.Regular, italic.Weight);
		Assert.AreEqual(FontStyles.Italic, italic.Style);
		Assert.AreEqual(baseTypeface.FontFamily, bold.FontFamily);
		Assert.AreEqual(FontStretches.Condensed, bold.Stretch);
	}

	[TestMethod]
	public void CreateTypeface_WithoutFontTraits_PreservesBaseTypeface()
	{
		var baseTypeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Oblique, FontWeights.Light, FontStretches.Expanded);

		Typeface derived = TextRunStyleApplier.CreateTypeface(baseTypeface, new TestRunStyle(null, false, false, null));

		Assert.AreEqual(baseTypeface.FontFamily, derived.FontFamily);
		Assert.AreEqual(baseTypeface.Style, derived.Style);
		Assert.AreEqual(baseTypeface.Weight, derived.Weight);
		Assert.AreEqual(baseTypeface.Stretch, derived.Stretch);
	}

	[TestMethod]
	public void CreateTypeface_WithNullBaseTypeface_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(
			() => TextRunStyleApplier.CreateTypeface(null!, new TestRunStyle(null, false, false, null)));
	}

	[TestMethod]
	public void CreateTypeface_WithNullStyle_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(
			() => TextRunStyleApplier.CreateTypeface(new Typeface("Segoe UI"), null!));
	}

	private static TextEditor CreateEditorWithStyle(ITextRunStyle style, Typeface? typeface = null)
	{
		TextEditor editor = WPFTestHost.CreateEditor("value = 1");
		editor.TextArea.TextView.LineTransformers.Add(new StyleApplyingTransformer(style, typeface));

		return editor;
	}

	private static VisualLineElement GetFirstElement(TextEditor editor)
	{
		editor.TextArea.TextView.EnsureVisualLines();

		VisualLine visualLine = editor.TextArea.TextView.GetVisualLine(1)
			?? throw new InvalidOperationException("The first visual line is not available.");

		foreach (VisualLineElement element in visualLine.Elements)
		{
			if (element.DocumentLength > 0)
				return element;
		}

		throw new InvalidOperationException("The first visual line has no element with document content.");
	}

	private static VisualLineElementTextRunProperties GetFirstElementProperties(TextEditor editor)
		=> GetFirstElement(editor).TextRunProperties;
}

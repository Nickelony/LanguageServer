using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.Core.Indentation;

namespace Nickelony.IDEKit.AvalonEdit.Indentation.Tests;

[TestClass]
public sealed class PolicyIndentationStrategyTests
{
	private sealed class TestPolicy : IIndentationPolicy
	{
		public string GetDesiredIndentation(in IndentationContext context)
		{
			string indentation = context.PreviousLineIndentation;

			if (context.UseSmartIndent && context.PreviousLineText.TrimEnd().EndsWith("then"))
				indentation += context.IndentationUnit;

			if (context.UseSmartIndent && context.CurrentLineText.TrimStart().StartsWith("end"))
				indentation = IndentationTextHelper.RemoveSingleIndentLevel(indentation, context.IndentationUnit);

			return indentation;
		}
	}

	private static PolicyIndentationStrategy CreateStrategy(Func<TextDocument, DocumentLine, bool>? shouldUseSmartIndent = null) => new(
		new TextEditorOptions
		{
			ConvertTabsToSpaces = true,
			IndentationSize = 4
		},
		new TestPolicy(),
		shouldUseSmartIndent);

	[TestMethod]
	public void IndentLine_AfterThen_AddsIndentToNewLine()
	{
		PolicyIndentationStrategy strategy = CreateStrategy();
		var document = new TextDocument("if value then\r\n");

		strategy.IndentLine(document, document.GetLineByNumber(2));

		Assert.AreEqual("if value then\r\n    ", document.Text);
	}

	[TestMethod]
	public void IndentLine_BeforeEnd_DedentsCurrentLineWithoutExtraInsertion()
	{
		PolicyIndentationStrategy strategy = CreateStrategy();
		var document = new TextDocument("if value then\r\n end");

		strategy.IndentLine(document, document.GetLineByNumber(2));

		Assert.AreEqual("if value then\r\nend", document.Text);
	}

	[TestMethod]
	public void IndentLine_AlreadyCorrect_DoesNotModifyDocument()
	{
		PolicyIndentationStrategy strategy = CreateStrategy();
		var document = new TextDocument("if value then\r\n    ");

		strategy.IndentLine(document, document.GetLineByNumber(2));

		Assert.AreEqual("if value then\r\n    ", document.Text);
	}

	[TestMethod]
	public void IndentLine_SmartIndentPredicateFalse_KeepsPreviousIndentation()
	{
		PolicyIndentationStrategy strategy = CreateStrategy((_, _) => false);
		var document = new TextDocument("if value then\r\n");

		strategy.IndentLine(document, document.GetLineByNumber(2));

		Assert.AreEqual("if value then\r\n", document.Text);
	}

	[TestMethod]
	public void IndentLines_IndentsEachLineInRangeSequentially()
	{
		PolicyIndentationStrategy strategy = CreateStrategy();
		var document = new TextDocument("if a then\r\nx\r\nif b then\r\ny");

		strategy.IndentLines(document, 2, 4);

		Assert.AreEqual("if a then\r\n    x\r\n    if b then\r\n        y", document.Text);
	}

	[TestMethod]
	public void IndentLines_EmptyDocument_DoesNothing()
	{
		PolicyIndentationStrategy strategy = CreateStrategy();
		var document = new TextDocument();

		strategy.IndentLines(document, 1, 10);

		Assert.AreEqual(string.Empty, document.Text);
	}
}

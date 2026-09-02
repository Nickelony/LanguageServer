using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Indentation;
using Nickelony.IDEKit.Core.Indentation;

namespace Nickelony.IDEKit.AvalonEdit.Indentation;

/// <summary>
/// An <see cref="IIndentationStrategy"/> that computes indentation by delegating to an
/// <see cref="IIndentationPolicy"/>, with an optional document-aware smart-indent predicate.
/// </summary>
/// <remarks>
/// The first line retains its existing leading whitespace because no previous line is available.
/// For a range, line numbers outside the document are clamped and all replacements are grouped in
/// one document update.
/// </remarks>
public sealed class PolicyIndentationStrategy : IIndentationStrategy
{
	private readonly TextEditorOptions _options;
	private readonly IIndentationPolicy _policy;
	private readonly Func<TextDocument, DocumentLine, bool>? _shouldUseSmartIndent;

	/// <summary>
	/// Initializes a new instance of the <see cref="PolicyIndentationStrategy"/> class.
	/// </summary>
	/// <param name="options">The editor options that determine the indentation unit.</param>
	/// <param name="policy">The policy that computes the desired indentation.</param>
	/// <param name="shouldUseSmartIndent">
	/// An optional predicate that decides whether smart indent applies for a previous line.
	/// When omitted, smart indent always applies.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="options"/> or <paramref name="policy"/> is null.
	/// </exception>
	public PolicyIndentationStrategy(
		TextEditorOptions options,
		IIndentationPolicy policy,
		Func<TextDocument, DocumentLine, bool>? shouldUseSmartIndent = null)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(policy);

		_options = options;
		_policy = policy;
		_shouldUseSmartIndent = shouldUseSmartIndent;
	}

	/// <inheritdoc/>
	public void IndentLine(TextDocument document, DocumentLine line)
	{
		string lineText = document.GetText(line);
		string desiredIndentation = GetDesiredIndentation(document, line, lineText);
		ReplaceLeadingWhitespace(document, line, lineText, desiredIndentation);
	}

	/// <inheritdoc/>
	public void IndentLines(TextDocument document, int beginLine, int endLine)
	{
		if (document.LineCount == 0)
			return;

		int startLine = Math.Max(1, Math.Min(beginLine, document.LineCount));
		int lastLine = Math.Max(startLine, Math.Min(endLine, document.LineCount));

		document.BeginUpdate();

		try
		{
			for (int lineNumber = startLine; lineNumber <= lastLine; lineNumber++)
				IndentLine(document, document.GetLineByNumber(lineNumber));
		}
		finally
		{
			document.EndUpdate();
		}
	}

	private string GetDesiredIndentation(TextDocument document, DocumentLine line, string lineText)
	{
		if (line.PreviousLine is null)
			return IndentationTextHelper.GetLeadingWhitespace(lineText);

		DocumentLine previousLine = line.PreviousLine;
		string previousLineText = document.GetText(previousLine);
		string previousLineIndentation = IndentationTextHelper.GetLeadingWhitespace(previousLineText);

		return _policy.GetDesiredIndentation(new IndentationContext(
			previousLineText,
			lineText,
			previousLineIndentation,
			IndentationTextHelper.CreateIndentationUnit(
				_options.ConvertTabsToSpaces,
				_options.IndentationSize,
				_options.IndentationSize),
			ShouldUseSmartIndent(document, previousLine)));
	}

	private bool ShouldUseSmartIndent(TextDocument document, DocumentLine previousLine)
		=> _shouldUseSmartIndent is null || _shouldUseSmartIndent(document, previousLine);

	private static void ReplaceLeadingWhitespace(
		TextDocument document,
		DocumentLine line,
		string lineText,
		string desiredIndentation)
	{
		int leadingWhitespaceLength = IndentationTextHelper.GetLeadingWhitespaceLength(lineText);

		if (leadingWhitespaceLength == desiredIndentation.Length
			&& string.CompareOrdinal(lineText, 0, desiredIndentation, 0, leadingWhitespaceLength) == 0)
		{
			return;
		}

		document.Replace(line.Offset, leadingWhitespaceLength, desiredIndentation);
	}
}

namespace Nickelony.IDEKit.Core.Comments;

/// <summary>
/// Provides comment-aware line-level syntax helpers driven by a <see cref="CommentSyntax"/>.
/// </summary>
/// <remarks>
/// This is the shared base for language line services that strip, mask, or detect line-comment
/// lines. The comment delimiter, string-literal awareness, and block-comment delimiters are
/// supplied by the <see cref="CommentSyntax"/>; the helpers themselves are syntax-agnostic.
/// </remarks>
public class TextLineSyntaxService
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextLineSyntaxService"/> class.
	/// </summary>
	/// <param name="syntax">The comment syntax of the language.</param>
	public TextLineSyntaxService(CommentSyntax syntax)
	{
		Syntax = syntax;
	}

	/// <summary>
	/// Gets the comment syntax this service is configured with.
	/// </summary>
	protected CommentSyntax Syntax { get; }

	/// <summary>
	/// Removes comments from the supplied line text.
	/// </summary>
	/// <param name="lineText">The line text to process.</param>
	/// <returns>The line text with comment content removed.</returns>
	public string RemoveComments(string lineText)
		=> CommentHelper.RemoveComments(lineText, Syntax);

	/// <summary>
	/// Masks comments in the supplied line text so comment delimiters do not interfere with
	/// downstream processing.
	/// </summary>
	/// <param name="lineText">The line text to process.</param>
	/// <returns>The line text with comment content masked.</returns>
	public string EscapeComments(string lineText)
		=> CommentHelper.MaskComments(lineText, Syntax);

	/// <summary>
	/// Returns whether the supplied line is empty or starts with the configured line-comment delimiter.
	/// </summary>
	/// <param name="lineText">The line text to inspect, or <see langword="null"/>.</param>
	/// <returns><see langword="true"/> when the line is blank or a comment; otherwise, <see langword="false"/>.</returns>
	public bool IsEmptyOrComments(string? lineText)
	{
		if (string.IsNullOrWhiteSpace(lineText))
			return true;

		string? delimiter = Syntax.LineCommentDelimiter;

		return delimiter is not null && lineText.TrimStart().StartsWith(delimiter, StringComparison.Ordinal);
	}
}

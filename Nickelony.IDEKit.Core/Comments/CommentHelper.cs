using System.Text;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Comments;

/// <summary>
/// Provides unified comment utilities for languages with line comments, block comments, or both.
/// </summary>
public static class CommentHelper
{
	/// <summary>
	/// Returns the first comment (line or block) in the text, or <see langword="null"/>
	/// when the text contains no comments.
	/// </summary>
	/// <param name="text">The text to search. May contain multiple lines.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	/// <returns>The first comment span, or <see langword="null"/> if no comment is found.</returns>
	public static CommentSpan? FindComment(ReadOnlySpan<char> text, CommentSyntax syntax)
	{
		var enumerator = new CommentSpanEnumerator(text, syntax);
		return enumerator.MoveNext() ? enumerator.Current : null;
	}

	/// <summary>
	/// Returns the first line comment in the text, or <see langword="null"/> when the
	/// text contains no line comments. Block comments are skipped.
	/// </summary>
	/// <param name="text">The text to search. May contain multiple lines.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	/// <returns>The first line-comment span, or <see langword="null"/> if none is found.</returns>
	public static CommentSpan? FindLineComment(ReadOnlySpan<char> text, CommentSyntax syntax)
	{
		var enumerator = new CommentSpanEnumerator(text, syntax);

		while (enumerator.MoveNext())
		{
			if (enumerator.Current.IsLineComment)
				return enumerator.Current;
		}

		return null;
	}

	/// <summary>
	/// Returns the first block comment in the text, or <see langword="null"/> when the
	/// text contains no block comments. Line comments are skipped.
	/// </summary>
	/// <param name="text">The text to search. May contain multiple lines.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	/// <returns>The first block-comment span, or <see langword="null"/> if none is found.</returns>
	public static CommentSpan? FindBlockComment(ReadOnlySpan<char> text, CommentSyntax syntax)
	{
		var enumerator = new CommentSpanEnumerator(text, syntax);

		while (enumerator.MoveNext())
		{
			if (enumerator.Current.IsBlockComment)
				return enumerator.Current;
		}

		return null;
	}

	/// <summary>
	/// Enumerates all comments in the text in a single forward pass.
	/// </summary>
	/// <param name="text">The text to scan. May contain multiple lines.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	/// <returns>An enumerator over each comment span in the text.</returns>
	public static CommentSpanEnumerator EnumerateComments(ReadOnlySpan<char> text, CommentSyntax syntax)
		=> new(text, syntax);

	/// <summary>
	/// Gets the code range for the given text, excluding comments. For a line comment,
	/// whitespace immediately before the delimiter belongs to the comment span and is
	/// therefore excluded; for a block comment, the range ends at the opener. If no
	/// comment is found, the range covers the entire text.
	/// </summary>
	/// <param name="text">The text to evaluate. May contain multiple lines.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	/// <returns>A <see cref="TextRange"/> covering the code portion of the text before the first comment.</returns>
	public static TextRange GetCodeRange(ReadOnlySpan<char> text, CommentSyntax syntax)
	{
		if (FindComment(text, syntax) is { } comment)
			return new(0, comment.Start);

		return new(0, text.Length);
	}

	/// <summary>
	/// Finds the end of the code portion of the text: the offset one past the last
	/// character that is not inside a string literal or comment, ignoring trailing
	/// whitespace. Used to test whether a line ends with a continuation marker.
	/// </summary>
	/// <param name="text">The text to evaluate. Typically a single line.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	/// <returns>The zero-based offset one past the last non-whitespace code character.</returns>
	public static int GetCodeEnd(ReadOnlySpan<char> text, CommentSyntax syntax)
	{
		var scanner = new CommentScanner(text, 0, syntax.StringStyle, syntax.LineCommentDelimiter, syntax.BlockCommentOpen, syntax.BlockCommentClose, syntax.AllowNestedBlockComments);
		int codeEnd = 0;

		while (scanner.MoveNext())
		{
			if (scanner.IsInCode && !char.IsWhiteSpace(text[scanner.CurrentIndex]))
				codeEnd = scanner.CurrentIndex + 1;
		}

		return codeEnd;
	}

	/// <summary>
	/// Removes all comments from the text. For a line comment the span from the
	/// whitespace before the delimiter through the line break (exclusive) is removed,
	/// so a comment-only line consumes its preceding line ending; for a block comment
	/// the span from the opener through the closer (or the end of the text when
	/// unclosed) is removed. Surrounding whitespace of block comments is left untouched.
	/// </summary>
	/// <param name="text">The text to process. May contain multiple lines.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	/// <returns>The text with all comments removed.</returns>
	public static string RemoveComments(string text, CommentSyntax syntax)
	{
		ArgumentNullException.ThrowIfNull(text);
		return TransformComments(text, syntax, maskComments: false, includeLineComments: true);
	}

	/// <summary>
	/// Masks all comments with spaces, preserving the total string length. LF characters inside
	/// block comments are preserved; line-comment spans are replaced entirely with spaces. Other
	/// line-ending characters inside block comments are replaced with spaces.
	/// </summary>
	/// <param name="text">The text to process. May contain multiple lines.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	/// <returns>The text with comment spans replaced by spaces.</returns>
	public static string MaskComments(string text, CommentSyntax syntax)
	{
		ArgumentNullException.ThrowIfNull(text);
		return TransformComments(text, syntax, maskComments: true, includeLineComments: true);
	}

	/// <summary>
	/// Removes all block comments from the text, leaving line comments untouched. For a
	/// block comment the span from the opener through the closer (or the end of the text
	/// when unclosed) is removed; surrounding whitespace is left untouched. Line comments
	/// still participate in scanning, so a block opener inside a line comment is not
	/// recognized.
	/// </summary>
	/// <param name="text">The text to process. May contain multiple lines.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	/// <returns>The text with all block comments removed.</returns>
	public static string RemoveBlockComments(string text, CommentSyntax syntax)
	{
		ArgumentNullException.ThrowIfNull(text);
		return TransformComments(text, syntax, maskComments: false, includeLineComments: false);
	}

	/// <summary>
	/// Masks all block comments with spaces, leaving line comments untouched, and
	/// preserving the total string length and LF characters inside block comments. Other
	/// line-ending characters are replaced with spaces. Line comments still participate in scanning, so
	/// a block opener inside a line comment is not recognized.
	/// </summary>
	/// <param name="text">The text to process. May contain multiple lines.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	/// <returns>The text with comment spans replaced by spaces.</returns>
	public static string MaskBlockComments(string text, CommentSyntax syntax)
	{
		ArgumentNullException.ThrowIfNull(text);
		return TransformComments(text, syntax, maskComments: true, includeLineComments: false);
	}

	// Transforms the text by removing or masking comment spans. When includeLineComments
	// is false, line comments are left untouched but still participate in scanning, so
	// block openers inside them are not recognized (used by RemoveBlockComments and
	// MaskBlockComments, which remove only block comments).
	internal static string TransformComments(string text, CommentSyntax syntax, bool maskComments, bool includeLineComments)
	{
		if (text.Length == 0)
			return string.Empty;

		var result = new StringBuilder(text.Length);

		int sourceOffset = 0; // The first character that has not been copied or transformed.

		var enumerator = new CommentSpanEnumerator(text, syntax);

		while (enumerator.MoveNext())
		{
			CommentSpan span = enumerator.Current;

			if (!includeLineComments && span.IsLineComment)
				continue;

			// Copy text before the comment; the comment itself is omitted or masked below.
			result.Append(text, sourceOffset, span.Start - sourceOffset);

			if (maskComments)
			{
				// Line comments mask everything to spaces, including a line ending that
				// precedes a comment-only line (matching the legacy line-comment behavior);
				// Block comments preserve LF characters; other characters, including CR,
				// are masked with spaces.
				AppendMasked(result, text.AsSpan(span.Start, span.Length), preserveLineBreaks: !span.IsLineComment);
			}

			// Resume after the comment span.
			sourceOffset = span.End;
		}

		result.Append(text, sourceOffset, text.Length - sourceOffset);
		return result.ToString();
	}

	// Masks a comment span with spaces, optionally preserving line breaks.
	private static void AppendMasked(StringBuilder builder, ReadOnlySpan<char> span, bool preserveLineBreaks)
	{
		for (int i = 0; i < span.Length; i++)
			builder.Append(preserveLineBreaks && span[i] == '\n' ? '\n' : ' ');
	}
}

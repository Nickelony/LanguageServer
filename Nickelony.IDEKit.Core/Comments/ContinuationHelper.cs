namespace Nickelony.IDEKit.Core.Comments;

/// <summary>
/// Provides continuation-marker utilities for languages that use a single-character
/// or multi-character continuation marker at the end of a line (e.g. <c>_</c> in
/// Visual Basic or <c>...</c> in MATLAB).
/// </summary>
public static class ContinuationHelper
{
	/// <summary>
	/// Determines whether the given code span ends with a valid single-character
	/// continuation marker, ignoring trailing comments and whitespace.
	/// </summary>
	/// <param name="text">The line text, potentially including a trailing comment.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	/// <param name="continuationMarker">The continuation marker character, e.g. <c>'_'</c>.</param>
	/// <returns>
	/// <see langword="true"/> if the code portion of the line ends with the continuation marker
	/// (ignoring trailing whitespace); otherwise <see langword="false"/>.
	/// </returns>
	/// <example>
	/// Visual Basic uses <c>_</c> at the end of a line to continue a statement onto the
	/// next line:
	/// <code>
	/// bool continues = ContinuationHelper.IsValidContinuation(
	///     "Dim total As Integer = 1 + _",
	///     new CommentSyntax("'", null, null, StringLiteralStyle.None),
	///     '_');
	/// </code>
	/// </example>
	public static bool IsValidContinuation(
		ReadOnlySpan<char> text,
		CommentSyntax syntax,
		char continuationMarker)
	{
		return IsValidContinuation(text, syntax, new ReadOnlySpan<char>(in continuationMarker));
	}

	/// <summary>
	/// Determines whether the given code span ends with a valid continuation marker,
	/// which may be a single character or a multi-character sequence, ignoring
	/// trailing comments and whitespace.
	/// </summary>
	/// <param name="text">The line text, potentially including a trailing comment.</param>
	/// <param name="syntax">The comment syntax of the language.</param>
	/// <param name="continuationMarker">The continuation marker, e.g. <c>"..."</c> for MATLAB.</param>
	/// <returns>
	/// <see langword="true"/> if the code portion of the line ends with the continuation marker
	/// (ignoring trailing whitespace and comments); otherwise <see langword="false"/>.
	/// </returns>
	/// <example>
	/// MATLAB uses <c>...</c> at the end of a line to continue a statement onto the
	/// next line:
	/// <code>
	/// bool continues = ContinuationHelper.IsValidContinuation(
	///     "total = 1 + 2 + ...",
	///     new CommentSyntax("%", null, null, StringLiteralStyle.None),
	///     "...");
	/// </code>
	/// </example>
	public static bool IsValidContinuation(
		ReadOnlySpan<char> text,
		CommentSyntax syntax,
		ReadOnlySpan<char> continuationMarker)
	{
		int codeEnd = CommentHelper.GetCodeEnd(text, syntax);

		return continuationMarker.Length > 0
			&& text[..codeEnd].EndsWith(continuationMarker, StringComparison.Ordinal);
	}
}

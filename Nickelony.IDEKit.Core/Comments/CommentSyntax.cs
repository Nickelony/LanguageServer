namespace Nickelony.IDEKit.Core.Comments;

/// <summary>
/// Describes the comment syntax of a language: the line-comment delimiter, the
/// optional block-comment delimiters, whether block comments may nest, and which
/// string-literal styles are recognized so a delimiter inside string content is not
/// treated as a comment start. Bundle the delimiters into one value and pass it to
/// <see cref="CommentHelper"/> instead of threading each delimiter as a separate
/// parameter.
/// </summary>
public readonly record struct CommentSyntax
{
	/// <summary>
	/// Gets the line-comment delimiter, such as <c>"//"</c>, <c>"--"</c>, or <c>";"</c>,
	/// or <see langword="null"/> when the language has no line comments.
	/// </summary>
	public string? LineCommentDelimiter { get; }

	/// <summary>
	/// Gets the block-comment opener, such as <c>"/*"</c> or <c>"--[["</c>, or
	/// <see langword="null"/> when the language has no block comments.
	/// </summary>
	public string? BlockCommentOpen { get; }

	/// <summary>
	/// Gets the block-comment closer, such as <c>"*/"</c> or <c>"]]"</c>, or
	/// <see langword="null"/> when the language has no block comments.
	/// </summary>
	public string? BlockCommentClose { get; }

	/// <summary>
	/// Gets the string-literal styles recognized while scanning, so a delimiter inside
	/// string content is not treated as a comment start.
	/// </summary>
	public StringLiteralStyle StringStyle { get; }

	/// <summary>
	/// Gets a value indicating whether an opener inside a block comment increases the
	/// nesting depth so the comment closes only after the matching final closer.
	/// Defaults to <see langword="false"/>, matching languages such as C where the
	/// first closer ends the comment.
	/// </summary>
	public bool AllowNestedBlockComments { get; }

	/// <summary>
	/// Initializes a new instance of the <see cref="CommentSyntax"/> struct. An empty
	/// or <see langword="null"/> line delimiter disables line-comment awareness, and
	/// an empty or <see langword="null"/> block-comment opener or closer disables
	/// block-comment awareness entirely (both delimiters must be present).
	/// </summary>
	/// <param name="lineCommentDelimiter">The line-comment delimiter, or <see langword="null"/> for none.</param>
	/// <param name="blockCommentOpen">The block-comment opener, or <see langword="null"/> for none.</param>
	/// <param name="blockCommentClose">The block-comment closer, or <see langword="null"/> for none.</param>
	/// <param name="stringStyle">The string-literal styles to recognize while scanning.</param>
	/// <param name="allowNestedBlockComments">Whether block comments may nest.</param>
	public CommentSyntax(
		string? lineCommentDelimiter,
		string? blockCommentOpen,
		string? blockCommentClose,
		StringLiteralStyle stringStyle,
		bool allowNestedBlockComments = false)
	{
		// Empty delimiters disable the corresponding comment kind.
		lineCommentDelimiter = string.IsNullOrEmpty(lineCommentDelimiter) ? null : lineCommentDelimiter;
		blockCommentOpen = string.IsNullOrEmpty(blockCommentOpen) ? null : blockCommentOpen;
		blockCommentClose = string.IsNullOrEmpty(blockCommentClose) ? null : blockCommentClose;

		// Both block delimiters are required together; when either is absent the
		// language has no block comments.
		if (blockCommentOpen is null)
			blockCommentClose = null;
		else if (blockCommentClose is null)
			blockCommentOpen = null;

		LineCommentDelimiter = lineCommentDelimiter;
		BlockCommentOpen = blockCommentOpen;
		BlockCommentClose = blockCommentClose;
		StringStyle = stringStyle;
		AllowNestedBlockComments = allowNestedBlockComments;
	}
}

namespace Nickelony.IDEKit.Core.Indentation;

/// <summary>
/// Describes the inputs required to normalize a multiline completion insertion.
/// </summary>
/// <param name="Text">The text being inserted by completion.</param>
/// <param name="CaretOffset">The caret offset within the inserted text, if known.</param>
/// <param name="CurrentLineIndentation">The leading whitespace of the line receiving the insertion.</param>
/// <param name="IndentationUnit">The text appended for one additional indent level.</param>
public readonly record struct CompletionInsertionContext(
	string Text,
	int? CaretOffset,
	string CurrentLineIndentation,
	string IndentationUnit);

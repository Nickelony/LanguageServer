namespace Nickelony.IDEKit.Core.Indentation;

/// <summary>
/// Describes the inputs required to compute the desired indentation for one line.
/// </summary>
/// <param name="PreviousLineText">The full text of the previous line.</param>
/// <param name="CurrentLineText">The full text of the line being indented.</param>
/// <param name="PreviousLineIndentation">The leading whitespace of the previous line.</param>
/// <param name="IndentationUnit">The text appended for one additional indent level.</param>
/// <param name="UseSmartIndent">Whether language-aware smart-indent rules should apply.</param>
public readonly record struct IndentationContext(
	string PreviousLineText,
	string CurrentLineText,
	string PreviousLineIndentation,
	string IndentationUnit,
	bool UseSmartIndent);

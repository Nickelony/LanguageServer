using System.Diagnostics.CodeAnalysis;
using System.Text;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Documents;
using Nickelony.IDEKit.Core.Comments;

namespace Nickelony.IDEKit.AvalonEdit.Comments;

/// <summary>
/// Applies line-comment transformations (comment, uncomment, toggle) to the selected lines of an
/// AvalonEdit document, using the line-comment delimiter from an explicit <see cref="CommentSyntax"/>.
/// </summary>
/// <remarks>
/// The service preserves each line's leading whitespace and leaves whitespace-only lines unchanged.
/// A selection is expanded to every line it touches, and the replacement appends the platform line
/// terminator after every selected line, including a final line that had no terminator.
/// </remarks>
[SuppressMessage(
	"Performance",
	"CA1822:Mark members as static",
	Justification = "The service is created per editor as part of the editor service composition.")]
public sealed class TextLineCommentService
{
	/// <summary>
	/// Creates an edit that comments, uncomments, or toggles commenting on the selected lines.
	/// </summary>
	/// <param name="document">The document the selection belongs to.</param>
	/// <param name="selectionStart">The zero-based start offset of the selection.</param>
	/// <param name="selectionLength">The length of the selection.</param>
	/// <param name="commentSyntax">The comment syntax whose line-comment delimiter is applied.</param>
	/// <param name="action">The transformation to apply.</param>
	/// <param name="edit">The created edit, when a transformation applies.</param>
	/// <returns>
	/// <see langword="true"/> when the document is non-empty and has a line-comment delimiter;
	/// otherwise, <see langword="false"/>.
	/// </returns>
	public bool TryCreateEdit(
		TextDocument document,
		int selectionStart,
		int selectionLength,
		CommentSyntax commentSyntax,
		TextLineCommentAction action,
		out TextLineCommentEdit edit)
	{
		edit = default;

		string? commentPrefix = commentSyntax.LineCommentDelimiter;

		if (string.IsNullOrWhiteSpace(commentPrefix) || document.TextLength == 0)
			return false;

		int safeSelectionStart = document.ClampOffset(selectionStart);
		int safeSelectionEnd = Math.Max(safeSelectionStart, document.ClampOffset(selectionStart + selectionLength));

		DocumentLine startLine = document.GetLineByOffset(safeSelectionStart);
		DocumentLine endLine = document.GetLineByOffset(safeSelectionEnd);

		TextLineCommentAction effectiveAction = action;

		if (action == TextLineCommentAction.Toggle)
		{
			effectiveAction = ShouldUncommentSelectedLines(document, startLine, endLine, commentPrefix)
				? TextLineCommentAction.Uncomment
				: TextLineCommentAction.Comment;
		}

		int totalLineLength = 0;
		var builder = new StringBuilder();

		for (int lineNumber = startLine.LineNumber; lineNumber <= endLine.LineNumber; lineNumber++)
		{
			DocumentLine currentLine = document.GetLineByNumber(lineNumber);
			string currentLineText = document.GetText(currentLine.Offset, currentLine.Length);

			builder.AppendLine(TransformLine(currentLineText, commentPrefix, effectiveAction));
			totalLineLength += currentLine.TotalLength;
		}

		string replacementText = builder.ToString();
		edit = new TextLineCommentEdit(
			startLine.Offset,
			totalLineLength,
			replacementText,
			startLine.Offset,
			Math.Max(0, replacementText.Length - 1));

		return true;
	}

	/// <summary>
	/// Applies a line-comment transformation to the editor's current selection.
	/// </summary>
	/// <param name="editor">The editor whose selection is transformed.</param>
	/// <param name="commentSyntax">The comment syntax whose line-comment delimiter is applied.</param>
	/// <param name="action">The transformation to apply.</param>
	public void ApplyEdit(
		ICSharpCode.AvalonEdit.TextEditor editor,
		CommentSyntax commentSyntax,
		TextLineCommentAction action)
	{
		if (!TryCreateEdit(
			editor.Document,
			editor.SelectionStart,
			editor.SelectionLength,
			commentSyntax,
			action,
			out TextLineCommentEdit edit))
		{
			return;
		}

		editor.Select(edit.ReplaceOffset, edit.ReplaceLength);
		editor.SelectedText = edit.ReplacementText;
		editor.Select(edit.SelectionStart, edit.SelectionLength);
	}

	private static bool ShouldUncommentSelectedLines(
		TextDocument document,
		DocumentLine startLine,
		DocumentLine endLine,
		string commentPrefix)
	{
		bool foundCommentableLine = false;

		for (int lineNumber = startLine.LineNumber; lineNumber <= endLine.LineNumber; lineNumber++)
		{
			DocumentLine currentLine = document.GetLineByNumber(lineNumber);
			string currentLineText = document.GetText(currentLine.Offset, currentLine.Length);
			string trimmedLineText = currentLineText.TrimStart();

			if (string.IsNullOrWhiteSpace(trimmedLineText))
				continue;

			foundCommentableLine = true;

			if (!trimmedLineText.StartsWith(commentPrefix, StringComparison.Ordinal))
				return false;
		}

		return foundCommentableLine;
	}

	private static string TransformLine(string currentLineText, string commentPrefix, TextLineCommentAction action)
		=> action == TextLineCommentAction.Uncomment
			? UncommentLine(currentLineText, commentPrefix)
			: CommentLine(currentLineText, commentPrefix);

	private static string CommentLine(string currentLineText, string commentPrefix)
	{
		string leadingWhitespace = GetLeadingWhitespace(currentLineText);

		return !string.IsNullOrWhiteSpace(currentLineText)
			? leadingWhitespace + commentPrefix + currentLineText.TrimStart()
			: leadingWhitespace;
	}

	private static string UncommentLine(string currentLineText, string commentPrefix)
	{
		string leadingWhitespace = GetLeadingWhitespace(currentLineText);
		string trimmedLineText = currentLineText.TrimStart();

		return trimmedLineText.StartsWith(commentPrefix, StringComparison.Ordinal)
			? leadingWhitespace + trimmedLineText[commentPrefix.Length..]
			: currentLineText;
	}

	private static string GetLeadingWhitespace(string currentLineText)
	{
		var whitespaceBuilder = new StringBuilder();

		for (int index = 0; index < currentLineText.Length; index++)
		{
			char character = currentLineText[index];

			if (char.IsWhiteSpace(character))
				whitespaceBuilder.Append(character);
			else
				break;
		}

		return whitespaceBuilder.ToString();
	}
}

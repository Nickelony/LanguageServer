using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Documents;
using Nickelony.IDEKit.Core.Comments;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Nickelony.IDEKit.AvalonEdit.Comments;

/// <summary>
/// Provides functionality to create and apply line-comment edits for selected lines in an AvalonEdit <see cref="TextDocument"/>.
/// </summary>
/// <remarks>
/// The service preserves leading whitespace and leaves whitespace-only lines unchanged.
/// </remarks>
[SuppressMessage(
	"Performance",
	"CA1822:Mark members as static",
	Justification = "The service is created per editor as part of the editor service composition.")]
public sealed class TextLineCommentService
{
	/// <summary>
	/// Creates an edit for the requested line-comment transformation on the selected lines.
	/// </summary>
	/// <param name="document">The AvalonEdit <see cref="TextDocument"/> containing the selection.</param>
	/// <param name="selectionStart">The zero-based start offset of the selection.</param>
	/// <param name="selectionLength">The length of the selection.</param>
	/// <param name="commentSyntax">The comment syntax whose line-comment delimiter is applied.</param>
	/// <param name="action">The line-comment transformation to apply.</param>
	/// <param name="edit">
	/// The created edit when the method returns <see langword="true"/>; otherwise, <see langword="default"/>.
	/// </param>
	/// <returns>
	/// <see langword="true"/> when the document contains text and its line-comment delimiter is non-blank;
	/// otherwise, <see langword="false"/>.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
	public bool TryCreateEdit(
		TextDocument document,
		int selectionStart,
		int selectionLength,
		CommentSyntax commentSyntax,
		TextLineCommentAction action,
		out TextLineCommentEdit edit)
	{
		ArgumentNullException.ThrowIfNull(document);

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
	/// Applies the requested line-comment transformation to the editor's current selection.
	/// </summary>
	/// <param name="editor">The editor whose current selection is transformed.</param>
	/// <param name="commentSyntax">The syntax providing the line-comment delimiter.</param>
	/// <param name="action">The transformation to apply.</param>
	/// <exception cref="ArgumentNullException"><paramref name="editor"/> is <see langword="null"/>.</exception>
	public void ApplyEdit(
		TextEditor editor,
		CommentSyntax commentSyntax,
		TextLineCommentAction action)
	{
		ArgumentNullException.ThrowIfNull(editor);

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
	{
		return action == TextLineCommentAction.Uncomment
			? UncommentLine(currentLineText, commentPrefix)
			: CommentLine(currentLineText, commentPrefix);
	}

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

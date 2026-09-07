using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.Core.Text;
using System.Text.RegularExpressions;

namespace Nickelony.IDEKit.AvalonEdit.Editing;

/// <summary>
/// Provides line-based editing operations for an AvalonEdit <see cref="TextEditor"/>.
/// </summary>
public static class TextEditorLineOperations
{
	/// <summary>
	/// Selects a document line's content, excluding its line terminator.
	/// </summary>
	/// <param name="textEditor">The editor whose selection is updated.</param>
	/// <param name="line">The line whose content to select.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textEditor"/> or <paramref name="line"/> is <see langword="null"/>.
	/// </exception>
	public static void SelectLine(this TextEditor textEditor, DocumentLine line)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(line);

		textEditor.Select(line.Offset, line.Length);
	}

	/// <summary>
	/// Replaces a document line's content,
	/// optionally clearing the selection and moving the caret to the end of the document.
	/// </summary>
	/// <remarks>The line terminator is preserved.</remarks>
	/// <param name="textEditor">The editor whose document is updated.</param>
	/// <param name="line">The line whose content is replaced.</param>
	/// <param name="replacement">The replacement text.</param>
	/// <param name="deselectAfterwards">Whether to clear the selection after replacement.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textEditor"/>, <paramref name="line"/>, or <paramref name="replacement"/> is <see langword="null"/>.
	/// </exception>
	public static void ReplaceLine(
		this TextEditor textEditor,
		DocumentLine line,
		string replacement,
		bool deselectAfterwards = false)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(line);
		ArgumentNullException.ThrowIfNull(replacement);

		textEditor.SelectLine(line);
		textEditor.SelectedText = replacement;

		if (deselectAfterwards)
			textEditor.ClearSelection();
	}

	/// <summary>
	/// Replaces the entire document content and places the caret at the end of the resulting document.
	/// </summary>
	/// <param name="textEditor">The editor whose document is updated.</param>
	/// <param name="newContent">The content to set.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textEditor"/> or <paramref name="newContent"/> is <see langword="null"/>.
	/// </exception>
	public static void ReplaceContent(this TextEditor textEditor, string newContent)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(newContent);

		textEditor.SelectAll();
		textEditor.SelectedText = newContent;
		textEditor.ClearSelection();
	}

	/// <summary>
	/// Clears the current selection and places the caret at the end of the document.
	/// </summary>
	/// <param name="textEditor">The editor whose selection is cleared.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textEditor"/> is <see langword="null"/>.
	/// </exception>
	public static void ClearSelection(this TextEditor textEditor)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		textEditor.Select(textEditor.Document.TextLength, 0);
	}

	/// <summary>
	/// Clears the selection and places the caret at the end of a document line.
	/// </summary>
	/// <remarks>The caret is positioned before the line terminator when one is present.</remarks>
	/// <param name="textEditor">The editor whose selection is cleared.</param>
	/// <param name="line">The line at which to place the caret.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textEditor"/> or <paramref name="line"/> is <see langword="null"/>.
	/// </exception>
	public static void ClearSelectionAt(this TextEditor textEditor, DocumentLine line)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(line);

		textEditor.Select(line.EndOffset, 0);
	}

	/// <summary>
	/// Replaces the first document line for which <paramref name="replacementSelector"/> returns replacement text.
	/// </summary>
	/// <param name="textEditor">The editor containing the lines to inspect.</param>
	/// <param name="replacementSelector">
	/// Returns replacement text for a line, or <see langword="null"/> to leave it unchanged.
	/// </param>
	/// <param name="scrollToLine">Whether to scroll to the replaced line.</param>
	/// <param name="workspaceEditTarget">
	/// The target on which to apply the edit, or <see langword="null"/> to edit the editor document directly.
	/// </param>
	/// <param name="onContentChanged">
	/// The callback invoked after a direct edit.
	/// It is not invoked when <paramref name="workspaceEditTarget"/> is supplied.
	/// Pass <see langword="null"/> to omit it.
	/// </param>
	/// <returns><see langword="true"/> when a replacement was applied; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textEditor"/> or <paramref name="replacementSelector"/> is <see langword="null"/>.
	/// </exception>
	public static bool TryReplaceFirstMatchingLine(
		this TextEditor textEditor,
		Func<string, string?> replacementSelector,
		bool scrollToLine = true,
		ITextEditTarget? workspaceEditTarget = null,
		Action? onContentChanged = null)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(replacementSelector);

		foreach (DocumentLine line in textEditor.Document.Lines)
		{
			string lineText = textEditor.Document.GetText(line.Offset, line.Length);
			string? replacementText = replacementSelector(lineText);

			if (replacementText is null)
				continue;

			TextEditorEditHelper.ReplaceText(
				textEditor, line.Offset, line.Length, replacementText, null, workspaceEditTarget, onContentChanged);

			if (scrollToLine)
				textEditor.ScrollToLine(line.LineNumber);

			return true;
		}

		return false;
	}

	/// <summary>
	/// Replaces all occurrences of <paramref name="oldName"/> with <paramref name="newName"/> on the first line
	/// that matches <paramref name="lineRegex"/> and whose extracted name equals <paramref name="oldName"/>.
	/// </summary>
	/// <param name="textEditor">The editor containing the lines to inspect.</param>
	/// <param name="lineRegex">The regular expression that identifies candidate lines.</param>
	/// <param name="nameExtractor">
	/// Produces the name compared with <paramref name="oldName"/> for each candidate line.
	/// Receives the full line text and <paramref name="lineRegex"/>.
	/// </param>
	/// <param name="oldName">The name to match and replace.</param>
	/// <param name="newName">The replacement name.</param>
	/// <param name="scrollToLine">Whether to scroll the editor to the replaced line.</param>
	/// <param name="workspaceEditTarget">
	/// The target on which to apply the edit, or <see langword="null"/> to edit the editor document directly.
	/// </param>
	/// <param name="onContentChanged">
	/// The callback invoked after a direct edit.
	/// It is not invoked when <paramref name="workspaceEditTarget"/> is supplied.
	/// Pass <see langword="null"/> to omit it.
	/// </param>
	/// <returns><see langword="true"/> when a replacement was applied; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textEditor"/>, <paramref name="lineRegex"/>, <paramref name="nameExtractor"/>,
	/// <paramref name="oldName"/>, or <paramref name="newName"/> is <see langword="null"/>.
	/// </exception>
	public static bool TryRenameInFirstMatchingLine(
		this TextEditor textEditor,
		Regex lineRegex,
		Func<string, Regex, string> nameExtractor,
		string oldName,
		string newName,
		bool scrollToLine = true,
		ITextEditTarget? workspaceEditTarget = null,
		Action? onContentChanged = null)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(lineRegex);
		ArgumentNullException.ThrowIfNull(nameExtractor);
		ArgumentNullException.ThrowIfNull(oldName);
		ArgumentNullException.ThrowIfNull(newName);

		return TryReplaceFirstMatchingLine(textEditor, lineText =>
		{
			if (!lineRegex.IsMatch(lineText))
				return null;

			string extractedName = nameExtractor(lineText, lineRegex);

			return extractedName == oldName
				? lineText.Replace(oldName, newName)
				: null;
		}, scrollToLine, workspaceEditTarget, onContentChanged);
	}
}

using System.Text.RegularExpressions;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.AvalonEdit.Editing;

/// <summary>
/// Provides shared line-based editor operations for small scripted document updates.
/// </summary>
public static class TextEditorLineOperations
{
	/// <summary>
	/// Selects the given document line.
	/// </summary>
	/// <param name="textEditor">The editor whose selection is updated.</param>
	/// <param name="line">The line to select.</param>
	public static void SelectLine(this ICSharpCode.AvalonEdit.TextEditor textEditor, DocumentLine line)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(line);

		textEditor.Select(line.Offset, line.Length);
	}

	/// <summary>
	/// Replaces the content of the given document line, optionally deselecting it afterwards.
	/// </summary>
	/// <remarks>The line terminator is not part of the replacement range and is preserved.</remarks>
	/// <param name="textEditor">The editor whose document is updated.</param>
	/// <param name="line">The line to replace.</param>
	/// <param name="replacement">The replacement text.</param>
	/// <param name="deselectAfterwards">Whether to deselect the replaced line afterwards.</param>
	public static void ReplaceLine(
		this ICSharpCode.AvalonEdit.TextEditor textEditor,
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
			textEditor.ResetSelection();
	}

	/// <summary>
	/// Replaces the entire document content with the given text.
	/// </summary>
	/// <param name="textEditor">The editor whose document is updated.</param>
	/// <param name="newContent">The new document content.</param>
	public static void ReplaceContent(this ICSharpCode.AvalonEdit.TextEditor textEditor, string newContent)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(newContent);

		textEditor.SelectAll();
		textEditor.SelectedText = newContent;
		textEditor.ResetSelection();
	}

	/// <summary>
	/// Resets the current selection to the default state, placing the caret at the end of the document.
	/// </summary>
	/// <param name="textEditor">The editor whose selection is updated.</param>
	public static void ResetSelection(this ICSharpCode.AvalonEdit.TextEditor textEditor)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		textEditor.Select(textEditor.Document.TextLength, 0);
	}

	/// <summary>
	/// Resets the selection and places the caret at the end of the given line.
	/// </summary>
	/// <remarks>The line terminator, when present, is not selected.</remarks>
	/// <param name="textEditor">The editor whose selection is updated.</param>
	/// <param name="line">The line to reset the selection at.</param>
	public static void ResetSelectionAt(this ICSharpCode.AvalonEdit.TextEditor textEditor, DocumentLine line)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(line);

		textEditor.Select(line.EndOffset, 0);
	}

	/// <summary>
	/// Replaces the first line whose selector returns replacement text.
	/// </summary>
	/// <param name="textEditor">The editor whose document should be updated.</param>
	/// <param name="replacementSelector">Returns the replacement text for a matching line, or <see langword="null"/> to skip the line.</param>
	/// <param name="scrollToLine">Whether to scroll the editor to the updated line.</param>
	/// <param name="workspaceEditTarget">
	/// The host workspace target to apply through, or <see langword="null"/> to apply to the editor document directly.
	/// </param>
	/// <param name="contentChanged">
	/// The callback invoked when the edit was applied directly to the editor document, or <see langword="null"/> for none.
	/// </param>
	/// <returns><see langword="true"/> when a matching line was replaced; otherwise, <see langword="false"/>.</returns>
	public static bool TryReplaceFirstMatchingLine(
		ICSharpCode.AvalonEdit.TextEditor textEditor,
		Func<string, string?> replacementSelector,
		bool scrollToLine = true,
		ITextEditTarget? workspaceEditTarget = null,
		Action? contentChanged = null)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(replacementSelector);

		foreach (DocumentLine line in textEditor.Document.Lines)
		{
			string lineText = textEditor.Document.GetText(line.Offset, line.Length);
			string? replacementText = replacementSelector(lineText);

			if (replacementText is null)
				continue;

			TextEditorEditHelper.ReplaceText(textEditor, line.Offset, line.Length, replacementText, null, workspaceEditTarget, contentChanged);

			if (scrollToLine)
				textEditor.ScrollToLine(line.LineNumber);

			return true;
		}

		return false;
	}

	/// <summary>
	/// Replaces occurrences of <paramref name="oldName"/> with <paramref name="newName"/>
	/// on the first line that matches <paramref name="lineRegex"/>. The name is extracted from each matching
	/// line via <paramref name="nameExtractor"/> before comparison.
	/// </summary>
	/// <param name="textEditor">The editor whose document should be updated.</param>
	/// <param name="lineRegex">The regular expression used to identify candidate lines.</param>
	/// <param name="nameExtractor">
	/// Extracts the normalized name from a candidate line. Receives the full line text and
	/// the <paramref name="lineRegex"/> to remove the pattern; returns the cleaned name.
	/// </param>
	/// <param name="oldName">The name to search for.</param>
	/// <param name="newName">The replacement name.</param>
	/// <param name="scrollToLine">Whether to scroll the editor to the updated line.</param>
	/// <param name="workspaceEditTarget">
	/// The host workspace target to apply through, or <see langword="null"/> to apply to the editor document directly.
	/// </param>
	/// <param name="contentChanged">
	/// The callback invoked when the edit was applied directly to the editor document, or <see langword="null"/> for none.
	/// </param>
	/// <returns><see langword="true"/> when a matching line was replaced; otherwise, <see langword="false"/>.</returns>
	public static bool TryReplaceFirstMatchingLine(
		ICSharpCode.AvalonEdit.TextEditor textEditor,
		Regex lineRegex,
		Func<string, Regex, string> nameExtractor,
		string oldName,
		string newName,
		bool scrollToLine = true,
		ITextEditTarget? workspaceEditTarget = null,
		Action? contentChanged = null)
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
		}, scrollToLine, workspaceEditTarget, contentChanged);
	}
}

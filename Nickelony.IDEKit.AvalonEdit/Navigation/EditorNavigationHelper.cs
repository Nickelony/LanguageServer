using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Documents;
using Nickelony.IDEKit.Core.FindReplace;
using Nickelony.IDEKit.Core.Navigation;

namespace Nickelony.IDEKit.AvalonEdit.Navigation;

/// <summary>
/// Creates and applies clamped caret, selection, and scroll locations for AvalonEdit editors.
/// </summary>
public static class EditorNavigationHelper
{
	/// <summary>
	/// Gets the document offset corresponding to the given point in the editor's view.
	/// </summary>
	/// <param name="textEditor">The editor to inspect.</param>
	/// <param name="point">The point in the editor's coordinate space.</param>
	/// <returns>The document offset, or <c>-1</c> when the point does not map to a position in the document.</returns>
	public static int GetOffsetFromPoint(this ICSharpCode.AvalonEdit.TextEditor textEditor, Point point)
	{
		ArgumentNullException.ThrowIfNull(textEditor);

		TextViewPosition? position = GetTextViewPosition(textEditor, point);

		if (position is null)
			return -1;

		DocumentLine pointLine = textEditor.Document.GetLineByNumber(position.Value.Line);
		int offset = pointLine.Offset + Math.Min(pointLine.Length, Math.Max(0, position.Value.Column - 1));

		return offset > textEditor.Document.TextLength ? -1 : offset;
	}

	/// <summary>
	/// Gets the word surrounding the given document offset, using AvalonEdit's word-border rules.
	/// </summary>
	/// <param name="textEditor">The editor to inspect.</param>
	/// <param name="offset">The document offset to inspect.</param>
	/// <returns>The word text, or <see langword="null"/> when no word surrounds the offset.</returns>
	public static string? GetWordFromOffset(this ICSharpCode.AvalonEdit.TextEditor textEditor, int offset)
	{
		ArgumentNullException.ThrowIfNull(textEditor);

		int wordStart = TextUtilities.GetNextCaretPosition(
			textEditor.Document,
			offset,
			LogicalDirection.Backward,
			CaretPositioningMode.WordBorder);
		int wordEnd = TextUtilities.GetNextCaretPosition(
			textEditor.Document,
			offset,
			LogicalDirection.Forward,
			CaretPositioningMode.WordBorder);

		return wordStart >= 0 && wordEnd >= 0
			? textEditor.Document.GetText(wordStart, wordEnd - wordStart)
			: null;
	}

	/// <summary>
	/// Moves the caret to the mouse position when the editor has no selected text.
	/// </summary>
	/// <param name="textEditor">The editor to update.</param>
	/// <returns><see langword="true"/> when the caret was moved; otherwise, <see langword="false"/>.</returns>
	public static bool TryMoveCaretToMousePosition(this ICSharpCode.AvalonEdit.TextEditor textEditor)
	{
		ArgumentNullException.ThrowIfNull(textEditor);

		if (!string.IsNullOrEmpty(textEditor.SelectedText))
			return false;

		TextViewPosition? position = GetTextViewPosition(textEditor, Mouse.GetPosition(textEditor));

		if (position is null)
			return false;

		int offset = textEditor.Document.GetOffset(new TextLocation(position.Value.Line, position.Value.Column));
		textEditor.Select(offset, 0);
		return true;
	}

	private static TextViewPosition? GetTextViewPosition(ICSharpCode.AvalonEdit.TextEditor textEditor, Point point)
	{
		if (textEditor.TextArea?.TextView is null)
			return null;

		Point textViewPoint = textEditor.TranslatePoint(point, textEditor.TextArea.TextView);
		return textEditor.TextArea.TextView.GetPosition(textViewPoint + textEditor.TextArea.TextView.ScrollOffset);
	}

	/// <summary>
	/// Captures the editor's current caret, selection, and preferred display line as a location.
	/// </summary>
	/// <param name="textEditor">The editor to capture.</param>
	/// <returns>The captured location.</returns>
	public static NavigationLocation CreateLocation(ICSharpCode.AvalonEdit.TextEditor textEditor)
	{
		ArgumentNullException.ThrowIfNull(textEditor);

		return new NavigationLocation(
			textEditor.Document.FileName,
			textEditor.CaretOffset,
			textEditor.SelectionStart,
			textEditor.SelectionLength,
			textEditor.TextArea.Caret.Position.Line);
	}

	/// <summary>
	/// Creates a location for a one-based line and column in the editor's document.
	/// </summary>
	/// <remarks>
	/// The line number is clamped to the document's lines, and the column is clamped to the range from
	/// the first character through one position past the line's final character.
	/// </remarks>
	/// <param name="textEditor">The editor whose document provides the offset mapping.</param>
	/// <param name="filePath">The logical path of the target document.</param>
	/// <param name="lineNumber">The one-based target line number.</param>
	/// <param name="columnNumber">The one-based target column number.</param>
	/// <returns>The created location.</returns>
	public static NavigationLocation CreateDefinitionLocation(
		ICSharpCode.AvalonEdit.TextEditor textEditor,
		string filePath,
		int lineNumber,
		int columnNumber)
	{
		ArgumentNullException.ThrowIfNull(textEditor);

		int offset = GetOffset(textEditor, lineNumber, columnNumber);

		return new NavigationLocation(filePath, offset, offset, 0, lineNumber);
	}

	/// <summary>
	/// Creates a location that selects the given one-based line and column range.
	/// </summary>
	/// <remarks>
	/// Both endpoints are clamped as they are converted to offsets. If the resulting end offset is
	/// before the start offset, the selection length is zero.
	/// </remarks>
	/// <param name="textEditor">The editor whose document provides the offset mapping.</param>
	/// <param name="filePath">The logical path of the target document.</param>
	/// <param name="startLineNumber">The one-based start line number.</param>
	/// <param name="startColumnNumber">The one-based start column number.</param>
	/// <param name="endLineNumber">The one-based end line number.</param>
	/// <param name="endColumnNumber">The one-based end column number.</param>
	/// <returns>The created location.</returns>
	public static NavigationLocation CreateRangeLocation(
		ICSharpCode.AvalonEdit.TextEditor textEditor,
		string filePath,
		int startLineNumber,
		int startColumnNumber,
		int endLineNumber,
		int endColumnNumber)
	{
		ArgumentNullException.ThrowIfNull(textEditor);

		int startOffset = GetOffset(textEditor, startLineNumber, startColumnNumber);
		int endOffset = GetOffset(textEditor, endLineNumber, endColumnNumber);
		int selectionLength = Math.Max(0, endOffset - startOffset);

		return new NavigationLocation(filePath, startOffset, startOffset, selectionLength, startLineNumber);
	}

	/// <summary>
	/// Applies a location to the editor: focuses it, clamps and restores the caret and selection,
	/// and scrolls to the preferred line.
	/// </summary>
	/// <remarks>
	/// The selection is clamped to the document length independently of the caret offset. When no
	/// preferred line is supplied, the line containing the selection start or caret is used.
	/// </remarks>
	/// <param name="textEditor">The editor to update.</param>
	/// <param name="location">The location to apply.</param>
	public static void ApplyLocation(ICSharpCode.AvalonEdit.TextEditor textEditor, NavigationLocation location)
	{
		ArgumentNullException.ThrowIfNull(textEditor);

		int documentLength = textEditor.Document.TextLength;
		int selectionStart = Math.Max(0, Math.Min(location.SelectionStart, documentLength));
		int selectionLength = Math.Max(0, Math.Min(location.SelectionLength, documentLength - selectionStart));
		int caretOffset = Math.Max(0, Math.Min(location.CaretOffset, documentLength));

		textEditor.Focus();
		textEditor.CaretOffset = caretOffset;
		textEditor.Select(selectionStart, selectionLength);
		textEditor.ScrollToLine(GetPreferredLine(textEditor, location, selectionStart, caretOffset));
	}

	/// <summary>
	/// Attempts to re-select the search result at <paramref name="item"/>'s line in the document.
	/// When the stored match index is out of range, the caret is placed at the start of the line
	/// and the location still reports success so hosts can navigate to the containing line.
	/// </summary>
	/// <remarks>
	/// <paramref name="item"/>.MatchSegmentText is interpreted as a regular expression when matches
	/// are reconstructed, consistent with the find-and-replace layer.
	/// </remarks>
	/// <param name="document">The document that contains the search result.</param>
	/// <param name="filePath">The logical path of the document.</param>
	/// <param name="item">The search result to re-select.</param>
	/// <param name="location">The created location, when the result could be mapped.</param>
	/// <returns><see langword="true"/> when a location was created; otherwise, <see langword="false"/>.</returns>
	public static bool TryCreateSearchResultLocation(
		this TextDocument document,
		string filePath,
		FindReplaceItem item,
		out NavigationLocation? location)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentNullException.ThrowIfNull(item);

		location = null;

		if (item.LineNumber < 1 || item.LineNumber > document.LineCount)
			return false;

		DocumentLine line = document.GetLineByNumber(item.LineNumber);
		string lineText = document.GetText(line.Offset, line.Length);
		MatchCollection matches = Regex.Matches(lineText, item.MatchSegmentText);

		if (item.MatchSegmentIndex < 0 || item.MatchSegmentIndex >= matches.Count)
		{
			location = new NavigationLocation(filePath, line.Offset, line.Offset, 0, line.LineNumber);
			return true;
		}

		Match match = matches[item.MatchSegmentIndex];
		int selectionStart = document.ClampOffset(line.Offset + match.Index);

		location = new NavigationLocation(filePath, selectionStart, selectionStart, match.Length, line.LineNumber);
		return true;
	}

	private static int GetPreferredLine(
		ICSharpCode.AvalonEdit.TextEditor textEditor,
		NavigationLocation location,
		int selectionStart,
		int caretOffset)
	{
		if (location.PreferredLine is int preferredLine)
			return Math.Max(1, Math.Min(preferredLine, textEditor.Document.LineCount));

		int offset = selectionStart > 0 || location.SelectionLength > 0
			? selectionStart
			: caretOffset;

		return textEditor.Document.GetLineByOffset(offset).LineNumber;
	}

	private static int GetOffset(ICSharpCode.AvalonEdit.TextEditor textEditor, int lineNumber, int columnNumber)
	{
		int safeLineNumber = Math.Max(1, Math.Min(lineNumber, textEditor.Document.LineCount));
		DocumentLine documentLine = textEditor.Document.GetLineByNumber(safeLineNumber);
		int safeColumnNumber = Math.Max(1, Math.Min(columnNumber, documentLine.Length + 1));
		return documentLine.Offset + safeColumnNumber - 1;
	}
}

using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Documents;
using Nickelony.IDEKit.Core.FindReplace;
using Nickelony.IDEKit.Core.Navigation;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;

namespace Nickelony.IDEKit.AvalonEdit.Navigation;

/// <summary>
/// Provides AvalonEdit helpers for resolving document positions and applying navigation locations.
/// </summary>
public static class EditorNavigationHelper
{
	/// <summary>
	/// Maps a point in the editor to a zero-based document offset.
	/// </summary>
	/// <param name="textEditor">The editor to inspect.</param>
	/// <param name="point">The point in the editor's coordinate space to map.</param>
	/// <returns>The document offset, or <c>-1</c> when the point does not map to the document.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="textEditor"/> is <see langword="null"/>.</exception>
	public static int GetOffsetFromPoint(this TextEditor textEditor, Point point)
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
	/// Gets the text between the nearest AvalonEdit word borders around a document offset.
	/// </summary>
	/// <param name="textEditor">The editor to inspect.</param>
	/// <param name="offset">The zero-based document offset to inspect.</param>
	/// <returns>
	/// The text between the surrounding word borders, or <see langword="null"/> when either border is unavailable.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="textEditor"/> is <see langword="null"/>.</exception>
	public static string? GetWordFromOffset(this TextEditor textEditor, int offset)
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
	/// Moves the caret to the current mouse position when no text is selected.
	/// </summary>
	/// <param name="textEditor">The editor to update.</param>
	/// <returns>
	/// <see langword="true"/> when a document position is found and applied; otherwise, <see langword="false"/>.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="textEditor"/> is <see langword="null"/>.</exception>
	public static bool TryMoveCaretToMousePosition(this TextEditor textEditor)
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

	private static TextViewPosition? GetTextViewPosition(TextEditor textEditor, Point point)
	{
		if (textEditor.TextArea?.TextView is null)
			return null;

		Point textViewPoint = textEditor.TranslatePoint(point, textEditor.TextArea.TextView);
		return textEditor.TextArea.TextView.GetPosition(textViewPoint + textEditor.TextArea.TextView.ScrollOffset);
	}

	/// <summary>
	/// Captures the editor's current caret, selection, and caret line in a navigation location.
	/// </summary>
	/// <param name="textEditor">The editor to capture from.</param>
	/// <returns>The captured location.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="textEditor"/> is <see langword="null"/>.</exception>
	public static NavigationLocation CreateLocation(TextEditor textEditor)
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
	/// Creates a zero-length location at a one-based line and column in the editor's document.
	/// </summary>
	/// <remarks>
	/// The line and column are clamped when the offset is calculated.
	/// The original line number is stored as the preferred scroll line and
	/// clamped to the editor's line count when the location is applied.
	/// </remarks>
	/// <param name="textEditor">The editor whose document provides the offset mapping.</param>
	/// <param name="filePath">The logical path of the target document.</param>
	/// <param name="lineNumber">The one-based target line number.</param>
	/// <param name="columnNumber">The one-based target column number.</param>
	/// <returns>The created location.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textEditor"/> or <paramref name="filePath"/> is <see langword="null"/>.
	/// </exception>
	public static NavigationLocation CreateDefinitionLocation(
		TextEditor textEditor,
		string filePath,
		int lineNumber,
		int columnNumber)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(filePath);

		int offset = GetOffset(textEditor, lineNumber, columnNumber);
		return new NavigationLocation(filePath, offset, offset, 0, lineNumber);
	}

	/// <summary>
	/// Creates a location that selects a one-based line and column range.
	/// </summary>
	/// <remarks>
	/// Both endpoints are clamped to the document.
	/// If the end offset precedes the start offset, the selection is empty.
	/// The original start line number is stored as the preferred scroll line and
	/// clamped to the editor's line count when the location is applied.
	/// </remarks>
	/// <param name="textEditor">The editor whose document provides the offset mapping.</param>
	/// <param name="filePath">The logical path of the target document.</param>
	/// <param name="startLineNumber">The one-based start line number.</param>
	/// <param name="startColumnNumber">The one-based start column number.</param>
	/// <param name="endLineNumber">The one-based end line number.</param>
	/// <param name="endColumnNumber">The one-based end column number.</param>
	/// <returns>The created location.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textEditor"/> or <paramref name="filePath"/> is <see langword="null"/>.
	/// </exception>
	public static NavigationLocation CreateRangeLocation(
		TextEditor textEditor,
		string filePath,
		int startLineNumber,
		int startColumnNumber,
		int endLineNumber,
		int endColumnNumber)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(filePath);

		int startOffset = GetOffset(textEditor, startLineNumber, startColumnNumber);
		int endOffset = GetOffset(textEditor, endLineNumber, endColumnNumber);
		int selectionLength = Math.Max(0, endOffset - startOffset);

		return new NavigationLocation(filePath, startOffset, startOffset, selectionLength, startLineNumber);
	}

	/// <summary>
	/// Applies a navigation location to the editor and scrolls to the target line.
	/// </summary>
	/// <remarks>
	/// The supplied caret and selection offsets are clamped before they are applied.
	/// The selection operation determines the final caret position.
	/// Without a preferred line, the selection start is used when it is after <c>0</c> or
	/// the selection length is positive; otherwise, the caret line is used.
	/// </remarks>
	/// <param name="textEditor">The editor to update.</param>
	/// <param name="location">The location to apply.</param>
	/// <exception cref="ArgumentNullException"><paramref name="textEditor"/> is <see langword="null"/>.</exception>
	public static void ApplyLocation(this TextEditor textEditor, NavigationLocation location)
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
	/// Tries to create a location for a search result.
	/// </summary>
	/// <remarks>
	/// The stored match text is treated as an unescaped regular-expression pattern.
	/// If the match index is invalid, a zero-length location at the line start is returned.
	/// </remarks>
	/// <param name="document">The document that contains the search result.</param>
	/// <param name="filePath">The logical path of the document.</param>
	/// <param name="item">The search result to re-select.</param>
	/// <param name="location">The mapped location, or <see langword="null"/> when the line is invalid.</param>
	/// <returns><see langword="true"/> when the line is valid; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="document"/>, <paramref name="filePath"/>, or <paramref name="item"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown when the stored match text is not a valid regular-expression pattern.
	/// </exception>
	public static bool TryCreateSearchResultLocation(
		this TextDocument document,
		string filePath,
		FindReplaceItem item,
		out NavigationLocation? location)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentNullException.ThrowIfNull(filePath);
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
		TextEditor textEditor,
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

	private static int GetOffset(TextEditor textEditor, int lineNumber, int columnNumber)
	{
		int safeLineNumber = Math.Max(1, Math.Min(lineNumber, textEditor.Document.LineCount));
		DocumentLine documentLine = textEditor.Document.GetLineByNumber(safeLineNumber);
		int safeColumnNumber = Math.Max(1, Math.Min(columnNumber, documentLine.Length + 1));

		return documentLine.Offset + safeColumnNumber - 1;
	}
}

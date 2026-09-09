using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.Core.Editing;

namespace Nickelony.IDEKit.AvalonEdit.Editing;

/// <summary>
/// Provides selector-driven operations that edit the first document line matching a condition.
/// </summary>
/// <remarks>
/// <para>
/// The helper inspects the document one line at a time and modifies only the first matching line's
/// range, optionally scrolling to it. The matching logic is supplied by the caller,
/// so the operation stays domain-agnostic.
/// </para>
/// <para>
/// The replacement selector must not mutate the document: the scan enumerates the document's lines and
/// captures the matching line's offset, text, and number, and a mutation during the scan invalidates
/// those captures (line numbers and offsets shift).
/// </para>
/// <para>
/// After a matching line is processed, the caret is placed at the end of the replacement text; an
/// identical replacement leaves the document and undo stack untouched but still positions the caret.
/// A multi-line replacement spans the line's range, so the caret ends after the last replacement line.
/// When an edit target that does not update the editor's document is supplied, the caret is still
/// applied against the editor's document; see <see cref="TextEditorEditOperations"/> for the canonical
/// edit-target contract.
/// </para>
/// </remarks>
public static class TextEditorFirstMatchingLineOperations
{
	/// <summary>
	/// Replaces the first document line for which <paramref name="replacementSelector"/> returns replacement text.
	/// </summary>
	/// <remarks>
	/// When the selected replacement text equals the current line text, the document and its undo stack
	/// are left untouched: the matching line is still reported as found, and the caret is still placed
	/// at the end of the resulting line text.
	/// </remarks>
	/// <param name="textEditor">The editor containing the lines to inspect.</param>
	/// <param name="replacementSelector">
	/// Returns replacement text for a line, or <see langword="null"/> to skip that line.
	/// </param>
	/// <param name="scrollToLine">Whether to scroll to the replaced line.</param>
	/// <param name="editTarget">
	/// The host-owned target to which the edit is applied, or <see langword="null"/> to edit the editor's document directly.
	/// </param>
	/// <returns>
	/// <see langword="true"/> when a matching line was found; otherwise, <see langword="false"/>. An editor
	/// without a document yields <see langword="false"/> instead of throwing.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textEditor"/> or <paramref name="replacementSelector"/> is <see langword="null"/>.
	/// </exception>
	public static bool TryReplaceFirstMatchingLine(
		this TextEditor textEditor,
		Func<string, string?> replacementSelector,
		bool scrollToLine = true,
		ITextEditTarget? editTarget = null)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(replacementSelector);

		// A Try member reports failure instead of throwing: an editor without a document has no lines
		// to scan.
		if (textEditor.Document is null)
			return false;

		DocumentLine? matchedLine = null;
		string matchedLineText = string.Empty;
		string matchedReplacementText = string.Empty;
		int matchedLineOffset = 0;

		// Find the match before editing: the document must not be mutated while its lines are being
		// enumerated, and the captured line data must describe the document as it was scanned. The offset
		// is captured during the scan so the edit uses the scanned range even though the handle could
		// resolve differently after a mutation-performing selector.
		foreach (DocumentLine line in textEditor.Document.Lines)
		{
			string lineText = textEditor.Document.GetText(line);
			string? replacementText = replacementSelector(lineText);

			if (replacementText is null)
				continue;

			matchedLine = line;
			matchedLineText = lineText;
			matchedReplacementText = replacementText;
			matchedLineOffset = line.Offset;
			break;
		}

		if (matchedLine is null)
			return false;

		int lineNumber = matchedLine.LineNumber;
		int desiredCaretOffset = matchedLineOffset + matchedReplacementText.Length;

		if (!string.Equals(matchedReplacementText, matchedLineText, StringComparison.Ordinal))
		{
			// The desired caret can exceed the pre-edit document length when the replacement grows the
			// document, so it is passed unclamped: the helper clamps it against the post-edit length.
			TextEditorEditOperations.ReplaceText(
				textEditor, matchedLineOffset, matchedLineText.Length, matchedReplacementText, desiredCaretOffset, editTarget);
		}
		else
		{
			// A replacement equal to the current line would create a pointless undo entry; the sibling
			// TextEditorLineOperations.ReplaceLine skips identical text for the same reason. The caret
			// contract stays identical because the caret is placed even when no edit is applied.
			textEditor.CaretOffset = Math.Clamp(desiredCaretOffset, min: 0, max: textEditor.Document.TextLength);
		}

		if (scrollToLine)
			textEditor.ScrollToLine(lineNumber);

		return true;
	}
}

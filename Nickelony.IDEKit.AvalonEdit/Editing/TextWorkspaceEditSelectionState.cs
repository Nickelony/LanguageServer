namespace Nickelony.IDEKit.AvalonEdit.Editing;

/// <summary>
/// Captures the selection and caret state for a text editor before applying workspace edits.
/// </summary>
/// <remarks>
/// Selection offsets use AvalonEdit's zero-based UTF-16 document offsets. <see cref="SelectionEnd"/>
/// is exclusive and equals <see cref="SelectionStart"/> plus the original selection length.
/// </remarks>
public sealed class TextWorkspaceEditSelectionState
{
	private TextWorkspaceEditSelectionState(string filePath, int selectionStart, int selectionEnd, int caretOffset)
	{
		FilePath = filePath;
		SelectionStart = selectionStart;
		SelectionEnd = selectionEnd;
		CaretOffset = caretOffset;
	}

	/// <summary>
	/// Gets the file path associated with the captured editor.
	/// </summary>
	public string FilePath { get; }

	/// <summary>
	/// Gets the selection start offset.
	/// </summary>
	public int SelectionStart { get; }

	/// <summary>
	/// Gets the selection end offset.
	/// </summary>
	public int SelectionEnd { get; }

	/// <summary>
	/// Gets the caret offset.
	/// </summary>
	public int CaretOffset { get; }

	/// <summary>
	/// Captures the current selection state from an editor.
	/// </summary>
	/// <param name="editor">The editor whose state should be captured.</param>
	/// <param name="filePath">The file path associated with the editor; <see langword="null"/> is stored as an empty string.</param>
	/// <returns>The captured selection state.</returns>
	public static TextWorkspaceEditSelectionState Capture(ICSharpCode.AvalonEdit.TextEditor editor, string filePath)
	{
		ArgumentNullException.ThrowIfNull(editor);

		int selectionStart = editor.SelectionStart;
		int selectionEnd = selectionStart + editor.SelectionLength;

		return new TextWorkspaceEditSelectionState(filePath ?? string.Empty, selectionStart, selectionEnd, editor.CaretOffset);
	}
}

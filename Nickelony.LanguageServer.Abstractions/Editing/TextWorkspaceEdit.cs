namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Represents text edits for one or more documents.
/// </summary>
public sealed class TextWorkspaceEdit
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextWorkspaceEdit"/> class.
	/// </summary>
	/// <param name="documentEdits">The edits grouped by document.</param>
	public TextWorkspaceEdit(IReadOnlyList<TextDocumentEdit> documentEdits)
	{
		ArgumentNullException.ThrowIfNull(documentEdits);
		DocumentEdits = Array.AsReadOnly([.. documentEdits]);
	}

	/// <summary>
	/// Gets the immutable snapshot of edits grouped by document.
	/// </summary>
	public IReadOnlyList<TextDocumentEdit> DocumentEdits { get; }

	/// <summary>
	/// Gets a value indicating whether any document contains a text edit.
	/// </summary>
	public bool HasEdits => DocumentEdits.Any(documentEdit => documentEdit.TextEdits.Count > 0);
}

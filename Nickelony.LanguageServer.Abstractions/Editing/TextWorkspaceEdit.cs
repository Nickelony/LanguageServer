namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Represents text edits for one or more documents.
/// </summary>
/// <remarks>
/// <para>
/// Record equality compares the <see cref="DocumentEdits"/> snapshot by reference; compare the collection
/// element-wise when structural equality is required.
/// </para>
/// <para>
/// This model represents text edits only. Workspace edits that also contain file create, rename, or delete
/// operations cannot be represented; providers must fail such results closed (for example by returning
/// <see langword="null"/> from the rename request) instead of applying a partial edit.
/// </para>
/// </remarks>
public sealed record TextWorkspaceEdit
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextWorkspaceEdit"/> record.
	/// </summary>
	/// <param name="documentEdits">The edits grouped by document.</param>
	/// <exception cref="ArgumentNullException"><paramref name="documentEdits"/> is <see langword="null"/>.</exception>
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

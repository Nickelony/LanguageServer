namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Represents the text edits for one document.
/// </summary>
/// <remarks>
/// Record equality compares the <see cref="TextEdits"/> snapshot by reference; compare the collection
/// element-wise when structural equality is required.
/// </remarks>
public sealed record TextDocumentEdit
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextDocumentEdit"/> record.
	/// </summary>
	/// <param name="filePath">The path of the document to update.</param>
	/// <param name="textEdits">The edits to apply.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="textEdits"/> is <see langword="null"/>.
	/// </exception>
	public TextDocumentEdit(string filePath, IReadOnlyList<TextEdit> textEdits)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(textEdits);

		FilePath = filePath;
		TextEdits = Array.AsReadOnly([.. textEdits]);
	}

	/// <summary>
	/// Gets the path of the document to update.
	/// </summary>
	public string FilePath { get; }

	/// <summary>
	/// Gets the immutable snapshot of edits to apply to the document.
	/// </summary>
	public IReadOnlyList<TextEdit> TextEdits { get; }
}

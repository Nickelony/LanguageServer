namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Represents the text edits for one document.
/// </summary>
public sealed class TextDocumentEdit
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextDocumentEdit"/> class.
	/// </summary>
	/// <param name="filePath">The path of the document to update.</param>
	/// <param name="textEdits">The edits to apply.</param>
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

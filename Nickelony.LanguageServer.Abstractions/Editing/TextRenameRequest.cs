namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Describes a request to rename the symbol at a document position.
/// </summary>
public sealed record TextRenameRequest
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextRenameRequest"/> record.
	/// </summary>
	/// <param name="filePath">The current document file path.</param>
	/// <param name="documentText">The current document content.</param>
	/// <param name="line">The zero-based current line index. Negative values are changed to zero.</param>
	/// <param name="column">The zero-based current column index. Negative values are changed to zero.</param>
	/// <param name="newName">The requested replacement name.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/>, <paramref name="documentText"/>, or <paramref name="newName"/> is
	/// <see langword="null"/>.
	/// </exception>
	public TextRenameRequest(string filePath, string documentText, int line, int column, string newName)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(documentText);
		ArgumentNullException.ThrowIfNull(newName);

		FilePath = filePath;
		DocumentText = documentText;
		Line = Math.Max(0, line);
		Column = Math.Max(0, column);
		NewName = newName;
	}

	/// <summary>
	/// Gets the current document file path.
	/// </summary>
	public string FilePath { get; }

	/// <summary>
	/// Gets the current document content.
	/// </summary>
	public string DocumentText { get; }

	/// <summary>
	/// Gets the zero-based current line index.
	/// </summary>
	public int Line { get; }

	/// <summary>
	/// Gets the zero-based current column index.
	/// </summary>
	public int Column { get; }

	/// <summary>
	/// Gets the requested replacement name.
	/// </summary>
	public string NewName { get; }
}

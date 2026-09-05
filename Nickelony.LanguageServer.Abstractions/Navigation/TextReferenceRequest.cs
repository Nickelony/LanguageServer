namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Describes a request to find symbol references at a document position.
/// </summary>
/// <remarks>
/// The request position uses zero-based line and column indices. Returned <see cref="TextReferenceLocation"/> values
/// use one-based coordinates. Negative line and column values are changed to zero.
/// </remarks>
public sealed class TextReferenceRequest
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextReferenceRequest"/> class.
	/// </summary>
	/// <param name="filePath">The current document file path.</param>
	/// <param name="documentText">The current document content.</param>
	/// <param name="line">The zero-based current line index.</param>
	/// <param name="column">The zero-based current column index.</param>
	/// <param name="includeDeclaration"><see langword="true"/> to include the symbol declaration when available.</param>
	public TextReferenceRequest(string filePath, string documentText, int line, int column, bool includeDeclaration = true)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(documentText);

		FilePath = filePath;
		DocumentText = documentText;
		Line = Math.Max(0, line);
		Column = Math.Max(0, column);
		IncludeDeclaration = includeDeclaration;
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
	/// Gets a value indicating whether the declaration should be included when available.
	/// </summary>
	public bool IncludeDeclaration { get; }
}

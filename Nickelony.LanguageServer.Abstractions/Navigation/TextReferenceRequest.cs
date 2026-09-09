namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Describes a request to find symbol references at a document position.
/// </summary>
/// <remarks>
/// Returned <see cref="TextReferenceLocation"/> values use the same zero-based LSP units as the request position.
/// </remarks>
public sealed record TextReferenceRequest
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextReferenceRequest"/> record.
	/// </summary>
	/// <param name="filePath">The current document file path.</param>
	/// <param name="documentText">The current document content.</param>
	/// <param name="line">The zero-based current line index. Negative values are changed to zero.</param>
	/// <param name="column">The zero-based current column index. Negative values are changed to zero.</param>
	/// <param name="includeDeclaration"><see langword="true"/> to include the symbol declaration when available.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="documentText"/> is <see langword="null"/>.
	/// </exception>
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

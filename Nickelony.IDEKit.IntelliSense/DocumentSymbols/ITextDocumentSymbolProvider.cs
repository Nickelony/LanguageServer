namespace Nickelony.IDEKit.IntelliSense.DocumentSymbols;

/// <summary>
/// Provides the document symbols (outline entries) for a document.
/// </summary>
/// <remarks>
/// Implementations receive the complete document text in each request and should avoid UI state,
/// allowing hosts to project large documents in the background when the implementation supports it.
/// </remarks>
public interface ITextDocumentSymbolProvider
{
	/// <summary>
	/// Gets the document symbols for the supplied request.
	/// </summary>
	/// <param name="request">The document-symbol request.</param>
	/// <returns>The matching document symbols, or an empty list when none apply.</returns>
	IReadOnlyList<TextDocumentSymbol> GetSymbols(TextDocumentSymbolRequest request);
}

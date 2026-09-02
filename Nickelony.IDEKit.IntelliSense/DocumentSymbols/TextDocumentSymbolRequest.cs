namespace Nickelony.IDEKit.IntelliSense.DocumentSymbols;

/// <summary>
/// Describes a document-symbol request.
/// </summary>
/// <param name="DocumentText">The document content to scan.</param>
/// <param name="Filter">The filter text used to narrow symbols, or an empty string to keep all.</param>
public readonly record struct TextDocumentSymbolRequest(string DocumentText, string Filter);

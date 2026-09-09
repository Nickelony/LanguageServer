using Nickelony.IDEKit.IntelliSense.DocumentSymbols;

namespace Nickelony.LanguageServer.Lua;

public sealed partial class LuaLanguageServerIntelliSenseProvider
{
	/// <inheritdoc/>
	public override Task<IReadOnlyList<TextDocumentSymbol>> GetDocumentSymbolsAsync(string filePath, string content,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(content);

		// The parse closure needs the normalized document identity for the flat same-document filter.
		// An unusable path fails normalization inside the request pipeline and returns the fallback.
		string documentFilePath = ResolveDocumentFilePath(filePath);

		return SendDocumentRequestAsync<DocumentSymbolsResponse?, IReadOnlyList<TextDocumentSymbol>>(
			filePath, content, "textDocument/documentSymbol",
			supportsRequest: static client => client.SupportsDocumentSymbols,
			buildParameters: static textDocument => new DocumentSymbolParams(textDocument),
			parseResponse: response => LuaLanguageServerResponseParser.ParseDocumentSymbols(response, content, documentFilePath, Logger),
			fallbackValue: [],
			cancellationToken);
	}
}

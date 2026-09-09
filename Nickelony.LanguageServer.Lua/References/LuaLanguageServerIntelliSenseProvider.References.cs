namespace Nickelony.LanguageServer.Lua;

public sealed partial class LuaLanguageServerIntelliSenseProvider
{
	/// <inheritdoc/>
	public override async Task<IReadOnlyList<TextReferenceLocation>> GetReferencesAsync(TextReferenceRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		return await SendDocumentRequestAsync<ReferenceLocationPayload[]?, IReadOnlyList<TextReferenceLocation>>(
			request.FilePath, request.DocumentText, "textDocument/references",
			supportsRequest: static client => client.SupportsReferences,
			// Request coordinates are clamped to zero like the shared position-based request path.
			buildParameters: textDocument => new ReferenceParams(textDocument,
				new ProtocolPosition(Math.Max(0, request.Line), Math.Max(0, request.Column)),
				new ReferenceContextPayload(IncludeDeclaration: request.IncludeDeclaration)),
			parseResponse: LuaLanguageServerResponseParser.ParseReferenceLocations,
			fallbackValue: [],
			cancellationToken).ConfigureAwait(false);
	}
}

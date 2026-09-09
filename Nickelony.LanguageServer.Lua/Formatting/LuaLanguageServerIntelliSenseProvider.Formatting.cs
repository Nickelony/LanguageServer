namespace Nickelony.LanguageServer.Lua;

public sealed partial class LuaLanguageServerIntelliSenseProvider
{
	/// <inheritdoc/>
	public override async Task<TextWorkspaceEdit?> FormatDocumentAsync(TextFormatRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		// The parse closure needs the normalized document identity for the resulting document edit; an
		// unusable path fails normalization inside the request pipeline and returns the fallback.
		string documentFilePath = ResolveDocumentFilePath(request.FilePath);

		return await SendDocumentRequestAsync<TextEditPayload[]?, TextWorkspaceEdit?>(
			request.FilePath, request.DocumentText, "textDocument/formatting",
			supportsRequest: static client => client.SupportsFormatting,
			buildParameters: textDocument => new DocumentFormattingParams(textDocument,
				new FormattingOptionsPayload(request.Options.TabSize, request.Options.InsertSpaces)),
			parseResponse: response =>
			{
				IReadOnlyList<TextEdit> textEdits = LuaLanguageServerResponseParser.ParseDocumentFormattingEdits(response);

				return textEdits.Count == 0
					? null
					: new TextWorkspaceEdit([
						new TextDocumentEdit(documentFilePath, textEdits)
					]);
			},
			fallbackValue: null,
			cancellationToken).ConfigureAwait(false);
	}
}

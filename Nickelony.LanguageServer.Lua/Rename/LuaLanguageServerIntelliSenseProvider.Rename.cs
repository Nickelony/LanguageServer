namespace Nickelony.LanguageServer.Lua;

public sealed partial class LuaLanguageServerIntelliSenseProvider
{
	/// <inheritdoc/>
	public override async Task<TextWorkspaceEdit?> RenameSymbolAsync(TextRenameRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (string.IsNullOrWhiteSpace(request.NewName))
			return null;

		return await SendDocumentRequestAsync<WorkspaceEditResponse?, TextWorkspaceEdit?>(
			request.FilePath, request.DocumentText, "textDocument/rename",
			supportsRequest: static client => client.SupportsRename,
			// Request coordinates are clamped to zero like the shared position-based request path.
			buildParameters: textDocument => new RenameParams(textDocument,
				new ProtocolPosition(Math.Max(0, request.Line), Math.Max(0, request.Column)), request.NewName),
			parseResponse: response => LuaLanguageServerResponseParser.ParseWorkspaceEdit(response, Logger),
			fallbackValue: null,
			cancellationToken).ConfigureAwait(false);
	}
}

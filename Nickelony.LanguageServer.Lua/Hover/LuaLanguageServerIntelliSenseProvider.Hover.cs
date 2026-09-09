using Nickelony.IDEKit.IntelliSense.Hover;

namespace Nickelony.LanguageServer.Lua;

public sealed partial class LuaLanguageServerIntelliSenseProvider
{
	/// <inheritdoc/>
	public override Task<TextHoverInfo?> GetHoverAsync(string filePath, string content,
		int line, int column, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(content);

		return SendDocumentPositionRequestAsync<HoverResponse?, TextHoverInfo?>(
			filePath, content, line, column, "textDocument/hover",
			static (textDocument, position) => new TextDocumentPositionParams(textDocument, position),
			response => LuaLanguageServerResponseParser.ParseHoverInfo(response, content),
			fallbackValue: null,
			cancellationToken);
	}
}

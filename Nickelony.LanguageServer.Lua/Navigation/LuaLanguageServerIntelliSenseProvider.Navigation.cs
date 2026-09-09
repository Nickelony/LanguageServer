using Nickelony.IDEKit.IntelliSense.Navigation;

namespace Nickelony.LanguageServer.Lua;

public sealed partial class LuaLanguageServerIntelliSenseProvider
{
	/// <inheritdoc/>
	public override Task<TextDefinitionLocation?> GetDefinitionAsync(string filePath, string content,
		int line, int column, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(content);

		return SendDocumentPositionRequestAsync<DefinitionResponse?, TextDefinitionLocation?>(
			filePath, content, line, column, "textDocument/definition",
			static (textDocument, position) => new TextDocumentPositionParams(textDocument, position),
			LuaLanguageServerResponseParser.ParseDefinitionLocation,
			fallbackValue: null,
			cancellationToken);
	}
}

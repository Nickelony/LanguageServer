using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.LanguageServer.Lua;

public sealed partial class LuaLanguageServerIntelliSenseProvider
{
	/// <inheritdoc/>
	/// <remarks>
	/// Every call sends a fresh <c>textDocument/completion</c> request; the server's
	/// <c>isIncomplete</c> flag is not acted upon because the provider keeps no completion-list
	/// cache, so each refresh (keystroke or manual trigger) already re-queries the server, which
	/// satisfies the protocol requirement to re-fetch incomplete lists.
	/// </remarks>
	public override async Task<IReadOnlyList<TextCompletionItem>> GetCompletionItemsAsync(string filePath, string content,
		int line, int column, char? triggerCharacter = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(content);

		return await SendDocumentPositionRequestAsync<CompletionResponse?, IReadOnlyList<TextCompletionItem>>(
			filePath, content, line, column, "textDocument/completion",
			(textDocument, position) => new CompletionParams(textDocument, position, BuildCompletionContext(triggerCharacter)),
			response =>
			{
				IReadOnlyList<CompletionItemPayload> itemPayloads = response?.Items ?? [];

				if (itemPayloads.Count == 0)
					return [];

				ILanguageServerClient? client = Client;
				CompletionResolveFactory? resolveFactory =
					client is not null && client.SupportsCompletionResolve
						? (unresolvedItem, itemPayload, priorityRank) =>
							resolveCancellationToken => ResolveCompletionItemAsync(client, unresolvedItem, itemPayload, priorityRank, content, resolveCancellationToken)
						: null;

				return LuaLanguageServerResponseParser.ParseCompletionItems(itemPayloads, content, resolveFactory);
			},
			fallbackValue: [],
			cancellationToken).ConfigureAwait(false);
	}

	private static CompletionContextPayload BuildCompletionContext(char? triggerCharacter)
	{
		return triggerCharacter is null
			? new CompletionContextPayload(TriggerKind: CompletionTriggerKind.Invoked)
			: new CompletionContextPayload(TriggerKind: CompletionTriggerKind.TriggerCharacter, triggerCharacter.ToString());
	}

	private async Task<TextCompletionItem> ResolveCompletionItemAsync(ILanguageServerClient client, TextCompletionItem unresolvedItem, CompletionItemPayload itemPayload, int priorityRank, string content, CancellationToken cancellationToken)
	{
		// The callback captures the client instance that negotiated resolve support, and the instance
		// never changes; the re-check below still covers a capability reset since the capture.
		if (!client.SupportsCompletionResolve)
			return unresolvedItem;

		try
		{
			CompletionItemPayload? resolvedItem = await RequestDispatcher.SendAsync<CompletionItemPayload?>("completionItem/resolve", itemPayload,
				fallbackValue: null, cancellationToken).ConfigureAwait(false);

			if (resolvedItem is not null)
			{
				TextCompletionItem? parsedItem = LuaLanguageServerResponseParser.ParseCompletionItem(resolvedItem, priorityRank, content);

				if (parsedItem is not null)
					return unresolvedItem.WithResolvedContent(parsedItem);
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception exception)
		{
			Logger.LogWarning(exception, "Failed to resolve Lua completion item '{Label}'; falling back to the unresolved item.", unresolvedItem.Label);
		}

		return unresolvedItem;
	}
}

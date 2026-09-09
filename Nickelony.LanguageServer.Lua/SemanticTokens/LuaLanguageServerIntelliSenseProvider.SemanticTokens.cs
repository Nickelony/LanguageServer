namespace Nickelony.LanguageServer.Lua;

public sealed partial class LuaLanguageServerIntelliSenseProvider
{
	// Semantic-token refreshes run concurrently, but bounded: a session with many open documents
	// must not send its whole burst on every refresh request.
	private const int MaxConcurrentSemanticTokenRefreshes = 4;

	/// <inheritdoc/>
	/// <remarks>
	/// The refresh runs detached from the document-sync pipeline: a stalled semantic-tokens round
	/// trip must not delay the next <c>didChange</c> notification or the replay of the remaining
	/// documents after a restart. Per-document supersession is arbitrated by
	/// <see cref="ReplaceSemanticTokenRequest"/>.
	/// </remarks>
	protected override Task OnDocumentSynchronizedAsync(DocumentSnapshot document, CancellationToken cancellationToken)
	{
		ObserveBackgroundTask(RefreshSemanticTokensAsync(document, CancellationToken.None), "Semantic tokens refresh");

		return Task.CompletedTask;
	}

	// LuaLS sends workspace/semanticTokens/refresh when a watched configuration file changes; the
	// fan-out mirrors the document-sync trigger but starts from the open-document snapshot.
	private void HandleSemanticTokensRefreshRequested(object? sender, EventArgs e)
		=> ObserveBackgroundTask(RefreshTrackedSemanticTokensAsync(CancellationToken.None), "Semantic tokens refresh");

	private async Task RefreshTrackedSemanticTokensAsync(CancellationToken cancellationToken)
	{
		if (IsDisposed || Client is null || !Client.SupportsSemanticTokensFull || Client.SemanticTokenTypes.Count == 0)
			return;

		IReadOnlyList<DocumentSnapshot> documents = DocumentStore.GetOpenDocuments();

		// Every document's refresh is superseded and canceled independently, so the fan-out runs
		// concurrently through a shared gate instead of serializing N server round trips behind
		// each other or sending an unbounded burst.
		using var concurrencyGate = new SemaphoreSlim(MaxConcurrentSemanticTokenRefreshes);
		var refreshes = new Task[documents.Count];

		for (int i = 0; i < documents.Count; i++)
			refreshes[i] = RefreshGatedSemanticTokensAsync(documents[i], concurrencyGate, cancellationToken);

		await Task.WhenAll(refreshes).ConfigureAwait(false);
	}

	private async Task RefreshGatedSemanticTokensAsync(DocumentSnapshot document, SemaphoreSlim concurrencyGate,
		CancellationToken cancellationToken)
	{
		await concurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			await RefreshSemanticTokensAsync(document, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			concurrencyGate.Release();
		}
	}

	/// <summary>
	/// Refreshes the semantic tokens for a tracked document with a full request, superseding any
	/// in-flight request for the same document. A failed round trip keeps the previously cached
	/// tokens: the last known token set is a better fallback than dropping the highlighting until
	/// the next successful refresh.
	/// </summary>
	private async Task RefreshSemanticTokensAsync(DocumentSnapshot document, CancellationToken cancellationToken)
	{
		if (Client is null || !Client.SupportsSemanticTokensFull || Client.SemanticTokenTypes.Count == 0)
			return;

		CancellationToken effectiveToken = ReplaceSemanticTokenRequest(document.FilePath, cancellationToken, out CancellationTokenSource? linkedSource);

		try
		{
			SemanticTokensWireResponse? response = await SendSemanticTokensRequestAsync(document, effectiveToken)
				.ConfigureAwait(false);

			if (IsDisposed || effectiveToken.IsCancellationRequested)
				return;

			SemanticTokensDeltaResponse parsedResponse = SemanticTokensDeltaParser.Parse(response);

			if (parsedResponse.Data is not { } data)
			{
				Logger.LogDebug("Lua semantic tokens response for '{FilePath}' did not contain a usable token stream; keeping the previously cached tokens.",
					document.FilePath);

				return;
			}

			IReadOnlyList<SemanticToken> semanticTokens = SemanticTokensDecoder.Decode(
				data, document.Content, Client.SemanticTokenTypes, Client.SemanticTokenModifiers);

			// Persist and announce only when the decoded tokens match the currently tracked document
			// version; otherwise the payload was decoded against a stale snapshot.
			if (!DocumentStore.TryStoreSemanticTokens(document.FilePath, document.Version, semanticTokens))
				return;

			RaiseSemanticTokensUpdated(document.FilePath, semanticTokens);
		}
		catch (OperationCanceledException) when (effectiveToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
		{
			// Provider-owned cancellation superseded this request because a newer request replaced it, the document closed,
			// or the provider was disposed.
		}
		catch (OperationCanceledException)
		{
			// Caller-owned cancellation rethrows without reaching the warning path below.
			throw;
		}
		catch (IOException exception)
		{
			// Defense in depth: the request dispatcher already converts transport failures to the
			// fallback value; these catches keep the refresh resilient if that conversion contracts.
			Logger.LogDebug(exception, "Lua semantic tokens request failed for '{FilePath}' due to a transport error; keeping the previously cached tokens.",
				document.FilePath);
		}
		catch (ObjectDisposedException)
		{
			// Defense in depth: the dispatcher already absorbs a torn-down client.
		}
		catch (Exception exception)
		{
			Logger.LogWarning(exception, "Lua semantic tokens request failed for '{FilePath}'; keeping the previously cached tokens.",
				document.FilePath);
		}
		finally
		{
			ClearSemanticTokenRequest(document.FilePath, linkedSource);
		}
	}

	private Task<SemanticTokensWireResponse?> SendSemanticTokensRequestAsync(DocumentSnapshot document, CancellationToken cancellationToken)
		=> RequestDispatcher.SendAsync<SemanticTokensWireResponse?>(
			"textDocument/semanticTokens/full",
			new SemanticTokensParams(new TextDocumentIdentifier(document.Uri)),
			fallbackValue: null,
			cancellationToken);

	private CancellationToken ReplaceSemanticTokenRequest(string filePath, CancellationToken cancellationToken, out CancellationTokenSource? linkedSource)
	{
		if (_semanticTokenRequestAdmissionClosed)
		{
			linkedSource = null;
			return new CancellationToken(canceled: true);
		}

		var freshSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

		// The token is captured before the source is published: once it is in the map a concurrent
		// refresh or the disposal drain may cancel and dispose it, and reading Token from a disposed
		// source throws ObjectDisposedException.
		CancellationToken freshToken = freshSource.Token;
		CancellationTokenSource? previousSource = null;

		// Replace the tracked source with an explicit retry loop instead of AddOrUpdate: the update
		// factory can run more than once under contention, and a losing invocation would leak its
		// fresh source. Every source that is actually replaced here is canceled below.
		while (true)
		{
			if (_semanticTokenRequests.TryGetValue(filePath, out CancellationTokenSource? existing))
			{
				if (!_semanticTokenRequests.TryUpdate(filePath, freshSource, existing))
					continue;

				previousSource = existing;
				break;
			}

			if (_semanticTokenRequests.TryAdd(filePath, freshSource))
				break;
		}

		// A refresh that raced provider disposal can slip past the admission check above while the
		// cancel-all drain runs; remove and dispose the fresh source here so a disposed provider never
		// strands one. The superseded source was replaced after the drain enumerated the map, so the
		// drain can no longer see it either and it is canceled here.
		if (_semanticTokenRequestAdmissionClosed)
		{
			_semanticTokenRequests.TryRemove(new KeyValuePair<string, CancellationTokenSource>(filePath, freshSource));
			CancelAndDispose(freshSource);
			CancelAndDispose(previousSource);
			linkedSource = null;
			return new CancellationToken(canceled: true);
		}

		// Publish the fresh source before canceling the superseded one so the caller can always
		// release what this call tracked.
		linkedSource = freshSource;
		CancelAndDispose(previousSource);
		return freshToken;
	}

	private void ClearSemanticTokenRequest(string filePath, CancellationTokenSource? linkedSource)
	{
		if (linkedSource is null)
			return;

		_semanticTokenRequests.TryRemove(new KeyValuePair<string, CancellationTokenSource>(filePath, linkedSource));
		linkedSource.Dispose();
	}

	private void CancelSemanticTokenRequest(string filePath)
	{
		if (_semanticTokenRequests.TryRemove(filePath, out CancellationTokenSource? source))
			CancelAndDispose(source);
	}

	private void CancelAllSemanticTokenRequests()
	{
		// Close admission before draining so a racing refresh cannot re-add a source after the drain:
		// a refresh that already published its source validates admission again after the add and
		// releases the source itself when admission has closed.
		_semanticTokenRequestAdmissionClosed = true;

		if (_semanticTokenRequests.IsEmpty)
			return;

		foreach (KeyValuePair<string, CancellationTokenSource> entry in _semanticTokenRequests)
		{
			if (_semanticTokenRequests.TryRemove(entry.Key, out CancellationTokenSource? source))
				CancelAndDispose(source);
		}
	}

	private static void CancelAndDispose(CancellationTokenSource? source)
	{
		if (source is null)
			return;

		try
		{
			source.Cancel();
		}
		catch (Exception exception) when (exception is ObjectDisposedException or AggregateException)
		{
			// Cancellation callbacks registered on provider-owned sources must not escape cleanup:
			// AggregateException surfaces a throwing callback, ObjectDisposedException a source the
			// drain already released.
		}
		finally
		{
			source.Dispose();
		}
	}
}

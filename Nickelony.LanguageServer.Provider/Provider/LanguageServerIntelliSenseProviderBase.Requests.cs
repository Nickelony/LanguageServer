namespace Nickelony.LanguageServer.Provider;

public abstract partial class LanguageServerIntelliSenseProviderBase<TDocumentState>
{
	// Upper bound for following a renamed record while completing its deferred close: each hop completes the close
	// through the record's current path, and a rename beyond this bound leaves the close pending for a later release.
	private const int PendingCloseRenameFollowLimit = 4;

	/// <summary>
	/// Synchronizes the requested document, executes a document-scoped language-server request, and
	/// releases the temporary request reference afterwards.
	/// </summary>
	/// <typeparam name="TResponse">The expected response payload type.</typeparam>
	/// <typeparam name="TResult">The parsed result type.</typeparam>
	/// <param name="filePath">The absolute local file path of the document.</param>
	/// <param name="content">The current document content.</param>
	/// <param name="method">The LSP method name.</param>
	/// <param name="supportsRequest">Reports whether the connected client supports the request.</param>
	/// <param name="buildParameters">Builds the request parameters from the normalized document identifier.</param>
	/// <param name="parseResponse">Parses the response payload.</param>
	/// <param name="fallbackValue">The fallback value returned when the request cannot be issued, the server rejects it, or the response is unusable.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The parsed result, or <paramref name="fallbackValue"/>.</returns>
	/// <remarks>
	/// <para>
	/// The capability gate is evaluated after the transport is ensured but before the document is synchronized,
	/// so a request the connected client does not support neither synchronizes nor temporarily tracks the
	/// document. The gate cannot run before startup: negotiated capabilities only exist after the initialize
	/// handshake. A response payload that cannot be deserialized as <typeparamref name="TResponse"/> is treated
	/// like any other unusable response and returns the fallback value.
	/// </para>
	/// <para>
	/// A throwing <paramref name="buildParameters"/> or <paramref name="parseResponse"/> delegate propagates
	/// unchanged: those delegates are caller-supplied code rather than framework policy.
	/// </para>
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/>, <paramref name="content"/>, <paramref name="method"/>,
	/// <paramref name="supportsRequest"/>, <paramref name="buildParameters"/>, or <paramref name="parseResponse"/> is
	/// <see langword="null"/>.
	/// </exception>
	protected async Task<TResult> SendDocumentRequestAsync<TResponse, TResult>(
		string filePath, string content,
		string method,
		Func<ILanguageServerClient, bool> supportsRequest,
		Func<TextDocumentIdentifier, object> buildParameters,
		Func<TResponse, TResult> parseResponse,
		TResult fallbackValue,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(content);
		ArgumentNullException.ThrowIfNull(method);
		ArgumentNullException.ThrowIfNull(supportsRequest);
		ArgumentNullException.ThrowIfNull(buildParameters);
		ArgumentNullException.ThrowIfNull(parseResponse);

		cancellationToken.ThrowIfCancellationRequested();

		ILanguageServerClient? client = _client;

		if (client is null)
		{
			ReportMissingClientFailure();
			return fallbackValue;
		}

		if (!TryNormalizeDocumentEntryPath(filePath, "Request", out string? normalizedFilePath))
			return fallbackValue;

		// Tracks the temporary request reference this request acquired, so the release below never consumes a
		// reference owned by a concurrent request and still finds the record when it is renamed meanwhile.
		var requestReference = new DocumentRequestReference();

		try
		{
			if (!await TryEnsureStartedForOperationAsync(cancellationToken).ConfigureAwait(false))
				return fallbackValue;

			// Evaluate the capability gate before synchronizing: synchronizing a document for a request the
			// server can never answer would send avoidable open/close traffic for idle documents.
			if (!supportsRequest(client))
				return fallbackValue;

			// Keep post-edit refresh owned by UpdateDocument so request-driven synchronization does not
			// issue an additional refresh for every IntelliSense request.
			if (!await SynchronizeDocumentAsync(normalizedFilePath, content,
				acquireOpenReference: false, acquireRequestReference: true, refreshDocument: false,
				requestReference, cancellationToken).ConfigureAwait(false))
			{
				return fallbackValue;
			}

			// Dispatch the request against the normalized document URI and classify the outcome instead of the
			// response value: a non-nullable struct response never compares equal to null, so a null check would
			// silently parse a default value instead of returning the fallback value.
			var textDocument = new TextDocumentIdentifier(LanguageServerPaths.CreateFileUri(normalizedFilePath));

			var (succeeded, response) = await _requestDispatcher.SendOutcomeAsync<TResponse>(method,
				buildParameters(textDocument), cancellationToken).ConfigureAwait(false);

			if (!succeeded || response is null)
				return fallbackValue;

			return parseResponse(response!);
		}
		finally
		{
			// Release only the reference this request actually acquired, and release it by identity: releasing
			// unconditionally can consume a concurrent request's reference, while a path-keyed release would miss
			// the record when it was renamed while the request was in flight.
			if (requestReference.IsAcquired)
				await ReleaseRequestDocumentAsync(requestReference, cancellationToken).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Synchronizes the requested document, executes a position-based language-server request, and
	/// releases the temporary request reference afterwards.
	/// </summary>
	/// <typeparam name="TResponse">The expected response payload type.</typeparam>
	/// <typeparam name="TResult">The parsed result type.</typeparam>
	/// <param name="filePath">The absolute local file path of the document.</param>
	/// <param name="content">The current document content.</param>
	/// <param name="line">The zero-based line index. Negative values are clamped to zero.</param>
	/// <param name="column">The zero-based column index. Negative values are clamped to zero.</param>
	/// <param name="method">The LSP method name.</param>
	/// <param name="buildParameters">Builds the request parameters from the normalized document identifier and clamped position.</param>
	/// <param name="parseResponse">Parses the response payload.</param>
	/// <param name="fallbackValue">The fallback value returned when the request cannot be issued, the server rejects it, or the response is unusable.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The parsed result, or <paramref name="fallbackValue"/>.</returns>
	/// <remarks>
	/// Position-based requests pass a capability gate that always grants them, because the Abstractions contract
	/// defines completion, hover, definition, and signature help as always attempted; a server that does not
	/// implement one of them rejects the request and the documented fallback value is returned.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/>, <paramref name="content"/>, <paramref name="method"/>,
	/// <paramref name="buildParameters"/>, or <paramref name="parseResponse"/> is
	/// <see langword="null"/>.
	/// </exception>
	protected Task<TResult> SendDocumentPositionRequestAsync<TResponse, TResult>(
		string filePath, string content, int line, int column,
		string method,
		Func<TextDocumentIdentifier, ProtocolPosition, object> buildParameters,
		Func<TResponse, TResult> parseResponse,
		TResult fallbackValue,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(content);
		ArgumentNullException.ThrowIfNull(method);
		ArgumentNullException.ThrowIfNull(buildParameters);
		ArgumentNullException.ThrowIfNull(parseResponse);

		var position = new ProtocolPosition(Math.Max(0, line), Math.Max(0, column));

		return SendDocumentRequestAsync<TResponse, TResult>(
			filePath, content, method,
			supportsRequest: static _ => true,
			buildParameters: textDocument => buildParameters(textDocument, position),
			parseResponse, fallbackValue, cancellationToken);
	}

	/// <summary>
	/// Ensures the transport is running before an operation enters its per-document scheduler slot, containing the
	/// startup failure modes a slot cannot recover from.
	/// </summary>
	/// <param name="cancellationToken">The caller's cancellation token.</param>
	/// <returns><see langword="true"/> when the transport is ready; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="OperationCanceledException">The caller's cancellation token was canceled.</exception>
	private async Task<bool> TryEnsureStartedForOperationAsync(CancellationToken cancellationToken)
	{
		try
		{
			return await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (OperationCanceledException) when (_isDisposed)
		{
			return false;
		}
		catch (OperationCanceledException)
		{
			// An unowned cancellation (neither the caller token nor disposal) is classified like the dispatcher
			// classifies it: the documented fallback value instead of a leaked cancellation.
			return false;
		}
		catch (IOException)
		{
			MarkStartupTransportUnavailable();
			return false;
		}
		catch (ObjectDisposedException)
		{
			if (!_isDisposed)
				MarkStartupTransportUnavailable();

			return false;
		}
	}

	/// <summary>
	/// Releases one request reference inside the record's per-document scheduler slot and completes a close that was
	/// deferred while the reference was active.
	/// </summary>
	/// <param name="requestReference">The reference bound to the tracked record.</param>
	/// <param name="cancellationToken">The caller's cancellation token; the release still runs when it is canceled.</param>
	private async Task ReleaseRequestDocumentAsync(DocumentRequestReference requestReference, CancellationToken cancellationToken)
	{
		if (_client is null || requestReference.CurrentFilePath is not { } releasePath)
			return;

		try
		{
			IReadOnlyList<DocumentSnapshot> documentsToClose = await _documentScheduler.EnqueuePerDocumentAsync(
				releasePath,
				async token =>
				{
					// Caller cancellation must not skip request-reference cleanup, otherwise a canceled
					// IntelliSense request can leave a request-only tracked document pinned indefinitely.
					// The release is bound to the record, so a rename that rekeyed it still releases here.
					_documents.ReleaseRequest(requestReference);

					// A rename that landed before this release ran moved any deferred close to the record's current
					// path; completing it needs that path's own scheduler slot, so the follow-up loop handles it.
					if (requestReference.CurrentFilePath is { } currentPath && LanguageServerPaths.AreLocalPathsEqual(currentPath, releasePath))
						await CompletePendingDocumentCloseAsync(releasePath).ConfigureAwait(false);

					return _documents.TrimIdleDocuments(_options.MaxTrackedIdleDocuments);
				},
				CancellationToken.None).ConfigureAwait(false);

			for (int i = 0; i < documentsToClose.Count; i++)
				await CloseTrimmedDocumentAsync(documentsToClose[i]).ConfigureAwait(false);

			await FollowRenamedDocumentCloseAsync(requestReference, releasePath).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _isDisposed)
		{ }
		catch (Exception exception)
		{
			LogBestEffortFailure("request-document release", releasePath, exception);
		}
	}

	/// <summary>
	/// Follows a record that was renamed while its request reference was being released and completes its deferred
	/// close through the record's current path.
	/// </summary>
	/// <param name="requestReference">The released reference bound to the record.</param>
	/// <param name="releasePath">The document path whose scheduler slot the release already went through.</param>
	private async Task FollowRenamedDocumentCloseAsync(DocumentRequestReference requestReference, string releasePath)
	{
		for (int i = 0; i < PendingCloseRenameFollowLimit; i++)
		{
			if (requestReference.CurrentFilePath is not { } currentPath
				|| LanguageServerPaths.AreLocalPathsEqual(currentPath, releasePath)
				|| !_pendingDocumentCloses.ContainsKey(currentPath))
			{
				return;
			}

			string closePath = currentPath;

			await _documentScheduler.EnqueuePerDocumentAsync(
				closePath,
				async _ =>
				{
					await CompletePendingDocumentCloseAsync(closePath).ConfigureAwait(false);
					return true;
				},
				CancellationToken.None).ConfigureAwait(false);

			releasePath = closePath;
		}
	}

	/// <summary>
	/// Closes the server copy of one document evicted by idle-document trimming. The close runs through the evicted
	/// document's own scheduler slot and re-checks the tracked state first, so a record that was recreated after the
	/// trim keeps its server copy open.
	/// </summary>
	/// <param name="document">The snapshot evicted by the tracked-document store.</param>
	internal async Task CloseTrimmedDocumentAsync(DocumentSnapshot document)
	{
		if (_client is null || _isDisposed)
			return;

		try
		{
			await _documentScheduler.EnqueuePerDocumentAsync(
				document.FilePath,
				async token =>
				{
					// A concurrent open or update recreated the record after the trim; its own lifecycle owns the
					// server copy now, so an unguarded close here would close a document the client considers
					// open again.
					if (_documents.GetDocumentSnapshot(document.FilePath) is not null)
						return false;

					_pendingDocumentCloses.TryRemove(document.FilePath, out _);
					InvokeContainedHook(() => OnTrackedDocumentInvalidated(document.FilePath), "tracked-document invalidation");

					if (!_client.IsReady)
						return false;

					await SendDidCloseAsync(document, CancellationToken.None).ConfigureAwait(false);

					return true;
				},
				CancellationToken.None).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (_isDisposed)
		{ }
		catch (Exception exception)
		{
			LogBestEffortFailure("trimmed-document close", document.FilePath, exception);
		}
	}

	/// <summary>
	/// Completes a close that was deferred while a temporary request reference kept the document tracked.
	/// The caller must hold the document's scheduler slot.
	/// </summary>
	/// <param name="filePath">The normalized document path whose deferred close should be completed.</param>
	private async Task CompletePendingDocumentCloseAsync(string filePath)
	{
		if (!_pendingDocumentCloses.TryRemove(filePath, out _))
			return;

		DocumentCloseResult closeResult = _documents.TryClose(filePath, out DocumentSnapshot? document);

		if (closeResult == DocumentCloseResult.BusyWithRequests)
		{
			// Another request reference is still active; keep the close pending for its release.
			_pendingDocumentCloses[filePath] = 0;
			return;
		}

		if (closeResult != DocumentCloseResult.Closed || document is null)
			return;

		InvokeContainedHook(() => OnTrackedDocumentInvalidated(filePath), "tracked-document invalidation");

		// The startup-succeeded flag is deliberately not consulted (see the close path): a deferred close must
		// still run while a restart replay owns the server session.
		if (_client is null || !_client.IsReady)
			return;

		try
		{
			await SendDidCloseAsync(document, CancellationToken.None).ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			LogBestEffortFailure("deferred document close", filePath, exception);
		}
	}
}

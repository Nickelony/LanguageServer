using Nickelony.IDEKit.IntelliSense.Diagnostics;

namespace Nickelony.LanguageServer.Provider;

public abstract partial class LanguageServerIntelliSenseProviderBase<TDocumentState>
{
	/// <summary>
	/// Ensures the transport outside the document's scheduler slot and then opens the document.
	/// </summary>
	/// <param name="filePath">The normalized document path.</param>
	/// <param name="content">The initial document content.</param>
	/// <remarks>
	/// The transport must be ensured before a slot runs: a restart replay queues per-document scheduler
	/// operations, and queueing them from inside a slot is rejected by the scheduler and can deadlock the slot's
	/// own chain while it waits on the startup lock. A slot therefore never starts the transport itself; it only
	/// observes <see cref="IsTransportReadyForDocumentOperation"/> after this flow returns.
	/// </remarks>
	private async Task OpenDocumentAsync(string filePath, string content)
	{
		await TryEnsureStartedForOperationAsync(CancellationToken.None).ConfigureAwait(false);

		await SynchronizeDocumentAsync(filePath, content,
			acquireOpenReference: true,
			acquireRequestReference: false,
			refreshDocument: true,
			requestReference: null,
			CancellationToken.None).ConfigureAwait(false);
	}

	/// <summary>
	/// Ensures the transport outside the document's scheduler slot and then synchronizes the latest update.
	/// </summary>
	/// <param name="filePath">The normalized document path.</param>
	/// <param name="content">The latest content of the document.</param>
	/// <remarks>See <see cref="OpenDocumentAsync(string, string)"/> for why the transport is ensured outside the slot.</remarks>
	private async Task UpdateDocumentAsync(string filePath, string content)
	{
		await TryEnsureStartedForOperationAsync(CancellationToken.None).ConfigureAwait(false);

		await SynchronizeLatestDocumentAsync(filePath, content).ConfigureAwait(false);
	}

	/// <summary>
	/// Gets a value indicating whether the transport is ready for a per-document scheduler slot to synchronize against.
	/// </summary>
	/// <remarks>
	/// This is a lock-free readiness snapshot: a client that is ready and whose most recent startup completed on
	/// its current transport generation. Per-document scheduler slots must not start the transport themselves, so they use this
	/// check instead of <see cref="EnsureStartedAsync"/>.
	/// </remarks>
	private bool IsTransportReadyForDocumentOperation
		=> !_isDisposed
			&& _client is not null
			&& _client.IsReady
			&& _startupState.GetStartupSucceeded();

	/// <summary>
	/// Coalesces a document update into the latest-update slot and synchronizes it when the slot runs.
	/// </summary>
	/// <param name="filePath">The normalized document path.</param>
	/// <param name="content">The latest content of the document.</param>
	/// <returns><see langword="true"/> when the synchronization succeeded or was superseded; otherwise, <see langword="false"/>.</returns>
	private async Task<bool> SynchronizeLatestDocumentAsync(string filePath, string content)
	{
		if (_isDisposed)
			return false;

		if (_client is null)
		{
			ReportMissingClientFailure();
			return false;
		}

		try
		{
			// The latest-update node runs the synchronization as the scheduled operation itself. Queueing an inner
			// per-document operation from inside the delegate would deadlock the document's chain, so the in-slot
			// synchronization must not enqueue again.
			await _documentScheduler.EnqueueLatestUpdateAsync(filePath,
				token => SynchronizeDocumentInSlotAsync(filePath, content,
					acquireOpenReference: false,
					acquireRequestReference: false,
					refreshDocument: true,
					requestReference: null,
					token)).ConfigureAwait(false);

			return true;
		}
		catch (OperationCanceledException) when (_isDisposed)
		{
			// Disposal-driven cancellation surfaces as the documented false result.
			return false;
		}
		catch (IOException)
		{
			// The tracked synchronization was already invalidated by the send-site catch inside the operation;
			// invalidating again here would run after the chain slot released and could mark the document closed
			// after a queued operation already reopened it.
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

	private async Task<bool> SynchronizeDocumentAsync(string filePath, string content,
		bool acquireOpenReference, bool acquireRequestReference, bool refreshDocument,
		DocumentRequestReference? requestReference, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (_isDisposed)
			return false;

		if (_client is null)
		{
			ReportMissingClientFailure();
			return false;
		}

		try
		{
			DocumentSynchronizationResult synchronizationResult = await _documentScheduler.EnqueuePerDocumentAsync(
				filePath,
				token => SynchronizeDocumentInSlotAsync(filePath, content, acquireOpenReference, acquireRequestReference, refreshDocument, requestReference, token),
				cancellationToken).ConfigureAwait(false);

			return synchronizationResult.Success;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (OperationCanceledException) when (_isDisposed)
		{
			// Disposal-driven cancellation surfaces as the documented false result.
			return false;
		}
		catch (OperationCanceledException)
		{
			// An unowned cancellation (neither the caller token nor disposal) is classified like the dispatcher
			// classifies it: the documented false result instead of a leaked cancellation.
			return false;
		}
		catch (IOException)
		{
			// Already invalidated by the send-site catch inside the per-document scheduler slot; see
			// SynchronizeLatestDocumentAsync.
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
	/// Synchronizes one document while its scheduler slot is already held and refreshes the language-specific state
	/// when requested. The slot must not start or restart the transport; the entry points ensure it before the
	/// slot runs (see <see cref="OpenDocumentAsync(string, string)"/>).
	/// </summary>
	/// <param name="filePath">The normalized document path.</param>
	/// <param name="content">The content to synchronize.</param>
	/// <param name="acquireOpenReference">Whether the synchronization acquires an open reference for the document.</param>
	/// <param name="acquireRequestReference">Whether the synchronization acquires a request reference for the document.</param>
	/// <param name="refreshDocument">Whether the language-specific state should be refreshed after a successful synchronization.</param>
	/// <param name="requestReference">The reference that receives the request reference acquired for this synchronization, or <see langword="null"/> when the synchronization acquires no request reference.</param>
	/// <param name="cancellationToken">Cancels the synchronization.</param>
	/// <returns>The synchronization result produced by the tracked-document store.</returns>
	private async Task<DocumentSynchronizationResult> SynchronizeDocumentInSlotAsync(string filePath, string content,
		bool acquireOpenReference, bool acquireRequestReference, bool refreshDocument,
		DocumentRequestReference? requestReference, CancellationToken cancellationToken)
	{
		DocumentSynchronizationResult synchronizationResult = await SynchronizeDocumentCoreAsync(
			filePath, content, acquireOpenReference, acquireRequestReference, requestReference, cancellationToken).ConfigureAwait(false);

		// An editor-open reference cancels a close that was deferred while a temporary request reference kept the
		// document tracked: the editor owns the document again, so the deferred close must not run later.
		if (acquireOpenReference)
			_pendingDocumentCloses.TryRemove(filePath, out _);

		if (refreshDocument && synchronizationResult.Success && synchronizationResult.Document is { } synchronizedDocument)
			await InvokeContainedHookAsync(() => OnDocumentSynchronizedAsync(synchronizedDocument, cancellationToken), "post-synchronization").ConfigureAwait(false);

		return synchronizationResult;
	}

	/// <summary>
	/// Marks one document's tracked state as no longer mirrored to the server. Unlike
	/// <see cref="MarkStartupTransportUnavailable"/> this only touches tracked state, so callers can run it
	/// while the per-document scheduler slot is still held; that keeps the invalidation ordered before any
	/// operation queued behind a failed send, which then observes the invalidated state and reopens the
	/// document instead of trusting a server copy that never received the change.
	/// </summary>
	/// <param name="filePath">The document whose tracked server synchronization should be invalidated.</param>
	private void InvalidateTrackedServerSynchronization(string filePath)
	{
		InvokeContainedHook(() => InvalidateTrackedDocumentSynchronization(filePath), "tracked-document invalidation");
		InvokeContainedHook(() => OnTrackedDocumentInvalidated(filePath), "tracked-document invalidation");

		// Deliberately no CancelQueuedDocumentUpdate here: this runs while the update that just failed still
		// holds the document's scheduler slot, and any still-pending update is the newest one for the path.
		// It waits for the failed update to complete, so it observes the invalidated state and reopens the
		// document; canceling it would drop that recovery.
	}

	private async Task<bool> RenameDocumentAsync(string oldFilePath, string newFilePath, string content, CancellationToken cancellationToken)
	{
		if (_isDisposed || _client is null)
			return false;

		try
		{
			DocumentRenameRequest? request = await _documentScheduler.EnqueueExclusivePerDocumentAsync(
				oldFilePath,
				newFilePath,
				token => RenameDocumentCoreAsync(oldFilePath, newFilePath, content, token),
				cancellationToken).ConfigureAwait(false);

			if (request is not { } renameRequest)
				return false;

			string filePath = renameRequest.RenamedDocument.FilePath;
			RaiseDiagnosticsUpdated(filePath, InvokeContainedHook(() => GetTrackedDiagnostics(filePath), [], "tracked diagnostics"));
			InvokeContainedHook(() => OnDocumentRenamed(filePath), "document rename");

			return true;
		}
		// Rename runs with no caller cancellation token of its own: an OperationCanceledException can only be
		// disposal-driven and stays with the observed background task (the observer treats it as expected teardown).
		catch (IOException)
		{
			// Already invalidated by the send-site catch inside the exclusive rename slot; see SynchronizeLatestDocumentAsync.
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
	/// Synchronizes one document and refreshes the language-specific state when requested, classifying the final
	/// synchronization result for the caller.
	/// </summary>
	/// <param name="filePath">The normalized document path.</param>
	/// <param name="content">The content to synchronize.</param>
	/// <param name="acquireOpenReference">Whether the synchronization acquires an open reference for the document.</param>
	/// <param name="acquireRequestReference">Whether the synchronization acquires a request reference for the document.</param>
	/// <param name="requestReference">The reference that receives the request reference acquired for this synchronization, or <see langword="null"/> when the synchronization acquires no request reference.</param>
	/// <param name="cancellationToken">Cancels the synchronization.</param>
	/// <returns>The synchronization result produced by the tracked-document store.</returns>
	/// <remarks>
	/// When the transport is not ready, the content is committed locally only while the tracked records are not
	/// owned by a restart replay, so a local commit cannot reopen a record that the guarded replay then skips.
	/// </remarks>
	private async Task<DocumentSynchronizationResult> SynchronizeDocumentCoreAsync(
		string filePath,
		string content,
		bool acquireOpenReference,
		bool acquireRequestReference,
		DocumentRequestReference? requestReference,
		CancellationToken cancellationToken)
	{
		bool shouldTrackLocallyWhileUnavailable = acquireOpenReference || _documents.GetDocumentSnapshot(filePath) is not null;
		bool includeChangeRange = _client?.TextDocumentSyncKind == TextDocumentSyncKind.Incremental;

		// Per-document scheduler slots never start or restart the transport: the entry points ensure it before the slot runs
		// (see OpenDocumentAsync), because a restart replay queues per-document scheduler operations, which is
		// forbidden inside a running slot and would deadlock the chain while the slot waits on the startup lock.
		if (!IsTransportReadyForDocumentOperation)
		{
			// A restart attempt or a pending replay owns the tracked records, and a local content commit can
			// reopen a record that the guarded replay then skips, leaving a record that claims a server-open
			// document that was never sent. Skip the commit while that flow is in progress.
			if (shouldTrackLocallyWhileUnavailable
				&& !_isDisposed
				&& State != LanguageServerProviderState.Starting
				&& _pendingReopenDocuments is null)
			{
				_documents.Synchronize(filePath, content, acquireOpenReference, acquireRequestReference: false, includeChangeRange: includeChangeRange);
			}

			return new(false, null);
		}

		// The store binds the reference while it holds its store lock, so the request pipeline releases it on every
		// exit path, including a failed send or a store hook that throws after the acquisition.
		DocumentSynchronizationRequest? request = _documents.Synchronize(filePath, content, acquireOpenReference, acquireRequestReference,
			includeChangeRange: includeChangeRange, requestReference: requestReference);

		if (request is not { } pendingRequest)
			return new(true, null);

		try
		{
			await SendDocumentSynchronizationNotificationAsync(pendingRequest, cancellationToken).ConfigureAwait(false);
		}
		catch (IOException)
		{
			InvalidateTrackedServerSynchronization(filePath);
			throw;
		}
		catch (ObjectDisposedException)
		{
			if (!_isDisposed)
				InvalidateTrackedServerSynchronization(filePath);

			throw;
		}
		catch (OperationCanceledException)
		{
			// The content and version were committed before the send, so a cancellation that aborts the send (a
			// superseding open/close cancels a running update, or the caller cancels a request) can leave the tracked
			// record claiming a server state that was never delivered. Invalidate so the next synchronization reopens
			// the document instead of computing incremental ranges against unsent content.
			if (!_isDisposed)
				InvalidateTrackedServerSynchronization(filePath);

			throw;
		}

		return new(true, pendingRequest.Document);
	}

	/// <summary>
	/// Rekeys the tracked record and moves its server copy: closes the previous identity and opens the renamed
	/// document when a server session is running. The caller must hold the exclusive rename slot.
	/// </summary>
	/// <param name="oldFilePath">The normalized path of the tracked document.</param>
	/// <param name="newFilePath">The normalized replacement path.</param>
	/// <param name="content">The current document content.</param>
	/// <param name="cancellationToken">Cancels the sends.</param>
	/// <returns>The rename request when the document was rekeyed; otherwise, <see langword="null"/>.</returns>
	private async Task<DocumentRenameRequest?> RenameDocumentCoreAsync(
		string oldFilePath,
		string newFilePath,
		string content,
		CancellationToken cancellationToken)
	{
		if (_client is null)
			return null;

		DocumentRenameRequest? request = _documents.Rename(oldFilePath, newFilePath, content);

		if (request is not { } renameRequest)
			return null;

		InvokeContainedHook(() => OnTrackedDocumentInvalidated(oldFilePath), "tracked-document invalidation");
		InvokeContainedHook(() => OnTrackedDocumentInvalidated(newFilePath), "tracked-document invalidation");

		// A close deferred while a request reference kept the old path tracked follows the rekeyed document, so the
		// eventual request release still completes it.
		if (_pendingDocumentCloses.TryRemove(oldFilePath, out _))
			_pendingDocumentCloses[newFilePath] = 0;

		if (!renameRequest.ReopenServerDocument)
			return renameRequest;

		// The startup-succeeded flag is deliberately not consulted: during a restart replay the server is already
		// running, and a document that the replay reopened carries a live server copy that the rename must move.
		if (!_client.IsReady)
		{
			InvokeContainedHook(() => InvalidateTrackedDocumentSynchronization(newFilePath), "tracked-document invalidation");
			return renameRequest;
		}

		try
		{
			if (renameRequest.PreviousDocument is not null)
				await SendDidCloseAsync(renameRequest.PreviousDocument, cancellationToken).ConfigureAwait(false);

			await SendDocumentSynchronizationNotificationAsync(
				new DocumentSynchronizationRequest(DocumentSynchronizationKind.Open, renameRequest.RenamedDocument),
				cancellationToken).ConfigureAwait(false);
		}
		catch (IOException)
		{
			InvalidateTrackedServerSynchronization(newFilePath);
			throw;
		}
		catch (ObjectDisposedException)
		{
			if (!_isDisposed)
				InvalidateTrackedServerSynchronization(newFilePath);

			throw;
		}

		return renameRequest;
	}

	/// <summary>
	/// Reopens the tracked documents captured before a restart, each through its own per-document scheduler slot so
	/// a concurrent close or retracking cannot race the reopen.
	/// </summary>
	/// <param name="documents">The snapshots captured by the tracked-document store before the restart.</param>
	/// <param name="cancellationToken">Cancels the replay.</param>
	/// <returns>
	/// <see langword="null"/> when every document was processed; otherwise, the documents that still need a reopen,
	/// starting with the one whose reopen failed, so a later start can resume the replay.
	/// </returns>
	private async Task<IReadOnlyList<DocumentSnapshot>?> ReopenTrackedDocumentsAsync(IReadOnlyList<DocumentSnapshot> documents, CancellationToken cancellationToken)
	{
		for (int i = 0; i < documents.Count; i++)
		{
			DocumentSnapshot document = documents[i];

			try
			{
				await _documentScheduler.EnqueuePerDocumentAsync(
					document.FilePath,
					async token =>
					{
						// The guarded reopen only mirrors documents that are still tracked with an open-editor
						// reference, so a document that was closed or retracked while the restart was in flight is
						// skipped instead of leaving a phantom server-open record behind.
						DocumentSynchronizationRequest? request = _documents.TryReopenTrackedDocument(document.FilePath);

						if (request is not { } pendingRequest)
							return false;

						try
						{
							await SendDocumentSynchronizationNotificationAsync(pendingRequest, token).ConfigureAwait(false);
							await InvokeContainedHookAsync(() => OnDocumentSynchronizedAsync(pendingRequest.Document, token), "post-synchronization").ConfigureAwait(false);
						}
						catch (IOException)
						{
							InvalidateTrackedServerSynchronization(document.FilePath);
							throw;
						}
						catch (ObjectDisposedException)
						{
							if (!_isDisposed)
								InvalidateTrackedServerSynchronization(document.FilePath);

							throw;
						}
						catch (OperationCanceledException)
						{
							if (!_isDisposed)
								InvalidateTrackedServerSynchronization(document.FilePath);

							throw;
						}

						return true;
					},
					cancellationToken).ConfigureAwait(false);
			}
			catch (IOException)
			{
				return [.. documents.Skip(i)];
			}
			catch (ObjectDisposedException)
			{
				return [.. documents.Skip(i)];
			}
		}

		return null;
	}

	/// <summary>
	/// Sends the open or change notification for one synchronization request; a change for a session without a
	/// negotiated synchronization mode is skipped with a warning.
	/// </summary>
	/// <param name="request">The synchronization request to deliver.</param>
	/// <param name="cancellationToken">Cancels the send.</param>
	private async Task SendDocumentSynchronizationNotificationAsync(DocumentSynchronizationRequest request, CancellationToken cancellationToken)
	{
		if (_client is null)
			return;

		if (request.Kind == DocumentSynchronizationKind.Open)
		{
			await _client.SendNotificationAsync(LspMethodNames.DidOpen,
				new DidOpenTextDocumentParams(
					new DidOpenTextDocumentPayload(
						request.Document.Uri,
						LanguageId,
						request.Document.Version,
						request.Document.Content)),
				cancellationToken).ConfigureAwait(false);
		}
		else if (request.Kind == DocumentSynchronizationKind.Change)
		{
			// A session without a negotiated synchronization mode cannot express the change and may also report
			// None after its active transport was detached. Skip the change with a warning instead of failing the
			// operation: the tracked content stays committed, nothing is sent while the mode is unavailable, and
			// the next session re-establishes the full content through the restart replay.
			if (_client.TextDocumentSyncKind == TextDocumentSyncKind.None)
			{
				_logger.LogWarning("{DisplayName} skipped a document change for '{FilePath}' because the language server session negotiated no document synchronization mode.",
					ProviderDisplayName, request.Document.FilePath);
				return;
			}

			TextDocumentContentChangePayload contentChange = _client.TextDocumentSyncKind switch
			{
				TextDocumentSyncKind.Incremental when request.ChangeRange is { } changeRange => new(
					changeRange.Text,
					new ProtocolRangePayload(
						new ProtocolPosition(changeRange.StartLine, changeRange.StartCharacter),
						new ProtocolPosition(changeRange.EndLine, changeRange.EndCharacter))),

				// Full synchronization, and incremental requests without a computed range, send the whole content.
				_ => new(request.Document.Content)
			};

			await _client.SendNotificationAsync(LspMethodNames.DidChange,
				new DidChangeTextDocumentParams(
					new VersionedTextDocumentIdentifier(request.Document.Uri, request.Document.Version),
					[contentChange]),
				cancellationToken).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Closes one document through its per-document scheduler slot: releases the last open reference, defers the
	/// close while a request reference is still active, and forwards <c>textDocument/didClose</c> when a server
	/// session is running.
	/// </summary>
	/// <param name="filePath">The normalized document path to close.</param>
	/// <param name="cancellationToken">Cancels the close operation.</param>
	private async Task CloseDocumentAsync(string filePath, CancellationToken cancellationToken)
	{
		if (_client is null)
			return;

		try
		{
			await _documentScheduler.EnqueuePerDocumentAsync(
				filePath,
				async token =>
				{
					DocumentCloseResult closeResult = _documents.TryClose(filePath, out DocumentSnapshot? document);

					if (closeResult == DocumentCloseResult.BusyWithRequests)
					{
						// The close released the last editor-open reference, but a temporary request reference still
						// keeps the document tracked. Remember the intent so the release path completes the close
						// instead of leaving the server copy open indefinitely.
						_pendingDocumentCloses[filePath] = 0;
						return false;
					}

					_pendingDocumentCloses.TryRemove(filePath, out _);

					if (closeResult != DocumentCloseResult.Closed)
						return false;

					// The document just dropped its last open reference; clear language-specific in-flight
					// work for it now that no consumer path will display the result.
					InvokeContainedHook(() => OnTrackedDocumentInvalidated(filePath), "tracked-document invalidation");

					// Only forward the close notification when a server session is running: starting the server just
					// to send didClose would be wasteful and can race with disposal. The startup-succeeded flag is
					// deliberately not consulted here, so a close that lands during a restart replay still closes a
					// document the replay already reopened instead of leaving a phantom server-open copy behind.
					if (document is null || !_client.IsReady)
						return false;

					await SendDidCloseAsync(document, token).ConfigureAwait(false);

					return true;
				},
				cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _isDisposed)
		{
			_logger.LogDebug("{DisplayName} best-effort document close for '{FilePath}' was canceled because the request token fired or the provider was disposed.",
				ProviderDisplayName, filePath);
		}
		catch (Exception exception)
		{
			LogBestEffortFailure("document close", filePath, exception);
		}
	}

	private void HandleDiagnosticsPublished(object? sender, DiagnosticsPublishedEventArgs eventArgs)
	{
		if (_isDisposed)
			return;

		PublishDiagnosticsParams parameters = eventArgs.Parameters;

		if (!LanguageServerPaths.TryGetLocalPath(parameters.Uri, out string filePath))
		{
			_logger.LogDebug("{DisplayName} diagnostics for URI '{Uri}' could not be matched to a local file path.",
				ProviderDisplayName, parameters.Uri);
			return;
		}

		DocumentSnapshot? document = _documents.GetDocumentSnapshot(filePath);

		// Diagnostics for documents that are not tracked locally are ignored: without a tracked snapshot, there is no
		// synchronized content to map them to document ranges, and reading the file from disk on the LSP read loop just to
		// discard the result is wasteful.
		if (document is null)
			return;

		IReadOnlyList<TextDiagnostic>? diagnostics = InvokeContainedHook<IReadOnlyList<TextDiagnostic>?>(
			() => HandleDiagnosticsPayload(filePath, parameters, document),
			fallbackValue: null,
			"diagnostics payload handling");

		if (_isDisposed || diagnostics is null)
			return;

		RaiseDiagnosticsUpdated(filePath, diagnostics);
	}

	private void CancelQueuedDocumentUpdate(string filePath)
		=> _documentScheduler.CancelQueuedUpdate(filePath);

	private void CancelAllQueuedDocumentUpdates()
		=> _documentScheduler.CancelAllQueuedUpdates();
}

namespace Nickelony.LanguageServer.Client;

public abstract partial class TrackedDocumentStore<TTrackedDocumentState>
	where TTrackedDocumentState : TrackedDocumentState
{
	/// <summary>
	/// Releases one temporary request-driven reference for <paramref name="filePath"/> without
	/// immediately evicting the cached request-only document.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <remarks>
	/// The record stays tracked even when this release leaves it idle, so a stale request release cannot silently
	/// evict an idle server-open record and cause close/reopen churn. Idle records are removed by
	/// <see cref="TryClose"/> or <see cref="TrimIdleDocuments"/>; <see cref="TryReleaseRequest"/> removes a record
	/// only when that call itself consumes the last request reference.
	/// </remarks>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="filePath"/> is empty or whitespace-only, or the path is invalid on the current platform.</exception>
	public void ReleaseRequest(string filePath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

		string normalizedFilePath = LanguageServerPaths.NormalizeLocalPath(filePath);

		lock (_syncRoot)
		{
			if (!_documents.TryGetValue(normalizedFilePath, out TTrackedDocumentState? state))
				return;

			state.References.ReleaseRequest();
			TouchTrackedDocumentState(state, GetNextAccessStamp());
		}
	}

	/// <summary>
	/// Releases one temporary request-driven reference acquired through <c>Synchronize</c> by identity, so the
	/// release also finds the record after a rename rekeyed it.
	/// </summary>
	/// <param name="requestReference">The reference bound to the acquisition.</param>
	/// <remarks>
	/// The reference is released exactly once; releasing an unbound reference or releasing the same reference again
	/// is a no-op. The record is not evicted here: idle records are removed by <see cref="TryClose"/> or
	/// <see cref="TrimIdleDocuments"/>.
	/// </remarks>
	/// <exception cref="ArgumentNullException"><paramref name="requestReference"/> is <see langword="null"/>.</exception>
	public void ReleaseRequest(DocumentRequestReference requestReference)
	{
		ArgumentNullException.ThrowIfNull(requestReference);

		if (!requestReference.TryClaimRelease(out TrackedDocumentState? state))
			return;

		lock (_syncRoot)
		{
			state.References.ReleaseRequest();

			if (_documents.TryGetValue(state.FilePath, out TTrackedDocumentState? trackedState) && ReferenceEquals(trackedState, state))
				TouchTrackedDocumentState(trackedState, GetNextAccessStamp());
		}
	}

	/// <summary>
	/// Releases one temporary request-driven reference for <paramref name="filePath"/> and
	/// removes the document from local tracking when no references remain.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <param name="closingDocument">When this method returns, contains the closing snapshot if the server copy is still open.</param>
	/// <returns><see langword="true"/> when the document was removed locally; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// A release that finds no request reference to consume (a stale or double release) leaves the document tracked
	/// even when the record is already idle, so it cannot silently evict an idle server-open record and cause
	/// close/reopen churn.
	/// </remarks>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="filePath"/> is empty or whitespace-only, or the path is invalid on the current platform.</exception>
	public bool TryReleaseRequest(string filePath, out DocumentSnapshot? closingDocument)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

		string normalizedFilePath = LanguageServerPaths.NormalizeLocalPath(filePath);

		lock (_syncRoot)
		{
			if (!_documents.TryGetValue(normalizedFilePath, out TTrackedDocumentState? state))
			{
				closingDocument = null;
				return false;
			}

			bool hadRequestReference = state.References.RequestReferenceCount > 0;

			state.References.ReleaseRequest();
			TouchTrackedDocumentState(state, GetNextAccessStamp());

			if (!hadRequestReference || !state.References.IsIdle)
			{
				closingDocument = null;
				return false;
			}

			closingDocument = state.IsOpen ? state.CreateSnapshot() : null;
			_documents.Remove(normalizedFilePath);
			return true;
		}
	}

	/// <summary>
	/// Evicts the oldest idle documents until at most <paramref name="maxCount"/> idle documents remain tracked.
	/// </summary>
	/// <param name="maxCount">The maximum number of idle documents to keep tracked.</param>
	/// <remarks>
	/// Idle means the document has no open or request references, so idle server-open records are also eligible.
	/// The snapshots of open evicted records are returned so the caller can close the server copies; an evicted
	/// record that was already closed is removed without a snapshot. Deliver each close through the
	/// same serialization the host uses for that document path (for example the path's per-document scheduler slot)
	/// and re-check the tracked state when it runs: a concurrent open can recreate the record, and an unguarded
	/// closing notification would close a server document that the client considers open again.
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="maxCount"/> is negative.</exception>
	public IReadOnlyList<DocumentSnapshot> TrimIdleDocuments(int maxCount)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(maxCount);

		lock (_syncRoot)
		{
			if (_documents.Count <= maxCount)
				return [];

			var candidates = new List<TTrackedDocumentState>();

			foreach (TTrackedDocumentState state in _documents.Values)
			{
				if (state.References.IsIdle)
					candidates.Add(state);
			}

			if (candidates.Count <= maxCount)
				return [];

			candidates.Sort((left, right) => GetLastAccessStamp(left).CompareTo(GetLastAccessStamp(right)));

			int removeCount = candidates.Count - maxCount;
			var documentsToClose = new List<DocumentSnapshot>(removeCount);

			for (int i = 0; i < removeCount; i++)
			{
				TTrackedDocumentState state = candidates[i];
				_documents.Remove(state.FilePath);

				if (state.IsOpen)
					documentsToClose.Add(state.CreateSnapshot());
			}

			return documentsToClose;
		}
	}

	/// <summary>
	/// Releases one open reference for <paramref name="filePath"/> and removes the document from local tracking
	/// when no references remain.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <param name="closingDocument">When this method returns, contains the closing snapshot if the server copy is still open.</param>
	/// <returns>
	/// <see cref="DocumentCloseResult.Closed"/> when the tracked record was removed;
	/// <see cref="DocumentCloseResult.StillOpen"/> when other editor-open references remain;
	/// <see cref="DocumentCloseResult.BusyWithRequests"/> when only temporary request references remain and the
	/// close is therefore deferred; otherwise, <see cref="DocumentCloseResult.Untracked"/>.
	/// </returns>
	/// <remarks>
	/// A call made after the state has already been removed is a no-op. Any existing idle record is treated as
	/// explicit cleanup even when it has no open reference, so this method is also the explicit close path for an
	/// idle record. A <see cref="DocumentCloseResult.BusyWithRequests"/> result means the
	/// caller should retry the close once the request references are released.
	/// </remarks>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="filePath"/> is empty or whitespace-only, or the path is invalid on the current platform.</exception>
	public DocumentCloseResult TryClose(string filePath, out DocumentSnapshot? closingDocument)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

		string normalizedFilePath = LanguageServerPaths.NormalizeLocalPath(filePath);

		lock (_syncRoot)
		{
			if (!_documents.TryGetValue(normalizedFilePath, out TTrackedDocumentState? state))
			{
				closingDocument = null;
				return DocumentCloseResult.Untracked;
			}

			state.References.ReleaseOpen();

			if (state.References.HasOpenReferences)
			{
				closingDocument = null;
				return DocumentCloseResult.StillOpen;
			}

			if (!state.References.IsIdle)
			{
				TouchTrackedDocumentState(state, GetNextAccessStamp());
				closingDocument = null;
				return DocumentCloseResult.BusyWithRequests;
			}

			closingDocument = state.IsOpen ? state.CreateSnapshot() : null;
			_documents.Remove(normalizedFilePath);
			return DocumentCloseResult.Closed;
		}
	}
}

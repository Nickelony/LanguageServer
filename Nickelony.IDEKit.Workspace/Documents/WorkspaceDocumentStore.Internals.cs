using static Nickelony.IDEKit.Workspace.Documents.WorkspaceDocumentResultFactory;

namespace Nickelony.IDEKit.Workspace.Documents;

public sealed partial class WorkspaceDocumentStore
{
	// Snapshot creation reads several LogicalDocument fields that are mutated under _stateLock.
	// Call sites that do not already hold the lock use these wrappers so a concurrent mutation
	// cannot produce a torn snapshot; the lock is re-entrant, so in-lock callers can keep using
	// the factory directly.
	private WorkspaceDocumentSnapshot CreateSnapshotUnderLock(LogicalDocument document)
	{
		lock (_stateLock)
			return CreateSnapshot(document);
	}

	private IReadOnlyList<WorkspaceDocumentSnapshot> CreateSnapshotsUnderLock(IEnumerable<LogicalDocument> documents)
	{
		lock (_stateLock)
			return CreateSnapshots(documents);
	}

	private static void ReleaseGates(IEnumerable<SemaphoreSlim> gates)
	{
		foreach (SemaphoreSlim gate in gates.Reverse())
			gate.Release();
	}

	/// <summary>
	/// The validation outcome of a <c>TryBeginOperation</c> attempt.
	/// </summary>
	private enum OperationBeginFailure
	{
		DocumentNotFound,
		StaleDocumentInstance,
		StaleDocument,
		OperationInProgress,
		PreGateCheckFailed,
	}

	// Outcome of a TryBeginOperation attempt. On success the document and, when requested, the
	// captured snapshot are populated; a gated operation also has an operation registration. On
	// failure the failure kind and, when available, a snapshot of the state that failed validation
	// are populated; the failure snapshot is captured under _stateLock so it matches the state that
	// was validated.
	private readonly struct OperationBegin
	{
		public LogicalDocument? Document { get; init; }
		public WorkspaceDocumentSnapshot? CapturedSnapshot { get; init; }
		public WorkspaceDocumentSnapshot? FailureSnapshot { get; init; }
		public OperationRegistration? Operation { get; init; }
		public OperationBeginFailure? Failure { get; init; }

		public bool Succeeded => Failure is null;
	}

	// Performs the shared operation preamble under _stateLock: validates the
	// disposed state and the request's document presence, key, and version
	// expectations; runs the optional pre-gate check; captures a snapshot;
	// acquires the document's disk gate; runs the optional post-gate
	// setup; and registers the operation. When skipGateWhen returns true (used by
	// reload for dirty documents) the operation proceeds without holding the gate
	// or registering an operation. Returns an OperationBegin describing either the
	// begun operation or the failure.
	private OperationBegin TryBeginOperation(
		WorkspaceDocumentRequestIdentity identity,
		bool captureSnapshot,
		Func<LogicalDocument, bool>? preGateCheck = null,
		Func<LogicalDocument, WorkspaceDocumentSnapshot?, bool>? skipGateWhen = null,
		Action<LogicalDocument>? postGateSetup = null)
	{
		// A null or blank id must not reach the dictionary lookup, which would throw with the
		// misleading parameter name "key"; request ids are the normalized id from a snapshot.
		ArgumentException.ThrowIfNullOrWhiteSpace(identity.DocumentId);

		lock (_stateLock)
		{
			ThrowIfDisposedUnderLock();

			if (!_documents.TryGetValue(identity.DocumentId, out LogicalDocument? document))
				return new OperationBegin { Failure = OperationBeginFailure.DocumentNotFound };

			if (document.DocumentKey != identity.DocumentKey)
				return new OperationBegin
				{
					Document = document,
					FailureSnapshot = CreateSnapshot(document),
					Failure = OperationBeginFailure.StaleDocumentInstance
				};

			if (document.Version != identity.Version)
				return new OperationBegin
				{
					Document = document,
					FailureSnapshot = CreateSnapshot(document),
					Failure = OperationBeginFailure.StaleDocument
				};

			if (preGateCheck is not null && !preGateCheck(document))
				return new OperationBegin
				{
					Document = document,
					FailureSnapshot = CreateSnapshot(document),
					Failure = OperationBeginFailure.PreGateCheckFailed
				};

			WorkspaceDocumentSnapshot? capturedSnapshot = captureSnapshot ? CreateSnapshot(document) : null;
			if (skipGateWhen is not null && skipGateWhen(document, capturedSnapshot))
				return new OperationBegin
				{
					Document = document,
					CapturedSnapshot = capturedSnapshot
				};

			if (!document.DiskOperationGate.Wait(0, CancellationToken.None))
				return new OperationBegin
				{
					Document = document,
					FailureSnapshot = CreateSnapshot(document),
					Failure = OperationBeginFailure.OperationInProgress
				};

			try
			{
				postGateSetup?.Invoke(document);
			}
			catch
			{
				// A throwing setup must not leave the acquired gate held, and the delete setup must not
				// leave the document rejecting replacements; the original failure still propagates.
				if (document.DeleteOperationActive)
				{
					document.DeleteOperationActive = false;
					document.DeleteCompletion?.TrySetResult();
					document.DeleteCompletion = null;
				}

				document.DiskOperationGate.Release();
				throw;
			}

			OperationRegistration operation = new(document.DiskOperationGate);
			_activeOperations.Add(operation);

			return new OperationBegin
			{
				Document = document,
				CapturedSnapshot = capturedSnapshot,
				Operation = operation
			};
		}
	}

	// Requires _stateLock. Used by the stale-reload path, where the document id no longer resolves but
	// the instance may still be tracked under a new id after a rename or save-as.
	private LogicalDocument? FindDocumentByKey(WorkspaceDocumentKey documentKey)
	{
		foreach (LogicalDocument document in _documents.Values)
		{
			if (document.DocumentKey == documentKey)
				return document;
		}

		return null;
	}

	private void CompleteOperation(OperationRegistration operation)
	{
		lock (_stateLock)
		{
			_activeOperations.Remove(operation);
			operation.Complete();
		}
	}

	private void CompleteDestinationReservation(DestinationReservation reservation)
	{
		lock (_stateLock)
		{
			if (_destinationReservations.TryGetValue(reservation.DocumentId, out DestinationReservation? current)
				&& ReferenceEquals(current, reservation))
			{
				_destinationReservations.Remove(reservation.DocumentId);
				reservation.Completion.TrySetResult(null);
			}
		}
	}
}

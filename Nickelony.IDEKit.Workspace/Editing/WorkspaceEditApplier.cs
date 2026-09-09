using Nickelony.IDEKit.Core.Pathing;
using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Editing;

/// <summary>
/// Applies prepared workspace document replacements through the workspace document authority, with
/// no-op detection and non-atomic partial results.
/// </summary>
/// <remarks>
/// <para>
/// Targets are processed in list order. The operation is deliberately non-atomic: confirmed changes
/// remain applied when a later target fails, and the returned change set is a record of those changes,
/// not a rollback mechanism. A target whose before and after content and format match is marked
/// applied without invoking the replacement delegate. A store result of
/// <see cref="WorkspaceDocumentMutationStatus.NoChange"/> also marks the target applied without a
/// change record, because the document already held the requested content and format and the applier
/// did not transform it.
/// </para>
/// <para>
/// The name is related to, but deliberately narrower than, the Language Server Protocol's
/// <c>WorkspaceEdit</c>: this applier applies prepared whole-content replacements to existing tracked
/// documents through the workspace document authority. It does not create, rename, or delete
/// resources and does not compute edits from scratch.
/// </para>
/// <para>
/// An <see cref="OperationCanceledException"/> thrown by the replacement delegate propagates to the
/// caller; the applier converts only cooperative cancellation through <c>cancellationToken</c> into
/// results.
/// </para>
/// </remarks>
public sealed class WorkspaceEditApplier
{
	private readonly Func<WorkspaceDocumentReplaceRequest, WorkspaceDocumentMutationResult> _replaceDocument;
	private readonly LocalPathComparisonPolicy _pathComparison;
	private readonly bool _continueOnFailure;

	/// <summary>
	/// Initializes a new instance of the <see cref="WorkspaceEditApplier"/> class.
	/// </summary>
	/// <param name="replaceDocument">Replaces a workspace document's logical content.</param>
	/// <param name="pathComparison">
	/// The comparison used for the target and document ids. Supply the store's
	/// <see cref="IWorkspaceDocumentReader.PathComparison"/> value so de-duplication and version
	/// chaining cannot drift from document identity; the default follows the operating system. The
	/// comparison de-duplicates the changed and unknown target ids in the returned results and groups
	/// targets that share a document id under a different spelling for version chaining and
	/// same-document rejection propagation.
	/// </param>
	/// <param name="continueOnFailure">
	/// <see langword="true"/> to continue past a rejected or unknown target and aggregate the
	/// per-target outcomes, matching how LSP clients apply a workspace edit; <see langword="false"/>
	/// (the default) to stop at the first target that is not applied and mark the remaining targets not
	/// applied. Later targets for a document whose replacement was not applied are skipped either way,
	/// and cancellation still stops the application.
	/// </param>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="replaceDocument"/> is <see langword="null"/>.</exception>
	public WorkspaceEditApplier(
		Func<WorkspaceDocumentReplaceRequest, WorkspaceDocumentMutationResult> replaceDocument,
		LocalPathComparisonPolicy? pathComparison = null,
		bool continueOnFailure = false)
	{
		ArgumentNullException.ThrowIfNull(replaceDocument);

		_replaceDocument = replaceDocument;
		_pathComparison = pathComparison ?? LocalPathComparisonPolicy.ForCurrentPlatform;
		_continueOnFailure = continueOnFailure;
	}

	/// <summary>
	/// Applies prepared replacements in order, stopping at the first target whose result is not applied
	/// unless the applier was constructed with <c>continueOnFailure</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Targets are processed in list order and the outcome is non-atomic, as described on the class.
	/// Targets that were not attempted because an earlier target failed or because the application
	/// was canceled are marked <see cref="WorkspaceEditTargetStatus.NotApplied"/>.
	/// </para>
	/// <para>
	/// A store rejection that is known not to have mutated the document marks the target
	/// <see cref="WorkspaceEditTargetStatus.NotApplied"/>, while a target whose replacement delegate
	/// threw is marked <see cref="WorkspaceEditTargetStatus.Unknown"/> because the delegate may have
	/// changed the document before throwing. Once a document's replacement was not applied, later
	/// targets for the same document are also marked <see cref="WorkspaceEditTargetStatus.NotApplied"/>:
	/// they were prepared against a document state the applier did not confirm, so applying them would
	/// overwrite content the caller never observed. When the first observation of
	/// <paramref name="cancellationToken"/> happens before any target changed a document and without a
	/// recorded failure, the result carries <see cref="WorkspaceEditApplicationStatus.Canceled"/> and
	/// holds no change set.
	/// </para>
	/// </remarks>
	/// <param name="targets">The prepared replacements to apply.</param>
	/// <param name="cancellationToken">Cancels the application before the next target is attempted.</param>
	/// <returns>The application result with the per-target outcomes and the confirmed change set.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="targets"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="targets"/> contains a <see langword="null"/> entry, an entry whose identity has
	/// an empty document id, or an entry whose <see cref="WorkspaceEditTargetPreparation.AfterFileFormat"/>
	/// combines Windows-1252 with a byte-order mark.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="targets"/> contains an entry whose
	/// <see cref="WorkspaceEditTargetPreparation.AfterFileFormat"/> uses an undefined text encoding.
	/// </exception>
	/// <exception cref="OperationCanceledException">The replacement delegate threw an <see cref="OperationCanceledException"/>.</exception>
	public WorkspaceEditApplicationResult Apply(
		IReadOnlyList<WorkspaceEditTargetPreparation> targets,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(targets);

		// Entries are validated up front - a null entry would otherwise surface as a
		// NullReferenceException from the preparation count, and an unencodable format would surface as
		// an unknown per-target outcome - and the prepared-operation count is accumulated in the same
		// pass.
		int preparedOperationCount = 0;
		for (int index = 0; index < targets.Count; index++)
		{
			if (targets[index] is null)
				throw new ArgumentException("A prepared replacement target cannot be null.", nameof(targets));

			WorkspaceEditTargetPreparation target = targets[index];
			if (string.IsNullOrWhiteSpace(target.Identity.DocumentId))
			{
				throw new ArgumentException(
					"A prepared replacement target must carry a non-empty document id in its identity.",
					nameof(targets));
			}

			WorkspaceTextCodec.EnsureEncodable(target.AfterFileFormat, nameof(targets));

			preparedOperationCount += GetPreparedReplacementCount(target);
		}

		if (targets.Count == 0)
			return CreateCompletedResult([], [], 0, _pathComparison);

		var documentChanges = new List<WorkspaceDocumentChange>();
		var targetResults = new List<WorkspaceEditTargetResult>(targets.Count);
		var unknownTargetIds = new List<string>();

		// One or more prepared changes can target the same document. The store advances the document
		// version on each accepted replacement, so every later request for the same document uses the
		// version confirmed by the earlier result instead of its own stale preflight version. The
		// dictionary uses the configured target-id comparison, so case-variant ids that the result
		// de-duplication treats as one document are also tracked as one document.
		var observedVersions = new Dictionary<string, long>(_pathComparison.Comparer);

		// Documents whose replacement did not apply (deterministic rejection or unknown outcome). A
		// later target for the same document is not attempted: it was prepared against a document state
		// the applier has not confirmed, and a rejection must never authorize a later write against
		// state the application did not observe.
		var rejectedDocuments = new HashSet<string>(_pathComparison.Comparer);

		WorkspaceOperationFailure? firstFailure = null;

		for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
		{
			WorkspaceEditTargetPreparation target = targets[targetIndex];

			if (cancellationToken.IsCancellationRequested)
			{
				AddNotAppliedTargetResults(targets, targetResults, targetIndex);

				// Nothing was applied when no failure occurred and no change was recorded: the processed
				// targets were removed by the no-op check or already held the requested content, so there is
				// no partial application to report.
				if (firstFailure is null && documentChanges.Count == 0)
					return CreateCanceledResult(targetResults, preparedOperationCount, _pathComparison);

				firstFailure ??= new WorkspaceOperationFailure(
					WorkspaceOperationFailureCodes.Canceled,
					"The application was canceled before the target was applied.");
				return CreatePartialResult(documentChanges, targetResults, unknownTargetIds, preparedOperationCount, firstFailure, _pathComparison);
			}

			// Reaching this point with a rejected document requires continueOnFailure: without it the
			// application already returned when the rejection was recorded.
			if (rejectedDocuments.Contains(target.Identity.DocumentId))
			{
				WorkspaceOperationFailure rejectedFailure = new(
					WorkspaceOperationFailureCodes.TargetSkipped,
					$"The workspace document '{target.TargetId}' was not replaced because an earlier target for the same document was not applied.");
				firstFailure ??= rejectedFailure;
				targetResults.Add(CreateResult(
					target,
					GetPreparedReplacementCount(target),
					WorkspaceEditTargetStatus.NotApplied,
					rejectedFailure,
					null));
				continue;
			}

			if (string.Equals(target.BeforeContent, target.AfterContent, StringComparison.Ordinal)
				&& target.BeforeFileFormat == target.AfterFileFormat)
			{
				// The store is not consulted, so the actual version is only observable when an earlier
				// target of the same document recorded one; otherwise it is unknown.
				long? noOpVersion = observedVersions.TryGetValue(target.Identity.DocumentId, out long recordedVersion)
					? recordedVersion
					: null;
				targetResults.Add(CreateResult(target, 0, WorkspaceEditTargetStatus.Applied, null, noOpVersion));
				continue;
			}

			long effectiveVersion = observedVersions.TryGetValue(target.Identity.DocumentId, out long previousVersion)
				? previousVersion
				: target.Identity.Version;

			// Both failure paths below assign a detail before the partial result is created.
			WorkspaceOperationFailure failure;

			try
			{
				WorkspaceDocumentMutationResult result = _replaceDocument(
					new WorkspaceDocumentReplaceRequest(
						new WorkspaceDocumentRequestIdentity(
							target.Identity.DocumentKey,
							target.Identity.DocumentId,
							effectiveVersion),
						target.AfterContent,
						target.AfterFileFormat));

				if (result.Status is WorkspaceDocumentMutationStatus.Changed or WorkspaceDocumentMutationStatus.NoChange)
				{
					// Only a confirmed replacement advances the version used by later targets for the same
					// document: a rejection carries a current-state snapshot of its own, and adopting that
					// version would authorize a later write against state the applier never observed.
					observedVersions[target.Identity.DocumentId] =
						result.Snapshot?.Version ?? result.RequestedIdentity.Version;

					// Only a confirmed replacement produces a change record. A NoChange result means the store
					// already held the requested content (for example after another writer applied it), so the
					// applier did not transform the document and recording BeforeContent -> AfterContent would
					// misreport the change to host undo tracking.
					if (result.Status == WorkspaceDocumentMutationStatus.Changed)
					{
						documentChanges.Add(new WorkspaceDocumentChange
						{
							TargetId = target.TargetId,
							BeforeContent = target.BeforeContent,
							AfterContent = target.AfterContent,
							BeforeFileFormat = target.BeforeFileFormat,
							AfterFileFormat = target.AfterFileFormat
						});
					}

					// The store increments the document version before it creates the result snapshot, so the
					// snapshot carries the post-mutation version while the requested identity echoes the version
					// the request was built with.
					targetResults.Add(CreateResult(
						target,
						GetPreparedReplacementCount(target),
						WorkspaceEditTargetStatus.Applied,
						null,
						result.Snapshot?.Version ?? result.RequestedIdentity.Version));
					continue;
				}

				failure = new WorkspaceOperationFailure(
					WorkspaceOperationFailureCodes.TargetNotChanged,
					$"The workspace document '{target.TargetId}' was not replaced: {result.Status}.");

				rejectedDocuments.Add(target.Identity.DocumentId);

				if (IsDeterministicRejection(result.Status))
				{
					// The store returns these statuses before any mutation, so the replacement is known
					// not to have touched the document.
					targetResults.Add(CreateResult(target, GetPreparedReplacementCount(target), WorkspaceEditTargetStatus.NotApplied, failure, null));
				}
				else
				{
					// The status is outside the document authority's vocabulary, so whether the document was
					// mutated cannot be established: the outcome is unknown, not "not changed".
					failure = new WorkspaceOperationFailure(
						WorkspaceOperationFailureCodes.TargetOutcomeUnknown,
						$"The workspace document '{target.TargetId}' returned the unrecognized replacement status '{result.Status}'.");
					unknownTargetIds.Add(target.TargetId);
					targetResults.Add(CreateResult(target, GetPreparedReplacementCount(target), WorkspaceEditTargetStatus.Unknown, failure, null));
				}
			}
			catch (OperationCanceledException)
			{
				// An external cancellation signal is not a target outcome: it propagates so the caller can
				// stop the session instead of receiving a per-target failure that hides the cancellation.
				throw;
			}
			catch (Exception exception)
			{
				failure = new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.TargetApplicationFailed, exception.Message, exception);
				rejectedDocuments.Add(target.Identity.DocumentId);
				unknownTargetIds.Add(target.TargetId);
				targetResults.Add(CreateResult(target, GetPreparedReplacementCount(target), WorkspaceEditTargetStatus.Unknown, failure, null));
			}

			firstFailure ??= failure;

			if (!_continueOnFailure)
			{
				AddNotAppliedTargetResults(targets, targetResults, targetIndex + 1);
				return CreatePartialResult(documentChanges, targetResults, unknownTargetIds, preparedOperationCount, failure, _pathComparison);
			}
		}

		return firstFailure is null
			? CreateCompletedResult(documentChanges, targetResults, preparedOperationCount, _pathComparison)
			: CreatePartialResult(documentChanges, targetResults, unknownTargetIds, preparedOperationCount, firstFailure, _pathComparison);
	}

	private static WorkspaceEditTargetResult CreateResult(
		WorkspaceEditTargetPreparation target,
		int preparedOperationCount,
		WorkspaceEditTargetStatus status,
		WorkspaceOperationFailure? failure,
		long? actualVersion)
		=> new()
		{
			TargetId = target.TargetId,
			ExpectedVersion = target.Identity.Version,
			ActualVersion = actualVersion,
			PreparedOperationCount = preparedOperationCount,
			Status = status,
			Failure = failure
		};

	private static void AddNotAppliedTargetResults(
		IReadOnlyList<WorkspaceEditTargetPreparation> targets,
		List<WorkspaceEditTargetResult> targetResults,
		int startIndex)
	{
		for (int index = startIndex; index < targets.Count; index++)
		{
			targetResults.Add(CreateResult(
				targets[index],
				GetPreparedReplacementCount(targets[index]),
				WorkspaceEditTargetStatus.NotApplied,
				null,
				null));
		}
	}

	// A target contributes one prepared replacement operation when its content or format differs from
	// the before state.
	private static int GetPreparedReplacementCount(WorkspaceEditTargetPreparation target)
		=> string.Equals(target.BeforeContent, target.AfterContent, StringComparison.Ordinal)
			&& target.BeforeFileFormat == target.AfterFileFormat
				? 0
				: 1;

	// Store statuses that prove the replacement did not touch the document: they are returned before
	// any mutation is applied.
	private static bool IsDeterministicRejection(WorkspaceDocumentMutationStatus status)
		=> status is
			WorkspaceDocumentMutationStatus.StaleDocument or
			WorkspaceDocumentMutationStatus.StaleDocumentInstance or
			WorkspaceDocumentMutationStatus.DocumentNotFound or
			WorkspaceDocumentMutationStatus.OperationInProgress;

	private static WorkspaceEditApplicationResult CreateCompletedResult(
		IReadOnlyList<WorkspaceDocumentChange> documentChanges,
		IReadOnlyList<WorkspaceEditTargetResult> targetResults,
		int preparedOperationCount,
		LocalPathComparisonPolicy pathComparison)
		=> WorkspaceEditApplicationResult.Completed(
			preparedOperationCount,
			targetResults,
			new WorkspaceEditChangeSet(documentChanges),
			pathComparison);

	private static WorkspaceEditApplicationResult CreateCanceledResult(
		IReadOnlyList<WorkspaceEditTargetResult> targetResults,
		int preparedOperationCount,
		LocalPathComparisonPolicy pathComparison)
		=> WorkspaceEditApplicationResult.Canceled(
			preparedOperationCount,
			targetResults,
			new WorkspaceOperationFailure(
				WorkspaceOperationFailureCodes.Canceled,
				"The application was canceled before any target changed a document."),
			pathComparison);

	private static WorkspaceEditApplicationResult CreatePartialResult(
		IReadOnlyList<WorkspaceDocumentChange> documentChanges,
		IReadOnlyList<WorkspaceEditTargetResult> targetResults,
		IReadOnlyList<string> unknownTargetIds,
		int preparedOperationCount,
		WorkspaceOperationFailure failure,
		LocalPathComparisonPolicy pathComparison)
		=> WorkspaceEditApplicationResult.PartiallyApplied(
			preparedOperationCount,
			targetResults,
			unknownTargetIds,
			failure,
			new WorkspaceEditChangeSet(documentChanges),
			pathComparison);
}

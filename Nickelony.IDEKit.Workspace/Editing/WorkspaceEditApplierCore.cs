using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Editing;

/// <summary>
/// Describes one prepared document replacement for <see cref="WorkspaceEditApplierCore"/>.
/// </summary>
/// <remarks>
/// <see cref="BeforeContent"/> is used for no-op detection and for the change record; the applier
/// does not independently verify that it still matches the workspace document.
/// </remarks>
public sealed record WorkspaceEditTargetPreparation(
	string TargetId,
	WorkspaceDocumentKey DocumentKey,
	string DocumentId,
	long ExpectedVersion,
	string BeforeContent,
	string AfterContent,
	TextFileFormat FileFormat);

/// <summary>
/// Applies prepared workspace document replacements through the workspace document authority with
/// preflight (no-op detection), apply, verify (mutation status), and partial-results semantics.
/// </summary>
/// <remarks>
/// Targets are processed in list order. The operation is deliberately non-atomic: confirmed changes
/// remain applied when a later target fails, and the returned transaction is a record of those changes,
/// not a rollback mechanism. A target whose before and after content match is marked applied without
/// invoking the replacement delegate.
/// </remarks>
public sealed class WorkspaceEditApplierCore
{
	private readonly Func<WorkspaceDocumentReplaceRequest, WorkspaceDocumentMutationResult> _replaceDocument;

	/// <summary>
	/// Initializes a new instance of the <see cref="WorkspaceEditApplierCore"/> class.
	/// </summary>
	/// <param name="replaceDocument">Replaces a workspace document's logical content.</param>
	public WorkspaceEditApplierCore(Func<WorkspaceDocumentReplaceRequest, WorkspaceDocumentMutationResult> replaceDocument)
	{
		_replaceDocument = replaceDocument ?? throw new ArgumentNullException(nameof(replaceDocument));
	}

	/// <summary>
	/// Applies prepared replacements in order, stopping at the first target whose result is not applied.
	/// </summary>
	/// <param name="targets">The prepared replacements to apply.</param>
	/// <returns>
	/// The application result and its non-atomic change set. Targets after the first failure are marked
	/// <see cref="TextWorkspaceEditTargetStatus.NotApplied"/>; a failed target is marked
	/// <see cref="TextWorkspaceEditTargetStatus.Unknown"/> because the delegate may have changed the
	/// document before returning or throwing.
	/// </returns>
	/// <example>
	/// <code>
	/// if (result.Status == TextWorkspaceEditApplicationStatus.PartiallyApplied)
	/// {
	/// 	IReadOnlyList&lt;string&gt; changed = result.ChangedTargetIds;
	/// 	IReadOnlyList&lt;string&gt; unknown = result.UnknownTargetIds;
	/// }
	/// </code>
	/// </example>
	public TextWorkspaceEditApplicationResult Apply(IReadOnlyList<WorkspaceEditTargetPreparation> targets)
	{
		ArgumentNullException.ThrowIfNull(targets);

		if (targets.Count == 0)
			return CreateCompletedResult([], [], 0);

		var documentChanges = new List<TextWorkspaceDocumentChange>();
		var targetResults = new List<TextWorkspaceEditTargetResult>(targets.Count);
		var changedTargetIds = new List<string>();
		var unknownTargetIds = new List<string>();
		int preparedOperationCount = 0;

		for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
		{
			WorkspaceEditTargetPreparation target = targets[targetIndex];

			if (string.Equals(target.BeforeContent, target.AfterContent, StringComparison.Ordinal))
			{
				targetResults.Add(CreateResult(target, 0, TextWorkspaceEditTargetStatus.Applied, null, target.ExpectedVersion));
				continue;
			}

			preparedOperationCount++;
			TextWorkspaceEditFailure? failure;

			try
			{
				WorkspaceDocumentMutationResult result = _replaceDocument(
					new WorkspaceDocumentReplaceRequest(
						target.DocumentKey,
						target.DocumentId,
						target.ExpectedVersion,
						target.AfterContent,
						target.FileFormat));

				if (result.Status is WorkspaceDocumentMutationStatus.Replaced or WorkspaceDocumentMutationStatus.NoChange)
				{
					documentChanges.Add(new TextWorkspaceDocumentChange(
						target.TargetId,
						target.BeforeContent,
						target.AfterContent));
					changedTargetIds.Add(target.TargetId);
					targetResults.Add(CreateResult(target, 1, TextWorkspaceEditTargetStatus.Applied, null, result.RequestedVersion));
					continue;
				}

				failure = new TextWorkspaceEditFailure(
					"TargetNotChanged",
					$"The workspace document '{target.TargetId}' was not replaced: {result.Status}.");
				unknownTargetIds.Add(target.TargetId);
				targetResults.Add(CreateResult(target, 1, TextWorkspaceEditTargetStatus.Unknown, failure, null));
			}
			catch (Exception exception)
			{
				failure = new TextWorkspaceEditFailure("TargetApplicationFailed", exception.Message, exception);
				unknownTargetIds.Add(target.TargetId);
				targetResults.Add(CreateResult(target, 1, TextWorkspaceEditTargetStatus.Unknown, failure, null));
			}

			AddNotAppliedTargetResults(targets, targetResults, targetIndex + 1);
			return CreatePartialResult(
				targets,
				documentChanges,
				targetResults,
				changedTargetIds,
				unknownTargetIds,
				preparedOperationCount,
				failure);
		}

		return CreateCompletedResult(
			documentChanges,
			targetResults,
			preparedOperationCount);
	}

	private static TextWorkspaceEditTargetResult CreateResult(
		WorkspaceEditTargetPreparation target,
		int preparedOperationCount,
		TextWorkspaceEditTargetStatus status,
		TextWorkspaceEditFailure? failure,
		long? actualVersion)
		=> new(
			target.TargetId,
			target.ExpectedVersion,
			actualVersion,
			preparedOperationCount,
			status,
			failure);

	private static void AddNotAppliedTargetResults(
		IReadOnlyList<WorkspaceEditTargetPreparation> targets,
		List<TextWorkspaceEditTargetResult> targetResults,
		int startIndex)
	{
		for (int index = startIndex; index < targets.Count; index++)
			targetResults.Add(CreateResult(targets[index], 0, TextWorkspaceEditTargetStatus.NotApplied, null, null));
	}

	private static TextWorkspaceEditApplicationResult CreateCompletedResult(
		IReadOnlyList<TextWorkspaceDocumentChange> documentChanges,
		IReadOnlyList<TextWorkspaceEditTargetResult> targetResults,
		int preparedOperationCount)
		=> new(
			TextWorkspaceEditApplicationStatus.Completed,
			preparedOperationCount,
			targetResults,
			documentChanges.Select(change => change.FilePath).ToArray(),
			[],
			[],
			null,
			new TextWorkspaceEditTransaction(documentChanges));

	private static TextWorkspaceEditApplicationResult CreatePartialResult(
		IReadOnlyList<WorkspaceEditTargetPreparation> targets,
		IReadOnlyList<TextWorkspaceDocumentChange> documentChanges,
		IReadOnlyList<TextWorkspaceEditTargetResult> targetResults,
		IReadOnlyList<string> changedTargetIds,
		IReadOnlyList<string> unknownTargetIds,
		int preparedOperationCount,
		TextWorkspaceEditFailure? failure)
		=> new(
			TextWorkspaceEditApplicationStatus.PartiallyApplied,
			preparedOperationCount,
			targetResults,
			changedTargetIds,
			unknownTargetIds,
			[],
			failure,
			new TextWorkspaceEditTransaction(documentChanges));
}

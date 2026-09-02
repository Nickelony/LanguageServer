namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Describes the outcome of applying a prepared multi-file workspace edit.
/// </summary>
public sealed class TextWorkspaceEditApplicationResult
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextWorkspaceEditApplicationResult"/> class.
	/// </summary>
	/// <param name="status">The overall application outcome.</param>
	/// <param name="preparedOperationCount">The number of non-no-op target replacements prepared.</param>
	/// <param name="targets">The per-target outcomes in application order.</param>
	/// <param name="changedTargetIds">The targets for which the document authority accepted the requested content.</param>
	/// <param name="unknownTargetIds">The targets whose final content could not be confirmed.</param>
	/// <param name="diagnostics">The edit-preparation diagnostics, if validation failed.</param>
	/// <param name="failure">The failure detail, when application did not complete.</param>
	/// <param name="changeSet">The before-and-after content captured for confirmed target transformations.</param>
	public TextWorkspaceEditApplicationResult(
		TextWorkspaceEditApplicationStatus status,
		int preparedOperationCount,
		IReadOnlyList<TextWorkspaceEditTargetResult> targets,
		IReadOnlyList<string> changedTargetIds,
		IReadOnlyList<string> unknownTargetIds,
		IReadOnlyList<TextEditPreparationDiagnostic> diagnostics,
		TextWorkspaceEditFailure? failure,
		TextWorkspaceEditTransaction changeSet)
	{
		ArgumentNullException.ThrowIfNull(targets);
		ArgumentNullException.ThrowIfNull(changedTargetIds);
		ArgumentNullException.ThrowIfNull(unknownTargetIds);
		ArgumentNullException.ThrowIfNull(diagnostics);
		ArgumentNullException.ThrowIfNull(changeSet);
		ArgumentOutOfRangeException.ThrowIfNegative(preparedOperationCount);

		Status = status;
		PreparedOperationCount = preparedOperationCount;
		Targets = Array.AsReadOnly([.. targets]);
		ChangedTargetIds = Array.AsReadOnly([.. changedTargetIds.Distinct(StringComparer.OrdinalIgnoreCase)]);
		UnknownTargetIds = Array.AsReadOnly([.. unknownTargetIds.Distinct(StringComparer.OrdinalIgnoreCase)]);
		Diagnostics = Array.AsReadOnly([.. diagnostics]);
		Failure = failure;
		ChangeSet = changeSet;
	}

	/// <summary>
	/// Gets the application outcome.
	/// </summary>
	public TextWorkspaceEditApplicationStatus Status { get; }

	/// <summary>
	/// Gets the number of prepared operations across all targets.
	/// </summary>
	public int PreparedOperationCount { get; }

	/// <summary>
	/// Gets the target outcomes in application order.
	/// </summary>
	public IReadOnlyList<TextWorkspaceEditTargetResult> Targets { get; }

	/// <summary>
	/// Gets the target IDs for which the document authority accepted the requested content.
	/// </summary>
	public IReadOnlyList<string> ChangedTargetIds { get; }

	/// <summary>
	/// Gets the target IDs whose final content could not be confirmed.
	/// </summary>
	public IReadOnlyList<string> UnknownTargetIds { get; }

	/// <summary>
	/// Gets the edit-preparation diagnostics. This is populated for validation failures.
	/// </summary>
	public IReadOnlyList<TextEditPreparationDiagnostic> Diagnostics { get; }

	/// <summary>
	/// Gets the failure detail, when the result is not fully completed.
	/// </summary>
	public TextWorkspaceEditFailure? Failure { get; }

	/// <summary>
	/// Gets the non-atomic before-and-after change set for confirmed target transformations.
	/// </summary>
	public TextWorkspaceEditTransaction ChangeSet { get; }

	/// <summary>
	/// Gets a value indicating whether at least one target transformation was recorded.
	/// </summary>
	public bool HasChanges => ChangeSet.HasChanges;
}

/// <summary>
/// Identifies the outcome of multi-file workspace-edit application.
/// </summary>
public enum TextWorkspaceEditApplicationStatus
{
	/// <summary>
	/// Target resolution or edit preparation failed before mutation.
	/// </summary>
	ValidationFailed,

	/// <summary>
	/// Every prepared target applied successfully or was a no-op.
	/// </summary>
	Completed,

	/// <summary>
	/// A runtime target failure occurred after preflight, so not all targets were applied or confirmed.
	/// </summary>
	PartiallyApplied
}

/// <summary>
/// Describes one target's prepared and runtime application state.
/// </summary>
public sealed class TextWorkspaceEditTargetResult
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextWorkspaceEditTargetResult"/> class.
	/// </summary>
	/// <param name="targetId">The stable target identifier.</param>
	/// <param name="expectedVersion">The target version captured during preflight.</param>
	/// <param name="actualVersion">The target version observed after application, when available.</param>
	/// <param name="preparedOperationCount">The number of prepared operations for this target.</param>
	/// <param name="status">The target application state.</param>
	/// <param name="failure">The target-specific failure detail, when available.</param>
	public TextWorkspaceEditTargetResult(
		string targetId,
		long expectedVersion,
		long? actualVersion,
		int preparedOperationCount,
		TextWorkspaceEditTargetStatus status,
		TextWorkspaceEditFailure? failure = null)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(preparedOperationCount);

		TargetId = targetId ?? string.Empty;
		ExpectedVersion = expectedVersion;
		ActualVersion = actualVersion;
		PreparedOperationCount = preparedOperationCount;
		Status = status;
		Failure = failure;
	}

	/// <summary>
	/// Gets the stable target identifier, currently the normalized edit path.
	/// </summary>
	public string TargetId { get; }

	/// <summary>
	/// Gets the target version captured during preflight.
	/// </summary>
	public long ExpectedVersion { get; }

	/// <summary>
	/// Gets the target version observed after application, when available.
	/// </summary>
	public long? ActualVersion { get; }

	/// <summary>
	/// Gets the number of prepared operations for this target.
	/// </summary>
	public int PreparedOperationCount { get; }

	/// <summary>
	/// Gets the target application state.
	/// </summary>
	public TextWorkspaceEditTargetStatus Status { get; }

	/// <summary>
	/// Gets the target-specific failure detail, when available.
	/// </summary>
	public TextWorkspaceEditFailure? Failure { get; }
}

/// <summary>
/// Identifies one target's runtime state after workspace-edit application.
/// </summary>
public enum TextWorkspaceEditTargetStatus
{
	/// <summary>
	/// The target was validated but not reached because an earlier target failed.
	/// </summary>
	NotApplied,

	/// <summary>
	/// The target applied successfully or was a confirmed no-op.
	/// </summary>
	Applied,

	/// <summary>
	/// The target content changed, but the requested final state was not confirmed.
	/// </summary>
	Changed,

	/// <summary>
	/// The target's final state could not be read or determined.
	/// </summary>
	Unknown
}

/// <summary>
/// Describes a target-resolution or runtime workspace-edit failure.
/// </summary>
/// <param name="Code">The stable failure code.</param>
/// <param name="Message">The failure message.</param>
/// <param name="Exception">The underlying exception, when available.</param>
public sealed record TextWorkspaceEditFailure(
	string Code,
	string Message,
	Exception? Exception = null);

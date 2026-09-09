namespace Nickelony.IDEKit.Workspace.Documents.Reloading;

/// <summary>
/// Supplies the asynchronous callbacks for a
/// <see cref="FileReloadCoordinator{TPromptResult}.ProcessQueuedFilesAsync"/> pass.
/// </summary>
/// <typeparam name="TPromptResult">The host-neutral reload prompt result type.</typeparam>
/// <param name="PromptReload">
/// Prompts the host for a reload decision for a conflicted document; the argument carries the
/// conflict outcome, the current snapshot, and the identity. Invoked only when
/// <paramref name="ResolveConflict"/> is supplied, because a decision without a resolver is
/// discarded.
/// </param>
/// <param name="ReloadDocument">Reloads a tracked document and returns its result.</param>
/// <param name="ReportReloadFailure">
/// Reports a result that is neither <see cref="WorkspaceDocumentReloadStatus.Reloaded"/> nor
/// <see cref="WorkspaceDocumentReloadStatus.Unchanged"/> unless an external conflict was resolved
/// successfully; a failed resolution is reported as the original conflict result, and a conflict
/// without a resolver is reported without prompting. May be
/// <see langword="null"/>.
/// </param>
/// <param name="ResolveConflict">
/// Optionally resolves an external file conflict using the host prompt result; when it is
/// <see langword="null"/>, conflicts are reported without prompting. May be
/// <see langword="null"/>.
/// </param>
public readonly record struct FileReloadCallbacks<TPromptResult>(
	Func<WorkspaceDocumentReloadResult, CancellationToken, Task<TPromptResult>> PromptReload,
	Func<string, CancellationToken, Task<WorkspaceDocumentReloadResult>> ReloadDocument,
	Func<WorkspaceDocumentReloadResult, CancellationToken, Task>? ReportReloadFailure = null,
	Func<WorkspaceDocumentReloadResult, TPromptResult, CancellationToken, Task<WorkspaceDocumentConflictResolutionResult?>>? ResolveConflict = null);

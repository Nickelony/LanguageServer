namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Queues external file reloads and drives the reload, conflict, and failure flow for tracked
/// workspace documents using a host-neutral prompt result.
/// </summary>
/// <typeparam name="TPromptResult">The host-neutral reload prompt result type.</typeparam>
/// <remarks>
/// This coordinator does not normalize paths or provide synchronization. Callers should serialize
/// access to an instance. A processing call that observes another pass already running returns
/// without processing.
/// On normal completion, queued entries are cleared, including entries added during the pass.
/// </remarks>
public sealed class FileReloadCoordinator<TPromptResult>
{
	private readonly List<string> _pendingFileReloads = [];

	/// <summary>
	/// Gets a value indicating whether a reload pass is currently running on this coordinator.
	/// </summary>
	/// <value><see langword="true"/> while <see cref="ProcessQueuedFiles"/> is invoking its callbacks.</value>
	public bool IsRunning { get; private set; }

	/// <summary>
	/// Queues a file for reload. Blank paths and case-sensitive exact duplicate strings are ignored.
	/// </summary>
	/// <param name="filePath">The file path to reload.</param>
	public void QueueFile(string filePath)
	{
		ArgumentNullException.ThrowIfNull(filePath);

		if (string.IsNullOrWhiteSpace(filePath) || _pendingFileReloads.Contains(filePath))
			return;

		_pendingFileReloads.Add(filePath);
	}

	/// <summary>
	/// Processes the files queued at the start of the pass, prompting the host only for conflicts.
	/// </summary>
	/// <param name="promptReload">Prompts the host for a reload decision for a file path.</param>
	/// <param name="reloadDocument">Reloads a tracked document and returns its result.</param>
	/// <param name="reportReloadFailure">Reports a result that was not reloaded, unchanged, or resolved; may be <see langword="null"/>.</param>
	/// <param name="resolveConflict">Optionally resolves an external-file conflict using the host prompt result.</param>
	/// <remarks>
	/// Reloaded and unchanged results are not reported as failures. A conflict is considered resolved
	/// only when the resolver returns <see cref="WorkspaceDocumentConflictResolutionStatus.ResolvedWithDisk"/>
	/// or <see cref="WorkspaceDocumentConflictResolutionStatus.ResolvedWithLogical"/>.
	/// </remarks>
	/// <example>
	/// <code>
	/// coordinator.QueueFile(filePath);
	/// coordinator.ProcessQueuedFiles(
	/// 	promptReload,
	/// 	reloadDocument,
	/// 	reportReloadFailure,
	/// 	resolveConflict);
	/// </code>
	/// </example>
	public void ProcessQueuedFiles(
		Func<string, TPromptResult> promptReload,
		Func<string, WorkspaceDocumentReloadResult> reloadDocument,
		Action<WorkspaceDocumentReloadResult>? reportReloadFailure = null,
		Func<WorkspaceDocumentReloadResult, TPromptResult, WorkspaceDocumentConflictResolutionResult?>? resolveConflict = null)
	{
		ArgumentNullException.ThrowIfNull(promptReload);
		ArgumentNullException.ThrowIfNull(reloadDocument);

		if (IsRunning)
			return;

		IsRunning = true;

		try
		{
			foreach (string filePath in _pendingFileReloads.ToArray())
			{
				WorkspaceDocumentReloadResult result = reloadDocument(filePath);
				if (result.Status is WorkspaceDocumentReloadStatus.Reloaded or WorkspaceDocumentReloadStatus.Unchanged)
					continue;

				if (result.Status == WorkspaceDocumentReloadStatus.ExternalFileConflict)
				{
					TPromptResult choice = promptReload(filePath);
					WorkspaceDocumentConflictResolutionResult? resolution = resolveConflict?.Invoke(result, choice);
					if (resolution?.Status is WorkspaceDocumentConflictResolutionStatus.ResolvedWithDisk
						or WorkspaceDocumentConflictResolutionStatus.ResolvedWithLogical)
						continue;
				}

				reportReloadFailure?.Invoke(result);
			}

			_pendingFileReloads.Clear();
		}
		finally
		{
			IsRunning = false;
		}
	}
}

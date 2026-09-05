using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>
/// Tests <see cref="FileReloadCoordinator{TDocument}"/> queueing, prompting, conflict resolution,
/// and failure reporting.
/// </summary>
/// <remarks>
/// The tests also verify that duplicate paths are ignored and nested processing cannot start while a
/// queue is already being processed.
/// </remarks>
[TestClass]
[TestCategory("TextEditorBaseModernization")]
public sealed class FileReloadCoordinatorTests
{
	private const string ReloadPromptResult = "reload";
	private const string KeepPromptResult = "keep";

	[TestMethod]
	public void QueueFile_IgnoresBlankAndDuplicatePaths()
	{
		var coordinator = new FileReloadCoordinator<string>();
		string reloaded = string.Empty;

		coordinator.QueueFile(string.Empty);
		coordinator.QueueFile("  ");
		coordinator.QueueFile(@"C:\Scripts\script.txt");
		coordinator.QueueFile(@"C:\Scripts\script.txt");

		coordinator.ProcessQueuedFiles(
			_ => ReloadPromptResult,
			filePath =>
			{
				reloaded = filePath;
				return CreateReloadResult(filePath, WorkspaceDocumentReloadStatus.Reloaded);
			});

		Assert.AreEqual(@"C:\Scripts\script.txt", reloaded);
	}

	[TestMethod]
	public void ProcessQueuedFiles_ReloadedAndUnchangedResultsAreSkipped()
	{
		var coordinator = new FileReloadCoordinator<string>();
		var failures = new List<WorkspaceDocumentReloadResult>();

		coordinator.QueueFile(@"C:\Scripts\reloaded.txt");
		coordinator.QueueFile(@"C:\Scripts\unchanged.txt");
		coordinator.QueueFile(@"C:\Scripts\failed.txt");

		coordinator.ProcessQueuedFiles(
			_ => ReloadPromptResult,
			filePath => CreateReloadResult(
				filePath,
				filePath.EndsWith("reloaded.txt", StringComparison.Ordinal)
					? WorkspaceDocumentReloadStatus.Reloaded
					: filePath.EndsWith("unchanged.txt", StringComparison.Ordinal)
						? WorkspaceDocumentReloadStatus.Unchanged
						: WorkspaceDocumentReloadStatus.ReadFailed),
			failures.Add);

		Assert.AreEqual(1, failures.Count);
		Assert.AreEqual(WorkspaceDocumentReloadStatus.ReadFailed, failures[0].Status);
		Assert.AreEqual(@"C:\Scripts\failed.txt", failures[0].RequestedDocumentId);
	}

	[TestMethod]
	public void ProcessQueuedFiles_ResolvedConflictSkipsFailureReporting()
	{
		var coordinator = new FileReloadCoordinator<string>();
		string? lastPrompt = null;
		var failures = new List<WorkspaceDocumentReloadResult>();

		coordinator.QueueFile(@"C:\Scripts\conflict.txt");

		coordinator.ProcessQueuedFiles(
			filePath =>
			{
				lastPrompt = filePath;
				return KeepPromptResult;
			},
			filePath => CreateReloadResult(filePath, WorkspaceDocumentReloadStatus.ExternalFileConflict),
			failures.Add,
			(result, choice) => new WorkspaceDocumentConflictResolutionResult(
				WorkspaceDocumentConflictResolutionStatus.ResolvedWithLogical,
				result.RequestedDocumentKey,
				result.RequestedDocumentId,
				result.RequestedVersion,
				WorkspaceDocumentConflictResolutionChoice.UseLogical,
				result.Snapshot));

		Assert.AreEqual(@"C:\Scripts\conflict.txt", lastPrompt);
		Assert.AreEqual(0, failures.Count);
	}

	[TestMethod]
	public void ProcessQueuedFiles_UnresolvedConflictIsReported()
	{
		var coordinator = new FileReloadCoordinator<string>();
		var failures = new List<WorkspaceDocumentReloadResult>();

		coordinator.QueueFile(@"C:\Scripts\conflict.txt");

		coordinator.ProcessQueuedFiles(
			_ => ReloadPromptResult,
			filePath => CreateReloadResult(filePath, WorkspaceDocumentReloadStatus.ExternalFileConflict),
			failures.Add,
			(result, _) => null);

		Assert.AreEqual(1, failures.Count);
		Assert.AreEqual(@"C:\Scripts\conflict.txt", failures[0].RequestedDocumentId);
	}

	[TestMethod]
	public void ProcessQueuedFiles_WhileRunningIsIgnored()
	{
		var coordinator = new FileReloadCoordinator<string>();
		int reloadCount = 0;

		coordinator.QueueFile(@"C:\Scripts\one.txt");

		coordinator.ProcessQueuedFiles(
			_ => ReloadPromptResult,
			filePath =>
			{
				coordinator.QueueFile(@"C:\Scripts\two.txt");
				coordinator.ProcessQueuedFiles(
					_ => ReloadPromptResult,
					_ =>
					{
						reloadCount++;
						return CreateReloadResult(filePath, WorkspaceDocumentReloadStatus.Reloaded);
					});
				reloadCount++;
				return CreateReloadResult(filePath, WorkspaceDocumentReloadStatus.Reloaded);
			});

		Assert.AreEqual(1, reloadCount);
		Assert.IsFalse(coordinator.IsRunning);
	}

	private static WorkspaceDocumentReloadResult CreateReloadResult(string filePath, WorkspaceDocumentReloadStatus status)
		=> new(status, new WorkspaceDocumentKey(Guid.NewGuid()), filePath, 0, null);
}

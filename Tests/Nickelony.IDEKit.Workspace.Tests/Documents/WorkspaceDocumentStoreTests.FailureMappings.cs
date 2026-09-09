using Nickelony.IDEKit.Core.Pathing;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

public sealed partial class WorkspaceDocumentStoreTests
{
	[TestMethod]
	public async Task Rename_SourceStampConflictReportsExternalFileConflictAndKeepsTracking()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		FileStamp observedStamp = new(true, 9, DateTime.UnixEpoch.AddMinutes(1), "changed");
		fileSystem.MoveResult = new WorkspaceFileMoveResult(WorkspaceFileMoveStatus.ExternalFileConflict, observedStamp);

		WorkspaceDocumentRenameResult result = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			TestPath("renamed.lua")));

		Assert.AreEqual(WorkspaceDocumentRenameStatus.ExternalFileConflict, result.Status);
		Assert.AreEqual(observedStamp, result.ObservedOnDiskStamp);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));

		// The failed move must release its destination reservation so the path can be opened later.
		Assert.AreEqual(WorkspaceDocumentOpenStatus.Opened, (await store.OpenAsync(TestPath("renamed.lua"), s_openOptions)).Status);
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task RenameDirectory_GateContentionReportsOperationInProgressAndReleasesAcquiredGates()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingReplacement = pendingReplacement.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot first = (await store.OpenAsync(TestPath("folder", "a.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot second = (await store.OpenAsync(TestPath("folder", "b.lua"), s_openOptions)).Snapshot!;

		// Hold the later document's gate with an in-flight commit so the directory rename acquires the
		// earlier document's gate and then fails on the later one.
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(second.DocumentKey, second.DocumentId, second.Version),
			"edited",
			second.FileFormat));
		Task<WorkspaceDocumentCommitResult> commit = store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(edited.Snapshot!.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version),
			edited.Snapshot.OnDiskStamp));
		await fileSystem.Replacements.WhenReachedAsync(1);

		WorkspaceDocumentDirectoryRenameResult rename = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(TestPath("folder"), TestPath("moved")));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameStatus.OperationInProgress, rename.Status);

		pendingReplacement.SetResult(new WorkspaceFileReplacementResult(
			WorkspaceFileReplacementStatus.Replaced,
			new FileStamp(true, 7, DateTime.UnixEpoch.AddMinutes(1), "committed")));
		Assert.AreEqual(WorkspaceDocumentCommitStatus.Committed, (await commit).Status);

		// The failed rename must release the gates it acquired; otherwise the earlier document would
		// keep reporting OperationInProgress for later disk operations.
		WorkspaceDocumentMutationResult replaced = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(first.DocumentKey, first.DocumentId, first.Version),
			"after failed rename",
			first.FileFormat));

		Assert.AreEqual(WorkspaceDocumentMutationStatus.Changed, replaced.Status);
	}

	[TestMethod]
	public async Task DeleteDirectory_StampMismatchReportsExternalFileConflictAndKeepsTracking()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot first = (await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions)).Snapshot!;
		FileStamp changedStamp = new(true, 42, DateTime.UnixEpoch.AddMinutes(1), "changed");
		fileSystem.CapturedStamps.Enqueue(changedStamp);

		WorkspaceDocumentDirectoryDeleteResult result = await store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(TestPath("folder")));

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteStatus.ExternalFileConflict, result.Status);
		Assert.AreEqual(WorkspaceOperationFailureCodes.ExternalFileConflict, result.Failure!.Code);
		Assert.AreEqual(0, fileSystem.DirectoryDeletes.Count);
		Assert.IsTrue(store.TryGetSnapshot(first.DocumentId, out _));
	}

	[TestMethod]
	public async Task DeleteDirectory_PreCanceledTokenReportsCanceledWithoutDeleting()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions);
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();

		WorkspaceDocumentDirectoryDeleteResult result = await store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(TestPath("folder")),
			cancellation.Token);

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteStatus.Canceled, result.Status);
		Assert.AreEqual(0, fileSystem.DirectoryDeletes.Count);
	}

	[TestMethod]
	public async Task RenameDirectory_DestinationOpenedDuringMoveIsSupersededByTheMovedInstance()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingMove = new TaskCompletionSource<WorkspaceFileMoveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingMoveDirectory = pendingMove.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		await store.OpenAsync(TestPath("folder", "tracked.lua"), s_openOptions);

		Task<WorkspaceDocumentDirectoryRenameResult> rename = store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(TestPath("folder"), TestPath("moved")));
		await fileSystem.MoveDirectories.WhenReachedAsync(1);

		// A document opened under the source path is rebased together with the captured batch, and a
		// second instance opened at the same destination path while the move runs occupies the id the
		// rebase is about to take. The rebase must not fail midway: the moved instance wins the id.
		WorkspaceDocumentSnapshot lateSource = (await store.OpenAsync(TestPath("folder", "late.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot lateDestination = (await store.OpenAsync(TestPath("moved", "late.lua"), s_openOptions)).Snapshot!;

		pendingMove.SetResult(new WorkspaceFileMoveResult(WorkspaceFileMoveStatus.Moved));
		WorkspaceDocumentDirectoryRenameResult result = await rename;

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameStatus.Renamed, result.Status);
		Assert.IsTrue(store.TryGetSnapshot(TestPath("moved", "tracked.lua"), out _));
		Assert.IsTrue(store.TryGetSnapshot(TestPath("moved", "late.lua"), out WorkspaceDocumentSnapshot? movedLate));
		Assert.AreEqual(lateSource.DocumentKey, movedLate!.DocumentKey);
		Assert.AreNotEqual(lateDestination.DocumentKey, movedLate.DocumentKey);
		Assert.AreEqual(2, store.GetSnapshotsUnderDirectory(TestPath("moved")).Count);
	}

	[TestMethod]
	public async Task GetSnapshotsUnderDirectory_ReturnsSnapshotsSortedByDocumentId()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem, LocalPathComparisonPolicy.CaseSensitive);
		await store.OpenAsync(TestPath("folder", "b.lua"), s_openOptions);
		await store.OpenAsync(TestPath("folder", "a.lua"), s_openOptions);
		await store.OpenAsync(TestPath("folder", "C.lua"), s_openOptions);

		IReadOnlyList<WorkspaceDocumentSnapshot> snapshots = store.GetSnapshotsUnderDirectory(TestPath("folder"));

		// Case-sensitive ordinal order: an uppercase 'C' sorts before the lowercase names.
		CollectionAssert.AreEqual(
			new[] { TestPath("folder", "C.lua"), TestPath("folder", "a.lua"), TestPath("folder", "b.lua") },
			snapshots.Select(snapshot => snapshot.DocumentId).ToArray());
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task Rename_GateContentionReportsOperationInProgressWithoutMoving()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingReplacement = pendingReplacement.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		// Hold the document's disk gate with an in-flight commit so the file rename cannot begin.
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"edited",
			initial.FileFormat));
		Task<WorkspaceDocumentCommitResult> commit = store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(edited.Snapshot!.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version),
			edited.Snapshot.OnDiskStamp));
		await fileSystem.Replacements.WhenReachedAsync(1);

		WorkspaceDocumentRenameResult rename = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(edited.Snapshot.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version),
			edited.Snapshot.OnDiskStamp,
			TestPath("renamed.lua")));

		Assert.AreEqual(WorkspaceDocumentRenameStatus.OperationInProgress, rename.Status);
		Assert.AreEqual(0, fileSystem.Moves.Count);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));

		pendingReplacement.SetResult(new WorkspaceFileReplacementResult(
			WorkspaceFileReplacementStatus.Replaced,
			new FileStamp(true, 7, DateTime.UnixEpoch.AddMinutes(1), "committed")));
		Assert.AreEqual(WorkspaceDocumentCommitStatus.Committed, (await commit).Status);
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task SaveAs_GateContentionReportsOperationInProgressWithoutWriting()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingReplacement = pendingReplacement.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		string destinationPath = TestPath("folder", "destination.lua");

		// Hold the document's disk gate with an in-flight commit so the save-as cannot begin.
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"edited",
			initial.FileFormat));
		Task<WorkspaceDocumentCommitResult> commit = store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(edited.Snapshot!.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version),
			edited.Snapshot.OnDiskStamp));
		await fileSystem.Replacements.WhenReachedAsync(1);

		WorkspaceDocumentSaveAsResult saveAs = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(edited.Snapshot.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version),
			edited.Snapshot.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentSaveAsStatus.OperationInProgress, saveAs.Status);

		// Only the in-flight commit wrote a temporary file; the save-as did not start.
		Assert.AreEqual(1, fileSystem.WriteTemporaryCount);
		Assert.IsFalse(store.TryGetSnapshot(destinationPath, out _));

		pendingReplacement.SetResult(new WorkspaceFileReplacementResult(
			WorkspaceFileReplacementStatus.Replaced,
			new FileStamp(true, 7, DateTime.UnixEpoch.AddMinutes(1), "committed")));
		Assert.AreEqual(WorkspaceDocumentCommitStatus.Committed, (await commit).Status);
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task Delete_GateContentionReportsOperationInProgressWithoutDeleting()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingReplacement = pendingReplacement.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		// Hold the document's disk gate with an in-flight commit so the delete cannot begin.
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"edited",
			initial.FileFormat));
		Task<WorkspaceDocumentCommitResult> commit = store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(edited.Snapshot!.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version),
			edited.Snapshot.OnDiskStamp));
		await fileSystem.Replacements.WhenReachedAsync(1);

		WorkspaceDocumentDeleteResult delete = await store.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(edited.Snapshot.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version),
			edited.Snapshot.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentDeleteStatus.OperationInProgress, delete.Status);
		Assert.AreEqual(0, fileSystem.Deletes.Count);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));

		pendingReplacement.SetResult(new WorkspaceFileReplacementResult(
			WorkspaceFileReplacementStatus.Replaced,
			new FileStamp(true, 7, DateTime.UnixEpoch.AddMinutes(1), "committed")));
		Assert.AreEqual(WorkspaceDocumentCommitStatus.Committed, (await commit).Status);
	}
}

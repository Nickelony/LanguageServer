using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

public sealed partial class WorkspaceDocumentStoreTests
{
	[TestMethod]
	public async Task Delete_BlocksLogicalReplacementUntilDeletionCompletes()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingDelete = new TaskCompletionSource<WorkspaceFileDeleteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingDelete = pendingDelete.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		Task<WorkspaceDocumentDeleteResult> delete = store.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp));
		await fileSystem.Deletes.WhenReachedAsync(1);

		WorkspaceDocumentMutationResult replacement = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"later",
			initial.FileFormat));

		pendingDelete.SetResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteStatus.Deleted));
		WorkspaceDocumentDeleteResult deleted = await delete;

		Assert.AreEqual(WorkspaceDocumentMutationStatus.OperationInProgress, replacement.Status);
		Assert.AreEqual(WorkspaceDocumentDeleteStatus.Deleted, deleted.Status);
		Assert.IsFalse(store.TryGetSnapshot(initial.DocumentId, out _));
	}

	[TestMethod]
	public async Task Delete_ForwardsTheExpectedStampToTheFileSystem()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		WorkspaceDocumentDeleteResult result = await store.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));

		// The caller's stamp is the delete precondition; the file system must receive exactly that
		// value so a file that changed since the snapshot is never deleted silently.
		Assert.AreEqual(WorkspaceDocumentDeleteStatus.Deleted, result.Status);
		Assert.AreEqual(snapshot.OnDiskStamp, fileSystem.LastDeleteExpectedStamp);
	}

	[TestMethod]
	public async Task DeleteDirectory_WithoutTrackedDescendantsStillDeletesTheDirectory()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentDirectoryDeleteResult result = await store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(TestPath("empty")));

		// A folder without tracked documents is still a recursive file-system delete; only the
		// tracking cleanup has nothing to do.
		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteStatus.Deleted, result.Status);
		Assert.AreEqual(0, result.Snapshots.Count);
		Assert.AreEqual(TestPath("empty"), fileSystem.LastDirectoryDeletePath);
	}

	[TestMethod]
	public async Task GetSnapshotsUnderDirectory_TrailingSeparatorReturnsTheSameSnapshots()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions);

		IReadOnlyList<WorkspaceDocumentSnapshot> plain = store.GetSnapshotsUnderDirectory(TestPath("folder"));
		IReadOnlyList<WorkspaceDocumentSnapshot> trailing = store.GetSnapshotsUnderDirectory(
			TestPath("folder") + Path.DirectorySeparatorChar);

		// A trailing separator normalizes to the same directory identity, so both spellings list the
		// same tracked descendants.
		Assert.AreEqual(1, plain.Count);
		Assert.AreEqual(1, trailing.Count);
		Assert.AreEqual(plain[0].DocumentId, trailing[0].DocumentId);
	}

	[TestMethod]
	public async Task DeleteAndDirectoryDelete_FailuresMapToStatuses()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot file = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		fileSystem.DeleteStatus = WorkspaceFileDeleteStatus.DeleteFailed;
		WorkspaceDocumentDeleteResult delete = await store.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(file.DocumentKey, file.DocumentId, file.Version),
			file.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentDeleteStatus.DeleteFailed, delete.Status);
		Assert.IsTrue(store.TryGetSnapshot(file.DocumentId, out _));

		fileSystem.DeleteStatus = WorkspaceFileDeleteStatus.Deleted;
		fileSystem.DirectoryDeleteStatus = WorkspaceFileDeleteStatus.DeleteFailed;
		WorkspaceDocumentSnapshot directoryFile = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;
		WorkspaceDocumentDirectoryDeleteResult directoryDelete = await store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(TestPath("folder")));

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteStatus.DeleteFailed, directoryDelete.Status);
		Assert.IsTrue(store.TryGetSnapshot(directoryFile.DocumentId, out _));
	}

	[TestMethod]
	public async Task DeleteDirectory_PassesNormalizedDirectoryIdToTheFileSystem()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot first = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;

		WorkspaceDocumentDirectoryDeleteResult result = await store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(TestPath("folder", ".")));

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteStatus.Deleted, result.Status);
		Assert.AreEqual(TestPath("folder"), fileSystem.LastDirectoryDeletePath);
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task DeleteDirectory_GateContentionLeavesEarlierDocumentsReplaceable()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingReplacement = pendingReplacement.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot first = (await store.OpenAsync(
			TestPath("folder", "a.lua"),
			s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot second = (await store.OpenAsync(
			TestPath("folder", "b.lua"),
			s_openOptions)).Snapshot!;

		// Hold the later document's gate with an in-flight commit so the directory delete acquires the
		// earlier document's gate and then fails on the later one.
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(second.DocumentKey, second.DocumentId, second.Version),
			"edited",
			second.FileFormat));
		Task<WorkspaceDocumentCommitResult> commit = store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(edited.Snapshot!.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version),
			edited.Snapshot.OnDiskStamp));
		await fileSystem.Replacements.WhenReachedAsync(1);

		WorkspaceDocumentDirectoryDeleteResult delete = await store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(TestPath("folder")));

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteStatus.OperationInProgress, delete.Status);

		pendingReplacement.SetResult(new WorkspaceFileReplacementResult(
			WorkspaceFileReplacementStatus.Replaced,
			new FileStamp(true, 7, DateTime.UnixEpoch.AddMinutes(1), "committed")));
		Assert.AreEqual(WorkspaceDocumentCommitStatus.Committed, (await commit).Status);

		// The failed delete must not leave the earlier document's delete-active flag set; otherwise it
		// rejects every later Replace with OperationInProgress.
		WorkspaceDocumentMutationResult replaced = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(first.DocumentKey, first.DocumentId, first.Version),
			"after failed delete",
			first.FileFormat));

		Assert.AreEqual(WorkspaceDocumentMutationStatus.Changed, replaced.Status);
	}

	[TestMethod]
	public async Task GetSnapshotsUnderDirectory_DoesNotMatchSiblingWithSharedPrefix()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		await store.OpenAsync(TestPath("a", "one.lua"), s_openOptions);
		await store.OpenAsync(TestPath("ab", "two.lua"), s_openOptions);

		IReadOnlyList<WorkspaceDocumentSnapshot> underA = store.GetSnapshotsUnderDirectory(TestPath("a"));

		Assert.AreEqual(1, underA.Count);
		Assert.AreEqual(Path.GetFullPath(TestPath("a", "one.lua")), underA[0].DocumentId);
	}

	[TestMethod]
	public async Task DeleteDirectory_AllTrackedDescendantsAreRemovedFromTracking()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot first = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot second = (await store.OpenAsync(
			TestPath("folder", "two.lua"),
			s_openOptions)).Snapshot!;

		// Every tracked descendant is part of the recursive delete because it applies to the whole
		// directory on disk.
		WorkspaceDocumentDirectoryDeleteResult result = await store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(TestPath("folder")));

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteStatus.Deleted, result.Status);
		Assert.AreEqual(2, result.Snapshots.Count);
		Assert.IsFalse(store.TryGetSnapshot(first.DocumentId, out _));
		Assert.IsFalse(store.TryGetSnapshot(second.DocumentId, out _));
		Assert.AreEqual(0, store.GetSnapshotsUnderDirectory(TestPath("folder")).Count);
	}

	[TestMethod]
	public async Task Delete_CanceledFileSystemDeleteReportsCanceledAndKeepsTracking()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat)
		{
			DeleteStatus = WorkspaceFileDeleteStatus.Canceled
		};
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;

		WorkspaceDocumentDeleteResult result = await store.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentDeleteStatus.Canceled, result.Status);
		Assert.IsTrue(store.TryGetSnapshot(snapshot.DocumentId, out _));
	}

	[TestMethod]
	public async Task Delete_ThrowingFileSystemReportsDeleteFailedAndKeepsTracking()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat) { ThrowOnDelete = true };
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;

		WorkspaceDocumentDeleteResult result = await store.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentDeleteStatus.DeleteFailed, result.Status);
		Assert.AreEqual(WorkspaceOperationFailureCodes.DeleteFailed, result.Failure?.Code);
		Assert.IsTrue(store.TryGetSnapshot(snapshot.DocumentId, out _));
	}

	[TestMethod]
	public async Task Delete_PreCanceledDeleteReportsCanceledAndKeepsTracking()
	{
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;

		WorkspaceDocumentDeleteResult result = await store.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(
				new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
				snapshot.OnDiskStamp),
			cancellation.Token);

		Assert.AreEqual(WorkspaceDocumentDeleteStatus.Canceled, result.Status);
		Assert.IsTrue(store.TryGetSnapshot(snapshot.DocumentId, out _));
	}

	[TestMethod]
	public async Task DeleteDirectory_CanceledFileSystemDeleteReportsCanceledAndKeepsTracking()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat)
		{
			DirectoryDeleteStatus = WorkspaceFileDeleteStatus.Canceled
		};
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;

		WorkspaceDocumentDirectoryDeleteResult result = await store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(TestPath("folder")));

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteStatus.Canceled, result.Status);
		Assert.IsTrue(store.TryGetSnapshot(snapshot.DocumentId, out _));
	}

	[TestMethod]
	public async Task DeleteDirectory_ThrowingFileSystemReportsDeleteFailedAndKeepsTracking()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat) { ThrowOnDirectoryDelete = true };
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;

		WorkspaceDocumentDirectoryDeleteResult result = await store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(TestPath("folder")));

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteStatus.DeleteFailed, result.Status);
		Assert.AreEqual(WorkspaceOperationFailureCodes.DeleteFailed, result.Failure?.Code);
		Assert.IsTrue(store.TryGetSnapshot(snapshot.DocumentId, out _));
	}

	[TestMethod]
	public async Task DeleteDirectory_DescendantOpenedDuringTheDeleteIsRemovedFromTracking()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingDelete = new TaskCompletionSource<WorkspaceFileDeleteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingDirectoryDelete = pendingDelete.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot captured = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;
		Task<WorkspaceDocumentDirectoryDeleteResult> delete = store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(TestPath("folder")));
		await fileSystem.DirectoryDeletes.WhenReachedAsync(1);

		// The newcomer is opened while the recursive delete is in flight, so it is not part of the
		// captured batch, but its file is deleted with the directory on disk.
		WorkspaceDocumentOpenResult newcomer = await store.OpenAsync(TestPath("folder", "two.lua"), s_openOptions);
		Assert.AreEqual(WorkspaceDocumentOpenStatus.Opened, newcomer.Status);

		pendingDelete.SetResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteStatus.Deleted));
		WorkspaceDocumentDirectoryDeleteResult result = await delete;

		// The newcomer is removed from tracking together with the captured descendants instead of
		// surviving as a document whose file no longer exists.
		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteStatus.Deleted, result.Status);
		Assert.AreEqual(2, result.Snapshots.Count);
		Assert.IsFalse(store.TryGetSnapshot(captured.DocumentId, out _));
		Assert.IsFalse(store.TryGetSnapshot(newcomer.Snapshot!.DocumentId, out _));
		Assert.AreEqual(0, store.GetSnapshotsUnderDirectory(TestPath("folder")).Count);
	}
}

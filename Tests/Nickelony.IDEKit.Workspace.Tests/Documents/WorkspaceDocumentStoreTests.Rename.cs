using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

public sealed partial class WorkspaceDocumentStoreTests
{
	[TestMethod]
	public async Task Rename_MoveFailuresMapToRenameStatusesAndKeepTracking()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(
			TestPath("folder", "file.lua"),
			s_openOptions)).Snapshot!;
		string destinationPath = TestPath("folder", "renamed.lua");

		fileSystem.MoveResult = new WorkspaceFileMoveResult(WorkspaceFileMoveStatus.DestinationExists);
		WorkspaceDocumentRenameResult exists = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentRenameStatus.DestinationExists, exists.Status);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));

		fileSystem.MoveResult = new WorkspaceFileMoveResult(WorkspaceFileMoveStatus.MoveFailed);
		WorkspaceDocumentRenameResult failed = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentRenameStatus.MoveFailed, failed.Status);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));

		fileSystem.MoveResult = new WorkspaceFileMoveResult(WorkspaceFileMoveStatus.MoveStateUnknown);
		WorkspaceDocumentRenameResult unknown = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentRenameStatus.MoveStateUnknown, unknown.Status);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));

		fileSystem.MoveResult = new WorkspaceFileMoveResult(WorkspaceFileMoveStatus.Canceled);
		WorkspaceDocumentRenameResult canceled = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentRenameStatus.Canceled, canceled.Status);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
	}

	[TestMethod]
	public async Task RenameDirectory_MoveFailuresMapToStatusesAndKeepTracking()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot first = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;

		foreach ((WorkspaceFileMoveStatus moveStatus, WorkspaceDocumentDirectoryRenameStatus expectedStatus) in new[]
		{
			(WorkspaceFileMoveStatus.DestinationExists, WorkspaceDocumentDirectoryRenameStatus.DestinationExists),
			(WorkspaceFileMoveStatus.MoveFailed, WorkspaceDocumentDirectoryRenameStatus.MoveFailed),
			(WorkspaceFileMoveStatus.MoveStateUnknown, WorkspaceDocumentDirectoryRenameStatus.MoveStateUnknown),
			(WorkspaceFileMoveStatus.Canceled, WorkspaceDocumentDirectoryRenameStatus.Canceled)
		})
		{
			fileSystem.MoveDirectoryResult = new WorkspaceFileMoveResult(moveStatus);

			WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
				new WorkspaceDocumentDirectoryRenameRequest(
					TestPath("folder"),
					TestPath("moved")));

			Assert.AreEqual(expectedStatus, result.Status);
			Assert.IsTrue(store.TryGetSnapshot(first.DocumentId, out _));
			Assert.AreEqual(0, store.GetSnapshotsUnderDirectory(TestPath("moved")).Count);
		}
	}

	[TestMethod]
	public async Task RenameDirectory_TrackedDestinationCollisionReportsDestinationInUse()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot source = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot collision = (await store.OpenAsync(
			TestPath("moved", "one.lua"),
			s_openOptions)).Snapshot!;

		// The rebased destination "moved/one.lua" is already tracked by an unrelated document, so the
		// directory rename must fail without touching that document or the source descendants.
		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(
				TestPath("folder"),
				TestPath("moved")));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameStatus.DestinationInUse, result.Status);
		Assert.IsTrue(store.TryGetSnapshot(source.DocumentId, out _));
		Assert.IsTrue(store.TryGetSnapshot(collision.DocumentId, out _));
		Assert.AreEqual(1, store.GetSnapshotsUnderDirectory(TestPath("moved")).Count);
	}

	[TestMethod]
	public async Task RenameDirectory_ReservedDestinationReportsDestinationBusy()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingReplacement = pendingReplacement.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot source = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot mover = (await store.OpenAsync(TestPath("other.lua"), s_openOptions)).Snapshot!;

		// Hold an in-flight Save As that has reserved "moved/one.lua"; the directory rename must see
		// the reservation as DestinationBusy because its rebased descendant targets the same destination.
		fileSystem.CapturedStamps.Enqueue(mover.OnDiskStamp);
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);
		Task<WorkspaceDocumentSaveAsResult> saveAs = store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(mover.DocumentKey, mover.DocumentId, mover.Version),
			mover.OnDiskStamp,
			TestPath("moved", "one.lua")));
		await fileSystem.Replacements.WhenReachedAsync(1);

		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(
				TestPath("folder"),
				TestPath("moved")));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameStatus.DestinationBusy, result.Status);
		Assert.IsTrue(store.TryGetSnapshot(source.DocumentId, out _));

		pendingReplacement.SetResult(new WorkspaceFileReplacementResult(
			WorkspaceFileReplacementStatus.Replaced,
			new FileStamp(true, 7, DateTime.UnixEpoch.AddMinutes(1), "saved")));
		WorkspaceDocumentSaveAsResult saved = await saveAs;

		Assert.AreEqual(WorkspaceDocumentSaveAsStatus.SavedAs, saved.Status);
		Assert.IsTrue(store.TryGetSnapshot(TestPath("moved", "one.lua"), out _));
	}

	[TestMethod]
	public async Task Rename_DestinationWithInFlightOpenReportsDestinationBusy()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("source.lua"), s_openOptions)).Snapshot!;
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingRead = pendingRead.Task;

		// The open holds a reservation for the destination path while it loads.
		Task<WorkspaceDocumentOpenResult> open = store.OpenAsync(TestPath("destination.lua"), s_openOptions);
		await fileSystem.Reads.WhenReachedAsync(2);

		WorkspaceDocumentRenameResult result = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			TestPath("destination.lua")));

		// The destination checks include the in-flight open reservation, so the file is never moved and
		// the tracked instance is never dropped by a colliding identity update.
		Assert.AreEqual(WorkspaceDocumentRenameStatus.DestinationBusy, result.Status);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out WorkspaceDocumentSnapshot? retained));
		Assert.AreEqual(initial.DocumentKey, retained!.DocumentKey);

		pendingRead.SetResult(fileSystem.CreateReadResult());
		WorkspaceDocumentOpenResult opened = await open;
		Assert.AreEqual(WorkspaceDocumentOpenStatus.Opened, opened.Status);
	}

	[TestMethod]
	public async Task RenameDirectory_RebasesAllTrackedDescendantsAndPreservesKeys()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot first = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot second = (await store.OpenAsync(
			TestPath("folder", "two.lua"),
			s_openOptions)).Snapshot!;

		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(
				TestPath("folder"),
				TestPath("moved")));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameStatus.Renamed, result.Status);
		Assert.AreEqual(2, result.Snapshots.Count);
		Assert.AreEqual(first.DocumentKey, result.Snapshots[0].DocumentKey);
		Assert.AreEqual(second.DocumentKey, result.Snapshots[1].DocumentKey);
		Assert.AreEqual(Path.GetFullPath(TestPath("moved", "one.lua")), result.Snapshots[0].DocumentId);
		Assert.AreEqual(Path.GetFullPath(TestPath("moved", "two.lua")), result.Snapshots[1].DocumentId);
		Assert.IsFalse(store.TryGetSnapshot(first.DocumentId, out _));
		Assert.IsFalse(store.TryGetSnapshot(second.DocumentId, out _));
		Assert.AreEqual(2, store.GetSnapshotsUnderDirectory(TestPath("moved")).Count);
	}

	[TestMethod]
	public async Task RenameDirectory_ExactSameSpellingReportsNoChange()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot first = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;

		// A destination that normalizes to the source directory is a no-op: the directory already is
		// where the caller wants it, so the result mirrors the file rename NoChange outcome instead of
		// reporting InvalidPath.
		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(
				TestPath("folder"),
				TestPath("folder")));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameStatus.NoChange, result.Status);
		Assert.AreEqual(1, result.Snapshots.Count);
		Assert.AreEqual(first.DocumentKey, result.Snapshots[0].DocumentKey);
		Assert.IsTrue(store.TryGetSnapshot(first.DocumentId, out _));
		Assert.AreEqual(0, fileSystem.MoveDirectoryCount);
	}

	[TestMethod]
	public async Task RenameDirectory_EquivalentSpellingReportsNoChangeWithoutReachingTheFileSystem()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot first = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;

		// A trailing separator or a "." segment normalizes to the same directory identity, so the
		// request is the same no-op as an exactly repeated spelling.
		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(
				TestPath("folder"),
				TestPath("folder", ".")));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameStatus.NoChange, result.Status);
		Assert.AreEqual(1, result.Snapshots.Count);
		Assert.IsTrue(store.TryGetSnapshot(first.DocumentId, out _));
		Assert.AreEqual(0, fileSystem.MoveDirectoryCount);

		WorkspaceDocumentDirectoryRenameResult trailingSeparator = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(
				TestPath("folder"),
				TestPath("folder") + Path.DirectorySeparatorChar));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameStatus.NoChange, trailingSeparator.Status);
		Assert.AreEqual(1, trailingSeparator.Snapshots.Count);
		Assert.AreEqual(0, fileSystem.MoveDirectoryCount);
	}

	[TestMethod]
	public async Task RenameDirectory_PassesNormalizedDirectoryIdsToTheFileSystem()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot first = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;

		// The caller brings a relative spelling with a redundant segment; the file system must receive
		// the normalized ids so a case-sensitive file system is not at the mercy of that spelling.
		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(
				TestPath("folder", "."),
				TestPath("moved")));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameStatus.Renamed, result.Status);
		Assert.AreEqual(TestPath("folder"), fileSystem.LastMoveDirectorySource);
		Assert.AreEqual(TestPath("moved"), fileSystem.LastMoveDirectoryDestination);
	}

	[TestMethod]
	public async Task Rename_EquivalentSpellingReturnsNoChangeWithoutMoving()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(
			TestPath("folder", "file.lua"),
			s_openOptions)).Snapshot!;
		string destinationPath = TestPath("folder", ".", "file.lua");

		WorkspaceDocumentRenameResult result = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentRenameStatus.NoChange, result.Status);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
		Assert.AreEqual(0, fileSystem.Moves.Count);
	}

	[TestMethod]
	public async Task Rename_ForwardsTheExpectedSourceStampToTheFileSystem()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		WorkspaceDocumentRenameResult result = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			TestPath("renamed.lua")));

		// The caller's stamp is the move precondition; the file system must receive exactly that value
		// so a source file that changed since the snapshot is never moved silently.
		Assert.AreEqual(WorkspaceDocumentRenameStatus.Renamed, result.Status);
		Assert.AreEqual(initial.OnDiskStamp, fileSystem.LastMoveExpectedStamp);
	}

	[TestMethod]
	public async Task Rename_ConcurrentReplacementStaysAppliedAfterTheMove()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		var pendingMove = new TaskCompletionSource<WorkspaceFileMoveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingMove = pendingMove.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		Task<WorkspaceDocumentRenameResult> rename = store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			TestPath("renamed.lua")));
		await fileSystem.Moves.WhenReachedAsync(1);

		// A logical replacement does not take the disk gate, so it interleaves with the in-flight move
		// by design; the renamed document must keep the edit.
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"edited",
			initial.FileFormat));

		pendingMove.SetResult(new WorkspaceFileMoveResult(WorkspaceFileMoveStatus.Moved));
		WorkspaceDocumentRenameResult result = await rename;

		Assert.AreEqual(WorkspaceDocumentMutationStatus.Changed, edited.Status);
		Assert.AreEqual(WorkspaceDocumentRenameStatus.Renamed, result.Status);
		Assert.AreEqual("edited", result.Snapshot!.Content);
		Assert.IsTrue(result.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task RenameDirectory_WithoutTrackedDescendantsStillMovesTheDirectory()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(TestPath("empty"), TestPath("moved")));

		// A folder without tracked documents is still a file-system rename; only the identity rebase has
		// nothing to do.
		Assert.AreEqual(WorkspaceDocumentDirectoryRenameStatus.Renamed, result.Status);
		Assert.AreEqual(0, result.Snapshots.Count);
		Assert.AreEqual(1, fileSystem.MoveDirectoryCount);
	}

	[TestMethod]
	public async Task RenameDirectory_StampMismatchReportsExternalFileConflict()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot first = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;
		fileSystem.CapturedStamps.Enqueue(new FileStamp(true, 7, DateTime.UnixEpoch.AddMinutes(1), "changed"));

		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(
				TestPath("folder"),
				TestPath("moved")));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameStatus.ExternalFileConflict, result.Status);
		Assert.IsTrue(store.TryGetSnapshot(first.DocumentId, out _));
		Assert.AreEqual(0, fileSystem.MoveDirectoryCount);
	}

	[TestMethod]
	public async Task Rename_BlankDestinationReportsInvalidPath()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		WorkspaceDocumentRenameResult result = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			"   "));

		// The destination is validated before the operation gate, so the document keeps its identity.
		Assert.AreEqual(WorkspaceDocumentRenameStatus.InvalidPath, result.Status);
		Assert.IsNull(result.Snapshot);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
	}

	[TestMethod]
	public async Task RenameDirectory_BlankDestinationReportsInvalidPath()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;

		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(
				TestPath("folder"),
				"   "));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameStatus.InvalidPath, result.Status);
		Assert.AreEqual(0, fileSystem.MoveDirectoryCount);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
	}

	[TestMethod]
	public async Task RenameAndSaveAs_PreserveDocumentKeyAndRetargetPath()
	{
		using var temp = new TestTempDirectory();
		string directory = temp.Path;
		string sourcePath = Path.Combine(directory, "source.lua");
		string renamedPath = Path.Combine(directory, "renamed.lua");
		string savedAsPath = Path.Combine(directory, "saved-as.lua");
		File.WriteAllText(sourcePath, "content");
		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(sourcePath, s_openOptions)).Snapshot!;

		WorkspaceDocumentRenameResult renamed = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			renamedPath));

		Assert.AreEqual(WorkspaceDocumentRenameStatus.Renamed, renamed.Status);
		Assert.IsFalse(store.TryGetSnapshot(sourcePath, out _));
		Assert.IsTrue(store.TryGetSnapshot(renamedPath, out WorkspaceDocumentSnapshot? renamedSnapshot));
		Assert.AreEqual(initial.DocumentKey, renamedSnapshot!.DocumentKey);

		WorkspaceDocumentSaveAsResult savedAs = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(renamedSnapshot.DocumentKey, renamedSnapshot.DocumentId, renamedSnapshot.Version),
			renamedSnapshot.OnDiskStamp,
			savedAsPath));

		Assert.AreEqual(WorkspaceDocumentSaveAsStatus.SavedAs, savedAs.Status);
		Assert.IsFalse(store.TryGetSnapshot(renamedPath, out _));
		Assert.IsTrue(store.TryGetSnapshot(savedAsPath, out WorkspaceDocumentSnapshot? savedAsSnapshot));
		Assert.AreEqual(initial.DocumentKey, savedAsSnapshot!.DocumentKey);

		// Save As follows the commit-path convention: the persisted version identifies the
		// captured content that was written while the retargeted snapshot version is incremented.
		Assert.AreEqual(renamedSnapshot.Version + 1, savedAsSnapshot.Version);
		Assert.AreEqual(renamedSnapshot.Version, savedAsSnapshot.PersistedVersion);
		Assert.IsFalse(savedAsSnapshot.IsDirty);

		WorkspaceDocumentDeleteResult deleted = await store.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(savedAsSnapshot.DocumentKey, savedAsSnapshot.DocumentId, savedAsSnapshot.Version),
			savedAsSnapshot.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentDeleteStatus.Deleted, deleted.Status);
		Assert.IsFalse(store.TryGetSnapshot(savedAsPath, out _));
		Assert.IsFalse(File.Exists(savedAsPath));
	}

	[TestMethod]
	public async Task Rename_CaseOnlyPreservesDocumentKeyAndUpdatesDisplayPath()
	{
		using var temp = new TestTempDirectory();
		string directory = temp.Path;
		string sourcePath = Path.Combine(directory, "Script.lua");
		string destinationPath = Path.Combine(directory, "script.lua");
		File.WriteAllText(sourcePath, "content");
		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(sourcePath, s_openOptions)).Snapshot!;

		WorkspaceDocumentRenameResult result = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentRenameStatus.Renamed, result.Status);
		Assert.AreEqual(initial.DocumentKey, result.Snapshot!.DocumentKey);
		Assert.AreEqual(destinationPath, result.Snapshot.DisplayPath);
		string[] files = Directory.GetFiles(directory);
		Assert.AreEqual(1, files.Length);
		Assert.AreEqual("script.lua", Path.GetFileName(files[0]));
	}

	[TestMethod]
	public async Task RenameDirectory_EquivalentSpellingWithFullPathsReportsNoChange()
	{
		using var temp = new TestTempDirectory();
		string directory = temp.Path;
		string sourcePath = Path.Combine(directory, "folder");
		Directory.CreateDirectory(sourcePath);
		string filePath = Path.Combine(sourcePath, "one.lua");
		File.WriteAllText(filePath, "content");
		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(filePath, s_openOptions)).Snapshot!;

		// A "." segment normalizes to the same directory identity, so the request is a no-op that
		// never reaches the file system.
		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(
				sourcePath,
				Path.Combine(sourcePath, ".")));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameStatus.NoChange, result.Status);
		Assert.AreEqual(1, result.Snapshots.Count);
		Assert.IsTrue(Directory.Exists(sourcePath));
		Assert.IsTrue(File.Exists(filePath));
		Assert.IsTrue(store.TryGetSnapshot(filePath, out WorkspaceDocumentSnapshot? retained));
		Assert.AreEqual(initial.DocumentId, retained!.DocumentId);
	}

	[TestMethod]
	public async Task Rename_TrackedDestinationCollisionReportsDestinationInUse()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot source = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot collision = (await store.OpenAsync(
			TestPath("folder", "two.lua"),
			s_openOptions)).Snapshot!;

		// The destination is tracked by an unrelated document instance, so the rename is rejected
		// before the file system is asked to move anything.
		WorkspaceDocumentRenameResult result = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(source.DocumentKey, source.DocumentId, source.Version),
			source.OnDiskStamp,
			TestPath("folder", "two.lua")));

		Assert.AreEqual(WorkspaceDocumentRenameStatus.DestinationInUse, result.Status);
		Assert.AreEqual(0, fileSystem.Moves.Count);
		Assert.IsTrue(store.TryGetSnapshot(source.DocumentId, out _));
		Assert.IsTrue(store.TryGetSnapshot(collision.DocumentId, out _));
	}

	[TestMethod]
	public async Task Rename_PreCanceledMoveReportsCanceledAndKeepsTracking()
	{
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions)).Snapshot!;

		WorkspaceDocumentRenameResult result = await store.RenameAsync(
			new WorkspaceDocumentRenameRequest(
				new(initial.DocumentKey, initial.DocumentId, initial.Version),
				initial.OnDiskStamp,
				TestPath("folder", "renamed.lua")),
			cancellation.Token);

		Assert.AreEqual(WorkspaceDocumentRenameStatus.Canceled, result.Status);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
	}

	[TestMethod]
	public async Task Rename_ThrowingFileSystemReportsMoveFailedAndKeepsTracking()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat) { ThrowOnMove = true };
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions)).Snapshot!;

		WorkspaceDocumentRenameResult result = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			TestPath("folder", "renamed.lua")));

		Assert.AreEqual(WorkspaceDocumentRenameStatus.MoveFailed, result.Status);
		Assert.AreEqual(WorkspaceOperationFailureCodes.MoveFailed, result.Failure?.Code);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
	}

	[TestMethod]
	public async Task RenameDirectory_PreCanceledMoveReportsCanceledAndKeepsTracking()
	{
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot first = (await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions)).Snapshot!;

		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(TestPath("folder"), TestPath("moved")),
			cancellation.Token);

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameStatus.Canceled, result.Status);
		Assert.IsTrue(store.TryGetSnapshot(first.DocumentId, out _));
		Assert.AreEqual(0, store.GetSnapshotsUnderDirectory(TestPath("moved")).Count);
	}

	[TestMethod]
	public async Task RenameDirectory_ThrowingFileSystemReportsMoveFailedAndKeepsTracking()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat) { ThrowOnMoveDirectory = true };
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot first = (await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions)).Snapshot!;

		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(TestPath("folder"), TestPath("moved")));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameStatus.MoveFailed, result.Status);
		Assert.AreEqual(WorkspaceOperationFailureCodes.MoveFailed, result.Failure?.Code);
		Assert.IsTrue(store.TryGetSnapshot(first.DocumentId, out _));
	}

	[TestMethod]
	public async Task RenameDirectory_DescendantOpenedDuringTheMoveIsRebasedWithTheOthers()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingMove = new TaskCompletionSource<WorkspaceFileMoveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingMoveDirectory = pendingMove.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot captured = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;
		Task<WorkspaceDocumentDirectoryRenameResult> rename = store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(TestPath("folder"), TestPath("moved")));
		await fileSystem.MoveDirectories.WhenReachedAsync(1);

		// The newcomer is opened while the move is in flight, so it is not part of the captured batch,
		// but its file moves with the directory on disk.
		WorkspaceDocumentOpenResult newcomer = await store.OpenAsync(TestPath("folder", "two.lua"), s_openOptions);
		Assert.AreEqual(WorkspaceDocumentOpenStatus.Opened, newcomer.Status);

		pendingMove.SetResult(new WorkspaceFileMoveResult(WorkspaceFileMoveStatus.Moved));
		WorkspaceDocumentDirectoryRenameResult result = await rename;

		// The newcomer is rebased together with the captured descendants instead of staying tracked
		// under the vacated source path.
		Assert.AreEqual(WorkspaceDocumentDirectoryRenameStatus.Renamed, result.Status);
		Assert.AreEqual(2, result.Snapshots.Count);
		Assert.IsFalse(store.TryGetSnapshot(captured.DocumentId, out _));
		Assert.IsTrue(store.TryGetSnapshot(TestPath("moved", "one.lua"), out WorkspaceDocumentSnapshot? rebasedCaptured));
		Assert.AreEqual(captured.DocumentKey, rebasedCaptured!.DocumentKey);
		Assert.IsFalse(store.TryGetSnapshot(newcomer.Snapshot!.DocumentId, out _));
		Assert.IsTrue(store.TryGetSnapshot(TestPath("moved", "two.lua"), out WorkspaceDocumentSnapshot? rebased));
		Assert.AreEqual(newcomer.Snapshot.DocumentKey, rebased!.DocumentKey);
	}
}

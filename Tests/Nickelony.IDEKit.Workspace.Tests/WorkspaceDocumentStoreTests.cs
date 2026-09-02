using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Tests;

[TestClass]
[TestCategory("TextEditorBaseModernization")]
public sealed class WorkspaceDocumentStoreTests
{
	private static readonly TextFileFormat s_defaultFormat = new(
		TextEncodingKind.Utf8,
		false,
		TextNewlineStyle.Lf);

	private static readonly WorkspaceDocumentOpenOptions s_openOptions = new(
		TextEncodingKind.Utf8,
		s_defaultFormat);

	[TestMethod]
	public async Task InvalidPaths_AreRejectedWithoutReading()
	{
		var fileSystem = new FakeFileSystem("unused", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentOpenResult nullPath = await store.OpenAsync(null, s_openOptions);
		WorkspaceDocumentOpenResult whitespacePath = await store.OpenAsync("  ", s_openOptions);
		bool found = store.TryGetSnapshot(string.Empty, out WorkspaceDocumentSnapshot? snapshot);

		Assert.AreEqual(WorkspaceDocumentOpenStatus.InvalidPath, nullPath.Status);
		Assert.AreEqual(WorkspaceDocumentOpenStatus.InvalidPath, whitespacePath.Status);
		Assert.IsFalse(found);
		Assert.IsNull(snapshot);
		Assert.AreEqual(0, fileSystem.ReadCount);
	}

	[TestMethod]
	public async Task OpenEquivalentPaths_UsesOneDocumentAndFirstDisplayPath()
	{
		var fileSystem = new FakeFileSystem("source", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		string firstPath = Path.Combine("workspace", "scripts", "main.lua");
		string equivalentPath = Path.Combine("workspace", "scripts", ".", "MAIN.lua");

		WorkspaceDocumentOpenResult first = await store.OpenAsync(firstPath, s_openOptions);
		WorkspaceDocumentOpenResult second = await store.OpenAsync(equivalentPath, s_openOptions);

		Assert.AreEqual(WorkspaceDocumentOpenStatus.Opened, first.Status);
		Assert.AreEqual(WorkspaceDocumentOpenStatus.AlreadyOpen, second.Status);
		Assert.IsNotNull(first.Snapshot);
		Assert.IsNotNull(second.Snapshot);
		Assert.AreEqual(first.Snapshot.DocumentKey, second.Snapshot.DocumentKey);
		Assert.AreEqual(first.Snapshot.DocumentId, second.Snapshot.DocumentId);
		Assert.AreEqual(Path.GetFullPath(firstPath), first.Snapshot.DocumentId);
		Assert.AreEqual(firstPath, first.Snapshot.DisplayPath);
		Assert.AreEqual(first.Snapshot.DocumentId, fileSystem.LastReadPath);
		Assert.AreEqual(1, fileSystem.ReadCount);
	}

	[TestMethod]
	public async Task Replacement_RetainsLogicalContentWhenOpenedAgain()
	{
		var fileSystem = new FakeFileSystem("disk", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentOpenResult opened = await store.OpenAsync("script.lua", s_openOptions);
		WorkspaceDocumentSnapshot initial = opened.Snapshot!;
		WorkspaceDocumentMutationResult noOp = store.TryReplace(new WorkspaceDocumentReplaceRequest(
			initial.DocumentKey,
			initial.DocumentId,
			initial.Version,
			initial.Content,
			initial.FileFormat));

		WorkspaceDocumentMutationResult replaced = store.TryReplace(new WorkspaceDocumentReplaceRequest(
			initial.DocumentKey,
			initial.DocumentId,
			initial.Version,
			"logical",
			initial.FileFormat));
		WorkspaceDocumentOpenResult reopened = await store.OpenAsync("script.lua", s_openOptions);

		Assert.AreEqual(WorkspaceDocumentMutationStatus.NoChange, noOp.Status);
		Assert.AreEqual(0, noOp.Snapshot!.Version);
		Assert.AreEqual(WorkspaceDocumentMutationStatus.Replaced, replaced.Status);
		Assert.IsNotNull(replaced.Snapshot);
		Assert.IsTrue(replaced.Snapshot.IsDirty);
		Assert.AreEqual("disk", initial.Content);
		Assert.AreEqual("logical", reopened.Snapshot!.Content);
		Assert.AreEqual(replaced.Snapshot.Version, reopened.Snapshot.Version);
		Assert.AreEqual(1, fileSystem.ReadCount);
	}

	[TestMethod]
	public async Task ConcurrentFirstOpen_UsesOneReadAndFollowerCancellationIsIndependent()
	{
		var fileSystem = new FakeFileSystem("loaded", s_defaultFormat);
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingRead = pendingRead.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);

		Task<WorkspaceDocumentOpenResult> loader = store.OpenAsync("script.lua", s_openOptions);
		await fileSystem.ReadStarted.Task;

		using var followerCancellation = new CancellationTokenSource();
		Task<WorkspaceDocumentOpenResult> follower = store.OpenAsync(
			"script.lua",
			s_openOptions,
			followerCancellation.Token);
		followerCancellation.Cancel();

		WorkspaceDocumentOpenResult followerResult = await follower;
		pendingRead.SetResult(fileSystem.CreateReadResult());
		WorkspaceDocumentOpenResult loaderResult = await loader;

		Assert.AreEqual(WorkspaceDocumentOpenStatus.Cancelled, followerResult.Status);
		Assert.AreEqual(WorkspaceDocumentOpenStatus.Opened, loaderResult.Status);
		Assert.AreEqual(1, fileSystem.ReadCount);
	}

	[TestMethod]
	public async Task FailedOpen_RemovesReservationAndAllowsRetry()
	{
		var fileSystem = new FakeFileSystem("retry", s_defaultFormat);
		var failedRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingRead = failedRead.Task;
		fileSystem.ReadResults.Enqueue(Task.FromResult(fileSystem.CreateReadResult()));
		await using var store = new WorkspaceDocumentStore(fileSystem);

		Task<WorkspaceDocumentOpenResult> loader = store.OpenAsync("script.lua", s_openOptions);
		await fileSystem.ReadStarted.Task;
		Task<WorkspaceDocumentOpenResult> follower = store.OpenAsync("script.lua", s_openOptions);
		fileSystem.PendingRead = null;
		failedRead.SetException(new IOException("read failed"));

		WorkspaceDocumentOpenResult failed = await loader;
		WorkspaceDocumentOpenResult retried = await follower;

		Assert.AreEqual(WorkspaceDocumentOpenStatus.LoadFailed, failed.Status);
		Assert.IsNotNull(failed.Failure);
		Assert.AreEqual(WorkspaceDocumentOpenStatus.Opened, retried.Status);
		Assert.AreEqual(2, fileSystem.ReadCount);
	}

	[TestMethod]
	public async Task CancelledOpen_RemovesReservationAndAllowsRetry()
	{
		var fileSystem = new FakeFileSystem("cancelled", s_defaultFormat);
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingRead = pendingRead.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);

		using var cancellation = new CancellationTokenSource();
		Task<WorkspaceDocumentOpenResult> cancelledOpen = store.OpenAsync(
			"script.lua",
			s_openOptions,
			cancellation.Token);
		await fileSystem.ReadStarted.Task;
		cancellation.Cancel();

		WorkspaceDocumentOpenResult cancelled = await cancelledOpen;
		fileSystem.PendingRead = null;
		WorkspaceDocumentOpenResult retried = await store.OpenAsync("script.lua", s_openOptions);

		Assert.AreEqual(WorkspaceDocumentOpenStatus.Cancelled, cancelled.Status);
		Assert.AreEqual(WorkspaceDocumentOpenStatus.Opened, retried.Status);
		Assert.AreEqual(2, fileSystem.ReadCount);
	}

	[TestMethod]
	public async Task ReplacementAndUndoToBaseline_AdvanceVersionButRestoreCleanState()
	{
		var fileSystem = new FakeFileSystem("baseline", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync("script.lua", s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.TryReplace(new WorkspaceDocumentReplaceRequest(
			initial.DocumentKey,
			initial.DocumentId,
			initial.Version,
			"edited",
			initial.FileFormat));
		WorkspaceDocumentMutationResult undone = store.TryReplace(new WorkspaceDocumentReplaceRequest(
			initial.DocumentKey,
			initial.DocumentId,
			edited.Snapshot!.Version,
			initial.Content,
			initial.FileFormat));

		Assert.AreEqual(1, edited.Snapshot.Version);
		Assert.IsTrue(edited.Snapshot.IsDirty);
		Assert.AreEqual(WorkspaceDocumentMutationStatus.Replaced, undone.Status);
		Assert.AreEqual(2, undone.Snapshot!.Version);
		Assert.AreEqual(0, undone.Snapshot.PersistedVersion);
		Assert.IsFalse(undone.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task StaleVersionAndKey_ReturnCurrentSnapshotWithoutMutation()
	{
		var fileSystem = new FakeFileSystem("current", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync("script.lua", s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult changed = store.TryReplace(new WorkspaceDocumentReplaceRequest(
			initial.DocumentKey,
			initial.DocumentId,
			initial.Version,
			"changed",
			initial.FileFormat));

		WorkspaceDocumentMutationResult staleVersion = store.TryReplace(new WorkspaceDocumentReplaceRequest(
			initial.DocumentKey,
			initial.DocumentId,
			initial.Version,
			"stale",
			initial.FileFormat));
		WorkspaceDocumentMutationResult staleKey = store.TryReplace(new WorkspaceDocumentReplaceRequest(
			new WorkspaceDocumentKey(Guid.NewGuid()),
			initial.DocumentId,
			changed.Snapshot!.Version,
			"stale key",
			initial.FileFormat));

		Assert.AreEqual(WorkspaceDocumentMutationStatus.StaleDocument, staleVersion.Status);
		Assert.AreEqual(WorkspaceDocumentMutationStatus.StaleDocumentInstance, staleKey.Status);
		Assert.IsNotNull(staleVersion.Snapshot);
		Assert.IsNotNull(staleKey.Snapshot);
		Assert.AreEqual("changed", staleVersion.Snapshot.Content);
		Assert.AreEqual(changed.Snapshot.Version, staleVersion.Snapshot.Version);
		Assert.AreEqual(staleVersion.Snapshot.Content, staleKey.Snapshot.Content);
	}

	[TestMethod]
	public async Task Discard_RestoresContentAndFormatAndIsNoOpWhenClean()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync("script.lua", s_openOptions)).Snapshot!;
		TextFileFormat changedFormat = new(TextEncodingKind.Utf16LittleEndian, true, TextNewlineStyle.CrLf);
		WorkspaceDocumentMutationResult changed = store.TryReplace(new WorkspaceDocumentReplaceRequest(
			initial.DocumentKey,
			initial.DocumentId,
			initial.Version,
			"changed",
			changedFormat));
		WorkspaceDocumentMutationResult discarded = store.Discard(new WorkspaceDocumentDiscardRequest(
			initial.DocumentKey,
			initial.DocumentId,
			changed.Snapshot!.Version));
		WorkspaceDocumentMutationResult noOp = store.Discard(new WorkspaceDocumentDiscardRequest(
			initial.DocumentKey,
			initial.DocumentId,
			discarded.Snapshot!.Version));

		Assert.AreEqual(WorkspaceDocumentMutationStatus.Replaced, discarded.Status);
		Assert.AreEqual(initial.Content, discarded.Snapshot!.Content);
		Assert.AreEqual(initial.FileFormat, discarded.Snapshot.FileFormat);
		Assert.AreEqual(2, discarded.Snapshot.Version);
		Assert.AreEqual(2, discarded.Snapshot.PersistedVersion);
		Assert.IsFalse(discarded.Snapshot.IsDirty);
		Assert.AreEqual(WorkspaceDocumentMutationStatus.NoChange, noOp.Status);
		Assert.AreEqual(discarded.Snapshot.Version, noOp.Snapshot!.Version);
	}

	[TestMethod]
	public async Task Reload_ChangedCleanDocumentInstallsNewContentAndBaseline()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync("script.lua", s_openOptions)).Snapshot!;
		FileStamp reloadedStamp = new(true, 7, DateTime.UnixEpoch.AddMinutes(1), "reloaded");
		fileSystem.ReadResults.Enqueue(Task.FromResult(new WorkspaceFileReadResult(
			"reloaded",
			s_defaultFormat,
			reloadedStamp)));

		WorkspaceDocumentReloadResult result = await store.ReloadAsync(new WorkspaceDocumentReloadRequest(
			initial.DocumentKey,
			initial.DocumentId,
			initial.Version,
			initial.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentReloadStatus.Reloaded, result.Status);
		Assert.IsNotNull(result.Snapshot);
		Assert.AreEqual("reloaded", result.Snapshot.Content);
		Assert.AreEqual(1, result.Snapshot.Version);
		Assert.AreEqual(1, result.Snapshot.PersistedVersion);
		Assert.IsFalse(result.Snapshot.IsDirty);
		Assert.AreEqual(reloadedStamp, result.Snapshot.OnDiskStamp);
	}

	[TestMethod]
	public async Task Reload_DirtyDocumentReturnsConflictWithoutReadingAgain()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync("script.lua", s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.TryReplace(new WorkspaceDocumentReplaceRequest(
			initial.DocumentKey,
			initial.DocumentId,
			initial.Version,
			"logical",
			initial.FileFormat));
		FileStamp externalStamp = new(true, 8, DateTime.UnixEpoch.AddMinutes(1), "external");
		fileSystem.CapturedStamps.Enqueue(externalStamp);

		WorkspaceDocumentReloadResult result = await store.ReloadAsync(new WorkspaceDocumentReloadRequest(
			initial.DocumentKey,
			initial.DocumentId,
			edited.Snapshot!.Version,
			initial.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentReloadStatus.ExternalFileConflict, result.Status);
		Assert.AreEqual("logical", result.Snapshot!.Content);
		Assert.AreEqual(externalStamp, result.ObservedOnDiskStamp);
		Assert.AreEqual(externalStamp, result.Snapshot.OnDiskStamp);
		Assert.AreEqual(1, fileSystem.ReadCount);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UseDiskInstallsCurrentDiskContent()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync("script.lua", s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.TryReplace(new WorkspaceDocumentReplaceRequest(
			initial.DocumentKey,
			initial.DocumentId,
			initial.Version,
			"logical",
			initial.FileFormat));
		FileStamp externalStamp = new(true, 3, DateTime.UnixEpoch.AddMinutes(1), "disk");
		fileSystem.ReadResults.Enqueue(Task.FromResult(new WorkspaceFileReadResult(
			"disk",
			s_defaultFormat,
			externalStamp)));
		fileSystem.CapturedStamps.Enqueue(externalStamp);

		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				initial.DocumentKey,
				initial.DocumentId,
				edited.Snapshot!.Version,
				initial.OnDiskStamp,
				WorkspaceDocumentConflictResolutionChoice.UseDisk));

		Assert.AreEqual(WorkspaceDocumentConflictResolutionStatus.ResolvedWithDisk, result.Status);
		Assert.AreEqual("disk", result.Snapshot!.Content);
		Assert.AreEqual(2, result.Snapshot.Version);
		Assert.AreEqual(2, result.Snapshot.PersistedVersion);
		Assert.IsFalse(result.Snapshot.IsDirty);
		Assert.AreEqual(externalStamp, result.Snapshot.OnDiskStamp);
		Assert.AreEqual(2, fileSystem.ReadCount);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UseLogicalOverwritesCurrentDiskAndCommitsBaseline()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync("script.lua", s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.TryReplace(new WorkspaceDocumentReplaceRequest(
			initial.DocumentKey,
			initial.DocumentId,
			initial.Version,
			"logical",
			initial.FileFormat));
		FileStamp externalStamp = new(true, 6, DateTime.UnixEpoch.AddMinutes(1), "external");
		FileStamp committedStamp = new(true, 7, DateTime.UnixEpoch.AddMinutes(2), "committed");
		fileSystem.CapturedStamps.Enqueue(externalStamp);
		fileSystem.ReplacementStamp = committedStamp;

		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				initial.DocumentKey,
				initial.DocumentId,
				edited.Snapshot!.Version,
				initial.OnDiskStamp,
				WorkspaceDocumentConflictResolutionChoice.UseLogical));

		Assert.AreEqual(WorkspaceDocumentConflictResolutionStatus.ResolvedWithLogical, result.Status);
		Assert.AreEqual("logical", result.Snapshot!.Content);
		Assert.AreEqual(1, result.Snapshot.Version);
		Assert.AreEqual(1, result.Snapshot.PersistedVersion);
		Assert.IsFalse(result.Snapshot.IsDirty);
		Assert.AreEqual(committedStamp, result.Snapshot.OnDiskStamp);
		Assert.AreEqual(externalStamp, fileSystem.LastReplacementExpectedStamp);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UseDiskDetectsSecondDiskChange()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync("script.lua", s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.TryReplace(new WorkspaceDocumentReplaceRequest(
			initial.DocumentKey,
			initial.DocumentId,
			initial.Version,
			"logical",
			initial.FileFormat));
		FileStamp firstDiskStamp = new(true, 3, DateTime.UnixEpoch.AddMinutes(1), "disk-1");
		FileStamp secondDiskStamp = new(true, 4, DateTime.UnixEpoch.AddMinutes(2), "disk-2");
		fileSystem.ReadResults.Enqueue(Task.FromResult(new WorkspaceFileReadResult(
			"disk-1",
			s_defaultFormat,
			firstDiskStamp)));
		fileSystem.CapturedStamps.Enqueue(secondDiskStamp);

		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				initial.DocumentKey,
				initial.DocumentId,
				edited.Snapshot!.Version,
				firstDiskStamp,
				WorkspaceDocumentConflictResolutionChoice.UseDisk));

		Assert.AreEqual(WorkspaceDocumentConflictResolutionStatus.ExternalFileConflict, result.Status);
		Assert.AreEqual("logical", result.Snapshot!.Content);
		Assert.AreEqual(secondDiskStamp, result.ObservedOnDiskStamp);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UseDiskReturnsStaleAfterLogicalEditDuringRead()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync("script.lua", s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.TryReplace(new WorkspaceDocumentReplaceRequest(
			initial.DocumentKey,
			initial.DocumentId,
			initial.Version,
			"logical",
			initial.FileFormat));
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingRead = pendingRead.Task;
		Task<WorkspaceDocumentConflictResolutionResult> resolutionTask = store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				initial.DocumentKey,
				initial.DocumentId,
				edited.Snapshot!.Version,
				initial.OnDiskStamp,
				WorkspaceDocumentConflictResolutionChoice.UseDisk));
		await fileSystem.ReadStarted.Task;

		WorkspaceDocumentMutationResult laterEdit = store.TryReplace(new WorkspaceDocumentReplaceRequest(
			initial.DocumentKey,
			initial.DocumentId,
			edited.Snapshot.Version,
			"later logical",
			initial.FileFormat));
		pendingRead.SetResult(new WorkspaceFileReadResult("disk", s_defaultFormat, initial.OnDiskStamp));

		WorkspaceDocumentConflictResolutionResult result = await resolutionTask;

		Assert.AreEqual(WorkspaceDocumentConflictResolutionStatus.StaleDocument, result.Status);
		Assert.AreEqual(laterEdit.Snapshot!.Version, result.Snapshot!.Version);
		Assert.AreEqual("later logical", result.Snapshot.Content);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UseLogicalKeepsLaterLogicalEditDirty()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingReplacement = pendingReplacement.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync("script.lua", s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.TryReplace(new WorkspaceDocumentReplaceRequest(
			initial.DocumentKey,
			initial.DocumentId,
			initial.Version,
			"logical",
			initial.FileFormat));
		Task<WorkspaceDocumentConflictResolutionResult> resolutionTask = store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				initial.DocumentKey,
				initial.DocumentId,
				edited.Snapshot!.Version,
				new FileStamp(true, 8, DateTime.UnixEpoch.AddMinutes(1), "external"),
				WorkspaceDocumentConflictResolutionChoice.UseLogical));
		await fileSystem.ReplacementStarted.Task;

		WorkspaceDocumentMutationResult laterEdit = store.TryReplace(new WorkspaceDocumentReplaceRequest(
			initial.DocumentKey,
			initial.DocumentId,
			edited.Snapshot.Version,
			"later logical",
			initial.FileFormat));
		pendingReplacement.SetResult(new WorkspaceFileReplacementResult(
			WorkspaceFileReplacementStatus.Replaced,
			new FileStamp(true, 7, DateTime.UnixEpoch.AddMinutes(1), "committed")));

		WorkspaceDocumentConflictResolutionResult result = await resolutionTask;

		Assert.AreEqual(WorkspaceDocumentConflictResolutionStatus.ResolvedWithLogical, result.Status);
		Assert.AreEqual(laterEdit.Snapshot!.Version, result.Snapshot!.Version);
		Assert.AreEqual(edited.Snapshot.Version, result.Snapshot.PersistedVersion);
		Assert.AreEqual("later logical", result.Snapshot.Content);
		Assert.IsTrue(result.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task Reload_LogicalEditDuringReadReturnsStaleDocument()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync("script.lua", s_openOptions)).Snapshot!;
		fileSystem.ResetReadStarted();
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingRead = pendingRead.Task;
		Task<WorkspaceDocumentReloadResult> reloadTask = store.ReloadAsync(new WorkspaceDocumentReloadRequest(
			initial.DocumentKey,
			initial.DocumentId,
			initial.Version,
			initial.OnDiskStamp));
		await fileSystem.ReadStarted.Task;

		WorkspaceDocumentMutationResult edited = store.TryReplace(new WorkspaceDocumentReplaceRequest(
			initial.DocumentKey,
			initial.DocumentId,
			initial.Version,
			"logical",
			initial.FileFormat));
		pendingRead.SetResult(new WorkspaceFileReadResult("disk", s_defaultFormat, new FileStamp(true, 4, DateTime.UnixEpoch.AddMinutes(1), "disk")));

		WorkspaceDocumentReloadResult result = await reloadTask;

		Assert.AreEqual(WorkspaceDocumentReloadStatus.StaleDocument, result.Status);
		Assert.AreEqual(edited.Snapshot!.Version, result.Snapshot!.Version);
		Assert.AreEqual("logical", result.Snapshot.Content);
	}

	[TestMethod]
	public async Task Commit_EditDuringWriteKeepsLaterLogicalEditDirtyWithoutRetry()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingReplacement = pendingReplacement.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync("script.lua", s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.TryReplace(new WorkspaceDocumentReplaceRequest(
			initial.DocumentKey,
			initial.DocumentId,
			initial.Version,
			"first",
			initial.FileFormat));
		Task<WorkspaceDocumentCommitResult> commitTask = store.CommitAsync(new WorkspaceDocumentCommitRequest(
			initial.DocumentKey,
			initial.DocumentId,
			edited.Snapshot!.Version,
			initial.OnDiskStamp));
		await fileSystem.ReplacementStarted.Task;

		WorkspaceDocumentMutationResult laterEdit = store.TryReplace(new WorkspaceDocumentReplaceRequest(
			initial.DocumentKey,
			initial.DocumentId,
			edited.Snapshot.Version,
			"second",
			initial.FileFormat));
		pendingReplacement.SetResult(new WorkspaceFileReplacementResult(
			WorkspaceFileReplacementStatus.Replaced,
			new FileStamp(true, 5, DateTime.UnixEpoch.AddMinutes(1), "committed")));

		WorkspaceDocumentCommitResult result = await commitTask;

		Assert.AreEqual(WorkspaceDocumentCommitStatus.Committed, result.Status);
		Assert.AreEqual("second", result.Snapshot!.Content);
		Assert.AreEqual(laterEdit.Snapshot!.Version, result.Snapshot.Version);
		Assert.AreEqual(edited.Snapshot.Version, result.Snapshot.PersistedVersion);
		Assert.IsTrue(result.Snapshot.IsDirty);
		Assert.AreEqual(1, fileSystem.ReplaceCount);
	}

	[TestMethod]
	public async Task Commit_ReplacementStateUnknownCanBeRecoveredByNextExplicitSave()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat)
		{
			ReplacementStatus = WorkspaceFileReplacementStatus.ReplacementStateUnknown,
			ReplacementStamp = new FileStamp(true, 6, DateTime.UnixEpoch.AddMinutes(1), "unknown")
		};
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync("script.lua", s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.TryReplace(new WorkspaceDocumentReplaceRequest(
			initial.DocumentKey,
			initial.DocumentId,
			initial.Version,
			"logical",
			initial.FileFormat));
		WorkspaceDocumentCommitResult unknown = await store.CommitAsync(new WorkspaceDocumentCommitRequest(
			initial.DocumentKey,
			initial.DocumentId,
			edited.Snapshot!.Version,
			initial.OnDiskStamp));

		fileSystem.ReplacementStatus = WorkspaceFileReplacementStatus.Replaced;
		fileSystem.CapturedStamps.Enqueue(unknown.Snapshot!.OnDiskStamp);
		WorkspaceDocumentCommitResult recovered = await store.CommitAsync(new WorkspaceDocumentCommitRequest(
			unknown.Snapshot.DocumentKey,
			unknown.Snapshot.DocumentId,
			unknown.Snapshot.Version,
			unknown.Snapshot.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentCommitStatus.ReplacementStateUnknown, unknown.Status);
		Assert.AreEqual(WorkspaceDocumentCommitStatus.Committed, recovered.Status);
		Assert.IsFalse(recovered.Snapshot!.IsDirty);
		Assert.AreEqual(2, fileSystem.ReplaceCount);
	}

	[TestMethod]
	public async Task Commit_CancellationLeavesLogicalDocumentDirty()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingReplacement = pendingReplacement.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync("script.lua", s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.TryReplace(new WorkspaceDocumentReplaceRequest(
			initial.DocumentKey,
			initial.DocumentId,
			initial.Version,
			"logical",
			initial.FileFormat));
		using var cancellation = new CancellationTokenSource();
		Task<WorkspaceDocumentCommitResult> commitTask = store.CommitAsync(new WorkspaceDocumentCommitRequest(
			initial.DocumentKey,
			initial.DocumentId,
			edited.Snapshot!.Version,
			initial.OnDiskStamp), cancellation.Token);
		await fileSystem.ReplacementStarted.Task;
		cancellation.Cancel();

		WorkspaceDocumentCommitResult result = await commitTask;

		Assert.AreEqual(WorkspaceDocumentCommitStatus.Cancelled, result.Status);
		Assert.IsTrue(result.Snapshot!.IsDirty);
	}

	[TestMethod]
	public async Task Delete_BlocksLogicalReplacementUntilDeletionCompletes()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingDelete = new TaskCompletionSource<WorkspaceFileDeleteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingDelete = pendingDelete.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync("script.lua", s_openOptions)).Snapshot!;

		Task<WorkspaceDocumentDeleteResult> delete = store.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			initial.DocumentKey,
			initial.DocumentId,
			initial.Version,
			initial.OnDiskStamp));
		await fileSystem.DeleteStarted.Task;

		WorkspaceDocumentMutationResult replacement = store.TryReplace(new WorkspaceDocumentReplaceRequest(
			initial.DocumentKey,
			initial.DocumentId,
			initial.Version,
			"later",
			initial.FileFormat));

		pendingDelete.SetResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteStatus.Deleted));
		WorkspaceDocumentDeleteResult deleted = await delete;

		Assert.AreEqual(WorkspaceDocumentMutationStatus.OperationInProgress, replacement.Status);
		Assert.AreEqual(WorkspaceDocumentDeleteStatus.Deleted, deleted.Status);
		Assert.IsFalse(store.TryGetSnapshot(initial.DocumentId, out _));
	}

	[TestMethod]
	public async Task DeleteOperations_PropagateRecycleBinChoiceToFilesystem()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot file = (await store.OpenAsync("folder/file.lua", s_openOptions)).Snapshot!;

		WorkspaceDocumentDeleteResult fileResult = await store.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			file.DocumentKey,
			file.DocumentId,
			file.Version,
			file.OnDiskStamp,
			UseRecycleBin: true));

		Assert.AreEqual(WorkspaceDocumentDeleteStatus.Deleted, fileResult.Status);
		Assert.IsTrue(fileSystem.LastDeleteUsedRecycleBin);

		WorkspaceDocumentSnapshot directoryFile = (await store.OpenAsync("folder/other.lua", s_openOptions)).Snapshot!;
		WorkspaceDocumentDirectoryDeleteResult directoryResult = await store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(
				"folder",
				[new WorkspaceDocumentBatchEntry(
					directoryFile.DocumentKey,
					directoryFile.DocumentId,
					directoryFile.Version,
					directoryFile.OnDiskStamp)],
				UseRecycleBin: true));

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteStatus.Deleted, directoryResult.Status);
		Assert.IsTrue(fileSystem.LastDirectoryDeleteUsedRecycleBin);
	}

	[TestMethod]
	public async Task Open_WaitsForSaveAsDestinationReservationAndReturnsRetargetedDocument()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingReplacement = pendingReplacement.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync("source.lua", s_openOptions)).Snapshot!;
		string destinationPath = Path.Combine(Path.GetDirectoryName(initial.DocumentId)!, "destination.lua");
		fileSystem.CapturedStamps.Enqueue(initial.OnDiskStamp);
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);

		Task<WorkspaceDocumentSaveAsResult> saveAs = store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			initial.DocumentKey,
			initial.DocumentId,
			initial.Version,
			initial.OnDiskStamp,
			destinationPath));
		await fileSystem.ReplacementStarted.Task;

		Task<WorkspaceDocumentOpenResult> open = store.OpenAsync(destinationPath, s_openOptions);
		Assert.IsFalse(open.IsCompleted);
		pendingReplacement.SetResult(new WorkspaceFileReplacementResult(
			WorkspaceFileReplacementStatus.Replaced,
			new FileStamp(true, 7, DateTime.UnixEpoch.AddMinutes(1), "saved")));

		WorkspaceDocumentSaveAsResult saved = await saveAs;
		WorkspaceDocumentOpenResult opened = await open;

		Assert.AreEqual(WorkspaceDocumentSaveAsStatus.SavedAs, saved.Status);
		Assert.AreEqual(WorkspaceDocumentOpenStatus.AlreadyOpen, opened.Status);
		Assert.AreEqual(saved.Snapshot!.DocumentKey, opened.Snapshot!.DocumentKey);
		Assert.AreEqual(saved.Snapshot.DocumentId, opened.Snapshot.DocumentId);
	}

	[TestMethod]
	public async Task Dispose_IsIdempotentAndRejectsSubsequentOperations()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync("script.lua", s_openOptions)).Snapshot!;

		await store.DisposeAsync();
		await store.DisposeAsync();

		Assert.ThrowsExactly<ObjectDisposedException>(() => store.TryGetSnapshot("script.lua", out _));
		Assert.ThrowsExactly<ObjectDisposedException>(() => store.TryReplace(new WorkspaceDocumentReplaceRequest(
			snapshot.DocumentKey,
			snapshot.DocumentId,
			snapshot.Version,
			"after disposal",
			snapshot.FileFormat)));
		Assert.ThrowsExactly<ObjectDisposedException>(() => store.Discard(new WorkspaceDocumentDiscardRequest(
			snapshot.DocumentKey,
			snapshot.DocumentId,
			snapshot.Version)));
		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => store.OpenAsync("other.lua", s_openOptions));
	}

	[TestMethod]
	public async Task RenameDirectory_RekeysAllRetainedDescendantsAndPreservesKeys()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot first = (await store.OpenAsync(
			Path.Combine("folder", "one.lua"),
			s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot second = (await store.OpenAsync(
			Path.Combine("folder", "two.lua"),
			s_openOptions)).Snapshot!;

		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(
				"folder",
				"moved",
				new[] { first, second }
					.Select(snapshot => new WorkspaceDocumentBatchEntry(
						snapshot.DocumentKey,
						snapshot.DocumentId,
						snapshot.Version,
						snapshot.OnDiskStamp))
					.ToArray()));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameStatus.Renamed, result.Status);
		Assert.AreEqual(2, result.Snapshots.Count);
		Assert.AreEqual(first.DocumentKey, result.Snapshots[0].DocumentKey);
		Assert.AreEqual(second.DocumentKey, result.Snapshots[1].DocumentKey);
		Assert.IsFalse(store.TryGetSnapshot(first.DocumentId, out _));
		Assert.IsFalse(store.TryGetSnapshot(second.DocumentId, out _));
		Assert.AreEqual(2, store.GetSnapshotsUnderDirectory("moved").Count);
	}

	[TestMethod]
	public async Task DeleteDirectory_RemovesAllTrackedDescendants()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot first = (await store.OpenAsync(
			Path.Combine("folder", "one.lua"),
			s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot second = (await store.OpenAsync(
			Path.Combine("folder", "two.lua"),
			s_openOptions)).Snapshot!;

		WorkspaceDocumentDirectoryDeleteResult result = await store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(
				"folder",
				new[] { first, second }
					.Select(snapshot => new WorkspaceDocumentBatchEntry(
						snapshot.DocumentKey,
						snapshot.DocumentId,
						snapshot.Version,
						snapshot.OnDiskStamp))
					.ToArray()));

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteStatus.Deleted, result.Status);
		Assert.AreEqual(2, result.Snapshots.Count);
		Assert.IsFalse(store.TryGetSnapshot(first.DocumentId, out _));
		Assert.IsFalse(store.TryGetSnapshot(second.DocumentId, out _));
	}

	private sealed class FakeFileSystem : IWorkspaceFileSystem
	{
		private readonly string _content;
		private readonly TextFileFormat _fileFormat;

		public FakeFileSystem(string content, TextFileFormat fileFormat)
		{
			_content = content;
			_fileFormat = fileFormat;
		}

		public int ReadCount { get; private set; }

		public string? LastReadPath { get; private set; }

		public Task<WorkspaceFileReadResult>? PendingRead { get; set; }

		public TaskCompletionSource<object?> ReadStarted { get; private set; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Queue<Task<WorkspaceFileReadResult>> ReadResults { get; } = new();

		public Queue<FileStamp> CapturedStamps { get; } = new();

		public Task<WorkspaceFileReplacementResult>? PendingReplacement { get; set; }

		public TaskCompletionSource<object?> ReplacementStarted { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public WorkspaceFileReplacementStatus ReplacementStatus { get; set; } =
			WorkspaceFileReplacementStatus.Replaced;

		public int ReplaceCount { get; private set; }

		public FileStamp? ReplacementStamp { get; set; }

		public FileStamp? LastReplacementExpectedStamp { get; private set; }

		public void ResetReadStarted()
			=> ReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public async Task<WorkspaceFileReadResult> ReadAsync(string path, CancellationToken cancellationToken)
		{
			ReadCount++;
			LastReadPath = path;
			ReadStarted.TrySetResult(null);

			if (PendingRead is not null)
				return await PendingRead.WaitAsync(cancellationToken);

			if (ReadResults.Count > 0)
				return await ReadResults.Dequeue();

			return CreateReadResult();
		}

		public Task<FileStamp> CaptureStampAsync(string path, CancellationToken cancellationToken)
		{
			if (CapturedStamps.Count > 0)
				return Task.FromResult(CapturedStamps.Dequeue());

			return Task.FromResult(CreateReadResult().OnDiskStamp);
		}

		public Task<WorkspaceTemporaryFile> WriteTemporaryAsync(
			string directory,
			ReadOnlyMemory<byte> content,
			CancellationToken cancellationToken)
			=> Task.FromResult(new WorkspaceTemporaryFile(
				Path.Combine(directory, "temporary"),
				content.Length,
				"logical"));

		public Task<WorkspaceFileReplacementResult> ReplaceAsync(
			WorkspaceTemporaryFile temporaryFile,
			string destinationPath,
			FileStamp expectedStamp,
			CancellationToken cancellationToken)
		{
			ReplaceCount++;
			LastReplacementExpectedStamp = expectedStamp;
			ReplacementStarted.TrySetResult(null);
			if (PendingReplacement is not null)
				return PendingReplacement.WaitAsync(cancellationToken);

			return Task.FromResult(new WorkspaceFileReplacementResult(
				ReplacementStatus,
				ReplacementStamp ?? new FileStamp(true, temporaryFile.Length, DateTime.UnixEpoch, temporaryFile.ContentHash)));
		}

		public Task<WorkspaceFileDeleteResult>? PendingDelete { get; set; }

		public TaskCompletionSource<object?> DeleteStarted { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public bool LastDeleteUsedRecycleBin { get; private set; }

		public bool LastDirectoryDeleteUsedRecycleBin { get; private set; }

		public Task<WorkspaceFileMoveResult> MoveAsync(
			string sourcePath,
			string destinationPath,
			FileStamp expectedSourceStamp,
			CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public Task<WorkspaceFileMoveResult> MoveDirectoryAsync(
			string sourcePath,
			string destinationPath,
			CancellationToken cancellationToken)
			=> Task.FromResult(new WorkspaceFileMoveResult(WorkspaceFileMoveStatus.Moved));

		public Task<WorkspaceFileDeleteResult> DeleteAsync(
			string path,
			FileStamp expectedStamp,
			CancellationToken cancellationToken,
			bool useRecycleBin = false)
		{
			LastDeleteUsedRecycleBin = useRecycleBin;
			DeleteStarted.TrySetResult(null);
			return PendingDelete?.WaitAsync(cancellationToken)
				?? Task.FromResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteStatus.Deleted));
		}

		public Task<WorkspaceFileDeleteResult> DeleteDirectoryAsync(
			string path,
			CancellationToken cancellationToken,
			bool useRecycleBin = false)
		{
			LastDirectoryDeleteUsedRecycleBin = useRecycleBin;
			return Task.FromResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteStatus.Deleted));
		}

		public Task DeleteTemporaryAsync(WorkspaceTemporaryFile temporaryFile)
			=> Task.CompletedTask;

		public WorkspaceFileReadResult CreateReadResult()
		{
			return new WorkspaceFileReadResult(
				_content,
				_fileFormat,
				new FileStamp(true, _content.Length, DateTime.UnixEpoch, "hash"));
		}
	}
}

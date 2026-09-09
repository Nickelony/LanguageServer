using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

public sealed partial class WorkspaceDocumentStoreTests
{
	[TestMethod]
	public async Task ResolveExternalConflict_UseDiskInstallsCurrentDiskContent()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		FileStamp externalStamp = new(true, 3, DateTime.UnixEpoch.AddMinutes(1), "disk");
		fileSystem.ReadResults.Enqueue(Task.FromResult(new WorkspaceFileReadResult(
			"disk",
			s_defaultFormat,
			externalStamp)));

		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				externalStamp,
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

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		FileStamp externalStamp = new(true, 6, DateTime.UnixEpoch.AddMinutes(1), "external");
		FileStamp committedStamp = new(true, 7, DateTime.UnixEpoch.AddMinutes(2), "committed");
		fileSystem.ReplacementStamp = committedStamp;

		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				externalStamp,
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
	public async Task ResolveExternalConflict_UseDiskAdoptsAMissingFileAsEmptyContentKeepingTheFormat()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		fileSystem.ReadResults.Enqueue(Task.FromResult(new WorkspaceFileReadResult(
			string.Empty,
			default,
			FileStamp.Missing)));

		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				FileStamp.Missing,
				WorkspaceDocumentConflictResolutionChoice.UseDisk));

		Assert.AreEqual(WorkspaceDocumentConflictResolutionStatus.ResolvedWithDisk, result.Status);
		Assert.AreEqual(string.Empty, result.Snapshot!.Content);
		Assert.AreEqual(FileStamp.Missing, result.Snapshot.OnDiskStamp);
		Assert.IsFalse(result.Snapshot.IsDirty);

		// A missing file has no on-disk format to adopt, so the document keeps the format it had.
		Assert.AreEqual(initial.FileFormat, result.Snapshot.FileFormat);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UseLogicalCreatesAMissingFileFromLogicalContent()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		FileStamp committedStamp = new(true, 7, DateTime.UnixEpoch.AddMinutes(2), "committed");
		fileSystem.ReplacementStamp = committedStamp;

		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				FileStamp.Missing,
				WorkspaceDocumentConflictResolutionChoice.UseLogical));

		// The missing file is created from the logical content with a missing-file expectation.
		Assert.AreEqual(WorkspaceDocumentConflictResolutionStatus.ResolvedWithLogical, result.Status);
		Assert.AreEqual("logical", result.Snapshot!.Content);
		Assert.AreEqual(FileStamp.Missing, fileSystem.LastReplacementExpectedStamp);
		Assert.AreEqual(committedStamp, result.Snapshot.OnDiskStamp);
		Assert.IsFalse(result.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UseDiskOnADirectoryReportsReadFailed()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		fileSystem.ReadResults.Enqueue(Task.FromResult(new WorkspaceFileReadResult(
			string.Empty,
			default,
			FileStamp.Missing,
			IsDirectory: true)));

		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				FileStamp.Missing,
				WorkspaceDocumentConflictResolutionChoice.UseDisk));

		// A directory is not disk content to adopt: the resolution fails with the directory-specific
		// code and keeps the logical content.
		Assert.AreEqual(WorkspaceDocumentConflictResolutionStatus.ReadFailed, result.Status);
		Assert.AreEqual(WorkspaceOperationFailureCodes.IsDirectory, result.Failure?.Code);
		Assert.AreEqual("logical", result.Snapshot!.Content);
		Assert.IsTrue(result.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UseDiskAdoptsTheReadStateWithASingleRead()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		FileStamp diskStamp = new(true, 3, DateTime.UnixEpoch.AddMinutes(1), "disk");
		fileSystem.ReadResults.Enqueue(Task.FromResult(new WorkspaceFileReadResult(
			"disk",
			s_defaultFormat,
			diskStamp)));

		// The read carries the stamp of the bytes it returned, so the resolution must not re-capture the
		// destination. The enqueued capture is never consumed when that holds.
		fileSystem.CapturedStamps.Enqueue(new FileStamp(true, 4, DateTime.UnixEpoch.AddMinutes(2), "later"));

		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				diskStamp,
				WorkspaceDocumentConflictResolutionChoice.UseDisk));

		Assert.AreEqual(WorkspaceDocumentConflictResolutionStatus.ResolvedWithDisk, result.Status);
		Assert.AreEqual("disk", result.Snapshot!.Content);
		Assert.AreEqual(diskStamp, result.Snapshot.OnDiskStamp);
		Assert.IsFalse(result.Snapshot.IsDirty);
		Assert.AreEqual(1, fileSystem.CapturedStamps.Count);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UseDiskRejectsAReadThatNoLongerMatchesTheObservation()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		FileStamp observedStamp = new(true, 3, DateTime.UnixEpoch.AddMinutes(1), "observed");
		FileStamp readStamp = new(true, 4, DateTime.UnixEpoch.AddMinutes(2), "read");
		fileSystem.ReadResults.Enqueue(Task.FromResult(new WorkspaceFileReadResult(
			"disk",
			s_defaultFormat,
			readStamp)));

		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				observedStamp,
				WorkspaceDocumentConflictResolutionChoice.UseDisk));

		// The disk changed again after the conflict was observed, so the logical content is retained and
		// the fresh stamp is reported for the next attempt.
		Assert.AreEqual(WorkspaceDocumentConflictResolutionStatus.ExternalFileConflict, result.Status);
		Assert.AreEqual("logical", result.Snapshot!.Content);
		Assert.AreEqual(readStamp, result.ObservedOnDiskStamp);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UndefinedChoiceIsAnArgumentError()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		// An undefined choice must not silently take the UseDisk branch; it is rejected as an
		// argument error instead of discarding unpublishable logical content.
		await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, initial.Version),
				initial.OnDiskStamp,
				(WorkspaceDocumentConflictResolutionChoice)42)));
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UseDiskReturnsStaleAfterLogicalEditDuringRead()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingRead = pendingRead.Task;
		Task<WorkspaceDocumentConflictResolutionResult> resolutionTask = store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				initial.OnDiskStamp,
				WorkspaceDocumentConflictResolutionChoice.UseDisk));
		await fileSystem.Reads.WhenReachedAsync(2);

		WorkspaceDocumentMutationResult laterEdit = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, edited.Snapshot.Version),
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

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		FileStamp observedStamp = new(true, 8, DateTime.UnixEpoch.AddMinutes(1), "external");

		// The conditional replacement validates the observed stamp itself, so no capture is enqueued.
		Task<WorkspaceDocumentConflictResolutionResult> resolutionTask = store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				observedStamp,
				WorkspaceDocumentConflictResolutionChoice.UseLogical));
		await fileSystem.Replacements.WhenReachedAsync(1);

		WorkspaceDocumentMutationResult laterEdit = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, edited.Snapshot.Version),
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
	public async Task ResolveExternalConflict_UseLogicalRejectsSecondDiskChange()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		FileStamp observedStamp = new(true, 6, DateTime.UnixEpoch.AddMinutes(1), "observed");
		FileStamp secondStamp = new(true, 7, DateTime.UnixEpoch.AddMinutes(2), "second");

		// The destination changed again after the conflict was observed; the conditional replacement
		// detects it and reports the conflict with the newly observed stamp instead of overwriting.
		fileSystem.ReplacementStatus = WorkspaceFileReplacementStatus.ExternalFileConflict;
		fileSystem.ReplacementStamp = secondStamp;

		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				observedStamp,
				WorkspaceDocumentConflictResolutionChoice.UseLogical));

		Assert.AreEqual(WorkspaceDocumentConflictResolutionStatus.ExternalFileConflict, result.Status);
		Assert.AreEqual(secondStamp, result.ObservedOnDiskStamp);
		Assert.AreEqual("logical", result.Snapshot!.Content);
		Assert.IsTrue(result.Snapshot.IsDirty);

		// The replacement is the validation point, so it was attempted before the conflict surfaced.
		Assert.AreEqual(1, fileSystem.ReplaceCount);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UseDiskRejectsDiskChangedAfterConflictObserved()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		FileStamp observedStamp = new(true, 6, DateTime.UnixEpoch.AddMinutes(1), "observed");
		FileStamp secondStamp = new(true, 7, DateTime.UnixEpoch.AddMinutes(2), "second");
		fileSystem.ReadResults.Enqueue(Task.FromResult(new WorkspaceFileReadResult(
			"newer",
			s_defaultFormat,
			secondStamp)));

		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				observedStamp,
				WorkspaceDocumentConflictResolutionChoice.UseDisk));

		Assert.AreEqual(WorkspaceDocumentConflictResolutionStatus.ExternalFileConflict, result.Status);
		Assert.AreEqual(secondStamp, result.ObservedOnDiskStamp);
		Assert.AreEqual("logical", result.Snapshot!.Content);
		Assert.IsTrue(result.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UnknownDocumentReportsNotFound()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, TestPath("other.lua"), initial.Version),
				initial.OnDiskStamp,
				WorkspaceDocumentConflictResolutionChoice.UseDisk));

		Assert.AreEqual(WorkspaceDocumentConflictResolutionStatus.DocumentNotFound, result.Status);
		Assert.IsNull(result.Snapshot);
		Assert.AreEqual(1, fileSystem.ReadCount);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_StaleDocumentKeyReportsStaleInstance()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(new WorkspaceDocumentKey(Guid.NewGuid()), initial.DocumentId, initial.Version),
				initial.OnDiskStamp,
				WorkspaceDocumentConflictResolutionChoice.UseDisk));

		Assert.AreEqual(WorkspaceDocumentConflictResolutionStatus.StaleDocumentInstance, result.Status);
		Assert.IsNotNull(result.Snapshot);
		Assert.AreEqual(initial.Content, result.Snapshot.Content);
		Assert.AreEqual(initial.Version, result.Snapshot.Version);
		Assert.AreEqual(1, fileSystem.ReadCount);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_WhileDeleteInFlightReportsOperationInProgress()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		var pendingDelete = new TaskCompletionSource<WorkspaceFileDeleteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingDelete = pendingDelete.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		Task<WorkspaceDocumentDeleteResult> delete = store.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp));
		await fileSystem.Deletes.WhenReachedAsync(1);

		// The delete holds the document's disk-operation gate, so the conflict resolution is rejected
		// instead of racing the removal.
		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, initial.Version),
				initial.OnDiskStamp,
				WorkspaceDocumentConflictResolutionChoice.UseDisk));

		pendingDelete.SetResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteStatus.Deleted));
		WorkspaceDocumentDeleteResult deleted = await delete;

		Assert.AreEqual(WorkspaceDocumentConflictResolutionStatus.OperationInProgress, result.Status);
		Assert.IsNotNull(result.Snapshot);
		Assert.AreEqual(initial.Content, result.Snapshot.Content);
		Assert.AreEqual(WorkspaceDocumentDeleteStatus.Deleted, deleted.Status);
		Assert.AreEqual(1, fileSystem.ReadCount);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_ReadFailureReportsReadFailed()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		FileStamp observedStamp = new(true, 8, DateTime.UnixEpoch.AddMinutes(1), "external");
		fileSystem.ReadResults.Enqueue(Task.FromException<WorkspaceFileReadResult>(new IOException("read failed")));

		// The read itself faulting is a read failure with the logical content retained, not a conflict;
		// the caller can retry once the file system recovers.
		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				observedStamp,
				WorkspaceDocumentConflictResolutionChoice.UseDisk));

		Assert.AreEqual(WorkspaceDocumentConflictResolutionStatus.ReadFailed, result.Status);
		Assert.AreEqual(WorkspaceOperationFailureCodes.ReadFailed, result.Failure!.Code);
		Assert.AreEqual("logical", result.Snapshot!.Content);
		Assert.IsTrue(result.Snapshot.IsDirty);
		Assert.AreEqual(2, fileSystem.ReadCount);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UseLogicalUnencodableContentReportsWriteFailed()
	{
		var fileSystem = new FakeFileSystem("disk", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		TextFileFormat windows1252Format = new(TextEncodingKind.Windows1252, false, TextNewlineStyle.None);
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"\u2603",
			windows1252Format));
		Assert.AreEqual(WorkspaceDocumentMutationStatus.Changed, edited.Status);

		// The force-write cannot encode the logical content in the document's format, so the resolution
		// reports a write failure classified as an encoding problem and keeps the content dirty.
		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				initial.OnDiskStamp,
				WorkspaceDocumentConflictResolutionChoice.UseLogical));

		Assert.AreEqual(WorkspaceDocumentConflictResolutionStatus.WriteFailed, result.Status);
		Assert.AreEqual(WorkspaceOperationFailureCodes.InvalidEncoding, result.Failure!.Code);
		Assert.AreEqual(0, fileSystem.WriteTemporaryCount);
		Assert.IsTrue(result.Snapshot!.IsDirty);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UseLogicalStampRecaptureFailureReportsStateUnknown()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));

		// The replacement completes but reports no resulting stamp, and the re-capture fails: whether the
		// write took effect is unknown, so the outcome is state-unknown rather than a failure or a commit.
		fileSystem.OmitReplacementStamp = true;
		fileSystem.CaptureTasks.Enqueue(Task.FromException<FileStamp>(new IOException("capture failed")));

		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				initial.OnDiskStamp,
				WorkspaceDocumentConflictResolutionChoice.UseLogical));

		Assert.AreEqual(WorkspaceDocumentConflictResolutionStatus.ReplacementStateUnknown, result.Status);
		Assert.AreEqual(WorkspaceOperationFailureCodes.ReplacementStateUnknown, result.Failure!.Code);
		Assert.AreEqual(1, fileSystem.ReplaceCount);
		Assert.IsTrue(result.Snapshot!.IsDirty);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_CancellationDuringReadReportsCanceled()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingRead = pendingRead.Task;

		using var cancellation = new CancellationTokenSource();
		Task<WorkspaceDocumentConflictResolutionResult> resolutionTask = store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				initial.OnDiskStamp,
				WorkspaceDocumentConflictResolutionChoice.UseDisk),
			cancellation.Token);
		await fileSystem.Reads.WhenReachedAsync(2);
		cancellation.Cancel();

		WorkspaceDocumentConflictResolutionResult result = await resolutionTask;

		Assert.AreEqual(WorkspaceDocumentConflictResolutionStatus.Canceled, result.Status);
		Assert.AreEqual("logical", result.Snapshot!.Content);
		Assert.IsTrue(result.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_StaleVersionIsRejectedBeforeTouchingDisk()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));

		// The request carries the pre-edit version; the stale expectation is rejected by the
		// operation preamble before any conflict work starts.
		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, initial.Version),
				initial.OnDiskStamp,
				WorkspaceDocumentConflictResolutionChoice.UseDisk));

		Assert.AreEqual(WorkspaceDocumentConflictResolutionStatus.StaleDocument, result.Status);
		Assert.AreEqual(edited.Snapshot!.Version, result.Snapshot!.Version);
		Assert.AreEqual(1, fileSystem.ReadCount);
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task ResolveExternalConflict_CancellationDuringWriteReportsCanceled()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingReplacement = pendingReplacement.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));

		using var cancellation = new CancellationTokenSource();
		Task<WorkspaceDocumentConflictResolutionResult> resolutionTask = store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				initial.OnDiskStamp,
				WorkspaceDocumentConflictResolutionChoice.UseLogical),
			cancellation.Token);
		await fileSystem.Replacements.WhenReachedAsync(1);
		cancellation.Cancel();

		WorkspaceDocumentConflictResolutionResult result = await resolutionTask;

		Assert.AreEqual(WorkspaceDocumentConflictResolutionStatus.Canceled, result.Status);
		Assert.AreEqual("logical", result.Snapshot!.Content);
		Assert.IsTrue(result.Snapshot.IsDirty);
	}
}

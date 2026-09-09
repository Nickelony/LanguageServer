using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;
using System.Security.Cryptography;
using System.Text;

namespace Nickelony.IDEKit.Workspace.Tests;

public sealed partial class WorkspaceDocumentStoreTests
{
	[TestMethod]
	public async Task Commit_DocumentNotFoundAndStaleInstanceReportTheirStatuses()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		WorkspaceDocumentCommitResult missing = await store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(initial.DocumentKey, TestPath("missing.lua"), 0),
			initial.OnDiskStamp));
		WorkspaceDocumentCommitResult staleInstance = await store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(new WorkspaceDocumentKey(Guid.NewGuid()), initial.DocumentId, initial.Version),
			initial.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentCommitStatus.DocumentNotFound, missing.Status);
		Assert.IsNull(missing.Snapshot);
		Assert.AreEqual(WorkspaceDocumentCommitStatus.StaleDocumentInstance, staleInstance.Status);
		Assert.IsNotNull(staleInstance.Snapshot);
		Assert.AreEqual(initial.Version, staleInstance.Snapshot.Version);
		Assert.IsFalse(staleInstance.Snapshot.IsDirty);
	}

	[TestMethod]
	[DataRow(WorkspaceFileReplacementStatus.Canceled, WorkspaceDocumentCommitStatus.Canceled, DisplayName = "Canceled")]
	[DataRow(WorkspaceFileReplacementStatus.DestinationExists, WorkspaceDocumentCommitStatus.WriteFailed, DisplayName = "DestinationExists")]
	public async Task Commit_ReplacementOutcomesMapToCommitStatuses(
		WorkspaceFileReplacementStatus replacementStatus,
		WorkspaceDocumentCommitStatus expectedStatus)
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat)
		{
			ReplacementStatus = replacementStatus
		};
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));

		WorkspaceDocumentCommitResult result = await store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
			initial.OnDiskStamp));

		Assert.AreEqual(expectedStatus, result.Status);

		// No failure outcome advances the document version or the persisted baseline.
		Assert.AreEqual(edited.Snapshot.Version, result.Snapshot!.Version);
		Assert.IsTrue(result.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task Commit_EditDuringWriteKeepsLaterLogicalEditDirtyWithoutRetry()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingReplacement = pendingReplacement.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"first",
			initial.FileFormat));
		Task<WorkspaceDocumentCommitResult> commitTask = store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
			initial.OnDiskStamp));
		await fileSystem.Replacements.WhenReachedAsync(1);

		WorkspaceDocumentMutationResult laterEdit = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, edited.Snapshot.Version),
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

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		WorkspaceDocumentCommitResult unknown = await store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
			initial.OnDiskStamp));

		fileSystem.ReplacementStatus = WorkspaceFileReplacementStatus.Replaced;
		fileSystem.CapturedStamps.Enqueue(unknown.Snapshot!.OnDiskStamp);
		WorkspaceDocumentCommitResult recovered = await store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(unknown.Snapshot.DocumentKey, unknown.Snapshot.DocumentId, unknown.Snapshot.Version),
			unknown.Snapshot.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentCommitStatus.ReplacementStateUnknown, unknown.Status);
		Assert.AreEqual(fileSystem.ReplacementStamp, unknown.ObservedOnDiskStamp);
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

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		using var cancellation = new CancellationTokenSource();
		Task<WorkspaceDocumentCommitResult> commitTask = store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
			initial.OnDiskStamp), cancellation.Token);
		await fileSystem.Replacements.WhenReachedAsync(1);
		cancellation.Cancel();

		WorkspaceDocumentCommitResult result = await commitTask;

		Assert.AreEqual(WorkspaceDocumentCommitStatus.Canceled, result.Status);
		Assert.IsTrue(result.Snapshot!.IsDirty);
	}

	[TestMethod]
	public async Task Commit_PreCanceledCleanDocumentReportsCanceledWithoutWriting()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();

		// The token is observed before the clean no-op short-circuit, so a canceled commit reports
		// Canceled and performs no write even though the document has nothing to persist.
		WorkspaceDocumentCommitResult result = await store.CommitAsync(
			new WorkspaceDocumentCommitRequest(
				new(initial.DocumentKey, initial.DocumentId, initial.Version),
				initial.OnDiskStamp),
			cancellation.Token);

		Assert.AreEqual(WorkspaceDocumentCommitStatus.Canceled, result.Status);
		Assert.AreEqual(0, fileSystem.WriteTemporaryCount);
		Assert.AreEqual(0, fileSystem.ReplaceCount);
	}

	[TestMethod]
	public async Task Commit_ReplacementWithoutObservedStamp_RecapturesTheStamp()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot dirty = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"changed",
			initial.FileFormat)).Snapshot!;

		// The replacement reports success without the resulting stamp, so the store re-captures it
		// exactly once; the commit itself no longer pre-captures the destination, which the
		// conditional replacement validates instead.
		FileStamp writtenStamp = new(true, 11, DateTime.UnixEpoch.AddMinutes(5), "written");
		fileSystem.OmitReplacementStamp = true;
		fileSystem.CapturedStamps.Enqueue(writtenStamp);

		WorkspaceDocumentCommitResult result = await store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(dirty.DocumentKey, dirty.DocumentId, dirty.Version),
			dirty.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentCommitStatus.Committed, result.Status);
		Assert.AreEqual(writtenStamp, result.Snapshot!.OnDiskStamp);
		Assert.IsFalse(result.Snapshot.IsDirty);
		Assert.AreEqual(1, fileSystem.Captures.Count);
	}

	[TestMethod]
	public async Task Commit_ReplacementWithoutObservedStampAndFailedRecapture_ReportsStateUnknown()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot dirty = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"changed",
			initial.FileFormat)).Snapshot!;

		fileSystem.OmitReplacementStamp = true;
		fileSystem.CaptureTasks.Enqueue(Task.FromException<FileStamp>(new IOException("capture failed")));

		WorkspaceDocumentCommitResult result = await store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(dirty.DocumentKey, dirty.DocumentId, dirty.Version),
			dirty.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentCommitStatus.ReplacementStateUnknown, result.Status);
		Assert.IsNotNull(result.Failure);
		Assert.AreEqual(WorkspaceOperationFailureCodes.ReplacementStateUnknown, result.Failure.Code);
		Assert.IsTrue(result.Snapshot!.IsDirty);
	}

	[TestMethod]
	public async Task Commit_UnencodableContentReportsInvalidEncoding()
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

		WorkspaceDocumentCommitResult result = await store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(edited.Snapshot!.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version),
			edited.Snapshot.OnDiskStamp));

		// The encoding failure is a property of the content and format, so it is classified as an
		// invalid-encoding write failure instead of a generic write problem.
		Assert.AreEqual(WorkspaceDocumentCommitStatus.WriteFailed, result.Status);
		Assert.AreEqual(WorkspaceOperationFailureCodes.InvalidEncoding, result.Failure!.Code);
		Assert.AreEqual(0, fileSystem.WriteTemporaryCount);
		Assert.IsTrue(result.Snapshot!.IsDirty);
	}

	[TestMethod]
	public async Task Commit_RootDocumentWritesTemporaryFileInTheRootDirectory()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		string rootPath = Path.GetPathRoot(Path.GetFullPath(Environment.CurrentDirectory))!;
		WorkspaceDocumentSnapshot opened = (await store.OpenAsync(rootPath, s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(opened.DocumentKey, opened.DocumentId, opened.Version),
			"edited",
			opened.FileFormat));

		WorkspaceDocumentCommitResult committed = await store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(edited.Snapshot!.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version),
			edited.Snapshot.OnDiskStamp));

		// A file id that is itself a volume root has no parent directory; the root is the same-volume
		// directory, never the process current directory.
		Assert.AreEqual(WorkspaceDocumentCommitStatus.Committed, committed.Status);
		Assert.AreEqual(rootPath, fileSystem.LastTemporaryDirectory);
	}

	[TestMethod]
	public async Task CleanExistingCommit_IsNoOpWithoutCapturingOrWriting()
	{
		var fileSystem = new ControllableFileSystem("disk", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		WorkspaceDocumentCommitResult result = await store.CommitAsync(CreateCommitRequest(snapshot));

		Assert.AreEqual(WorkspaceDocumentCommitStatus.Committed, result.Status);

		// A clean existing document has nothing to write, so the whole-file stamp capture is skipped
		// as well; only a dirty or force-write commit reads and hashes the file.
		Assert.AreEqual(0, fileSystem.CaptureCount);
		Assert.AreEqual(0, fileSystem.WriteTemporaryCount);
		Assert.AreEqual(0, fileSystem.ReplaceCount);
		Assert.AreEqual(snapshot.Version, result.Snapshot!.PersistedVersion);
	}

	[TestMethod]
	public async Task CleanExistingCommit_WithMismatchedExpectedStamp_ReportsExternalFileConflict()
	{
		var fileSystem = new ControllableFileSystem("disk", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		var staleExpectedStamp = new FileStamp(true, 4, DateTime.UnixEpoch.AddDays(-1), "stale");

		// The no-op path cannot verify the file without reading it, so the caller's expectation is
		// validated against the tracked stamp; an expectation that disagrees with the state the store
		// last observed reports a conflict instead of claiming a match.
		WorkspaceDocumentCommitResult result = await store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			staleExpectedStamp));

		Assert.AreEqual(WorkspaceDocumentCommitStatus.ExternalFileConflict, result.Status);
		Assert.AreEqual(snapshot.OnDiskStamp, result.ObservedOnDiskStamp);
		Assert.AreEqual(0, fileSystem.CaptureCount);
		Assert.AreEqual(0, fileSystem.WriteTemporaryCount);
		Assert.AreEqual(0, fileSystem.ReplaceCount);
	}

	[TestMethod]
	public async Task StaleCommit_IsRejectedWithoutFilesystemWork()
	{
		var fileSystem = new ControllableFileSystem("disk", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult changed = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"changed",
			initial.FileFormat));

		WorkspaceDocumentCommitResult result = await store.CommitAsync(CreateCommitRequest(initial));

		Assert.AreEqual(WorkspaceDocumentCommitStatus.StaleDocument, result.Status);
		Assert.AreEqual(changed.Snapshot!.Content, result.Snapshot!.Content);
		Assert.AreEqual(0, fileSystem.CaptureCount);
		Assert.AreEqual(0, fileSystem.WriteTemporaryCount);
	}

	[TestMethod]
	public async Task ObservedExternalConflict_LeavesBaselineUnchanged()
	{
		var fileSystem = new ControllableFileSystem("disk", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		fileSystem.CurrentStamp = new FileStamp(true, 8, DateTime.UnixEpoch.AddDays(1), "external");
		WorkspaceDocumentMutationResult changed = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"changed",
			snapshot.FileFormat));
		WorkspaceDocumentSnapshot changedSnapshot = RequireSnapshot(changed);

		// The conditional replacement validates the expected stamp, so the conflict surfaces from
		// the replacement result after the temporary file has been written.
		fileSystem.ReplacementResult = new WorkspaceFileReplacementResult(
			WorkspaceFileReplacementStatus.ExternalFileConflict,
			fileSystem.CurrentStamp);
		WorkspaceDocumentCommitResult result = await store.CommitAsync(CreateCommitRequest(changedSnapshot));

		Assert.AreEqual(WorkspaceDocumentCommitStatus.ExternalFileConflict, result.Status);
		Assert.AreEqual(fileSystem.CurrentStamp, result.ObservedOnDiskStamp);
		Assert.AreEqual(1, fileSystem.WriteTemporaryCount);
		Assert.AreEqual(1, fileSystem.DeleteTemporaryCount);
		Assert.IsTrue(result.Snapshot!.IsDirty);
		Assert.AreEqual(changedSnapshot.PersistedVersion, result.Snapshot.PersistedVersion);
	}

	[TestMethod]
	public async Task CancellationBeforeReplacement_CleansTemporaryFileAndPreservesBaseline()
	{
		var fileSystem = new ControllableFileSystem("disk", s_defaultFormat)
		{
			PendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously)
		};
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult changed = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"changed",
			initial.FileFormat));
		WorkspaceDocumentSnapshot changedSnapshot = RequireSnapshot(changed);
		using var cancellation = new CancellationTokenSource();
		Task<WorkspaceDocumentCommitResult> commit = store.CommitAsync(CreateCommitRequest(changedSnapshot), cancellation.Token);
		await fileSystem.Replacements.WhenReachedAsync(1);

		cancellation.Cancel();
		WorkspaceDocumentCommitResult result = await commit;

		Assert.AreEqual(WorkspaceDocumentCommitStatus.Canceled, result.Status);
		Assert.AreEqual(1, fileSystem.DeleteTemporaryCount);
		Assert.IsFalse(fileSystem.ReplacementBegan);
		Assert.AreEqual(changedSnapshot.PersistedVersion, result.Snapshot!.PersistedVersion);
		Assert.IsTrue(result.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task ReplacementFailure_CleansTemporaryFileAndDoesNotAdvanceBaseline()
	{
		var fileSystem = new ControllableFileSystem("disk", s_defaultFormat)
		{
			ReplacementResult = new WorkspaceFileReplacementResult(
				WorkspaceFileReplacementStatus.Failed,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.WriteFailed, "write failed"))
		};
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult changed = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"changed",
			initial.FileFormat));

		WorkspaceDocumentCommitResult result = await store.CommitAsync(CreateCommitRequest(changed.Snapshot!));

		Assert.AreEqual(WorkspaceDocumentCommitStatus.WriteFailed, result.Status);
		Assert.AreEqual(1, fileSystem.DeleteTemporaryCount);
		Assert.IsTrue(result.Snapshot!.IsDirty);
		Assert.AreEqual(initial.PersistedVersion, result.Snapshot.PersistedVersion);
		Assert.IsNotNull(result.Failure);
	}

	[TestMethod]
	public async Task TemporaryWriteFailure_ReturnsWriteFailedAndDoesNotAdvanceBaseline()
	{
		var fileSystem = new ControllableFileSystem("disk", s_defaultFormat)
		{
			WriteException = new IOException("flush failed")
		};
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult changed = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"changed",
			initial.FileFormat));
		WorkspaceDocumentSnapshot changedSnapshot = RequireSnapshot(changed);

		WorkspaceDocumentCommitResult result = await store.CommitAsync(CreateCommitRequest(changedSnapshot));

		Assert.AreEqual(WorkspaceDocumentCommitStatus.WriteFailed, result.Status);
		Assert.AreEqual(0, fileSystem.DeleteTemporaryCount);
		Assert.AreEqual(changedSnapshot.PersistedVersion, result.Snapshot!.PersistedVersion);
		Assert.IsTrue(result.Snapshot.IsDirty);
		Assert.IsNotNull(result.Failure);
	}

	[TestMethod]
	public async Task ReplacementStateUnknown_ReturnsObservedStampAndDoesNotAdvanceBaseline()
	{
		var fileSystem = new ControllableFileSystem("disk", s_defaultFormat)
		{
			ReplacementResult = new WorkspaceFileReplacementResult(
				WorkspaceFileReplacementStatus.ReplacementStateUnknown,
				new FileStamp(true, 9, DateTime.UnixEpoch.AddDays(2), "unknown"),
				new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.ReplacementStateUnknown, "unknown"))
		};
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult changed = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"changed",
			initial.FileFormat));

		WorkspaceDocumentCommitResult result = await store.CommitAsync(CreateCommitRequest(changed.Snapshot!));

		Assert.AreEqual(WorkspaceDocumentCommitStatus.ReplacementStateUnknown, result.Status);
		Assert.AreEqual(fileSystem.ReplacementResult.ObservedOnDiskStamp, result.ObservedOnDiskStamp);
		Assert.AreEqual(fileSystem.ReplacementResult.ObservedOnDiskStamp, result.Snapshot!.OnDiskStamp);
		Assert.AreEqual(initial.PersistedVersion, result.Snapshot.PersistedVersion);
		Assert.IsTrue(result.Snapshot.IsDirty);
		Assert.AreEqual(1, fileSystem.DeleteTemporaryCount);
	}

	[TestMethod]
	public async Task EditDuringCommit_InstallsCapturedBaselineAndLeavesLaterEditDirty()
	{
		var fileSystem = new ControllableFileSystem("disk", s_defaultFormat)
		{
			PendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously)
		};
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult captured = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"captured",
			initial.FileFormat));
		WorkspaceDocumentSnapshot capturedSnapshot = RequireSnapshot(captured);
		Task<WorkspaceDocumentCommitResult> commit = store.CommitAsync(CreateCommitRequest(capturedSnapshot));
		await fileSystem.Replacements.WhenReachedAsync(1);
		WorkspaceDocumentMutationResult later = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, capturedSnapshot.Version),
			"later",
			initial.FileFormat));
		fileSystem.PendingReplacement!.SetResult(fileSystem.ReplacementResult);
		WorkspaceDocumentSnapshot laterSnapshot = RequireSnapshot(later);

		WorkspaceDocumentCommitResult result = await commit;

		Assert.AreEqual(WorkspaceDocumentCommitStatus.Committed, result.Status);
		Assert.AreEqual("later", result.Snapshot!.Content);
		Assert.AreEqual(capturedSnapshot.Version, result.Snapshot.PersistedVersion);
		Assert.IsTrue(result.Snapshot.IsDirty);
		Assert.AreEqual("captured", fileSystem.WrittenContent);
		Assert.AreEqual(laterSnapshot.Version, result.Snapshot.Version);
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task DiscardAndSecondCommit_ReturnOperationInProgressWithoutSecondIo()
	{
		var fileSystem = new ControllableFileSystem("disk", s_defaultFormat)
		{
			PendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously)
		};
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult changed = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"changed",
			initial.FileFormat));
		WorkspaceDocumentSnapshot changedSnapshot = RequireSnapshot(changed);
		WorkspaceDocumentCommitRequest request = CreateCommitRequest(changedSnapshot);
		Task<WorkspaceDocumentCommitResult> firstCommit = store.CommitAsync(request);
		await fileSystem.Replacements.WhenReachedAsync(1);

		WorkspaceDocumentMutationResult discard = store.Discard(new WorkspaceDocumentDiscardRequest(
			new(changedSnapshot.DocumentKey, changedSnapshot.DocumentId, changedSnapshot.Version)));
		WorkspaceDocumentCommitResult secondCommit = await store.CommitAsync(request);
		fileSystem.PendingReplacement!.SetResult(fileSystem.ReplacementResult);
		WorkspaceDocumentCommitResult completed = await firstCommit;

		Assert.AreEqual(WorkspaceDocumentMutationStatus.OperationInProgress, discard.Status);
		Assert.AreEqual(WorkspaceDocumentCommitStatus.OperationInProgress, secondCommit.Status);

		// A commit no longer pre-captures the destination; the replacement reports its resulting
		// stamp, so neither commit captures one.
		Assert.AreEqual(0, fileSystem.CaptureCount);
		Assert.AreEqual(1, fileSystem.WriteTemporaryCount);
		Assert.AreEqual(1, fileSystem.ReplaceCount);
		Assert.AreEqual(WorkspaceDocumentCommitStatus.Committed, completed.Status);
	}

	[TestMethod]
	public async Task QuickStart_OpenNewDocumentAndCommitCreatesTheFile()
	{
		// Mirrors the package README quick start: the destination directory is created first because
		// the commit writes a temporary file next to the target before replacing it.
		using var temp = new TestTempDirectory();
		string documentDirectory = Path.Combine(temp.Path, "workspace", "documents");
		string documentPath = Path.Combine(documentDirectory, "notes.txt");
		Directory.CreateDirectory(documentDirectory);

		IWorkspaceFileSystem fileSystem = new LocalWorkspaceFileSystem();
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentOpenResult opened = await store.OpenAsync(
			documentPath,
			new WorkspaceDocumentOpenOptions(
				TextEncodingKind.Utf8,
				new TextFileFormat(TextEncodingKind.Utf8, false, TextNewlineStyle.Lf)));
		Assert.AreEqual(WorkspaceDocumentOpenStatus.Opened, opened.Status);

		WorkspaceDocumentSnapshot snapshot = opened.Snapshot!;
		WorkspaceDocumentCommitResult committed = await store.CommitAsync(
			new WorkspaceDocumentCommitRequest(
				new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
				snapshot.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentCommitStatus.Committed, committed.Status);
		Assert.IsTrue(File.Exists(documentPath));
	}

	// File-system-only test double that holds the replacement/read rendezvous open while counting
	// invocations and capturing written content; the store tests use FakeFileSystem instead, which
	// scripts results through queues and knobs. Keep the two dialects separate.
	private sealed class ControllableFileSystem : IWorkspaceFileSystem
	{
		private readonly string _content;
		private readonly TextFileFormat _fileFormat;

		public ControllableFileSystem(string content, TextFileFormat fileFormat)
		{
			_content = content;
			_fileFormat = fileFormat;
			CurrentStamp = new FileStamp(true, content.Length, DateTime.UnixEpoch, "disk");
			ReplacementResult = new WorkspaceFileReplacementResult(
				WorkspaceFileReplacementStatus.Replaced,
				CurrentStamp);
		}

		public FileStamp CurrentStamp { get; set; }

		public WorkspaceFileReplacementResult ReplacementResult { get; set; }

		public TaskCompletionSource<WorkspaceFileReplacementResult>? PendingReplacement { get; set; }

		/// <summary>
		/// Signals that a replacement started; waiters name the one-based ordinal.
		/// </summary>
		public TestSignal Replacements { get; } = new();

		public bool ReplacementBegan { get; private set; }

		public int CaptureCount { get; private set; }

		public int WriteTemporaryCount { get; private set; }

		public int ReplaceCount => Replacements.Count;

		public int DeleteTemporaryCount { get; private set; }

		public string? WrittenContent { get; private set; }

		public Exception? WriteException { get; set; }

		public Task<WorkspaceFileReadResult> ReadAsync(string path, CancellationToken cancellationToken)
		{
			return Task.FromResult(new WorkspaceFileReadResult(_content, _fileFormat, CurrentStamp));
		}

		public Task<FileStamp> CaptureStampAsync(string path, CancellationToken cancellationToken)
		{
			CaptureCount++;
			return Task.FromResult(CurrentStamp);
		}

		public Task<WorkspaceTemporaryFile> WriteTemporaryAsync(
			string directory,
			ReadOnlyMemory<byte> content,
			CancellationToken cancellationToken)
		{
			WriteTemporaryCount++;
			if (WriteException is not null)
				throw WriteException;

			WrittenContent = Encoding.UTF8.GetString(content.Span);
			return Task.FromResult(new WorkspaceTemporaryFile(
				"fake.tmp",
				content.Length,
				Convert.ToHexString(SHA256.HashData(content.Span))));
		}

		public async Task<WorkspaceFileReplacementResult> ReplaceFileAsync(
			WorkspaceTemporaryFile temporaryFile,
			string destinationPath,
			FileStamp expectedStamp,
			CancellationToken cancellationToken)
		{
			Replacements.Advance();

			if (PendingReplacement is not null)
				await PendingReplacement.Task.WaitAsync(cancellationToken);

			ReplacementBegan = true;
			return ReplacementResult;
		}

		public Task DeleteTemporaryAsync(WorkspaceTemporaryFile temporaryFile)
		{
			DeleteTemporaryCount++;
			return Task.CompletedTask;
		}

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
			=> throw new NotSupportedException();

		public Task<WorkspaceFileDeleteResult> DeleteAsync(
			string path,
			FileStamp expectedStamp,
			CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public Task<WorkspaceFileDeleteResult> DeleteDirectoryAsync(
			string path,
			CancellationToken cancellationToken)
			=> throw new NotSupportedException();
	}
}

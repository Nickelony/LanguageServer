using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

public sealed partial class WorkspaceDocumentStoreTests
{
	[TestMethod]
	public async Task SaveAs_DestinationAndWriteFailuresMapToStatuses()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("source.lua"), s_openOptions)).Snapshot!;
		string destinationPath = Path.Combine(Path.GetDirectoryName(initial.DocumentId)!, "destination.lua");

		// Save As captures the source stamp and then the destination stamp; an existing destination
		// stamp stops the operation before any write.
		fileSystem.CapturedStamps.Enqueue(initial.OnDiskStamp);
		fileSystem.CapturedStamps.Enqueue(new FileStamp(true, 3, DateTime.UnixEpoch.AddMinutes(1), "existing"));
		WorkspaceDocumentSaveAsResult exists = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentSaveAsStatus.DestinationExists, exists.Status);
		Assert.AreEqual(0, fileSystem.WriteTemporaryCount);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));

		fileSystem.ReplacementStatus = WorkspaceFileReplacementStatus.Failed;
		fileSystem.CapturedStamps.Enqueue(initial.OnDiskStamp);
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);
		WorkspaceDocumentSaveAsResult failed = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentSaveAsStatus.WriteFailed, failed.Status);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));

		// The failed write must not advance the persisted baseline or mark the document dirty.
		Assert.AreEqual(initial.PersistedVersion, failed.Snapshot!.PersistedVersion);
		Assert.IsFalse(failed.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task SaveAs_ReplacementOutcomesMapToStatuses()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat)
		{
			ReplacementStatus = WorkspaceFileReplacementStatus.DestinationExists
		};
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("source.lua"), s_openOptions)).Snapshot!;
		string destinationPath = TestPath("folder", "destination.lua");

		// The source stamp matches and the destination is missing, so the write runs and reports the
		// directory that occupies the destination path.
		fileSystem.CapturedStamps.Enqueue(initial.OnDiskStamp);
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);
		WorkspaceDocumentSaveAsResult exists = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentSaveAsStatus.DestinationExists, exists.Status);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));

		fileSystem.ReplacementStatus = WorkspaceFileReplacementStatus.Canceled;
		fileSystem.CapturedStamps.Enqueue(initial.OnDiskStamp);
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);
		WorkspaceDocumentSaveAsResult canceled = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentSaveAsStatus.Canceled, canceled.Status);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
	}

	[TestMethod]
	public async Task SaveAs_SourceStampConflictReportsExternalFileConflictAndKeepsTracking()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("source.lua"), s_openOptions)).Snapshot!;
		FileStamp observedStamp = new(true, 11, DateTime.UnixEpoch.AddMinutes(1), "changed");
		fileSystem.CapturedStamps.Enqueue(observedStamp);

		WorkspaceDocumentSaveAsResult result = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			TestPath("folder", "destination.lua")));

		Assert.AreEqual(WorkspaceDocumentSaveAsStatus.ExternalFileConflict, result.Status);
		Assert.AreEqual(observedStamp, result.ObservedOnDiskStamp);
		Assert.AreEqual(WorkspaceOperationFailureCodes.ExternalFileConflict, result.Failure!.Code);
		Assert.AreEqual(0, fileSystem.WriteTemporaryCount);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
	}

	[TestMethod]
	public async Task SaveAs_DestinationWithInFlightOpenReportsDestinationBusy()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("source.lua"), s_openOptions)).Snapshot!;
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingRead = pendingRead.Task;

		// The open holds a reservation for the destination path while it loads.
		Task<WorkspaceDocumentOpenResult> open = store.OpenAsync(TestPath("destination.lua"), s_openOptions);
		await fileSystem.Reads.WhenReachedAsync(2);

		WorkspaceDocumentSaveAsResult result = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			TestPath("destination.lua")));

		// An in-flight open would add its document at the destination path, so the save must not
		// mutate anything there.
		Assert.AreEqual(WorkspaceDocumentSaveAsStatus.DestinationBusy, result.Status);
		Assert.AreEqual(0, fileSystem.WriteTemporaryCount);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));

		pendingRead.SetResult(fileSystem.CreateReadResult());
		WorkspaceDocumentOpenResult opened = await open;
		Assert.AreEqual(WorkspaceDocumentOpenStatus.Opened, opened.Status);
	}

	[TestMethod]
	public async Task SaveAs_UnencodableContentReportsInvalidEncoding()
	{
		var fileSystem = new FakeFileSystem("disk", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("source.lua"), s_openOptions)).Snapshot!;
		TextFileFormat windows1252Format = new(TextEncodingKind.Windows1252, false, TextNewlineStyle.None);
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"\u2605",
			windows1252Format));
		Assert.AreEqual(WorkspaceDocumentMutationStatus.Changed, edited.Status);

		// The source and destination stamps are captured before the content is encoded, so both
		// captures must succeed for the encoding failure to be reached.
		fileSystem.CapturedStamps.Enqueue(edited.Snapshot!.OnDiskStamp);
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);
		WorkspaceDocumentSaveAsResult result = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(edited.Snapshot.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version),
			edited.Snapshot.OnDiskStamp,
			TestPath("copy.lua")));

		Assert.AreEqual(WorkspaceDocumentSaveAsStatus.WriteFailed, result.Status);
		Assert.AreEqual(WorkspaceOperationFailureCodes.InvalidEncoding, result.Failure!.Code);
		Assert.AreEqual(0, fileSystem.WriteTemporaryCount);
	}

	[TestMethod]
	public async Task SaveAs_ReplacementWithoutObservedStamp_RecapturesTheStamp()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot dirty = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"changed",
			initial.FileFormat)).Snapshot!;

		// A stamp of Missing here would claim the destination that was just written does not exist.
		FileStamp writtenStamp = new(true, 11, DateTime.UnixEpoch.AddMinutes(5), "written");
		fileSystem.OmitReplacementStamp = true;
		fileSystem.CapturedStamps.Enqueue(initial.OnDiskStamp);
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);
		fileSystem.CapturedStamps.Enqueue(writtenStamp);

		WorkspaceDocumentSaveAsResult result = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(dirty.DocumentKey, dirty.DocumentId, dirty.Version),
			dirty.OnDiskStamp,
			TestPath("folder", "copy.lua")));

		Assert.AreEqual(WorkspaceDocumentSaveAsStatus.SavedAs, result.Status);
		Assert.AreEqual(writtenStamp, result.Snapshot!.OnDiskStamp);
		Assert.IsTrue(result.Snapshot.ExistsOnDisk);
	}

	[TestMethod]
	public async Task SaveAs_SecondPathReplacementConflictReportsDestinationExists()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("source.lua"), s_openOptions)).Snapshot!;
		FileStamp appearedStamp = new(true, 4, DateTime.UnixEpoch.AddMinutes(1), "appeared");

		// The destination appeared between the pre-write probe and the conditional replacement; for a
		// second path the benign race is a destination collision, not a source-file conflict.
		fileSystem.CapturedStamps.Enqueue(initial.OnDiskStamp);
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);
		fileSystem.ReplacementStatus = WorkspaceFileReplacementStatus.ExternalFileConflict;
		fileSystem.ReplacementStamp = appearedStamp;

		WorkspaceDocumentSaveAsResult result = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			TestPath("copy.lua")));

		Assert.AreEqual(WorkspaceDocumentSaveAsStatus.DestinationExists, result.Status);
		Assert.AreEqual(appearedStamp, result.ObservedOnDiskStamp);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
	}

	[TestMethod]
	public async Task SaveAs_RootDestinationWritesTemporaryFileInTheRootDirectory()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		string rootPath = Path.GetPathRoot(Path.GetFullPath(Environment.CurrentDirectory))!;
		WorkspaceDocumentSnapshot source = (await store.OpenAsync(TestPath("source.lua"), s_openOptions)).Snapshot!;
		fileSystem.CapturedStamps.Enqueue(source.OnDiskStamp);
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);

		WorkspaceDocumentSaveAsResult result = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(source.DocumentKey, source.DocumentId, source.Version),
			source.OnDiskStamp,
			rootPath));

		Assert.AreEqual(WorkspaceDocumentSaveAsStatus.SavedAs, result.Status);
		Assert.AreEqual(rootPath, fileSystem.LastTemporaryDirectory);
	}

	[TestMethod]
	public async Task SaveAs_BlankDestinationReportsInvalidPath()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		WorkspaceDocumentSaveAsResult result = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			"   "));

		Assert.AreEqual(WorkspaceDocumentSaveAsStatus.InvalidPath, result.Status);
		Assert.IsNull(result.Snapshot);
		Assert.AreEqual(0, fileSystem.WriteTemporaryCount);
	}

	[TestMethod]
	public async Task SaveAs_TrackedDestinationCollisionReportsDestinationInUse()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot source = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot collision = (await store.OpenAsync(
			TestPath("folder", "two.lua"),
			s_openOptions)).Snapshot!;

		// The destination is tracked by an unrelated document instance, so Save As is rejected before
		// the source stamp is captured and before the file system is asked to write anything.
		WorkspaceDocumentSaveAsResult result = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(source.DocumentKey, source.DocumentId, source.Version),
			source.OnDiskStamp,
			TestPath("folder", "two.lua")));

		Assert.AreEqual(WorkspaceDocumentSaveAsStatus.DestinationInUse, result.Status);
		Assert.AreEqual(0, fileSystem.WriteTemporaryCount);
		Assert.IsTrue(store.TryGetSnapshot(source.DocumentId, out _));
		Assert.IsTrue(store.TryGetSnapshot(collision.DocumentId, out _));
	}

	[TestMethod]
	public async Task SaveAs_PreCanceledWriteReportsCanceledAndKeepsTracking()
	{
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		WorkspaceDocumentSaveAsResult result = await store.SaveAsAsync(
			new WorkspaceDocumentSaveAsRequest(
				new(initial.DocumentKey, initial.DocumentId, initial.Version),
				initial.OnDiskStamp,
				TestPath("copy.lua")),
			cancellation.Token);

		Assert.AreEqual(WorkspaceDocumentSaveAsStatus.Canceled, result.Status);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
	}
}

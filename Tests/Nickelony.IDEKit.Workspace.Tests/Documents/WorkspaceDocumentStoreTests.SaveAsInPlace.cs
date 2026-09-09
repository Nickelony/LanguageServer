using Nickelony.IDEKit.Core.Pathing;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

public sealed partial class WorkspaceDocumentStoreTests
{
	[TestMethod]
	public async Task SaveAs_SamePathSavesInPlace()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot dirty = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"changed",
			initial.FileFormat)).Snapshot!;
		FileStamp writtenStamp = new(true, 7, DateTime.UnixEpoch.AddMinutes(1), "written");

		// The destination resolves to the tracked document's own file, so the save writes in place:
		// the validated source stamp is the replacement expectation instead of an absent destination.
		fileSystem.CapturedStamps.Enqueue(initial.OnDiskStamp);
		fileSystem.ReplacementStamp = writtenStamp;

		WorkspaceDocumentSaveAsResult result = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(dirty.DocumentKey, dirty.DocumentId, dirty.Version),
			dirty.OnDiskStamp,
			initial.DocumentId));

		Assert.AreEqual(WorkspaceDocumentSaveAsStatus.SavedAs, result.Status);
		Assert.AreEqual(initial.DocumentId, result.Snapshot!.DocumentId);
		Assert.IsFalse(result.Snapshot.IsDirty);
		Assert.AreEqual(writtenStamp, result.Snapshot.OnDiskStamp);
		Assert.AreEqual(initial.OnDiskStamp, fileSystem.LastReplacementExpectedStamp);
		Assert.AreEqual(1, fileSystem.ReplaceCount);
	}

	[TestMethod]
	public async Task SaveAs_CaseVariantSamePathSavesInPlaceOnCaseInsensitivePolicy()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem, LocalPathComparisonPolicy.CaseInsensitive);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot dirty = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"changed",
			initial.FileFormat)).Snapshot!;

		fileSystem.CapturedStamps.Enqueue(initial.OnDiskStamp);

		// A case variant resolves to the same tracked instance, so it is a save in place rather than
		// a destination collision; the caller's spelling becomes the display path.
		WorkspaceDocumentSaveAsResult result = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(dirty.DocumentKey, dirty.DocumentId, dirty.Version),
			dirty.OnDiskStamp,
			TestPath("SCRIPT.lua")));

		Assert.AreEqual(WorkspaceDocumentSaveAsStatus.SavedAs, result.Status);
		Assert.IsFalse(result.Snapshot!.IsDirty);
		Assert.AreEqual(TestPath("SCRIPT.lua"), result.Snapshot.DisplayPath);
		Assert.IsTrue(store.TryGetSnapshot(TestPath("script.lua"), out _));
	}

	[TestMethod]
	public async Task SaveAs_SamePathReplacementConflictReportsExternalFileConflict()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		// The source stamp was current when the save started, so a conflict from the in-place
		// replacement means the tracked file changed again instead of a destination collision.
		fileSystem.ReplacementStatus = WorkspaceFileReplacementStatus.ExternalFileConflict;
		fileSystem.CapturedStamps.Enqueue(initial.OnDiskStamp);

		WorkspaceDocumentSaveAsResult result = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			initial.DocumentId));

		Assert.AreEqual(WorkspaceDocumentSaveAsStatus.ExternalFileConflict, result.Status);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
	}

	[TestMethod]
	public async Task SaveAs_ReplacementStateUnknownReportsReplacementStateUnknown()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		fileSystem.ReplacementStatus = WorkspaceFileReplacementStatus.ReplacementStateUnknown;
		fileSystem.CapturedStamps.Enqueue(initial.OnDiskStamp);
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);

		WorkspaceDocumentSaveAsResult result = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			TestPath("copy.lua")));

		Assert.AreEqual(WorkspaceDocumentSaveAsStatus.ReplacementStateUnknown, result.Status);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
	}

	[TestMethod]
	public async Task SaveAs_ReplacementWithoutStampAndFailedRecaptureReportsReplacementStateUnknown()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		// The replacement completed without reporting its stamp and the re-capture fails, so the
		// final state cannot be established and no baseline may be installed.
		fileSystem.OmitReplacementStamp = true;
		fileSystem.CaptureTasks.Enqueue(Task.FromResult(initial.OnDiskStamp));
		fileSystem.CaptureTasks.Enqueue(Task.FromResult(FileStamp.Missing));
		fileSystem.CaptureTasks.Enqueue(Task.FromException<FileStamp>(new IOException("The stamp could not be captured.")));

		WorkspaceDocumentSaveAsResult result = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			TestPath("copy.lua")));

		Assert.AreEqual(WorkspaceDocumentSaveAsStatus.ReplacementStateUnknown, result.Status);
		Assert.AreEqual(WorkspaceOperationFailureCodes.ReplacementStateUnknown, result.Failure!.Code);
		Assert.AreEqual(initial.OnDiskStamp, result.Snapshot!.OnDiskStamp);
	}
}

using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

public sealed partial class WorkspaceDocumentStoreTests
{
	[TestMethod]
	[Timeout(15000)]
	public async Task Dispose_WhileCommitWaitsForReplacementCompletesWithCanceled()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingReplacement = pendingReplacement.Task;
		var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot dirty = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"changed",
			initial.FileFormat)).Snapshot!;

		Task<WorkspaceDocumentCommitResult> commit = store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(dirty.DocumentKey, dirty.DocumentId, dirty.Version),
			dirty.OnDiskStamp));
		await fileSystem.Replacements.WhenReachedAsync(1);

		// Disposal cancels the in-flight commit through the lifetime token and waits for the
		// registered operation before it disposes the per-document disk gates.
		ValueTask disposal = store.DisposeAsync();
		WorkspaceDocumentCommitResult result = await commit;

		Assert.AreEqual(WorkspaceDocumentCommitStatus.Canceled, result.Status);
		Assert.IsTrue(result.Snapshot!.IsDirty);
		await disposal;
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task Dispose_WhileSaveAsWaitsForReplacementCompletesWithCanceled()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingReplacement = pendingReplacement.Task;
		var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		string destinationPath = TestPath("folder", "destination.lua");
		fileSystem.CapturedStamps.Enqueue(initial.OnDiskStamp);
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);

		Task<WorkspaceDocumentSaveAsResult> saveAs = store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			destinationPath));
		await fileSystem.Replacements.WhenReachedAsync(1);

		// Disposal cancels the in-flight save-as through the lifetime token and waits for the
		// registered operation before it disposes the per-document disk gates.
		ValueTask disposal = store.DisposeAsync();
		WorkspaceDocumentSaveAsResult result = await saveAs;

		Assert.AreEqual(WorkspaceDocumentSaveAsStatus.Canceled, result.Status);
		Assert.AreEqual(initial.DocumentId, result.Snapshot!.DocumentId);
		await disposal;
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task Dispose_WhileOpenWaitsForPendingReadCompletesWithCanceled()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingRead = pendingRead.Task;
		var store = new WorkspaceDocumentStore(fileSystem);

		Task<WorkspaceDocumentOpenResult> open = store.OpenAsync(TestPath("script.lua"), s_openOptions);
		await fileSystem.Reads.WhenReachedAsync(1);

		// Disposal cancels the in-flight load through the lifetime token and waits for the open
		// reservation before it disposes the per-document gates.
		ValueTask disposal = store.DisposeAsync();
		WorkspaceDocumentOpenResult result = await open;

		Assert.AreEqual(WorkspaceDocumentOpenStatus.Canceled, result.Status);
		Assert.IsNull(result.Snapshot);
		await disposal;
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task Dispose_WhileDeleteWaitsForPendingDeleteCompletesWithCanceled()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingDelete = new TaskCompletionSource<WorkspaceFileDeleteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingDelete = pendingDelete.Task;
		var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		Task<WorkspaceDocumentDeleteResult> delete = store.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));
		await fileSystem.Deletes.WhenReachedAsync(1);

		// Disposal cancels the in-flight delete through the lifetime token and waits for the registered
		// operation before it disposes the per-document gates.
		ValueTask disposal = store.DisposeAsync();
		WorkspaceDocumentDeleteResult result = await delete;

		Assert.AreEqual(WorkspaceDocumentDeleteStatus.Canceled, result.Status);
		Assert.AreEqual(snapshot.DocumentId, result.Snapshot!.DocumentId);
		await disposal;
	}
}

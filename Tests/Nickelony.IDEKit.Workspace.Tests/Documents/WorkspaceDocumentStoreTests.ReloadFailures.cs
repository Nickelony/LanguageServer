using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

public sealed partial class WorkspaceDocumentStoreTests
{
	[TestMethod]
	public async Task Reload_CleanDocumentWithPreCanceledTokenReportsCanceled()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		int readsBeforeReload = fileSystem.ReadCount;
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();

		WorkspaceDocumentReloadResult result = await store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version)),
			cancellation.Token);

		Assert.AreEqual(WorkspaceDocumentReloadStatus.Canceled, result.Status);
		Assert.AreEqual(readsBeforeReload, fileSystem.ReadCount);
	}

	[TestMethod]
	public async Task Reload_CleanReadFailureReportsReadFailedAndKeepsTracking()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		fileSystem.ReadResults.Enqueue(Task.FromException<WorkspaceFileReadResult>(
			new IOException("The file could not be read.")));

		WorkspaceDocumentReloadResult result = await store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version)));

		Assert.AreEqual(WorkspaceDocumentReloadStatus.ReadFailed, result.Status);
		Assert.AreEqual(WorkspaceOperationFailureCodes.ReadFailed, result.Failure!.Code);
		Assert.AreEqual(initial.Version, result.Snapshot!.Version);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
	}

	[TestMethod]
	public async Task Reload_CleanDocumentWithUndecodableBytesReportsInvalidEncoding()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		// 0xC3 followed by 0x28 is never valid UTF-8; the clean reload path decodes raw bytes exactly
		// like the open and conflict-resolution paths and must classify the failure as an encoding
		// problem instead of a generic read failure.
		fileSystem.ReadResults.Enqueue(Task.FromResult(new WorkspaceFileReadResult(
			string.Empty,
			default,
			new FileStamp(true, 2, DateTime.UnixEpoch.AddMinutes(1), "bytes"),
			new ReadOnlyMemory<byte>([0xC3, 0x28]))));

		WorkspaceDocumentReloadResult result = await store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version)));

		Assert.AreEqual(WorkspaceDocumentReloadStatus.ReadFailed, result.Status);
		Assert.AreEqual(WorkspaceOperationFailureCodes.InvalidEncoding, result.Failure!.Code);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
	}
}

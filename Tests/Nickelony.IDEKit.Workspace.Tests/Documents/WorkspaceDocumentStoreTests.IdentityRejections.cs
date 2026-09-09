using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Tests;

public sealed partial class WorkspaceDocumentStoreTests
{
	[TestMethod]
	public async Task Rename_IdentityRejectionsReportTheirStatuses()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		string destinationPath = TestPath("renamed.lua");

		WorkspaceDocumentRenameResult notFound = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(new WorkspaceDocumentKey(Guid.NewGuid()), TestPath("absent.lua"), 0),
			initial.OnDiskStamp,
			destinationPath));
		Assert.AreEqual(WorkspaceDocumentRenameStatus.DocumentNotFound, notFound.Status);

		WorkspaceDocumentRenameResult staleInstance = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(new WorkspaceDocumentKey(Guid.NewGuid()), initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			destinationPath));
		Assert.AreEqual(WorkspaceDocumentRenameStatus.StaleDocumentInstance, staleInstance.Status);

		WorkspaceDocumentRenameResult staleVersion = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version + 1),
			initial.OnDiskStamp,
			destinationPath));
		Assert.AreEqual(WorkspaceDocumentRenameStatus.StaleDocument, staleVersion.Status);

		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
		Assert.AreEqual(WorkspaceDocumentOpenStatus.Opened, (await store.OpenAsync(destinationPath, s_openOptions)).Status);
	}

	[TestMethod]
	public async Task SaveAs_IdentityRejectionsReportTheirStatuses()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		string destinationPath = TestPath("copy.lua");

		WorkspaceDocumentSaveAsResult notFound = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(new WorkspaceDocumentKey(Guid.NewGuid()), TestPath("absent.lua"), 0),
			initial.OnDiskStamp,
			destinationPath));
		Assert.AreEqual(WorkspaceDocumentSaveAsStatus.DocumentNotFound, notFound.Status);

		WorkspaceDocumentSaveAsResult staleInstance = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(new WorkspaceDocumentKey(Guid.NewGuid()), initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			destinationPath));
		Assert.AreEqual(WorkspaceDocumentSaveAsStatus.StaleDocumentInstance, staleInstance.Status);

		WorkspaceDocumentSaveAsResult staleVersion = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version + 1),
			initial.OnDiskStamp,
			destinationPath));
		Assert.AreEqual(WorkspaceDocumentSaveAsStatus.StaleDocument, staleVersion.Status);

		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
		Assert.AreEqual(0, fileSystem.WriteTemporaryCount);
	}

	[TestMethod]
	public async Task Delete_IdentityRejectionsReportTheirStatuses()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		WorkspaceDocumentDeleteResult notFound = await store.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(new WorkspaceDocumentKey(Guid.NewGuid()), TestPath("absent.lua"), 0),
			initial.OnDiskStamp));
		Assert.AreEqual(WorkspaceDocumentDeleteStatus.DocumentNotFound, notFound.Status);

		WorkspaceDocumentDeleteResult staleInstance = await store.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(new WorkspaceDocumentKey(Guid.NewGuid()), initial.DocumentId, initial.Version),
			initial.OnDiskStamp));
		Assert.AreEqual(WorkspaceDocumentDeleteStatus.StaleDocumentInstance, staleInstance.Status);

		WorkspaceDocumentDeleteResult staleVersion = await store.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version + 1),
			initial.OnDiskStamp));
		Assert.AreEqual(WorkspaceDocumentDeleteStatus.StaleDocument, staleVersion.Status);

		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
		Assert.AreEqual(0, fileSystem.Deletes.Count);
	}
}

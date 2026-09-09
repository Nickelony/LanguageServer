using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Tests;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

public sealed partial class WorkspaceDocumentManagerTests
{
	[TestMethod]
	public async Task Rename_ProceedsWhileAViewHasPendingEditsAndConflict()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		string destinationPath = Path.Combine(fixture.DirectoryPath, "renamed.txt");

		// View state cannot be lost by a rename, so pending edits and a conflict do not block it; the
		// view is rekeyed to the new identity afterwards.
		fixture.View.HasPendingEdits = true;
		fixture.View.HasConflict = true;

		WorkspaceDocumentManagerRenameResult result = await fixture.Manager.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp,
			destinationPath));

		Assert.IsNotNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentRenameStatus.Renamed, result.Status);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationStatus.Synchronized, result.Views.Status);
		Assert.IsTrue(File.Exists(destinationPath));
		Assert.IsFalse(File.Exists(fixture.DocumentPath));
		Assert.AreEqual(result.Snapshot!.DocumentId, fixture.View.DocumentId);
	}

	[TestMethod]
	public async Task DirectoryRename_ProceedsWhileAViewIsUnsynchronized()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		using var destination = new TestTempDirectory();
		string destinationPath = Path.Combine(destination.Path, "moved");

		// An unsynchronized view does not block a directory rename: the move cannot lose view state,
		// and the identity acknowledgment gives the view a chance to re-synchronize.
		await fixture.MarkViewUnsynchronizedAsync(snapshot);

		WorkspaceDocumentManagerDirectoryRenameResult result = await fixture.Manager.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(fixture.DirectoryPath, destinationPath));

		Assert.IsNotNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentDirectoryRenameStatus.Renamed, result.Status);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationStatus.Synchronized, result.Views.Status);
		Assert.IsTrue(Directory.Exists(destinationPath));
		Assert.IsFalse(Directory.Exists(fixture.DirectoryPath));
		Assert.AreEqual(result.Snapshots[0].DocumentId, fixture.View.DocumentId);
	}

	[TestMethod]
	public async Task DirectoryDelete_BlockedByUnsynchronizedView()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		await fixture.MarkViewUnsynchronizedAsync(snapshot);

		WorkspaceDocumentManagerDirectoryDeleteResult result = await fixture.Manager.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(fixture.DirectoryPath));

		Assert.IsNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationStatus.Blocked, result.Views.Status);
		CollectionAssert.Contains(result.Views.ViewIds.ToArray(), fixture.View.ViewId);
		Assert.IsTrue(File.Exists(fixture.DocumentPath));
	}

	[TestMethod]
	public async Task Delete_GuardRollbackFailureIsRecordedWithoutFaulting()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		var secondView = new TestView(fixture, "second-view");
		WorkspaceDocumentManagerOpenResult opened = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			secondView);
		Assert.AreEqual(WorkspaceDocumentManagerOpenStatus.Opened, opened.Status);

		fixture.View.ThrowOnDeleteGuardRelease = true;
		secondView.BlockDeleteGuard = true;

		WorkspaceDocumentManagerDeleteResult result = await fixture.Manager.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));

		Assert.IsNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationStatus.Blocked, result.Views.Status);
		CollectionAssert.Contains(result.Views.ViewIds.ToArray(), "second-view");
		Assert.AreEqual(1, fixture.View.DeleteGuardReleaseCount, "The rollback must attempt to release entered guards.");
		Assert.IsTrue(File.Exists(fixture.DocumentPath));

		// The failed release leaves both views unsynchronized, so the next destructive operation is
		// rejected by the pre-check instead of running against views with unknown state.
		WorkspaceDocumentManagerDeleteResult retry = await fixture.Manager.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));

		Assert.IsNull(retry.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationStatus.Blocked, retry.Views.Status);
	}

	[TestMethod]
	public async Task DirectoryRename_GuardRejectionBlocksAndNothingMoves()
	{
		await using var fixture = new ManagerFixture();
		await fixture.OpenViewAsync();
		using var destination = new TestTempDirectory();
		string destinationPath = Path.Combine(destination.Path, "moved");
		fixture.View.BlockDeleteGuard = true;

		// A view that rejects the delete guard blocks the directory rename before the store call; the
		// rejected guard was never applied, so the rollback has nothing to release.
		WorkspaceDocumentManagerDirectoryRenameResult result = await fixture.Manager.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(fixture.DirectoryPath, destinationPath));

		Assert.IsNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationStatus.Blocked, result.Views.Status);
		CollectionAssert.Contains(result.Views.ViewIds.ToArray(), fixture.View.ViewId);
		Assert.AreEqual(0, fixture.View.DeleteGuardReleaseCount);
		Assert.IsTrue(Directory.Exists(fixture.DirectoryPath));
		Assert.IsFalse(Directory.Exists(destinationPath));
	}

	[TestMethod]
	public async Task DirectoryRename_IdentityAckFailureReportsUnsynchronizedView()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		using var destination = new TestTempDirectory();
		string destinationPath = Path.Combine(destination.Path, "moved");
		fixture.View.FailIdentityAck = true;

		WorkspaceDocumentManagerDirectoryRenameResult result = await fixture.Manager.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(fixture.DirectoryPath, destinationPath));

		// The move succeeded, but the view rejected the identity change, so it stays bound to the
		// vacated id and is reported as unsynchronized.
		Assert.AreEqual(WorkspaceDocumentDirectoryRenameStatus.Renamed, result.Status);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationStatus.Unsynchronized, result.Views.Status);
		CollectionAssert.Contains(result.Views.ViewIds.ToArray(), fixture.View.ViewId);
		Assert.AreEqual(snapshot.DocumentId, fixture.View.DocumentId);
		Assert.IsTrue(Directory.Exists(destinationPath));
		Assert.IsFalse(Directory.Exists(fixture.DirectoryPath));
	}

	[TestMethod]
	public async Task DirectoryRename_LateViewIsAcknowledgedAndRekeyed()
	{
		await using var fixture = new ManagerFixture();
		await fixture.OpenViewAsync();
		using var destination = new TestTempDirectory();
		string destinationPath = Path.Combine(destination.Path, "moved");
		var lateView = new TestView(fixture, "late-view");
		Task<WorkspaceDocumentManagerOpenResult>? attachTask = null;

		// The guard callback runs after the manager captured its bindings but before the store move
		// starts, which is exactly the window in which a competing attach can register.
		fixture.View.OnDeleteGuardEntered = () =>
		{
			if (attachTask is not null)
				return;

			attachTask = fixture.Manager.OpenWithViewAsync(fixture.DocumentPath, s_openOptions, lateView);
		};

		WorkspaceDocumentManagerDirectoryRenameResult result = await fixture.Manager.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(fixture.DirectoryPath, destinationPath));

		Assert.IsNotNull(attachTask);
		Assert.AreEqual(WorkspaceDocumentManagerOpenStatus.Opened, (await attachTask).Status);
		Assert.AreEqual(WorkspaceDocumentDirectoryRenameStatus.Renamed, result.Status);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationStatus.Synchronized, result.Views.Status);
		Assert.AreEqual(0, lateView.CloseCount, "A view attached during the move must not be closed by it.");

		// The late view is matched by its document key and rekeyed to the moved identity instead of
		// staying bound to the vacated id.
		Assert.AreEqual(result.Snapshots[0].DocumentId, lateView.DocumentId);

		// It is reachable for later operations: a replacement refreshes it through the new identity.
		WorkspaceDocumentManagerMutationResult replaced = await fixture.Manager.ReplaceAsync(new WorkspaceDocumentReplaceRequest(
			new(result.Snapshots[0].DocumentKey, result.Snapshots[0].DocumentId, result.Snapshots[0].Version),
			"after move",
			result.Snapshots[0].FileFormat));

		Assert.AreEqual(WorkspaceDocumentMutationStatus.Changed, replaced.Status);
		Assert.AreEqual(1, lateView.RefreshCount);
		Assert.AreEqual("after move", lateView.Text);
	}

	[TestMethod]
	public async Task DeleteDirectory_GuardReleaseFailureIsReportedAsUnsynchronizedView()
	{
		await using var fixture = new ManagerFixture();
		await fixture.OpenViewAsync();
		fixture.View.FailDeleteGuardRelease = true;

		WorkspaceDocumentManagerDirectoryDeleteResult result = await fixture.Manager.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(fixture.DirectoryPath));

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteStatus.Deleted, result.Status);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationStatus.Unsynchronized, result.Views.Status);
		CollectionAssert.Contains(result.Views.ViewIds.ToArray(), fixture.View.ViewId);
		Assert.AreEqual(1, fixture.View.CloseCount);
		Assert.IsFalse(Directory.Exists(fixture.DirectoryPath));
	}

	[TestMethod]
	public async Task Delete_ViewAttachedAfterCaptureButBeforeStoreDelete_IsClosedAndUnregistered()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		var lateView = new TestView(fixture, "late-view");
		Task<WorkspaceDocumentManagerOpenResult>? attachTask = null;

		// The guard callback runs after the manager captured its view set but before the store delete
		// starts, which is exactly the window in which a competing attach can register.
		fixture.View.OnDeleteGuardEntered = () =>
		{
			if (attachTask is not null)
				return;

			attachTask = fixture.Manager.OpenWithViewAsync(fixture.DocumentPath, s_openOptions, lateView);
		};

		WorkspaceDocumentManagerDeleteResult result = await fixture.Manager.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));

		Assert.IsNotNull(attachTask);
		WorkspaceDocumentManagerOpenResult attached = await attachTask;
		Assert.AreEqual(WorkspaceDocumentManagerOpenStatus.Opened, attached.Status);
		Assert.AreEqual(snapshot.DocumentKey, attached.Snapshot!.DocumentKey);
		Assert.AreEqual(WorkspaceDocumentDeleteStatus.Deleted, result.Status);
		Assert.AreEqual(1, fixture.View.CloseCount);
		Assert.AreEqual(1, lateView.CloseCount, "A view registered during the delete must be closed with the deleted document.");

		// The late view must also be unregistered: stopping the manager must not close it a second time.
		await fixture.Manager.StopAsync();
		Assert.AreEqual(1, lateView.CloseCount);
	}

	[TestMethod]
	public async Task Delete_ViewThatCannotReportItsKeyIsClosedAsDeletedInstance()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		var lateView = new TestView(fixture, "late-view")
		{
			// The first key read is the attachment validation; the sweep's read throws.
			ThrowAfterDocumentKeyReads = 1
		};
		Task<WorkspaceDocumentManagerOpenResult>? attachTask = null;

		fixture.View.OnDeleteGuardEntered = () =>
		{
			if (attachTask is not null)
				return;

			attachTask = fixture.Manager.OpenWithViewAsync(fixture.DocumentPath, s_openOptions, lateView);
		};

		WorkspaceDocumentManagerDeleteResult result = await fixture.Manager.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));

		Assert.IsNotNull(attachTask);
		Assert.AreEqual(WorkspaceDocumentManagerOpenStatus.Opened, (await attachTask).Status);
		Assert.AreEqual(WorkspaceDocumentDeleteStatus.Deleted, result.Status);
		Assert.AreEqual(1, lateView.CloseCount, "A view that cannot report its key must be treated as bound to the removed instance.");
	}

	[TestMethod]
	public async Task DeleteDirectory_ViewReopenedDuringDeleteIsLeftAloneAndLateViewIsClosed()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		var lateView = new TestView(fixture, "late-view");
		Task<WorkspaceDocumentManagerOpenResult>? attachTask = null;

		fixture.View.OnDeleteGuardEntered = () =>
		{
			if (attachTask is not null)
				return;

			attachTask = fixture.Manager.OpenWithViewAsync(fixture.DocumentPath, s_openOptions, lateView);
		};

		WorkspaceDocumentManagerDirectoryDeleteResult result = await fixture.Manager.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(fixture.DirectoryPath));

		Assert.IsNotNull(attachTask);
		Assert.AreEqual(WorkspaceDocumentManagerOpenStatus.Opened, (await attachTask).Status);
		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteStatus.Deleted, result.Status);
		Assert.AreEqual(1, fixture.View.CloseCount);
		Assert.AreEqual(1, lateView.CloseCount, "A view registered during the directory delete must be closed with the removed descendant.");
	}

	[TestMethod]
	public async Task Delete_HappyPathClosesViewAndRemovesTracking()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		WorkspaceDocumentManagerDeleteResult result = await fixture.Manager.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentDeleteStatus.Deleted, result.Status);
		Assert.AreEqual(1, fixture.View.CloseCount);
		Assert.AreEqual(1, fixture.View.DeleteGuardReleaseCount);
		Assert.IsFalse(File.Exists(fixture.DocumentPath));
		Assert.IsFalse(fixture.Store.TryGetSnapshot(snapshot.DocumentId, out _));
	}
}

using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

public sealed partial class WorkspaceDocumentManagerTests
{
	[TestMethod]
	public async Task ApplyRequested_ReplacesDocumentWithPublishedContent()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		fixture.View.RaiseApply(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"edited",
			snapshot.FileFormat));

		Assert.IsTrue(fixture.Store.TryGetSnapshot(snapshot.DocumentId, out WorkspaceDocumentSnapshot? replaced));
		Assert.IsNotNull(replaced);
		Assert.AreEqual("edited", replaced.Content);
		Assert.IsTrue(replaced.IsDirty);
		Assert.AreEqual("edited", fixture.View.Text);
	}

	[TestMethod]
	public async Task ApplyRequested_ViewWithPendingEditsPublishesThemAndClearsTheBlockingState()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		string destinationPath = Path.Combine(fixture.DirectoryPath, "renamed.txt");
		fixture.View.HasPendingEdits = true;

		// The apply is the view publishing its own pending edits; the acknowledgment applies the
		// replacement and must clear the pending state instead of leaving the view blocking.
		fixture.View.RaiseApply(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"edited",
			snapshot.FileFormat));

		Assert.IsTrue(fixture.Store.TryGetSnapshot(snapshot.DocumentId, out WorkspaceDocumentSnapshot? replaced));
		Assert.AreEqual("edited", replaced!.Content);
		Assert.IsFalse(fixture.View.HasPendingEdits);

		WorkspaceDocumentManagerRenameResult renamed = await fixture.Manager.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(replaced.DocumentKey, replaced.DocumentId, replaced.Version),
			replaced.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentRenameStatus.Renamed, renamed.Status);
		Assert.IsTrue(File.Exists(destinationPath));
	}

	[TestMethod]
	public async Task Discard_RestoresBaselineAndRefreshesAttachedView()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		WorkspaceDocumentMutationResult edited = fixture.Store.Replace(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"edited",
			snapshot.FileFormat));
		Assert.AreEqual(WorkspaceDocumentMutationStatus.Changed, edited.Status);

		// Stale text proves the discard acknowledgment actually refreshed the view.
		fixture.View.SetText("stale view text");
		int refreshCountBefore = fixture.View.RefreshCount;

		WorkspaceDocumentManagerMutationResult discarded = await fixture.Manager.DiscardAsync(new WorkspaceDocumentDiscardRequest(
			new(edited.Snapshot!.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version)));

		Assert.AreEqual(WorkspaceDocumentMutationStatus.Changed, discarded.Status);
		Assert.IsFalse(discarded.Snapshot!.IsDirty);
		Assert.AreEqual("initial", fixture.View.Text);
		Assert.AreEqual(refreshCountBefore + 1, fixture.View.RefreshCount);
	}

	[TestMethod]
	public async Task Replace_WithoutSourceViewMutatesDocumentAndRefreshesFallenBehindView()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		// Advance the store behind the view so the manager sees an older peer to refresh.
		WorkspaceDocumentMutationResult behind = fixture.Store.Replace(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"behind the view",
			snapshot.FileFormat));
		WorkspaceDocumentSnapshot behindSnapshot = behind.Snapshot!;
		int refreshBefore = fixture.View.RefreshCount;

		WorkspaceDocumentManagerMutationResult result = await fixture.Manager.ReplaceAsync(new WorkspaceDocumentReplaceRequest(
			new(behindSnapshot.DocumentKey, behindSnapshot.DocumentId, behindSnapshot.Version),
			"manager edit",
			behindSnapshot.FileFormat));

		Assert.AreEqual(WorkspaceDocumentMutationStatus.Changed, result.Status);
		Assert.AreEqual(refreshBefore + 1, fixture.View.RefreshCount);
		Assert.AreEqual("manager edit", fixture.View.Text);
	}

	[TestMethod]
	public async Task Replace_NonThrowingRefreshStatusesMarkViewsUnsynchronizedAndBlockLaterDiskOperations()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		var secondView = new TestView(fixture, "second-view");
		WorkspaceDocumentManagerOpenResult opened = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			secondView);
		Assert.AreEqual(WorkspaceDocumentManagerOpenStatus.Opened, opened.Status);

		// Dirty the store behind both views so the Replace has fallen-behind peers to refresh.
		WorkspaceDocumentMutationResult behind = fixture.Store.Replace(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"behind the views",
			snapshot.FileFormat));
		WorkspaceDocumentSnapshot behindSnapshot = behind.Snapshot!;
		fixture.View.RefreshStatus = WorkspaceDocumentViewRefreshStatus.MarkedStale;
		secondView.RefreshStatus = WorkspaceDocumentViewRefreshStatus.UpdateFailed;

		WorkspaceDocumentManagerMutationResult result = await fixture.Manager.ReplaceAsync(new WorkspaceDocumentReplaceRequest(
			new(behindSnapshot.DocumentKey, behindSnapshot.DocumentId, behindSnapshot.Version),
			"manager edit",
			behindSnapshot.FileFormat));

		Assert.AreEqual(WorkspaceDocumentMutationStatus.Changed, result.Status);

		// A refresh result that is not Refreshed marks the view unsynchronized even when the view
		// reports it without throwing, so a later delete reports both views as blocking.
		WorkspaceDocumentManagerDeleteResult blocked = await fixture.Manager.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(result.Snapshot!.DocumentKey, result.Snapshot.DocumentId, result.Snapshot.Version),
			result.Snapshot.OnDiskStamp));

		Assert.IsNull(blocked.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationStatus.Blocked, blocked.Views.Status);
		CollectionAssert.AreEquivalent(
			new[] { fixture.View.ViewId, secondView.ViewId },
			blocked.Views.ViewIds.ToArray());
		Assert.IsTrue(File.Exists(fixture.DocumentPath));
	}

	[TestMethod]
	public async Task SaveAs_ProceedsWhileAViewHasConflict()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		string destinationPath = Path.Combine(fixture.DirectoryPath, "saved-as.txt");

		// View state cannot be lost by a save-as, so a conflict does not block it; the view is
		// rekeyed to the new identity afterwards.
		fixture.View.HasConflict = true;

		WorkspaceDocumentManagerSaveAsResult result = await fixture.Manager.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp,
			destinationPath));

		Assert.IsNotNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentSaveAsStatus.SavedAs, result.Status);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationStatus.Synchronized, result.Views.Status);
		Assert.IsTrue(File.Exists(destinationPath));
		Assert.AreEqual(result.Snapshot!.DocumentId, fixture.View.DocumentId);
	}

	[TestMethod]
	public async Task Reload_RefreshesAttachedViewOnReloadedResult()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		File.WriteAllText(fixture.DocumentPath, "changed externally");
		int refreshCountBefore = fixture.View.RefreshCount;

		WorkspaceDocumentManagerReloadResult result = await fixture.Manager.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version)));

		Assert.AreEqual(WorkspaceDocumentReloadStatus.Reloaded, result.Status);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationStatus.Synchronized, result.Views.Status);
		Assert.AreEqual("changed externally", fixture.View.Text);
		Assert.AreEqual(refreshCountBefore + 1, fixture.View.RefreshCount);
	}

	[TestMethod]
	public async Task Reload_BlockedByPendingEditsLeavesTheDocumentUnmodified()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		fixture.View.HasPendingEdits = true;

		WorkspaceDocumentManagerReloadResult result = await fixture.Manager.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version)));

		// A view with pending edits would lose them when the content is replaced from disk, so the
		// reload is blocked before the store is reached.
		Assert.IsNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationStatus.Blocked, result.Views.Status);
		CollectionAssert.Contains(result.Views.ViewIds.ToArray(), fixture.View.ViewId);
		Assert.AreEqual("initial", fixture.View.Text);
	}

	[TestMethod]
	public async Task Commit_BlockedByPendingEditsReportsBlockedViewWithoutSaving()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		WorkspaceDocumentMutationResult edited = fixture.Store.Replace(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"edited",
			snapshot.FileFormat));
		fixture.View.HasPendingEdits = true;

		// The view still holds unpublished edits, so a commit would write content the view has not
		// published; the commit is blocked before the store is reached.
		WorkspaceDocumentManagerCommitResult result = await fixture.Manager.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(edited.Snapshot!.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version),
			edited.Snapshot.OnDiskStamp));

		Assert.IsNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationStatus.Blocked, result.Views.Status);
		CollectionAssert.Contains(result.Views.ViewIds.ToArray(), fixture.View.ViewId);
		Assert.AreEqual("initial", File.ReadAllText(fixture.DocumentPath));
	}

	[TestMethod]
	public async Task Reload_ViewRefreshFailureReportsUnsynchronizedView()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		File.WriteAllText(fixture.DocumentPath, "changed externally");
		fixture.View.FailRefresh = true;

		WorkspaceDocumentManagerReloadResult result = await fixture.Manager.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version)));

		// The store reload succeeded, but the view could not refresh, so it stays unsynchronized.
		Assert.AreEqual(WorkspaceDocumentReloadStatus.Reloaded, result.Status);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationStatus.Unsynchronized, result.Views.Status);
		CollectionAssert.Contains(result.Views.ViewIds.ToArray(), fixture.View.ViewId);
		Assert.IsTrue(fixture.Store.TryGetSnapshot(snapshot.DocumentId, out WorkspaceDocumentSnapshot? reloaded));
		Assert.AreEqual("changed externally", reloaded!.Content);
	}

	[TestMethod]
	public async Task Commit_FailedViewRefreshReportsUnsynchronizedViewAndRecoversOnRetry()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		// Dirty the document behind the view so the commit writes and the view falls behind.
		WorkspaceDocumentMutationResult replaced = fixture.Store.Replace(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"edited",
			snapshot.FileFormat));
		Assert.AreEqual(WorkspaceDocumentMutationStatus.Changed, replaced.Status);
		WorkspaceDocumentSnapshot updated = replaced.Snapshot!;
		fixture.View.FailRefresh = true;

		WorkspaceDocumentManagerCommitResult failed = await fixture.Manager.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(updated.DocumentKey, updated.DocumentId, updated.Version),
			updated.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentCommitStatus.Committed, failed.Status);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationStatus.Unsynchronized, failed.Views.Status);
		CollectionAssert.Contains(failed.Views.ViewIds.ToArray(), fixture.View.ViewId);

		fixture.View.FailRefresh = false;

		// The retry must not be rejected by the pre-check: the commit itself is a clean no-op and
		// the post-commit refresh retries the failed view synchronization.
		WorkspaceDocumentManagerCommitResult recovered = await fixture.Manager.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(updated.DocumentKey, updated.DocumentId, updated.Version),
			failed.Snapshot!.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentCommitStatus.Committed, recovered.Status);
		Assert.AreEqual("edited", fixture.View.Text);
	}

	[TestMethod]
	public async Task Commit_RetriesSynchronizationForUnsynchronizedViewAtCurrentVersion()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		// The view is marked unsynchronized without falling behind the document version, so only the
		// synchronization retry can clear the state; the manually staled text proves the retry ran.
		await fixture.MarkViewUnsynchronizedAsync(snapshot);
		fixture.View.SetText("stale view text");

		WorkspaceDocumentManagerCommitResult result = await fixture.Manager.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentCommitStatus.Committed, result.Status);
		Assert.AreEqual("initial", fixture.View.Text);
	}

	[TestMethod]
	public async Task SaveAs_ViewIdentityUpdateFailureReportsUnsynchronizedView()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		string destinationPath = Path.Combine(fixture.DirectoryPath, "saved-as.txt");
		fixture.View.FailIdentityAck = true;

		WorkspaceDocumentManagerSaveAsResult result = await fixture.Manager.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentSaveAsStatus.SavedAs, result.Status);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationStatus.Unsynchronized, result.Views.Status);
		CollectionAssert.Contains(result.Views.ViewIds.ToArray(), fixture.View.ViewId);
		Assert.IsTrue(File.Exists(destinationPath));
	}

	[TestMethod]
	public async Task SaveAs_SucceedsAndRekeysSynchronizedView()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		string destinationPath = Path.Combine(fixture.DirectoryPath, "saved-as.txt");

		WorkspaceDocumentManagerSaveAsResult result = await fixture.Manager.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentSaveAsStatus.SavedAs, result.Status);
		Assert.IsNotNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationStatus.Synchronized, result.Views.Status);
		Assert.AreEqual(0, result.Views.ViewIds.Count);
		Assert.AreEqual(Path.GetFullPath(destinationPath), result.Snapshot!.DocumentId);
		Assert.AreEqual(result.Snapshot.DocumentId, fixture.View.DocumentId);
		Assert.IsTrue(File.Exists(destinationPath));

		// The view is tracked under the destination identity, so a later mutation of the retargeted
		// document still reaches it.
		int refreshBefore = fixture.View.RefreshCount;
		WorkspaceDocumentManagerMutationResult replaced = await fixture.Manager.ReplaceAsync(new WorkspaceDocumentReplaceRequest(
			new(result.Snapshot.DocumentKey, result.Snapshot.DocumentId, result.Snapshot.Version),
			"after save-as",
			result.Snapshot.FileFormat));

		Assert.AreEqual(WorkspaceDocumentMutationStatus.Changed, replaced.Status);
		Assert.AreEqual(refreshBefore + 1, fixture.View.RefreshCount);
		Assert.AreEqual("after save-as", fixture.View.Text);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_ViewRefreshFailureReportsUnsynchronizedView()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		// Dirty the document and change the file externally to create a conflict.
		WorkspaceDocumentMutationResult edited = fixture.Store.Replace(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"edited",
			snapshot.FileFormat));
		WorkspaceDocumentSnapshot dirty = edited.Snapshot!;
		File.WriteAllText(fixture.DocumentPath, "external");

		WorkspaceDocumentManagerReloadResult conflict = await fixture.Manager.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(dirty.DocumentKey, dirty.DocumentId, dirty.Version)));
		Assert.AreEqual(WorkspaceDocumentReloadStatus.ExternalFileConflict, conflict.Status);

		fixture.View.FailRefresh = true;

		WorkspaceDocumentManagerConflictResolutionResult result = await fixture.Manager.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(dirty.DocumentKey, dirty.DocumentId, dirty.Version),
				conflict.StoreResult!.ObservedOnDiskStamp!.Value,
				WorkspaceDocumentConflictResolutionChoice.UseDisk));

		Assert.AreEqual(WorkspaceDocumentConflictResolutionStatus.ResolvedWithDisk, result.Status);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationStatus.Unsynchronized, result.Views.Status);
		CollectionAssert.Contains(result.Views.ViewIds.ToArray(), fixture.View.ViewId);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UseLogicalCommitsAndKeepsViewSynchronized()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		// Dirty the document and change the file externally to create a conflict.
		WorkspaceDocumentMutationResult edited = fixture.Store.Replace(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"edited",
			snapshot.FileFormat));
		WorkspaceDocumentSnapshot dirty = edited.Snapshot!;
		File.WriteAllText(fixture.DocumentPath, "external");

		WorkspaceDocumentManagerReloadResult conflict = await fixture.Manager.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(dirty.DocumentKey, dirty.DocumentId, dirty.Version)));
		Assert.AreEqual(WorkspaceDocumentReloadStatus.ExternalFileConflict, conflict.Status);

		WorkspaceDocumentManagerConflictResolutionResult result = await fixture.Manager.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(dirty.DocumentKey, dirty.DocumentId, dirty.Version),
				conflict.StoreResult!.ObservedOnDiskStamp!.Value,
				WorkspaceDocumentConflictResolutionChoice.UseLogical));

		Assert.AreEqual(WorkspaceDocumentConflictResolutionStatus.ResolvedWithLogical, result.Status);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationStatus.Synchronized, result.Views.Status);
		Assert.IsFalse(result.Snapshot!.IsDirty);
		Assert.AreEqual("edited", File.ReadAllText(fixture.DocumentPath));
		Assert.AreEqual("edited", fixture.View.Text);
	}
}

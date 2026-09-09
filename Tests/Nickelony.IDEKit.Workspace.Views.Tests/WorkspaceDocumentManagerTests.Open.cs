using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

public sealed partial class WorkspaceDocumentManagerTests
{
	[TestMethod]
	public async Task OpenWithView_UsesHostCallbackForViewAccess()
	{
		await using var fixture = new ManagerFixture();

		await fixture.OpenViewAsync();

		Assert.IsTrue(fixture.HostInvocationCount > 0, "View access must be dispatched through the host callback.");
		Assert.IsTrue(fixture.View.ReceivedSnapshotOnHost);
		Assert.AreEqual("initial", fixture.View.Text);
	}

	[TestMethod]
	public async Task OpenWithoutView_ReturnsManagerResultWithSnapshot()
	{
		await using var fixture = new ManagerFixture();

		WorkspaceDocumentManagerOpenResult opened = await fixture.Manager.OpenAsync(fixture.DocumentPath, s_openOptions);

		Assert.AreEqual(WorkspaceDocumentManagerOpenStatus.Opened, opened.Status);
		Assert.IsNotNull(opened.Snapshot);
		Assert.AreEqual("initial", opened.Snapshot.Content);
		Assert.IsNull(opened.Failure);

		// A second open reports the store's already-open state through the same result family.
		WorkspaceDocumentManagerOpenResult reopened = await fixture.Manager.OpenAsync(fixture.DocumentPath, s_openOptions);

		Assert.AreEqual(WorkspaceDocumentManagerOpenStatus.AlreadyOpen, reopened.Status);
		Assert.IsNotNull(reopened.Snapshot);
	}

	[TestMethod]
	public async Task OpenWithoutView_InvalidPath_ReturnsManagerFailureStatus()
	{
		await using var fixture = new ManagerFixture();

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenAsync(null, s_openOptions);

		Assert.AreEqual(WorkspaceDocumentManagerOpenStatus.InvalidPath, result.Status);
		Assert.IsNull(result.Snapshot);
	}

	[TestMethod]
	public async Task UnregisterOpenView_NullViewIsAnArgumentError()
	{
		await using var fixture = new ManagerFixture();

		Assert.ThrowsExactly<ArgumentNullException>(() => fixture.Manager.UnregisterOpenView(null!));
	}

	[TestMethod]
	public async Task OpenWithView_NullViewThrowsSynchronously()
	{
		await using var fixture = new ManagerFixture();

		Assert.ThrowsExactly<ArgumentNullException>(() => fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			null!));
	}

	[TestMethod]
	public async Task OpenWithView_InvalidPathAndPendingEditsAreRejectedBeforeOpening()
	{
		await using var fixture = new ManagerFixture();

		WorkspaceDocumentManagerOpenResult invalidPath = await fixture.Manager.OpenWithViewAsync(
			"   ",
			s_openOptions,
			fixture.View);
		fixture.View.HasPendingEdits = true;
		WorkspaceDocumentManagerOpenResult conflict = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);

		Assert.AreEqual(WorkspaceDocumentManagerOpenStatus.InvalidPath, invalidPath.Status);
		Assert.IsNull(invalidPath.Snapshot);
		Assert.AreEqual(WorkspaceDocumentManagerOpenStatus.ViewUnavailable, conflict.Status);
		Assert.IsNull(conflict.Snapshot);
		Assert.AreEqual(0, fixture.View.CloseCount);
	}

	[TestMethod]
	public async Task OpenWithView_SameRegisteredInstanceReportsAlreadyOpen()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot opened = await fixture.OpenViewAsync();

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);

		// A view that is already registered is a no-op: the view is not attached a second time, and
		// the result carries the snapshot of the document the view is attached to.
		Assert.AreEqual(WorkspaceDocumentManagerOpenStatus.AlreadyOpen, result.Status);
		Assert.IsNotNull(result.Snapshot);
		Assert.AreEqual(opened.DocumentId, result.Snapshot.DocumentId);
		Assert.AreEqual("initial", result.Snapshot.Content);
	}

	[TestMethod]
	public async Task OpenWithView_DifferentViewWithDuplicateViewIdReportsViewInUse()
	{
		await using var fixture = new ManagerFixture();
		await fixture.OpenViewAsync();
		var duplicate = new TestView(fixture, fixture.View.ViewId);

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			duplicate);

		// A different instance that duplicates a registered view id cannot be attached.
		Assert.AreEqual(WorkspaceDocumentManagerOpenStatus.ViewInUse, result.Status);
		Assert.IsNull(result.Snapshot);
		Assert.AreEqual(0, duplicate.CloseCount);
	}

	[TestMethod]
	public async Task OpenWithView_ViewThatReportsAlreadyOpenIsRejectedAsViewInUse()
	{
		await using var fixture = new ManagerFixture();
		fixture.View.OpenStatus = WorkspaceDocumentViewOpenStatus.AlreadyOpen;

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);

		// The document was loaded, but the view reports that it is attached elsewhere; the snapshot
		// is carried so the caller can decide what to do with the loaded content.
		Assert.AreEqual(WorkspaceDocumentManagerOpenStatus.ViewInUse, result.Status);
		Assert.IsNotNull(result.Snapshot);
	}

	[TestMethod]
	public async Task OpenWithView_MissingPathWithoutCreateIfMissingReportsNotFound()
	{
		await using var fixture = new ManagerFixture();
		string missingPath = Path.Combine(fixture.DirectoryPath, "missing.txt");
		WorkspaceDocumentOpenOptions options = new(
			TextEncodingKind.Utf8,
			new TextFileFormat(TextEncodingKind.Utf8, false, TextNewlineStyle.Lf),
			CreateIfMissing: false);

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenWithViewAsync(
			missingPath,
			options,
			fixture.View);

		Assert.AreEqual(WorkspaceDocumentManagerOpenStatus.NotFound, result.Status);
		Assert.IsNull(result.Snapshot);
	}

	[TestMethod]
	public async Task OpenWithView_UnexpectedViewExceptionReportsOpenFailedWithoutSnapshot()
	{
		await using var fixture = new ManagerFixture();
		fixture.View.ThrowOnOpen = true;

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);

		// An unexpected exception on the open path is an open failure, unlike a view that reports a
		// rejection status.
		Assert.AreEqual(WorkspaceDocumentManagerOpenStatus.OpenFailed, result.Status);
		Assert.IsNull(result.Snapshot);
		Assert.AreEqual(WorkspaceOperationFailureCodes.OpenFailed, result.Failure!.Code);
	}

	[TestMethod]
	public async Task OpenWithView_LoadFailurePropagatesFailureCode()
	{
		await using var fixture = new ManagerFixture();
		string invalidPath = Path.Combine(fixture.DirectoryPath, "invalid.lua");
		File.WriteAllBytes(invalidPath, [0xC3, 0x28]);

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenWithViewAsync(
			invalidPath,
			s_openOptions,
			fixture.View);

		Assert.AreEqual(WorkspaceDocumentManagerOpenStatus.LoadFailed, result.Status);
		Assert.AreEqual(WorkspaceOperationFailureCodes.InvalidEncoding, result.Failure!.Code);
		Assert.IsNull(result.Snapshot);
	}

	[TestMethod]
	public async Task OpenWithView_CanceledTokenReportsCanceled()
	{
		await using var fixture = new ManagerFixture();
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View,
			cancellation.Token);

		Assert.AreEqual(WorkspaceDocumentManagerOpenStatus.Canceled, result.Status);
		Assert.IsNull(result.Snapshot);
	}

	[TestMethod]
	public async Task OpenWithView_ConsumesHostCallbackResultSynchronously()
	{
		await using var fixture = new ManagerFixture();
		fixture.View.OpenStatus = WorkspaceDocumentViewOpenStatus.Unavailable;
		fixture.View.OpenFailure = new WorkspaceOperationFailure("AttachRejected", "Attach rejected.");

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);

		// The manager consumes the attach result the host callback produces before the callback
		// returns; an asynchronous dispatch would leave it unset and surface a different failure.
		Assert.AreEqual(WorkspaceDocumentManagerOpenStatus.ViewRejected, result.Status);
		Assert.IsNotNull(result.Snapshot);
		Assert.AreEqual("Attach rejected.", result.Failure!.Message);
	}

	[TestMethod]
	public async Task OpenWithView_ConcurrentSameInstanceReportsOneOpenAndOneAlreadyOpenWithSnapshot()
	{
		await using var fixture = new ManagerFixture();

		// Both calls dispatch inline until the store read suspends, so the second call registers after
		// the first and resolves through one of the two already-registered paths.
		Task<WorkspaceDocumentManagerOpenResult> first = fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);
		Task<WorkspaceDocumentManagerOpenResult> second = fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);

		WorkspaceDocumentManagerOpenResult[] results = await Task.WhenAll(first, second);
		WorkspaceDocumentManagerOpenResult[] alreadyOpen = [.. results.Where(result => result.Status == WorkspaceDocumentManagerOpenStatus.AlreadyOpen)];

		Assert.AreEqual(1, results.Count(result => result.Status == WorkspaceDocumentManagerOpenStatus.Opened));
		Assert.AreEqual(1, alreadyOpen.Length);
		Assert.IsNotNull(alreadyOpen[0].Snapshot);
		Assert.AreEqual("initial", alreadyOpen[0].Snapshot!.Content);
	}

	[TestMethod]
	public async Task OpenWithView_FailingEventSubscriptionRollsBackRegistration()
	{
		await using var fixture = new ManagerFixture();
		fixture.View.ThrowOnSubscribe = true;

		WorkspaceDocumentManagerOpenResult failed = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);

		Assert.AreEqual(WorkspaceDocumentManagerOpenStatus.OpenFailed, failed.Status);
		Assert.AreEqual(1, fixture.View.CloseCount);

		// The failed registration is rolled back, so attaching the same view succeeds once the
		// subscription works instead of reporting the view as already open or in use.
		fixture.View.ThrowOnSubscribe = false;
		WorkspaceDocumentManagerOpenResult opened = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);

		Assert.AreEqual(WorkspaceDocumentManagerOpenStatus.Opened, opened.Status);
	}

	[TestMethod]
	public async Task OpenWithView_ViewWhoseIdGetterThrowsIsUnavailable()
	{
		await using var fixture = new ManagerFixture();
		fixture.View.ViewIdProvider = () => throw new InvalidOperationException("The view cannot report its id.");

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);

		Assert.AreEqual(WorkspaceDocumentManagerOpenStatus.ViewUnavailable, result.Status);
		Assert.AreEqual(0, fixture.View.CloseCount);
	}

	[TestMethod]
	public async Task OpenWithView_ViewThatReportsNoIdIsUnavailable()
	{
		await using var fixture = new ManagerFixture();
		fixture.View.ViewIdProvider = static () => string.Empty;

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);

		Assert.AreEqual(WorkspaceDocumentManagerOpenStatus.ViewUnavailable, result.Status);
		Assert.AreEqual(0, fixture.View.CloseCount);
	}

	[TestMethod]
	public async Task OpenWithView_ViewThatLosesItsIdDuringOpenIsRejectedAndClosed()
	{
		await using var fixture = new ManagerFixture();
		int viewIdReads = 0;
		fixture.View.ViewIdProvider = () => ++viewIdReads == 1 ? "live-view" : string.Empty;

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);

		// The id was present when the view was validated but gone after the attach; the view cannot be
		// indexed for duplicate detection, so it is rejected and closed.
		Assert.AreEqual(WorkspaceDocumentManagerOpenStatus.ViewRejected, result.Status);
		Assert.AreEqual(1, fixture.View.CloseCount);
		Assert.IsNotNull(result.Failure);
	}

	[TestMethod]
	public async Task UnregisterOpenView_RemovesBlockingStateAndIsIdempotent()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		fixture.View.HasPendingEdits = true;

		WorkspaceDocumentManagerReloadResult blocked = await fixture.Manager.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version)));

		fixture.Manager.UnregisterOpenView(fixture.View);
		fixture.Manager.UnregisterOpenView(fixture.View);

		WorkspaceDocumentManagerReloadResult reloaded = await fixture.Manager.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version)));

		Assert.IsNull(blocked.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationStatus.Blocked, blocked.Views.Status);
		Assert.AreEqual(WorkspaceDocumentReloadStatus.Unchanged, reloaded.Status);
	}
}

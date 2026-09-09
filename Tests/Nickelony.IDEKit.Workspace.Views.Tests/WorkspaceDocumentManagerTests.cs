using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Pathing;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;
using Nickelony.IDEKit.Workspace.Tests;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

[TestClass]
public sealed partial class WorkspaceDocumentManagerTests
{
	private static readonly WorkspaceDocumentOpenOptions s_openOptions = new(
		TextEncodingKind.Utf8,
		new TextFileFormat(TextEncodingKind.Utf8, false, TextNewlineStyle.Lf));

	private sealed class TestView(ManagerFixture fixture, string viewId = "test-view")
		: IWorkspaceDocumentEditView, IWorkspaceDocumentDeleteGuardView
	{
		private string _text = string.Empty;
		private WorkspaceDocumentKey? _documentKey;
		private EventHandler<WorkspaceDocumentViewApplyRequestedEventArgs>? _applyRequested;
		private int _documentKeyReads;

		public event EventHandler<WorkspaceDocumentViewApplyRequestedEventArgs>? ApplyRequested
		{
			add
			{
				if (ThrowOnSubscribe)
					throw new InvalidOperationException("The view cannot subscribe.");

				_applyRequested += value;
			}
			remove => _applyRequested -= value;
		}

		public string ViewId => ViewIdProvider?.Invoke() ?? viewId;

		/// <summary>
		/// Gets or sets a provider for the view id; the default is the id supplied to the constructor.
		/// </summary>
		public Func<string>? ViewIdProvider { get; set; }

		public string? DocumentId { get; private set; }

		/// <summary>
		/// Gets or sets the number of successful document-key reads after which the getter throws; a
		/// negative value (the default) never throws.
		/// </summary>
		public int ThrowAfterDocumentKeyReads { get; set; } = -1;

		public bool ThrowOnSubscribe { get; set; }

		public WorkspaceDocumentKey? DocumentKey
		{
			get
			{
				if (ThrowAfterDocumentKeyReads >= 0 && _documentKeyReads++ >= ThrowAfterDocumentKeyReads)
					throw new InvalidOperationException("The view cannot report its document key.");

				return _documentKey;
			}
			private set => _documentKey = value;
		}

		public bool HasPendingEdits { get; set; }

		public bool HasConflict { get; set; }

		public bool FailRefresh { get; set; }

		/// <summary>
		/// Gets or sets the refresh status returned when <see cref="FailRefresh"/> is not set; a
		/// non-refreshed status models a view that keeps its content without throwing.
		/// </summary>
		public WorkspaceDocumentViewRefreshStatus RefreshStatus { get; set; } = WorkspaceDocumentViewRefreshStatus.Refreshed;

		public bool FailIdentityAck { get; set; }

		public bool BlockDeleteGuard { get; set; }

		public bool FailDeleteGuardRelease { get; set; }

		public bool ThrowOnDeleteGuardRelease { get; set; }

		/// <summary>
		/// Runs when a delete guard is entered, after the manager captured its view set but before the
		/// store delete starts; tests use it to attach competing views inside that window.
		/// </summary>
		public Action? OnDeleteGuardEntered { get; set; }

		public WorkspaceDocumentViewOpenStatus OpenStatus { get; set; } = WorkspaceDocumentViewOpenStatus.Opened;

		public WorkspaceOperationFailure? OpenFailure { get; set; }

		public bool ThrowOnOpen { get; set; }

		public bool ThrowOnClose { get; set; }

		public int CloseCount { get; private set; }

		public int RefreshCount { get; private set; }

		public int DeleteGuardReleaseCount { get; private set; }

		public bool ReceivedSnapshotOnHost { get; private set; }

		public string Text => _text;

		public List<TextEditOperation> AppliedOperations { get; } = [];

		public void Apply(PreparedTextEdits edits) => AppliedOperations.AddRange(edits.Operations);

		public void SetText(string text) => _text = text;

		public WorkspaceDocumentViewOpenResult Open(WorkspaceDocumentSnapshot snapshot)
		{
			if (ThrowOnOpen)
				throw new InvalidOperationException("The view cannot attach.");

			ReceivedSnapshotOnHost = fixture.IsRunningOnHost;
			AdoptSnapshot(snapshot);
			return new WorkspaceDocumentViewOpenResult(OpenStatus, OpenFailure);
		}

		public WorkspaceDocumentViewRefreshResult Refresh(WorkspaceDocumentSnapshot snapshot)
		{
			if (FailRefresh)
				throw new InvalidOperationException("The view cannot refresh the document.");

			RefreshCount++;
			if (RefreshStatus != WorkspaceDocumentViewRefreshStatus.Refreshed)
			{
				// A view that cannot apply the snapshot reports a non-refreshed status without throwing;
				// it keeps its current content.
				return new WorkspaceDocumentViewRefreshResult(
					RefreshStatus,
					new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.ViewRefreshFailed, "The view kept its current content."));
			}

			AdoptSnapshot(snapshot);
			return new WorkspaceDocumentViewRefreshResult(WorkspaceDocumentViewRefreshStatus.Refreshed);
		}

		public WorkspaceDocumentViewIdentityResult AcknowledgeIdentity(WorkspaceDocumentIdentityChange change)
		{
			if (FailIdentityAck)
				return new WorkspaceDocumentViewIdentityResult(
					WorkspaceDocumentViewIdentityStatus.Failed,
					new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.ViewIdentityUpdateFailed, "The view rejected the identity change."));

			AdoptSnapshot(change.Snapshot);
			return new WorkspaceDocumentViewIdentityResult(WorkspaceDocumentViewIdentityStatus.Updated);
		}

		public WorkspaceDocumentViewDeleteGuardResult ApplyDeleteGuard()
		{
			OnDeleteGuardEntered?.Invoke();
			return new(BlockDeleteGuard
				? WorkspaceDocumentViewDeleteGuardStatus.Failed
				: WorkspaceDocumentViewDeleteGuardStatus.Applied);
		}

		public WorkspaceDocumentViewDeleteGuardResult ReleaseDeleteGuard()
		{
			DeleteGuardReleaseCount++;
			if (ThrowOnDeleteGuardRelease)
				throw new InvalidOperationException("Guard release failed.");

			return new(FailDeleteGuardRelease
				? WorkspaceDocumentViewDeleteGuardStatus.Failed
				: WorkspaceDocumentViewDeleteGuardStatus.Applied);
		}

		public WorkspaceDocumentViewRefreshResult AcknowledgeApply(WorkspaceDocumentMutationResult result)
		{
			if (result.Snapshot is null)
				return new WorkspaceDocumentViewRefreshResult(WorkspaceDocumentViewRefreshStatus.Refreshed);

			WorkspaceDocumentViewRefreshResult refresh = Refresh(result.Snapshot);

			// The acknowledgment of an applied mutation is the view's own published content becoming
			// the document state, so successfully applied acknowledgments clear the pending edits that
			// would otherwise keep blocking disk operations.
			if (refresh.Status == WorkspaceDocumentViewRefreshStatus.Refreshed)
				HasPendingEdits = false;

			return refresh;
		}

		public void Close()
		{
			// Closing detaches the view from its document, matching the interface contract.
			CloseCount++;
			if (ThrowOnClose)
				throw new InvalidOperationException("The view cannot close.");

			DocumentId = null;
			DocumentKey = null;
		}

		public void RaiseApply(WorkspaceDocumentReplaceRequest request)
			=> _applyRequested?.Invoke(this, new WorkspaceDocumentViewApplyRequestedEventArgs(request));

		private void AdoptSnapshot(WorkspaceDocumentSnapshot snapshot)
		{
			DocumentId = snapshot.DocumentId;
			DocumentKey = snapshot.DocumentKey;
			_text = snapshot.Content;
		}
	}

	[TestMethod]
	public async Task AsyncOperations_NullRequest_ThrowSynchronously()
	{
		await using var fixture = new ManagerFixture();
		var operations = new (string Name, Action Invoke)[]
		{
			("RenameAsync", () => _ = fixture.Manager.RenameAsync(null!)),
			("SaveAsAsync", () => _ = fixture.Manager.SaveAsAsync(null!)),
			("DeleteAsync", () => _ = fixture.Manager.DeleteAsync(null!)),
			("RenameDirectoryAsync", () => _ = fixture.Manager.RenameDirectoryAsync(null!)),
			("DeleteDirectoryAsync", () => _ = fixture.Manager.DeleteDirectoryAsync(null!)),
			("CommitAsync", () => _ = fixture.Manager.CommitAsync(null!)),
			("ReloadAsync", () => _ = fixture.Manager.ReloadAsync(null!)),
			("ResolveExternalConflictAsync", () => _ = fixture.Manager.ResolveExternalConflictAsync(null!)),
		};

		// A null request is an argument error and must surface as an immediate exception instead of a
		// faulted task, matching the manager's synchronous members.
		foreach ((string name, Action invoke) in operations)
			Assert.ThrowsExactly<ArgumentNullException>(invoke, $"{name} must throw ArgumentNullException synchronously.");
	}

	[TestMethod]
	public async Task StopAsync_ClosesRemainingViewsWhenAnEarlierViewThrowsOnClose()
	{
		await using var fixture = new ManagerFixture();
		await fixture.OpenViewAsync();
		var secondView = new TestView(fixture, "second-view");
		WorkspaceDocumentManagerOpenResult opened = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			secondView);
		Assert.AreEqual(WorkspaceDocumentManagerOpenStatus.Opened, opened.Status);
		fixture.View.ThrowOnClose = true;

		await fixture.Manager.StopAsync();

		// A throwing close must not prevent the remaining views from being released or fault the
		// memoized stop task for later callers.
		Assert.AreEqual(1, secondView.CloseCount);
		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => fixture.Manager.OpenAsync(
			fixture.DocumentPath,
			s_openOptions));
	}

	[TestMethod]
	public async Task Constructor_ExplicitPathComparisonOverrideDrivesViewMembership()
	{
		// The store treats the two spellings as different documents (case-sensitive policy), while the
		// manager is told that document identities are case-insensitive: the view sets follow the
		// override, so the case-variant view is a peer of the other document.
		await using var fixture = new ManagerFixture(
			pathComparison: LocalPathComparisonPolicy.CaseSensitive,
			managerPathComparison: LocalPathComparisonPolicy.CaseInsensitive);
		WorkspaceDocumentSnapshot first = await fixture.OpenViewAsync();
		var secondView = new TestView(fixture, "second-view");
		string caseVariantPath = Path.Combine(fixture.DirectoryPath, "DOCUMENT.txt");

		WorkspaceDocumentManagerOpenResult second = await fixture.Manager.OpenWithViewAsync(
			caseVariantPath,
			s_openOptions,
			secondView);
		Assert.AreEqual(WorkspaceDocumentManagerOpenStatus.Opened, second.Status);
		Assert.AreNotEqual(first.DocumentId, second.Snapshot!.DocumentId);

		WorkspaceDocumentManagerMutationResult replaced = await fixture.Manager.ReplaceAsync(new WorkspaceDocumentReplaceRequest(
			new(second.Snapshot.DocumentKey, second.Snapshot.DocumentId, second.Snapshot.Version),
			"edited",
			second.Snapshot.FileFormat));

		Assert.AreEqual(WorkspaceDocumentMutationStatus.Changed, replaced.Status);
		Assert.AreEqual(1, fixture.View.RefreshCount);
		Assert.AreEqual("edited", fixture.View.Text);
	}

	[TestMethod]
	public async Task StopAsync_IsIdempotentClosesViewsAndRejectsNewOperations()
	{
		await using var fixture = new ManagerFixture();
		await fixture.OpenViewAsync();

		Task first = fixture.Manager.StopAsync();
		Task second = fixture.Manager.StopAsync();

		Assert.AreSame(first, second);
		await first;

		Assert.AreEqual(1, fixture.View.CloseCount);
		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => fixture.Manager.OpenAsync(
			fixture.DocumentPath,
			s_openOptions));
	}

	[TestMethod]
	public async Task Documents_ExposesTrackedSnapshotsFromStore()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		IReadOnlyList<WorkspaceDocumentSnapshot> snapshots = fixture.Manager.Documents.GetSnapshotsUnderDirectory(fixture.DirectoryPath);

		Assert.AreEqual(1, snapshots.Count);
		Assert.AreEqual(snapshot.DocumentId, snapshots[0].DocumentId);
		Assert.AreEqual(snapshot.Version, snapshots[0].Version);
	}

	[TestMethod]
	public async Task Constructor_InheritsStorePathComparisonWhenNoOverrideIsSupplied()
	{
		await using var fixture = new ManagerFixture(LocalPathComparisonPolicy.CaseSensitive);
		WorkspaceDocumentSnapshot first = await fixture.OpenViewAsync();
		string caseVariantPath = Path.Combine(fixture.DirectoryPath, "DOCUMENT.txt");
		var secondView = new TestView(fixture, "second-view");

		WorkspaceDocumentManagerOpenResult second = await fixture.Manager.OpenWithViewAsync(
			caseVariantPath,
			s_openOptions,
			secondView);
		Assert.AreEqual(WorkspaceDocumentManagerOpenStatus.Opened, second.Status);
		Assert.AreNotEqual(first.DocumentId, second.Snapshot!.DocumentId);

		// A mutation of the first document must not refresh the case-variant view: the manager uses the
		// case-sensitive policy of the store instead of the operating-system default.
		WorkspaceDocumentManagerMutationResult replaced = await fixture.Manager.ReplaceAsync(new WorkspaceDocumentReplaceRequest(
			new(first.DocumentKey, first.DocumentId, first.Version),
			"edited",
			first.FileFormat));

		Assert.AreEqual(WorkspaceDocumentMutationStatus.Changed, replaced.Status);
		Assert.AreEqual(1, fixture.View.RefreshCount);
		Assert.AreEqual(0, secondView.RefreshCount);
		Assert.AreEqual(second.Snapshot.DocumentId, secondView.DocumentId);
	}

	[TestMethod]
	public async Task Rename_IdentitySynchronizationRekeysViewForLaterOperations()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		string destinationPath = Path.Combine(fixture.DirectoryPath, "renamed.txt");

		WorkspaceDocumentManagerRenameResult renamed = await fixture.Manager.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentRenameStatus.Renamed, renamed.Status);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationStatus.Synchronized, renamed.Views.Status);
		Assert.AreEqual(renamed.Snapshot!.DocumentId, fixture.View.DocumentId);

		// The view is now tracked under the destination id and its new version, so a later mutation
		// must still find it as a peer instead of leaving it bound to the old identity.
		int refreshBefore = fixture.View.RefreshCount;
		WorkspaceDocumentManagerMutationResult replaced = await fixture.Manager.ReplaceAsync(new WorkspaceDocumentReplaceRequest(
			new(renamed.Snapshot.DocumentKey, renamed.Snapshot.DocumentId, renamed.Snapshot.Version),
			"after rename",
			renamed.Snapshot.FileFormat));

		Assert.AreEqual(WorkspaceDocumentMutationStatus.Changed, replaced.Status);
		Assert.AreEqual(refreshBefore + 1, fixture.View.RefreshCount);
		Assert.AreEqual("after rename", fixture.View.Text);
	}

	[TestMethod]
	public async Task DirectoryRename_SucceedsAndRekeysSynchronizedView()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		using var destination = new TestTempDirectory();
		string destinationPath = Path.Combine(destination.Path, "moved");

		WorkspaceDocumentManagerDirectoryRenameResult result = await fixture.Manager.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(fixture.DirectoryPath, destinationPath));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameStatus.Renamed, result.Status);
		Assert.IsNotNull(result.StoreResult);
		Assert.AreEqual(1, result.StoreResult.Snapshots.Count);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationStatus.Synchronized, result.Views.Status);
		Assert.AreEqual(0, result.Views.ViewIds.Count);

		string movedDocumentPath = Path.Combine(destinationPath, "document.txt");
		Assert.AreEqual(Path.GetFullPath(movedDocumentPath), result.Snapshots[0].DocumentId);
		Assert.AreEqual(result.Snapshots[0].DocumentId, fixture.View.DocumentId);
		Assert.IsTrue(File.Exists(movedDocumentPath));
		Assert.IsFalse(File.Exists(fixture.DocumentPath));

		// The view is tracked under the moved document's new identity, so a later mutation still
		// reaches it.
		int refreshBefore = fixture.View.RefreshCount;
		WorkspaceDocumentManagerMutationResult replaced = await fixture.Manager.ReplaceAsync(new WorkspaceDocumentReplaceRequest(
			new(result.Snapshots[0].DocumentKey, result.Snapshots[0].DocumentId, result.Snapshots[0].Version),
			"after move",
			result.Snapshots[0].FileFormat));

		Assert.AreEqual(WorkspaceDocumentMutationStatus.Changed, replaced.Status);
		Assert.AreEqual(refreshBefore + 1, fixture.View.RefreshCount);
		Assert.AreEqual("after move", fixture.View.Text);
	}

	[TestMethod]
	public async Task StopAsync_WaitsForActiveOperationToFinish()
	{
		var fileSystem = new BlockingDeleteFileSystem(new LocalWorkspaceFileSystem());
		await using var fixture = new ManagerFixture(fileSystem: fileSystem);
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		Task<WorkspaceDocumentManagerDeleteResult> delete = fixture.Manager.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));
		await fileSystem.DeleteEntered.WaitAsync(TimeSpan.FromSeconds(10));

		// The delete holds the active-operation registration, so the stop must wait for it instead of
		// detaching views while the operation is still running.
		Task stop = fixture.Manager.StopAsync();
		Assert.IsFalse(stop.IsCompleted);

		fileSystem.ReleaseDelete();
		WorkspaceDocumentManagerDeleteResult result = await delete;
		await stop;

		Assert.AreEqual(WorkspaceDocumentDeleteStatus.Deleted, result.Status);
		Assert.AreEqual(1, fixture.View.CloseCount);
	}

	// Holds file deletes until released so tests can keep a delete operation in flight while they
	// inspect the manager; the decorator base forwards every other member to the real file system.
	private sealed class BlockingDeleteFileSystem(LocalWorkspaceFileSystem inner) : WorkspaceFileSystemDecorator(inner)
	{
		private readonly TaskCompletionSource _deleteGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource _deleteEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task DeleteEntered => _deleteEntered.Task;

		public void ReleaseDelete() => _deleteGate.TrySetResult();

		public override async Task<WorkspaceFileDeleteResult> DeleteAsync(
			string path,
			FileStamp expectedStamp,
			CancellationToken cancellationToken)
		{
			_deleteEntered.TrySetResult();
			await _deleteGate.Task.ConfigureAwait(false);
			return await Inner.DeleteAsync(path, expectedStamp, cancellationToken).ConfigureAwait(false);
		}
	}

	// Owns a temporary directory with one document file, the store, and a manager that runs host
	// dispatch inline while tracking its nesting depth.
	private sealed class ManagerFixture : IAsyncDisposable
	{
		private readonly TestTempDirectory _tempDirectory;
		private int _hostDepth;

		public ManagerFixture(
			LocalPathComparisonPolicy? pathComparison = null,
			IWorkspaceFileSystem? fileSystem = null,
			LocalPathComparisonPolicy? managerPathComparison = null)
		{
			_tempDirectory = new TestTempDirectory();
			DirectoryPath = _tempDirectory.Path;
			DocumentPath = Path.Combine(DirectoryPath, "document.txt");
			File.WriteAllText(DocumentPath, "initial");
			Store = new WorkspaceDocumentStore(fileSystem ?? new LocalWorkspaceFileSystem(), pathComparison);
			Manager = new WorkspaceDocumentManager(Store, RunOnHost, managerPathComparison);
			View = new TestView(this);
		}

		public string DirectoryPath { get; }

		public string DocumentPath { get; }

		public WorkspaceDocumentStore Store { get; }

		public WorkspaceDocumentManager Manager { get; }

		public TestView View { get; }

		public int HostInvocationCount { get; private set; }

		public bool IsRunningOnHost => _hostDepth > 0;

		public async Task<WorkspaceDocumentSnapshot> OpenViewAsync()
		{
			WorkspaceDocumentManagerOpenResult result = await Manager.OpenWithViewAsync(
				DocumentPath,
				s_openOptions,
				View);

			Assert.AreEqual(WorkspaceDocumentManagerOpenStatus.Opened, result.Status);
			return result.Snapshot!;
		}

		// Discard acknowledges the views of a clean document, which returns NoChange without
		// touching the file. The failing refresh records the view as unsynchronized.
		public async Task MarkViewUnsynchronizedAsync(WorkspaceDocumentSnapshot snapshot)
		{
			View.FailRefresh = true;
			WorkspaceDocumentManagerMutationResult discard = await Manager.DiscardAsync(
				new WorkspaceDocumentDiscardRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version)));
			View.FailRefresh = false;

			Assert.AreEqual(WorkspaceDocumentMutationStatus.NoChange, discard.Status);
		}

		public async ValueTask DisposeAsync()
		{
			await Manager.DisposeAsync();
			await Store.DisposeAsync();
			_tempDirectory.Dispose();
		}

		private Task RunOnHost(Action action)
		{
			HostInvocationCount++;
			_hostDepth++;
			try
			{
				action();
			}
			finally
			{
				_hostDepth--;
			}

			return Task.CompletedTask;
		}
	}
}

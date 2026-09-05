using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

/// <summary>
/// Tests <see cref="WorkspaceDocumentManager.OpenWithViewAsync"/> view opening and host callback
/// dispatch.
/// </summary>
/// <remarks>
/// A temporary file supplies the document content, and a minimal test view records the snapshot it
/// receives for the final assertion.
/// </remarks>
[TestClass]
public sealed class WorkspaceDocumentManagerTests
{
	[TestMethod]
	public async Task OpenWithView_UsesHostCallbackForViewAccess()
	{
		string filePath = Path.Combine(Path.GetTempPath(), $"text-editor-view-{Guid.NewGuid():N}.txt");
		await File.WriteAllTextAsync(filePath, "initial");

		try
		{
			await using var store = new WorkspaceDocumentStore(new WorkspaceFileCodec());
			int hostInvocationCount = 0;
			await using var manager = new WorkspaceDocumentManager(
				store,
				action =>
				{
					hostInvocationCount++;
					action();
				});
			var view = new TestView();
			var options = new WorkspaceDocumentOpenOptions(
				TextEncodingKind.Utf8,
				new TextFileFormat(TextEncodingKind.Utf8, false, TextNewlineStyle.Lf));

			WorkspaceDocumentManagerOpenResult result = await manager.OpenWithViewAsync(
				filePath,
				options,
				view);

			Assert.AreEqual(WorkspaceDocumentManagerOpenStatus.Opened, result.Status);
			Assert.AreEqual(2, hostInvocationCount);
			Assert.AreEqual("initial", view.Text);
		}
		finally
		{
			File.Delete(filePath);
		}
	}

	private sealed class TestView : IWorkspaceDocumentView
	{
		private string _text = string.Empty;

		public event EventHandler<WorkspaceDocumentViewApplyRequestedEventArgs>? ApplyRequested;

		public string ViewId => "test-view";

		public string DocumentId { get; private set; } = string.Empty;

		public WorkspaceDocumentKey? DocumentKey { get; private set; }

		public bool HasPendingEdits => false;

		public bool HasConflict => false;

		public string Text => _text;

		public void Apply(IReadOnlyList<TextEditOperation> operations) { }

		public WorkspaceDocumentViewOpenResult Open(WorkspaceDocumentSnapshot snapshot)
		{
			DocumentId = snapshot.DocumentId;
			DocumentKey = snapshot.DocumentKey;
			_text = snapshot.Content;
			return new WorkspaceDocumentViewOpenResult(WorkspaceDocumentViewOpenStatus.Opened);
		}

		public WorkspaceDocumentViewRefreshResult Refresh(WorkspaceDocumentSnapshot snapshot)
		{
			DocumentId = snapshot.DocumentId;
			DocumentKey = snapshot.DocumentKey;
			_text = snapshot.Content;
			return new WorkspaceDocumentViewRefreshResult(WorkspaceDocumentViewRefreshStatus.Refreshed);
		}

		public WorkspaceDocumentViewRefreshResult DiscardPendingEdits(WorkspaceDocumentSnapshot snapshot)
			=> Refresh(snapshot);

		public WorkspaceDocumentViewIdentityResult AcknowledgeIdentity(WorkspaceDocumentIdentityChange change)
		{
			DocumentId = change.Snapshot.DocumentId;
			DocumentKey = change.Snapshot.DocumentKey;
			_text = change.Snapshot.Content;
			return new WorkspaceDocumentViewIdentityResult(WorkspaceDocumentViewIdentityStatus.Updated);
		}

		public WorkspaceDocumentViewDeleteGuardResult SetDeleteGuard(bool active)
			=> new(WorkspaceDocumentViewDeleteGuardStatus.Applied);

		public WorkspaceDocumentViewRefreshResult AcknowledgeApply(WorkspaceDocumentMutationResult result)
			=> result.Snapshot is null
				? new WorkspaceDocumentViewRefreshResult(WorkspaceDocumentViewRefreshStatus.Refreshed)
				: Refresh(result.Snapshot);

		public void Close() { }

		public void RaiseApply(WorkspaceDocumentReplaceRequest request)
			=> ApplyRequested?.Invoke(this, new WorkspaceDocumentViewApplyRequestedEventArgs(request));
	}
}

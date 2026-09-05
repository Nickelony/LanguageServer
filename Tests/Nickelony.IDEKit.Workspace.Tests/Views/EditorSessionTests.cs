using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Views;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>
/// Tests <see cref="EditorSession"/> disposal, identity, view ownership, and editor restoration
/// behavior.
/// </summary>
/// <remarks>
/// Callback counters and return values make view closing and previous-editor activation observable
/// without creating a real editor view.
/// </remarks>
[TestClass]
public sealed class EditorSessionTests
{
	private static readonly TextFileFormat s_fileFormat = new(
		TextEncodingKind.Utf8,
		false,
		TextNewlineStyle.Lf);

	[TestMethod]
	public void TransientAcquiredSession_DisposeClosesViewAndRestoresPrevious()
	{
		WorkspaceDocumentSnapshot snapshot = CreateSnapshot("script.lua", "canonical");
		int closeCalls = 0;
		int activateCalls = 0;

		var session = new EditorSession(
			snapshot,
			EditorSessionMode.Transient,
			acquiredView: true,
			closeView: () =>
			{
				closeCalls++;
				return true;
			},
			activateEditor: () => activateCalls++);

		Assert.IsTrue(session.IsActive);

		session.Dispose();
		session.Dispose();

		Assert.AreEqual(1, closeCalls);
		Assert.AreEqual(1, activateCalls);
		Assert.IsFalse(session.IsActive);
	}

	[TestMethod]
	public void PersistentAcquiredSession_DisposeDoesNotCloseViewButRestoresPrevious()
	{
		WorkspaceDocumentSnapshot snapshot = CreateSnapshot("script.lua", "canonical");
		int closeCalls = 0;
		int activateCalls = 0;

		var session = new EditorSession(
			snapshot,
			EditorSessionMode.Persistent,
			acquiredView: true,
			closeView: () =>
			{
				closeCalls++;
				return true;
			},
			activateEditor: () => activateCalls++);

		session.Dispose();

		Assert.AreEqual(0, closeCalls);
		Assert.AreEqual(1, activateCalls);
	}

	[TestMethod]
	public void NonAcquiredSession_DisposeDoesNotCloseViewButRestoresPrevious()
	{
		WorkspaceDocumentSnapshot snapshot = CreateSnapshot("script.lua", "canonical");
		int closeCalls = 0;
		int activateCalls = 0;

		var session = new EditorSession(
			snapshot,
			EditorSessionMode.Transient,
			acquiredView: false,
			closeView: () =>
			{
				closeCalls++;
				return true;
			},
			activateEditor: () => activateCalls++);

		session.Dispose();

		Assert.AreEqual(0, closeCalls);
		Assert.AreEqual(1, activateCalls);
	}

	[TestMethod]
	public void SessionExposesSnapshotIdentityAndMode()
	{
		WorkspaceDocumentSnapshot snapshot = CreateSnapshot("script.lua", "canonical");

		var session = new EditorSession(
			snapshot,
			EditorSessionMode.Transient,
			acquiredView: true,
			closeView: () => true,
			activateEditor: () => { });

		Assert.AreEqual(snapshot.DocumentKey, session.DocumentKey);
		Assert.AreEqual(snapshot.DocumentId, session.DocumentId);
		Assert.AreEqual(EditorSessionMode.Transient, session.Mode);
	}

	[TestMethod]
	public void CloseViewFailure_StillRestoresPreviousView()
	{
		WorkspaceDocumentSnapshot snapshot = CreateSnapshot("script.lua", "canonical");
		int activateCalls = 0;

		var session = new EditorSession(
			snapshot,
			EditorSessionMode.Transient,
			acquiredView: true,
			closeView: () => false,
			activateEditor: () => activateCalls++);

		session.Dispose();

		Assert.AreEqual(1, activateCalls);
	}

	private static WorkspaceDocumentSnapshot CreateSnapshot(string filePath, string content)
		=> new(
			new WorkspaceDocumentKey(Guid.NewGuid()),
			filePath,
			filePath,
			0,
			0,
			false,
			new StringTextSnapshot(content, filePath),
			s_fileFormat,
			FileStamp.Missing);
}

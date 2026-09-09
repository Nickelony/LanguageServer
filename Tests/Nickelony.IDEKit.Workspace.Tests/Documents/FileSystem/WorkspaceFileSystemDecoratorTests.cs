using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

[TestClass]
public sealed class WorkspaceFileSystemDecoratorTests
{
	[TestMethod]
	public void Constructor_NullInnerThrows()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new TrashFileSystem(null!));
	}

	[TestMethod]
	public async Task DefaultMembers_ForwardToTheInnerFileSystem()
	{
		var inner = new RecordingFileSystem();
		var decorator = new TrashFileSystem(inner);
		var temporaryFile = new WorkspaceTemporaryFile("temporary.tmp", 1, "hash");

		WorkspaceFileReadResult read = await decorator.ReadAsync("script.lua", CancellationToken.None);
		FileStamp stamp = await decorator.CaptureStampAsync("script.lua", CancellationToken.None);
		WorkspaceTemporaryFile written = await decorator.WriteTemporaryAsync(
			"folder",
			new byte[] { 1 },
			CancellationToken.None);
		WorkspaceFileReplacementResult replaced = await decorator.ReplaceFileAsync(
			temporaryFile,
			"script.lua",
			FileStamp.Missing,
			CancellationToken.None);
		WorkspaceFileMoveResult moved = await decorator.MoveAsync(
			"source.lua",
			"destination.lua",
			FileStamp.Missing,
			CancellationToken.None);
		WorkspaceFileMoveResult directoryMoved = await decorator.MoveDirectoryAsync(
			"source",
			"destination",
			CancellationToken.None);
		WorkspaceFileDeleteResult directoryDeleted = await decorator.DeleteDirectoryAsync(
			"folder",
			CancellationToken.None);
		await decorator.DeleteTemporaryAsync(temporaryFile);

		// Every member the decorator does not override forwards to the inner file system.
		Assert.AreEqual("content", read.Content);
		Assert.AreEqual(FileStamp.Missing, stamp);
		Assert.AreEqual("temporary.tmp", written.Path);
		Assert.AreEqual(WorkspaceFileReplacementStatus.Replaced, replaced.Status);
		Assert.AreEqual(WorkspaceFileMoveStatus.Moved, moved.Status);
		Assert.AreEqual(WorkspaceFileMoveStatus.Moved, directoryMoved.Status);
		Assert.AreEqual(WorkspaceFileDeleteStatus.Deleted, directoryDeleted.Status);
		Assert.AreEqual(1, inner.ReadCount);
		Assert.AreEqual(1, inner.CaptureCount);
		Assert.AreEqual(1, inner.WriteTemporaryCount);
		Assert.AreEqual(1, inner.ReplaceCount);
		Assert.AreEqual(1, inner.MoveCount);
		Assert.AreEqual(1, inner.MoveDirectoryCount);
		Assert.AreEqual(1, inner.DeleteDirectoryCount);
		Assert.AreEqual(1, inner.DeleteTemporaryCount);
	}

	[TestMethod]
	public async Task DefaultDeleteForwardsToTheInnerFileSystem()
	{
		var inner = new RecordingFileSystem();
		var decorator = new PassThroughFileSystem(inner);

		WorkspaceFileDeleteResult deleted = await decorator.DeleteAsync(
			"script.lua",
			FileStamp.Missing,
			CancellationToken.None);

		// A decorator without a delete override forwards the delete to the inner file system.
		Assert.AreEqual(WorkspaceFileDeleteStatus.Deleted, deleted.Status);
		Assert.AreEqual(1, inner.DeleteCount);
	}

	[TestMethod]
	public async Task OverriddenMember_UsesHostPolicyWhileOthersForward()
	{
		var inner = new RecordingFileSystem();
		var decorator = new TrashFileSystem(inner);

		WorkspaceFileDeleteResult deleted = await decorator.DeleteAsync(
			"script.lua",
			FileStamp.Missing,
			CancellationToken.None);
		WorkspaceFileReadResult read = await decorator.ReadAsync("script.lua", CancellationToken.None);

		// The overridden delete routes through the host policy while the remaining members keep
		// forwarding to the inner file system.
		Assert.AreEqual(WorkspaceFileDeleteStatus.Deleted, deleted.Status);
		CollectionAssert.AreEqual(new[] { "script.lua" }, decorator.TrashedPaths.ToArray());
		Assert.AreEqual(0, inner.DeleteCount);
		Assert.AreEqual("content", read.Content);
		Assert.AreEqual(1, inner.ReadCount);
	}

	private sealed class TrashFileSystem : WorkspaceFileSystemDecorator
	{
		public TrashFileSystem(IWorkspaceFileSystem inner)
			: base(inner)
		{ }

		public List<string> TrashedPaths { get; } = [];

		public override Task<WorkspaceFileDeleteResult> DeleteAsync(
			string path,
			FileStamp expectedStamp,
			CancellationToken cancellationToken)
		{
			TrashedPaths.Add(path);
			return Task.FromResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteStatus.Deleted));
		}
	}

	private sealed class PassThroughFileSystem : WorkspaceFileSystemDecorator
	{
		public PassThroughFileSystem(IWorkspaceFileSystem inner)
			: base(inner)
		{ }
	}

	private sealed class RecordingFileSystem : IWorkspaceFileSystem
	{
		public int ReadCount { get; private set; }

		public int CaptureCount { get; private set; }

		public int WriteTemporaryCount { get; private set; }

		public int ReplaceCount { get; private set; }

		public int MoveCount { get; private set; }

		public int MoveDirectoryCount { get; private set; }

		public int DeleteCount { get; private set; }

		public int DeleteDirectoryCount { get; private set; }

		public int DeleteTemporaryCount { get; private set; }

		public Task<WorkspaceFileReadResult> ReadAsync(
			string path,
			CancellationToken cancellationToken)
		{
			ReadCount++;
			return Task.FromResult(new WorkspaceFileReadResult("content", default, FileStamp.Missing));
		}

		public Task<FileStamp> CaptureStampAsync(
			string path,
			CancellationToken cancellationToken)
		{
			CaptureCount++;
			return Task.FromResult(FileStamp.Missing);
		}

		public Task<WorkspaceTemporaryFile> WriteTemporaryAsync(
			string directory,
			ReadOnlyMemory<byte> content,
			CancellationToken cancellationToken)
		{
			WriteTemporaryCount++;
			return Task.FromResult(new WorkspaceTemporaryFile("temporary.tmp", content.Length, "hash"));
		}

		public Task<WorkspaceFileReplacementResult> ReplaceFileAsync(
			WorkspaceTemporaryFile temporaryFile,
			string destinationPath,
			FileStamp expectedStamp,
			CancellationToken cancellationToken)
		{
			ReplaceCount++;
			return Task.FromResult(new WorkspaceFileReplacementResult(WorkspaceFileReplacementStatus.Replaced));
		}

		public Task<WorkspaceFileMoveResult> MoveAsync(
			string sourcePath,
			string destinationPath,
			FileStamp expectedSourceStamp,
			CancellationToken cancellationToken)
		{
			MoveCount++;
			return Task.FromResult(new WorkspaceFileMoveResult(WorkspaceFileMoveStatus.Moved));
		}

		public Task<WorkspaceFileMoveResult> MoveDirectoryAsync(
			string sourcePath,
			string destinationPath,
			CancellationToken cancellationToken)
		{
			MoveDirectoryCount++;
			return Task.FromResult(new WorkspaceFileMoveResult(WorkspaceFileMoveStatus.Moved));
		}

		public Task<WorkspaceFileDeleteResult> DeleteAsync(
			string path,
			FileStamp expectedStamp,
			CancellationToken cancellationToken)
		{
			DeleteCount++;
			return Task.FromResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteStatus.Deleted));
		}

		public Task<WorkspaceFileDeleteResult> DeleteDirectoryAsync(
			string path,
			CancellationToken cancellationToken)
		{
			DeleteDirectoryCount++;
			return Task.FromResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteStatus.Deleted));
		}

		public Task DeleteTemporaryAsync(WorkspaceTemporaryFile temporaryFile)
		{
			DeleteTemporaryCount++;
			return Task.CompletedTask;
		}
	}
}

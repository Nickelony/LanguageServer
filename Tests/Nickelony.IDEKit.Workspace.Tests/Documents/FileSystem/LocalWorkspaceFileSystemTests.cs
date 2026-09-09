using Nickelony.IDEKit.Core.Pathing;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;
using System.Security.Cryptography;
using System.Text;

namespace Nickelony.IDEKit.Workspace.Tests;

[TestClass]
public sealed class LocalWorkspaceFileSystemTests
{
	[TestMethod]
	public async Task LocalWorkspaceFileSystem_ReplaceFileAsync_ClassifiesVanishedPathsAsFailed()
	{
		using var temp = new TestTempDirectory();
		string directory = temp.Path;
		var fileSystem = new LocalWorkspaceFileSystem();

		// A missing temporary file fails the move before any replacement starts.
		WorkspaceFileReplacementResult missingTemporary = await fileSystem.ReplaceFileAsync(
			new WorkspaceTemporaryFile(Path.Combine(directory, "missing.tmp"), 6, "hash"),
			Path.Combine(directory, "destination.txt"),
			FileStamp.Missing,
			CancellationToken.None);
		Assert.AreEqual(WorkspaceFileReplacementStatus.Failed, missingTemporary.Status);
		Assert.IsNotNull(missingTemporary.Failure);

		// A missing destination directory fails the move with a known final state as well.
		string temporaryPath = Path.Combine(directory, "temporary.tmp");
		File.WriteAllText(temporaryPath, "content");
		WorkspaceFileReplacementResult missingDirectory = await fileSystem.ReplaceFileAsync(
			new WorkspaceTemporaryFile(temporaryPath, 7, "hash"),
			Path.Combine(directory, "missing", "destination.txt"),
			FileStamp.Missing,
			CancellationToken.None);
		Assert.AreEqual(WorkspaceFileReplacementStatus.Failed, missingDirectory.Status);
		Assert.IsNotNull(missingDirectory.Failure);
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_CaptureStampForMissingPathReturnsMissing()
	{
		using var temp = new TestTempDirectory();
		var fileSystem = new LocalWorkspaceFileSystem();

		FileStamp stamp = await fileSystem.CaptureStampAsync(
			Path.Combine(temp.Path, "missing.txt"),
			CancellationToken.None);

		Assert.AreEqual(FileStamp.Missing, stamp);
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_MovesAndDeletesOnlyWhenExpectedStampMatches()
	{
		using var temp = new TestTempDirectory();
		string directory = temp.Path;
		string sourcePath = Path.Combine(directory, "source.lua");
		string destinationPath = Path.Combine(directory, "destination.lua");
		File.WriteAllText(sourcePath, "content");
		var fileSystem = new LocalWorkspaceFileSystem();
		FileStamp sourceStamp = await fileSystem.CaptureStampAsync(sourcePath, CancellationToken.None);

		WorkspaceFileMoveResult moved = await fileSystem.MoveAsync(
			sourcePath,
			destinationPath,
			sourceStamp,
			CancellationToken.None);

		Assert.AreEqual(WorkspaceFileMoveStatus.Moved, moved.Status);
		Assert.IsFalse(File.Exists(sourcePath));
		Assert.AreEqual("content", File.ReadAllText(destinationPath));
		File.WriteAllText(destinationPath, "changed");

		WorkspaceFileDeleteResult staleDelete = await fileSystem.DeleteAsync(
			destinationPath,
			sourceStamp,
			CancellationToken.None);
		Assert.AreEqual(WorkspaceFileDeleteStatus.ExternalFileConflict, staleDelete.Status);

		FileStamp destinationStamp = await fileSystem.CaptureStampAsync(destinationPath, CancellationToken.None);
		WorkspaceFileDeleteResult deleted = await fileSystem.DeleteAsync(
			destinationPath,
			destinationStamp,
			CancellationToken.None);

		Assert.AreEqual(WorkspaceFileDeleteStatus.Deleted, deleted.Status);
		Assert.IsFalse(File.Exists(destinationPath));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_CaseOnlyFileRenameFollowsConfiguredPathComparison()
	{
		using var temp = new TestTempDirectory();
		string directory = temp.Path;
		string sourcePath = Path.Combine(directory, "Script.lua");
		string destinationPath = Path.Combine(directory, "script.lua");
		File.WriteAllText(sourcePath, "content");

		// The case-sensitive policy treats the destination spelling as a distinct path, so the
		// outcome follows the actual volume: a case-insensitive volume still reports the variant as
		// an existing destination, while a case-sensitive volume performs an ordinary rename.
		bool volumeResolvesCaseVariants = File.Exists(Path.Combine(directory, "SCRIPT.lua"));
		var caseSensitive = new LocalWorkspaceFileSystem(LocalPathComparisonPolicy.CaseSensitive);
		FileStamp sourceStamp = await caseSensitive.CaptureStampAsync(sourcePath, CancellationToken.None);
		WorkspaceFileMoveResult sensitive = await caseSensitive.MoveAsync(
			sourcePath,
			destinationPath,
			sourceStamp,
			CancellationToken.None);

		Assert.AreEqual(
			volumeResolvesCaseVariants ? WorkspaceFileMoveStatus.DestinationExists : WorkspaceFileMoveStatus.Moved,
			sensitive.Status);

		// Reset to the original spelling and verify the case-insensitive policy completes the
		// case-only rename on every platform instead of rejecting the existing variant.
		if (File.Exists(destinationPath))
		{
			File.Delete(destinationPath);
			File.WriteAllText(sourcePath, "content");
		}

		var caseInsensitive = new LocalWorkspaceFileSystem(LocalPathComparisonPolicy.CaseInsensitive);
		FileStamp resetStamp = await caseInsensitive.CaptureStampAsync(sourcePath, CancellationToken.None);
		WorkspaceFileMoveResult insensitive = await caseInsensitive.MoveAsync(
			sourcePath,
			destinationPath,
			resetStamp,
			CancellationToken.None);

		Assert.AreEqual(WorkspaceFileMoveStatus.Moved, insensitive.Status);
		string[] files = Directory.GetFiles(directory);
		Assert.AreEqual(1, files.Length);
		Assert.AreEqual("script.lua", Path.GetFileName(files[0]));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_CaseOnlyDirectoryRenameFollowsConfiguredPathComparison()
	{
		using var temp = new TestTempDirectory();
		string directory = temp.Path;
		string sourcePath = Path.Combine(directory, "Folder");
		Directory.CreateDirectory(sourcePath);
		File.WriteAllText(Path.Combine(sourcePath, "one.lua"), "content");
		string destinationPath = Path.Combine(directory, "folder");
		var caseInsensitive = new LocalWorkspaceFileSystem(LocalPathComparisonPolicy.CaseInsensitive);

		WorkspaceFileMoveResult moved = await caseInsensitive.MoveDirectoryAsync(
			sourcePath,
			destinationPath,
			CancellationToken.None);

		Assert.AreEqual(WorkspaceFileMoveStatus.Moved, moved.Status);
		string[] directories = Directory.GetDirectories(directory);
		Assert.AreEqual(1, directories.Length);
		Assert.AreEqual("folder", Path.GetFileName(directories[0]));
		Assert.IsTrue(File.Exists(Path.Combine(destinationPath, "one.lua")));

		// A missing source is a move failure, and deleting the renamed directory works recursively.
		WorkspaceFileMoveResult missing = await caseInsensitive.MoveDirectoryAsync(
			Path.Combine(directory, "absent"),
			Path.Combine(directory, "other"),
			CancellationToken.None);
		Assert.AreEqual(WorkspaceFileMoveStatus.MoveFailed, missing.Status);

		WorkspaceFileDeleteResult deleted = await caseInsensitive.DeleteDirectoryAsync(destinationPath, CancellationToken.None);
		Assert.AreEqual(WorkspaceFileDeleteStatus.Deleted, deleted.Status);
		Assert.IsFalse(Directory.Exists(destinationPath));

		WorkspaceFileDeleteResult deletedAgain = await caseInsensitive.DeleteDirectoryAsync(destinationPath, CancellationToken.None);
		Assert.AreEqual(WorkspaceFileDeleteStatus.Deleted, deletedAgain.Status);
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_ReplaceFileAsyncReportsConflictWhenExpectedMissingButDestinationExists()
	{
		using var temp = new TestTempDirectory();
		string directory = temp.Path;
		var fileSystem = new LocalWorkspaceFileSystem();
		string temporaryPath = Path.Combine(directory, "temporary.tmp");
		File.WriteAllText(temporaryPath, "content");
		string destinationPath = Path.Combine(directory, "destination.txt");
		File.WriteAllText(destinationPath, "existing");

		// The expected stamp says the destination is missing while the destination exists: the
		// conflict is reported with the observed stamp instead of a replacement attempt.
		WorkspaceFileReplacementResult result = await fileSystem.ReplaceFileAsync(
			new WorkspaceTemporaryFile(temporaryPath, 7, "hash"),
			destinationPath,
			FileStamp.Missing,
			CancellationToken.None);

		Assert.AreEqual(WorkspaceFileReplacementStatus.ExternalFileConflict, result.Status);
		Assert.IsTrue(result.ObservedOnDiskStamp!.Value.Exists);
		Assert.AreEqual("existing", File.ReadAllText(destinationPath));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_ReplaceFileAsyncReportsConflictWhenDestinationAppearsDuringMove()
	{
		using var temp = new TestTempDirectory();
		string directory = temp.Path;
		string temporaryPath = Path.Combine(directory, "temporary.tmp");
		File.WriteAllText(temporaryPath, "content");
		string destinationPath = Path.Combine(directory, "destination.txt");
		string? movedSourcePath = null;

		// The destination appears between the stamp check and the missing-destination move: the
		// substituted move creates the concurrent file and then reports the same IOException the
		// real move raises when the destination was created in the meantime.
		var fileSystem = new LocalWorkspaceFileSystem(null, new WorkspaceFileReplaceOperations(
			Replace: static (_, _) => throw new InvalidOperationException("A missing destination must use the move operation."),
			Move: (sourcePath, path) =>
			{
				movedSourcePath = sourcePath;
				File.WriteAllText(path, "concurrent");
				throw new IOException("Cannot create a file when that file already exists.");
			}));

		WorkspaceFileReplacementResult result = await fileSystem.ReplaceFileAsync(
			new WorkspaceTemporaryFile(temporaryPath, 7, "hash"),
			destinationPath,
			FileStamp.Missing,
			CancellationToken.None);

		Assert.AreEqual(temporaryPath, movedSourcePath);
		Assert.AreEqual(WorkspaceFileReplacementStatus.ExternalFileConflict, result.Status);
		Assert.IsTrue(result.ObservedOnDiskStamp!.Value.Exists);
		Assert.AreEqual((long)"concurrent".Length, result.ObservedOnDiskStamp!.Value.Length);
		Assert.IsNull(result.Failure);
		Assert.AreEqual("concurrent", File.ReadAllText(destinationPath));
		Assert.IsTrue(File.Exists(temporaryPath));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_PreCanceledTokenReportsCanceledForEveryMutation()
	{
		using var temp = new TestTempDirectory();
		string directory = temp.Path;
		var fileSystem = new LocalWorkspaceFileSystem();
		string sourcePath = Path.Combine(directory, "source.lua");
		File.WriteAllText(sourcePath, "content");
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();

		WorkspaceFileMoveResult move = await fileSystem.MoveAsync(
			sourcePath,
			Path.Combine(directory, "moved.lua"),
			FileStamp.Missing,
			cancellation.Token);
		WorkspaceFileDeleteResult delete = await fileSystem.DeleteAsync(sourcePath, FileStamp.Missing, cancellation.Token);
		WorkspaceFileMoveResult directoryMove = await fileSystem.MoveDirectoryAsync(
			directory,
			directory + "-moved",
			cancellation.Token);
		WorkspaceFileDeleteResult directoryDelete = await fileSystem.DeleteDirectoryAsync(directory, cancellation.Token);

		WorkspaceFileReplacementResult replacement = await fileSystem.ReplaceFileAsync(
			new WorkspaceTemporaryFile(sourcePath, 7, "hash"),
			Path.Combine(directory, "replaced.lua"),
			FileStamp.Missing,
			cancellation.Token);

		Assert.AreEqual(WorkspaceFileMoveStatus.Canceled, move.Status);
		Assert.AreEqual(WorkspaceFileDeleteStatus.Canceled, delete.Status);
		Assert.AreEqual(WorkspaceFileMoveStatus.Canceled, directoryMove.Status);
		Assert.AreEqual(WorkspaceFileDeleteStatus.Canceled, directoryDelete.Status);
		Assert.AreEqual(WorkspaceFileReplacementStatus.Canceled, replacement.Status);
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_ReplaceFileAsyncDirectoryDestinationReportsDestinationExists()
	{
		using var temp = new TestTempDirectory();
		string directory = temp.Path;
		var fileSystem = new LocalWorkspaceFileSystem();
		string temporaryPath = Path.Combine(directory, "temporary.tmp");
		File.WriteAllText(temporaryPath, "content");
		string destinationPath = Path.Combine(directory, "destination");
		Directory.CreateDirectory(destinationPath);

		WorkspaceFileReplacementResult result = await fileSystem.ReplaceFileAsync(
			new WorkspaceTemporaryFile(temporaryPath, 7, "hash"),
			destinationPath,
			FileStamp.Missing,
			CancellationToken.None);

		// A directory occupies the destination where a missing file was expected: the move is not
		// attempted and the deterministic conflict is reported.
		Assert.AreEqual(WorkspaceFileReplacementStatus.DestinationExists, result.Status);
		Assert.AreEqual(WorkspaceOperationFailureCodes.DestinationExists, result.Failure?.Code);
		Assert.IsTrue(Directory.Exists(destinationPath));
		Assert.IsTrue(File.Exists(temporaryPath));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_ReadAsync_DirectoryReturnsIsDirectoryResult()
	{
		using var temp = new TestTempDirectory();
		string directoryPath = Path.Combine(temp.Path, "folder");
		Directory.CreateDirectory(directoryPath);
		var fileSystem = new LocalWorkspaceFileSystem();

		// A directory is not a missing file: the read reports the directory flag so the document
		// store can reject the path instead of tracking a new empty document for it.
		WorkspaceFileReadResult result = await fileSystem.ReadAsync(directoryPath, CancellationToken.None);

		Assert.IsTrue(result.IsDirectory);
		Assert.AreEqual(FileStamp.Missing, result.OnDiskStamp);
		Assert.AreEqual(string.Empty, result.Content);
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_WriteTemporaryAndDeleteTemporary_RoundTrip()
	{
		using var temp = new TestTempDirectory();
		var fileSystem = new LocalWorkspaceFileSystem();
		byte[] content = Encoding.UTF8.GetBytes("temporary");

		WorkspaceTemporaryFile temporary = await fileSystem.WriteTemporaryAsync(
			temp.Path,
			content,
			CancellationToken.None);

		// The temporary file is created in the supplied directory and its descriptor describes the
		// bytes actually written.
		Assert.IsTrue(File.Exists(temporary.Path));
		Assert.AreEqual(temp.Path, Path.GetDirectoryName(temporary.Path));
		Assert.AreEqual(content.Length, temporary.Length);
		Assert.AreEqual(Convert.ToHexString(SHA256.HashData(content)), temporary.ContentHash);

		await fileSystem.DeleteTemporaryAsync(temporary);
		Assert.IsFalse(File.Exists(temporary.Path));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_MoveAsyncReportsStampConflictAndExistingDestination()
	{
		using var temp = new TestTempDirectory();
		string sourcePath = Path.Combine(temp.Path, "source.lua");
		string existingPath = Path.Combine(temp.Path, "existing.lua");
		File.WriteAllText(sourcePath, "content");
		File.WriteAllText(existingPath, "existing");
		var fileSystem = new LocalWorkspaceFileSystem();
		FileStamp sourceStamp = await fileSystem.CaptureStampAsync(sourcePath, CancellationToken.None);

		WorkspaceFileMoveResult stale = await fileSystem.MoveAsync(
			sourcePath,
			Path.Combine(temp.Path, "destination.lua"),
			FileStamp.Missing,
			CancellationToken.None);

		Assert.AreEqual(WorkspaceFileMoveStatus.ExternalFileConflict, stale.Status);
		Assert.AreEqual(sourceStamp, stale.ObservedOnDiskStamp);

		WorkspaceFileMoveResult collision = await fileSystem.MoveAsync(
			sourcePath,
			existingPath,
			sourceStamp,
			CancellationToken.None);

		Assert.AreEqual(WorkspaceFileMoveStatus.DestinationExists, collision.Status);
		Assert.IsTrue(File.Exists(sourcePath));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_ReplaceFileAsync_ClassifiesFailedReplacementFromTheObservedDestination()
	{
		using var temp = new TestTempDirectory();
		string directory = temp.Path;
		string destinationPath = Path.Combine(directory, "destination.txt");
		byte[] replacementBytes = Encoding.UTF8.GetBytes("replacement");
		string temporaryPath = Path.Combine(directory, "temporary.tmp");
		File.WriteAllBytes(temporaryPath, replacementBytes);
		var temporaryFile = new WorkspaceTemporaryFile(
			temporaryPath,
			replacementBytes.Length,
			Convert.ToHexString(SHA256.HashData(replacementBytes)));

		// An unchanged destination means the failed replacement did not alter it, so the final state
		// is known to be the expected stamp.
		File.WriteAllText(destinationPath, "original");
		var unchanged = new LocalWorkspaceFileSystem(null, new WorkspaceFileReplaceOperations(
			Replace: static (_, _) => throw new IOException("The replacement failed."),
			Move: static (_, _) => throw new IOException("The replacement failed.")));
		FileStamp unchangedStamp = await unchanged.CaptureStampAsync(destinationPath, CancellationToken.None);
		WorkspaceFileReplacementResult unchangedResult = await unchanged.ReplaceFileAsync(
			temporaryFile,
			destinationPath,
			unchangedStamp,
			CancellationToken.None);

		Assert.AreEqual(WorkspaceFileReplacementStatus.Failed, unchangedResult.Status);
		Assert.AreEqual(WorkspaceOperationFailureCodes.WriteFailed, unchangedResult.Failure!.Code);
		Assert.AreEqual(unchangedStamp, unchangedResult.ObservedOnDiskStamp);
		Assert.AreEqual("original", File.ReadAllText(destinationPath));

		// The destination already carries the intended bytes, so the replacement completed before the
		// reported failure.
		var completed = new LocalWorkspaceFileSystem(null, new WorkspaceFileReplaceOperations(
			Replace: (_, path) =>
			{
				File.WriteAllBytes(path, replacementBytes);
				throw new IOException("The replacement failed after the write.");
			},
			Move: static (_, _) => throw new IOException("The replacement failed.")));
		File.WriteAllText(destinationPath, "original");
		FileStamp completedStamp = await completed.CaptureStampAsync(destinationPath, CancellationToken.None);
		WorkspaceFileReplacementResult completedResult = await completed.ReplaceFileAsync(
			temporaryFile,
			destinationPath,
			completedStamp,
			CancellationToken.None);

		Assert.AreEqual(WorkspaceFileReplacementStatus.Replaced, completedResult.Status);
		Assert.AreEqual((long)replacementBytes.Length, completedResult.ObservedOnDiskStamp!.Value.Length);

		// The destination carries neither the expected stamp nor the intended bytes, so the final state
		// cannot be established.
		var unknown = new LocalWorkspaceFileSystem(null, new WorkspaceFileReplaceOperations(
			Replace: (_, path) =>
			{
				File.WriteAllText(path, "unrelated");
				throw new IOException("The replacement failed with an unrelated destination state.");
			},
			Move: static (_, _) => throw new IOException("The replacement failed.")));
		File.WriteAllText(destinationPath, "original");
		FileStamp unknownStamp = await unknown.CaptureStampAsync(destinationPath, CancellationToken.None);
		WorkspaceFileReplacementResult unknownResult = await unknown.ReplaceFileAsync(
			temporaryFile,
			destinationPath,
			unknownStamp,
			CancellationToken.None);

		Assert.AreEqual(WorkspaceFileReplacementStatus.ReplacementStateUnknown, unknownResult.Status);
		Assert.AreEqual(WorkspaceOperationFailureCodes.ReplacementStateUnknown, unknownResult.Failure!.Code);
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_ReplaceFileAsync_UnauthorizedAccessReportsAccessDenied()
	{
		using var temp = new TestTempDirectory();
		string directory = temp.Path;
		string destinationPath = Path.Combine(directory, "destination.txt");
		File.WriteAllText(destinationPath, "original");
		string temporaryPath = Path.Combine(directory, "temporary.tmp");
		File.WriteAllText(temporaryPath, "content");

		// A permission failure is a host-actionable outcome, so it carries the AccessDenied code instead
		// of the generic write-failure code.
		var fileSystem = new LocalWorkspaceFileSystem(null, new WorkspaceFileReplaceOperations(
			Replace: static (_, _) => throw new UnauthorizedAccessException("The destination is read-only."),
			Move: static (_, _) => throw new UnauthorizedAccessException("The destination is read-only.")));
		FileStamp stamp = await fileSystem.CaptureStampAsync(destinationPath, CancellationToken.None);

		WorkspaceFileReplacementResult result = await fileSystem.ReplaceFileAsync(
			new WorkspaceTemporaryFile(temporaryPath, 7, "hash"),
			destinationPath,
			stamp,
			CancellationToken.None);

		Assert.AreEqual(WorkspaceFileReplacementStatus.Failed, result.Status);
		Assert.AreEqual(WorkspaceOperationFailureCodes.AccessDenied, result.Failure!.Code);
	}
}

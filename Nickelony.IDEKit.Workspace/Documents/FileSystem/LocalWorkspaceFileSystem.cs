using Nickelony.IDEKit.Core.Pathing;
using System.Buffers;
using System.Security.Cryptography;

namespace Nickelony.IDEKit.Workspace.Documents.FileSystem;

/// <summary>
/// Provides the default local-disk <see cref="IWorkspaceFileSystem"/> implementation used by the document store.
/// </summary>
/// <remarks>
/// Deletion is permanent; see the <see cref="IWorkspaceFileSystem"/> remarks for how a host supplies
/// recoverable deletion such as a trash folder. Text encoding is provided separately by
/// <see cref="WorkspaceTextCodec"/>. Permission failures are reported with the
/// <see cref="WorkspaceOperationFailureCodes.AccessDenied"/> failure code by the members whose results
/// carry a failure; <see cref="ReadAsync"/>, <see cref="CaptureStampAsync"/>,
/// <see cref="WriteTemporaryAsync"/>, and <see cref="DeleteTemporaryAsync"/> surface
/// <see cref="UnauthorizedAccessException"/> because their results cannot carry one. The implementation
/// does not retry transient sharing violations on
/// replace or move operations; a host that needs bounded retry behavior supplies a decorator.
/// </remarks>
public sealed class LocalWorkspaceFileSystem : IWorkspaceFileSystem
{
	private readonly LocalPathComparisonPolicy _pathComparison;
	private readonly WorkspaceFileReplaceOperations _replaceOperations;

	/// <summary>
	/// Initializes a new instance of the <see cref="LocalWorkspaceFileSystem"/> class.
	/// </summary>
	/// <param name="pathComparison">
	/// The path comparison policy that describes the target file system. It decides how a case-only
	/// rename (for example <c>document.txt</c> to <c>Document.txt</c>) is handled: a case-insensitive
	/// policy routes the move through a temporary intermediate path because the destination resolves
	/// to the source file, while a case-sensitive policy treats the destination spelling as a distinct
	/// path. The default follows the operating system; supply the same policy that the host uses for
	/// document identities when the file system's semantics differ from it.
	/// </param>
	public LocalWorkspaceFileSystem(LocalPathComparisonPolicy? pathComparison = null)
		: this(pathComparison, WorkspaceFileReplaceOperations.Default)
	{
	}

	internal LocalWorkspaceFileSystem(LocalPathComparisonPolicy? pathComparison, WorkspaceFileReplaceOperations replaceOperations)
	{
		_pathComparison = pathComparison ?? LocalPathComparisonPolicy.ForCurrentPlatform;
		_replaceOperations = replaceOperations;
	}

	/// <inheritdoc />
	/// <remarks>
	/// For an existing file, <see cref="WorkspaceFileReadResult.RawBytes"/> contains the bytes and
	/// <see cref="WorkspaceFileReadResult.Content"/> is empty; decoding is deferred to the document
	/// store. A missing file returns <see cref="FileStamp.Missing"/>.
	/// </remarks>
	public async Task<WorkspaceFileReadResult> ReadAsync(
		string path,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(path);

		cancellationToken.ThrowIfCancellationRequested();

		// A directory is not a missing file: reporting it as missing would let the store track a
		// directory path as a new empty document whose later writes and deletes silently fail.
		if (Directory.Exists(path))
			return new WorkspaceFileReadResult(string.Empty, default, FileStamp.Missing, IsDirectory: true);

		if (!File.Exists(path))
			return new WorkspaceFileReadResult(string.Empty, default, FileStamp.Missing);

		byte[] bytes;
		try
		{
			bytes = await ReadBytesAsync(path, cancellationToken).ConfigureAwait(false);
		}
		catch (FileNotFoundException)
		{
			// The file vanished between the existence probe and the open. The requested end state - no
			// file - still holds, so a missing file is reported instead of a load failure.
			return new WorkspaceFileReadResult(string.Empty, default, FileStamp.Missing);
		}
		catch (DirectoryNotFoundException)
		{
			return new WorkspaceFileReadResult(string.Empty, default, FileStamp.Missing);
		}

		FileStamp stamp = CaptureStampFromBytes(path, bytes, cancellationToken);
		return new WorkspaceFileReadResult(string.Empty, default, stamp, bytes);
	}

	/// <inheritdoc />
	/// <remarks>Reads and hashes the whole file once, so the capture cost is proportional to the file size.</remarks>
	public async Task<FileStamp> CaptureStampAsync(string path, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(path);

		cancellationToken.ThrowIfCancellationRequested();

		if (!File.Exists(path))
			return FileStamp.Missing;

		return await CaptureStampFromFileAsync(path, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	/// <remarks>
	/// The bytes are flushed to the physical device before this member returns, so a crash after the
	/// following replacement cannot expose a zero-length or partial destination that the store already
	/// reported as written. The temporary file is not removed on success; the caller must delete it.
	/// </remarks>
	public async Task<WorkspaceTemporaryFile> WriteTemporaryAsync(
		string directory,
		ReadOnlyMemory<byte> content,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(directory);

		string path = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
		try
		{
			await using FileStream stream = new(
				path,
				FileMode.CreateNew,
				FileAccess.Write,
				FileShare.None,
				64 * 1024,
				FileOptions.Asynchronous | FileOptions.SequentialScan);
			await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
			await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

			// FlushAsync only reaches the operating system cache. The replacement that publishes this
			// file must not be able to complete before the data itself is durable, so the buffers are
			// flushed to disk here; otherwise a power loss can leave a zero-length destination behind an
			// operation that already reported a completed replacement.
			stream.Flush(flushToDisk: true);

			return new WorkspaceTemporaryFile(path, content.Length, Convert.ToHexString(SHA256.HashData(content.Span)));
		}
		catch
		{
			TryDelete(path);
			throw;
		}
	}

	/// <inheritdoc />
	/// <remarks>
	/// Existing files are replaced; missing destinations are created. If the replacement throws after
	/// the operation begins, the result can be <see cref="WorkspaceFileReplacementStatus.ReplacementStateUnknown"/>
	/// because the final on-disk state cannot be established reliably. A destination that appears after
	/// the stamp check reported it missing is reported as <see cref="WorkspaceFileReplacementStatus.ExternalFileConflict"/>
	/// because the conflict itself is deterministic even though the write raced with it. A directory
	/// that occupies a destination expected to be missing is reported as
	/// <see cref="WorkspaceFileReplacementStatus.DestinationExists"/>. A canceled token is observed
	/// before the destination is touched and reports <see cref="WorkspaceFileReplacementStatus.Canceled"/>.
	/// </remarks>
	public async Task<WorkspaceFileReplacementResult> ReplaceFileAsync(
		WorkspaceTemporaryFile temporaryFile,
		string destinationPath,
		FileStamp expectedStamp,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(temporaryFile);
		ArgumentNullException.ThrowIfNull(destinationPath);

		try
		{
			cancellationToken.ThrowIfCancellationRequested();

			FileStamp actualStamp = await CaptureStampAsync(destinationPath, cancellationToken).ConfigureAwait(false);
			if (actualStamp != expectedStamp)
				return new WorkspaceFileReplacementResult(
					WorkspaceFileReplacementStatus.ExternalFileConflict,
					actualStamp);

			// The destination is expected to be absent; a directory occupying the path cannot receive the
			// temporary file, and the missing-file stamp shape does not distinguish it from a free path.
			if (!expectedStamp.Exists && Directory.Exists(destinationPath))
				return CreateDestinationDirectoryResult();

			cancellationToken.ThrowIfCancellationRequested();
			if (expectedStamp.Exists)
				_replaceOperations.Replace(temporaryFile.Path, destinationPath);
			else
				_replaceOperations.Move(temporaryFile.Path, destinationPath);

			DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(destinationPath);
			FileStamp replacementStamp = new(
				true,
				temporaryFile.Length,
				lastWriteTimeUtc,
				temporaryFile.ContentHash);
			return new WorkspaceFileReplacementResult(
				WorkspaceFileReplacementStatus.Replaced,
				replacementStamp);
		}
		catch (OperationCanceledException)
		{
			return new WorkspaceFileReplacementResult(WorkspaceFileReplacementStatus.Canceled);
		}
		catch (IOException exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
		{
			// The destination or temporary file vanished before the replacement started: nothing was
			// replaced and the final state is known, unlike a mid-replacement failure.
			return new WorkspaceFileReplacementResult(
				WorkspaceFileReplacementStatus.Failed,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.WriteFailed, exception.Message, exception));
		}
		catch (IOException exception)
		{
			FileStamp? observedOnDiskStamp = await TryCaptureStampAsync(destinationPath).ConfigureAwait(false);
			return ClassifyReplacementFailure(temporaryFile, destinationPath, expectedStamp, observedOnDiskStamp, exception);
		}
		catch (PlatformNotSupportedException exception)
		{
			FileStamp? observedOnDiskStamp = await TryCaptureStampAsync(destinationPath).ConfigureAwait(false);
			return ClassifyReplacementFailure(temporaryFile, destinationPath, expectedStamp, observedOnDiskStamp, exception);
		}
		catch (UnauthorizedAccessException exception)
		{
			return new WorkspaceFileReplacementResult(
				WorkspaceFileReplacementStatus.Failed,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.AccessDenied, exception.Message, exception));
		}
	}

	// The destination path is occupied by a directory where a missing file was expected: the temporary
	// file cannot be moved onto it, and the conflict is deterministic.
	private static WorkspaceFileReplacementResult CreateDestinationDirectoryResult()
		=> new(
			WorkspaceFileReplacementStatus.DestinationExists,
			Failure: new WorkspaceOperationFailure(
				WorkspaceOperationFailureCodes.DestinationExists,
				"The destination path exists as a directory."));

	/// <inheritdoc />
	/// <remarks>
	/// A case-only rename on a case-insensitive target uses a temporary intermediate path, so the file
	/// is briefly renamed before it receives the requested spelling. If the rollback move of that path
	/// also fails, the file can be left at the intermediate path and the failure is reported as
	/// <see cref="WorkspaceFileMoveStatus.MoveStateUnknown"/> with an exception that aggregates the
	/// original move failure and the rollback failure; the document store keeps tracking the source
	/// identity.
	/// </remarks>
	public async Task<WorkspaceFileMoveResult> MoveAsync(
		string sourcePath,
		string destinationPath,
		FileStamp expectedSourceStamp,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(sourcePath);
		ArgumentNullException.ThrowIfNull(destinationPath);

		bool isCaseOnlyRename = false;

		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			FileStamp actualSourceStamp = await CaptureStampAsync(sourcePath, cancellationToken).ConfigureAwait(false);
			if (actualSourceStamp != expectedSourceStamp)
				return new WorkspaceFileMoveResult(
					WorkspaceFileMoveStatus.ExternalFileConflict,
					actualSourceStamp);

			isCaseOnlyRename = IsCaseOnlyRename(sourcePath, destinationPath);
			if (!isCaseOnlyRename && (File.Exists(destinationPath) || Directory.Exists(destinationPath)))
				return new WorkspaceFileMoveResult(WorkspaceFileMoveStatus.DestinationExists);

			MoveWithCaseOnlyRenameHandling(sourcePath, destinationPath, isCaseOnlyRename, File.Move, File.Exists);

			return new WorkspaceFileMoveResult(WorkspaceFileMoveStatus.Moved);
		}
		catch (OperationCanceledException)
		{
			return new WorkspaceFileMoveResult(WorkspaceFileMoveStatus.Canceled);
		}
		// Only a move whose destination was verified absent can turn a destination that now exists
		// into an existence conflict. A case-only rename resolves to the source file itself, so an
		// I/O failure there - or any failure with no destination present - is a real move failure.
		catch (IOException exception) when (!isCaseOnlyRename
			&& (File.Exists(destinationPath) || Directory.Exists(destinationPath)))
		{
			return new WorkspaceFileMoveResult(
				WorkspaceFileMoveStatus.DestinationExists,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.DestinationExists, exception.Message, exception));
		}
		catch (UnauthorizedAccessException exception)
		{
			return new WorkspaceFileMoveResult(
				WorkspaceFileMoveStatus.MoveFailed,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.AccessDenied, exception.Message, exception));
		}
		catch (AggregateException exception)
		{
			return new WorkspaceFileMoveResult(
				WorkspaceFileMoveStatus.MoveStateUnknown,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.MoveStateUnknown, exception.Message, exception));
		}
		catch (Exception exception)
		{
			return new WorkspaceFileMoveResult(
				WorkspaceFileMoveStatus.MoveFailed,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.MoveFailed, exception.Message, exception));
		}
	}

	/// <inheritdoc />
	/// <remarks>
	/// The file is deleted permanently. A path that no longer exists after a matching stamp is reported
	/// as <see cref="WorkspaceFileDeleteStatus.Deleted"/> because the requested end state already holds.
	/// </remarks>
	public async Task<WorkspaceFileDeleteResult> DeleteAsync(
		string path,
		FileStamp expectedStamp,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(path);

		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			FileStamp actualStamp = await CaptureStampAsync(path, cancellationToken).ConfigureAwait(false);
			if (actualStamp != expectedStamp)
				return new WorkspaceFileDeleteResult(
					WorkspaceFileDeleteStatus.ExternalFileConflict,
					actualStamp);

			if (File.Exists(path))
				File.Delete(path);

			return new WorkspaceFileDeleteResult(WorkspaceFileDeleteStatus.Deleted);
		}
		catch (OperationCanceledException)
		{
			return new WorkspaceFileDeleteResult(WorkspaceFileDeleteStatus.Canceled);
		}
		catch (UnauthorizedAccessException exception)
		{
			return new WorkspaceFileDeleteResult(
				WorkspaceFileDeleteStatus.DeleteFailed,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.AccessDenied, exception.Message, exception));
		}
		catch (Exception exception)
		{
			return new WorkspaceFileDeleteResult(
				WorkspaceFileDeleteStatus.DeleteFailed,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.DeleteFailed, exception.Message, exception));
		}
	}

	/// <inheritdoc />
	/// <remarks>
	/// The move runs synchronously, so the returned task is already completed and
	/// <paramref name="cancellationToken"/> is observed before the move starts. A case-only rename on
	/// a case-insensitive target uses a temporary intermediate path like <see cref="MoveAsync(string, string, FileStamp, CancellationToken)"/>.
	/// </remarks>
	public Task<WorkspaceFileMoveResult> MoveDirectoryAsync(
		string sourcePath,
		string destinationPath,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(sourcePath);
		ArgumentNullException.ThrowIfNull(destinationPath);

		bool isCaseOnlyRename = false;

		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!Directory.Exists(sourcePath))
				return Task.FromResult(new WorkspaceFileMoveResult(
					WorkspaceFileMoveStatus.MoveFailed,
					Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.MoveFailed, "The source directory does not exist.")));

			isCaseOnlyRename = IsCaseOnlyRename(sourcePath, destinationPath);
			if (!isCaseOnlyRename && (Directory.Exists(destinationPath) || File.Exists(destinationPath)))
				return Task.FromResult(new WorkspaceFileMoveResult(WorkspaceFileMoveStatus.DestinationExists));

			MoveWithCaseOnlyRenameHandling(sourcePath, destinationPath, isCaseOnlyRename, Directory.Move, Directory.Exists);

			return Task.FromResult(new WorkspaceFileMoveResult(WorkspaceFileMoveStatus.Moved));
		}
		catch (OperationCanceledException)
		{
			return Task.FromResult(new WorkspaceFileMoveResult(WorkspaceFileMoveStatus.Canceled));
		}
		// See MoveAsync: a case-only rename resolves to the source directory, so its I/O failures are
		// never existence conflicts.
		catch (IOException exception) when (!isCaseOnlyRename
			&& (Directory.Exists(destinationPath) || File.Exists(destinationPath)))
		{
			return Task.FromResult(new WorkspaceFileMoveResult(
				WorkspaceFileMoveStatus.DestinationExists,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.DestinationExists, exception.Message, exception)));
		}
		catch (UnauthorizedAccessException exception)
		{
			return Task.FromResult(new WorkspaceFileMoveResult(
				WorkspaceFileMoveStatus.MoveFailed,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.AccessDenied, exception.Message, exception)));
		}
		catch (AggregateException exception)
		{
			return Task.FromResult(new WorkspaceFileMoveResult(
				WorkspaceFileMoveStatus.MoveStateUnknown,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.MoveStateUnknown, exception.Message, exception)));
		}
		catch (Exception exception)
		{
			return Task.FromResult(new WorkspaceFileMoveResult(
				WorkspaceFileMoveStatus.MoveFailed,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.MoveFailed, exception.Message, exception)));
		}
	}

	/// <inheritdoc />
	/// <remarks>
	/// The directory is deleted permanently. A path that no longer exists is reported as
	/// <see cref="WorkspaceFileDeleteStatus.Deleted"/> because the requested end state already holds.
	/// The deletion runs synchronously, so the returned task is already completed and
	/// <paramref name="cancellationToken"/> is observed before the deletion starts.
	/// </remarks>
	public Task<WorkspaceFileDeleteResult> DeleteDirectoryAsync(
		string path,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(path);

		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (Directory.Exists(path))
				Directory.Delete(path, recursive: true);

			return Task.FromResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteStatus.Deleted));
		}
		catch (OperationCanceledException)
		{
			return Task.FromResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteStatus.Canceled));
		}
		catch (UnauthorizedAccessException exception)
		{
			return Task.FromResult(new WorkspaceFileDeleteResult(
				WorkspaceFileDeleteStatus.DeleteFailed,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.AccessDenied, exception.Message, exception)));
		}
		catch (Exception exception)
		{
			return Task.FromResult(new WorkspaceFileDeleteResult(
				WorkspaceFileDeleteStatus.DeleteFailed,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.DeleteFailed, exception.Message, exception)));
		}
	}

	/// <inheritdoc />
	/// <remarks>The file is deleted synchronously, so the returned task is already completed.</remarks>
	public Task DeleteTemporaryAsync(WorkspaceTemporaryFile temporaryFile)
	{
		ArgumentNullException.ThrowIfNull(temporaryFile);

		File.Delete(temporaryFile.Path);
		return Task.CompletedTask;
	}

	private static void TryDelete(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (Exception)
		{
			// Cleanup is best effort and must not mask the original failure.
		}
	}

	// Classifies a failed or partially applied replacement from the re-captured destination stamp,
	// which is the only evidence available after the exception. A directory that occupies the expected
	// free destination is a deterministic conflict; a destination that appeared after the stamp check
	// reported it missing is a deterministic conflict; an unchanged destination means the failed
	// operation did not alter it; a destination that already carries the intended content means the
	// replacement completed. Only a failed capture or an unrecognized state stays indeterminate.
	private static WorkspaceFileReplacementResult ClassifyReplacementFailure(
		WorkspaceTemporaryFile temporaryFile,
		string destinationPath,
		FileStamp expectedStamp,
		FileStamp? observedOnDiskStamp,
		Exception exception)
	{
		if (!expectedStamp.Exists && Directory.Exists(destinationPath))
			return CreateDestinationDirectoryResult();

		if (!expectedStamp.Exists && observedOnDiskStamp is { Exists: true })
			return new WorkspaceFileReplacementResult(
				WorkspaceFileReplacementStatus.ExternalFileConflict,
				observedOnDiskStamp);

		if (observedOnDiskStamp is { } stamp)
		{
			if (stamp == expectedStamp)
				return new WorkspaceFileReplacementResult(
					WorkspaceFileReplacementStatus.Failed,
					observedOnDiskStamp,
					new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.WriteFailed, exception.Message, exception));

			if (stamp.Length == temporaryFile.Length
				&& string.Equals(stamp.ContentHash, temporaryFile.ContentHash, StringComparison.Ordinal))
				return new WorkspaceFileReplacementResult(
					WorkspaceFileReplacementStatus.Replaced,
					observedOnDiskStamp);
		}

		return new WorkspaceFileReplacementResult(
			WorkspaceFileReplacementStatus.ReplacementStateUnknown,
			observedOnDiskStamp,
			Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.ReplacementStateUnknown, exception.Message, exception));
	}

	// A case-only rename targets a path that differs from the source only in casing. On a
	// case-insensitive file system the destination resolves to the source file, so the move needs the
	// intermediate-path strategy instead of being rejected as an existing destination; on a
	// case-sensitive file system it is an ordinary move and an existing variant is a real collision.
	private bool IsCaseOnlyRename(string sourcePath, string destinationPath)
		=> _pathComparison.IgnoreCase
			&& string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(sourcePath, destinationPath, StringComparison.Ordinal);

	// Moves a file or directory, routing a case-only rename on a case-insensitive target through a
	// temporary intermediate path so the destination can be recreated with its new spelling. The move
	// and exists delegates keep the file and directory variants on the same implementation.
	private static void MoveWithCaseOnlyRenameHandling(
		string sourcePath,
		string destinationPath,
		bool isCaseOnlyRename,
		Action<string, string> move,
		Func<string, bool> pathExists)
	{
		if (!isCaseOnlyRename)
		{
			move(sourcePath, destinationPath);
			return;
		}

		string intermediatePath = sourcePath + "." + Guid.NewGuid().ToString("N") + ".rename";
		move(sourcePath, intermediatePath);
		try
		{
			move(intermediatePath, destinationPath);
		}
		catch (Exception moveException)
		{
			if (pathExists(intermediatePath))
			{
				try
				{
					move(intermediatePath, sourcePath);
				}
				catch (Exception rollbackException)
				{
					// The rollback failure must not replace the original move failure: the aggregate keeps
					// both causes, and a caller that wants the classification reads the move exception.
					throw new AggregateException(
						"The case-only rename failed and the source path could not be restored.",
						moveException,
						rollbackException);
				}
			}

			throw;
		}
	}

	// Reads the file into a pre-sized buffer instead of growing a MemoryStream and copying it again
	// with ToArray; a concurrent writer can make the final length differ from the initial one.
	private static async Task<byte[]> ReadBytesAsync(string path, CancellationToken cancellationToken)
	{
		await using FileStream stream = new(
			path,
			FileMode.Open,
			FileAccess.Read,
			FileShare.ReadWrite | FileShare.Delete,
			64 * 1024,
			FileOptions.Asynchronous | FileOptions.SequentialScan);

		long fileLength = stream.Length;
		if (fileLength == 0)
			return [];

		if (fileLength > int.MaxValue)
			throw new IOException("The file is too large to read into memory.");

		byte[] bytes = new byte[(int)fileLength];
		int offset = 0;
		while (offset < bytes.Length)
		{
			int read = await stream.ReadAsync(bytes.AsMemory(offset), cancellationToken).ConfigureAwait(false);
			if (read == 0)
				break;

			offset += read;
		}

		return offset == bytes.Length ? bytes : bytes[..offset];
	}

	// Streams the file once and hashes while reading so the length and hash describe the same byte
	// sequence without buffering the whole file in memory.
	private static async Task<FileStamp> CaptureStampFromFileAsync(string path, CancellationToken cancellationToken)
	{
		await using FileStream stream = new(
			path,
			FileMode.Open,
			FileAccess.Read,
			FileShare.ReadWrite | FileShare.Delete,
			64 * 1024,
			FileOptions.Asynchronous | FileOptions.SequentialScan);

		using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
		long length = 0;
		try
		{
			int read;
			while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
			{
				hash.AppendData(buffer, 0, read);
				length += read;
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}

		return new FileStamp(
			true,
			length,
			File.GetLastWriteTimeUtc(path),
			Convert.ToHexString(hash.GetHashAndReset()));
	}

	private static FileStamp CaptureStampFromBytes(
		string path,
		ReadOnlyMemory<byte> bytes,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
		return new FileStamp(
			true,
			bytes.Length,
			lastWriteTimeUtc,
			Convert.ToHexString(SHA256.HashData(bytes.Span)));
	}

	private static async Task<FileStamp?> TryCaptureStampAsync(string path)
	{
		try
		{
			if (!File.Exists(path))
				return FileStamp.Missing;

			return await CaptureStampFromFileAsync(path, CancellationToken.None).ConfigureAwait(false);
		}
		catch (IOException)
		{
			return null;
		}
		catch (UnauthorizedAccessException)
		{
			return null;
		}
	}
}

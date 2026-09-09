namespace Nickelony.IDEKit.Workspace.Documents.FileSystem;

/// <summary>
/// Contains file content or raw bytes and the metadata captured while reading it.
/// </summary>
/// <remarks>
/// A file-system implementation may provide decoded <see cref="Content"/>, raw bytes through
/// <see cref="RawBytes"/>, or both. <see cref="LocalWorkspaceFileSystem"/>
/// returns raw bytes for an existing file so the document store can apply its selected no-BOM encoding.
/// A missing file is represented by an empty content string, a default-constructed
/// <see cref="TextFileFormat"/>, and <see cref="FileStamp.Missing"/>. Two results are equal only when
/// their <see cref="RawBytes"/> values reference the same underlying storage: record equality never
/// compares byte contents.
/// </remarks>
/// <param name="Content">The decoded content, or an empty string when the file is missing.</param>
/// <param name="FileFormat">The format captured while reading.</param>
/// <param name="OnDiskStamp">The stamp captured while reading.</param>
/// <param name="RawBytes">The raw bytes when the implementation provides them; otherwise, <see langword="null"/>.</param>
/// <param name="IsDirectory">
/// <see langword="true"/> when the path exists as a directory instead of a file. A directory read
/// returns the missing-file shape (<see cref="FileStamp.Missing"/>) and sets this flag so callers can
/// reject the path instead of treating it as a missing file.
/// </param>
public sealed record WorkspaceFileReadResult(
	string Content,
	TextFileFormat FileFormat,
	FileStamp OnDiskStamp,
	ReadOnlyMemory<byte>? RawBytes = null,
	bool IsDirectory = false);

/// <summary>
/// Identifies a temporary encoded file used during a same-volume replacement write.
/// </summary>
/// <remarks>
/// <see cref="ContentHash"/> is the hash of the bytes written to <see cref="Path"/>, formatted as
/// SHA-256 in uppercase hexadecimal. The replacement-failure classification compares this hash and
/// <see cref="Length"/> ordinally against a re-captured <see cref="FileStamp"/>, so an
/// implementation must keep that format. The caller owns cleanup of the temporary path through
/// <see cref="IWorkspaceFileSystem.DeleteTemporaryAsync(WorkspaceTemporaryFile)"/>.
/// The temporary file is created in the destination's directory so the final replacement stays on one
/// volume; the guarantees of the final step depend on the host file system and platform.
/// </remarks>
/// <param name="Path">The path of the temporary file.</param>
/// <param name="Length">The byte length of the temporary file.</param>
/// <param name="ContentHash">The hash of the bytes written to the temporary file: SHA-256, uppercase hexadecimal.</param>
public sealed record WorkspaceTemporaryFile(
	string Path,
	long Length,
	string ContentHash);

/// <summary>
/// Describes the outcome of conditionally replacing a destination file.
/// </summary>
public enum WorkspaceFileReplacementStatus
{
	/// <summary>The destination was replaced after its stamp matched the expected stamp.</summary>
	Replaced,

	/// <summary>The destination stamp did not match the expected stamp.</summary>
	ExternalFileConflict,

	/// <summary>A directory occupies the destination path where the replacement expected no file, so the temporary file cannot be moved onto it.</summary>
	DestinationExists,

	/// <summary>The replacement may have occurred, but its final state could not be determined.</summary>
	ReplacementStateUnknown,

	/// <summary>The replacement did not complete because of an unexpected error.</summary>
	Failed,

	/// <summary>The replacement was canceled before the destination was touched.</summary>
	Canceled
}

/// <summary>
/// Contains the outcome of replacing a destination file.
/// </summary>
/// <remarks>
/// <see cref="ObservedOnDiskStamp"/> reports the stamp observed during conflict detection or the
/// resulting destination stamp after replacement when it could be captured. <see cref="Failure"/>
/// explains a failed or indeterminate operation.
/// </remarks>
/// <param name="Status">The replacement outcome.</param>
/// <param name="ObservedOnDiskStamp">The stamp observed during conflict detection or after replacement, when available.</param>
/// <param name="Failure">Explains a failed or indeterminate operation, when one occurred.</param>
public sealed record WorkspaceFileReplacementResult(
	WorkspaceFileReplacementStatus Status,
	FileStamp? ObservedOnDiskStamp = null,
	WorkspaceOperationFailure? Failure = null);

/// <summary>
/// Describes the outcome of moving a file or directory after validating the source when applicable.
/// </summary>
public enum WorkspaceFileMoveStatus
{
	/// <summary>The source was moved.</summary>
	Moved,

	/// <summary>The destination already exists.</summary>
	DestinationExists,

	/// <summary>The source stamp did not match the expected stamp.</summary>
	ExternalFileConflict,

	/// <summary>The move did not complete because of an unexpected error.</summary>
	MoveFailed,

	/// <summary>The move failed and its final state could not be established; for example, a case-only rename whose rollback move also failed.</summary>
	MoveStateUnknown,

	/// <summary>The move was canceled.</summary>
	Canceled
}

/// <summary>
/// Contains the outcome of moving a file or directory.
/// </summary>
/// <remarks>
/// <see cref="ObservedOnDiskStamp"/> is populated when a source-stamp conflict is observed.
/// </remarks>
/// <param name="Status">The move outcome.</param>
/// <param name="ObservedOnDiskStamp">The source stamp observed when a conflict was detected, when available.</param>
/// <param name="Failure">Explains a failed operation, when one occurred.</param>
public sealed record WorkspaceFileMoveResult(
	WorkspaceFileMoveStatus Status,
	FileStamp? ObservedOnDiskStamp = null,
	WorkspaceOperationFailure? Failure = null);

/// <summary>
/// Describes the outcome of deleting a file or directory.
/// </summary>
public enum WorkspaceFileDeleteStatus
{
	/// <summary>The file or directory is absent after the delete operation.</summary>
	Deleted,

	/// <summary>The expected stamp did not match for a delete operation that validates a stamp.</summary>
	ExternalFileConflict,

	/// <summary>The delete did not complete because of an unexpected error.</summary>
	DeleteFailed,

	/// <summary>The delete operation was canceled.</summary>
	Canceled
}

/// <summary>
/// Contains the outcome of deleting a file or directory.
/// </summary>
/// <remarks>
/// A file delete validates its expected stamp before deleting. Directory deletion is recursive and
/// does not use a stamp because the directory operation has no expected-stamp parameter.
/// </remarks>
/// <param name="Status">The delete outcome.</param>
/// <param name="ObservedOnDiskStamp">The stamp observed when a conflict or failure was detected, when available.</param>
/// <param name="Failure">Explains a failed operation, when one occurred.</param>
public sealed record WorkspaceFileDeleteResult(
	WorkspaceFileDeleteStatus Status,
	FileStamp? ObservedOnDiskStamp = null,
	WorkspaceOperationFailure? Failure = null);

/// <summary>
/// Provides asynchronous file-system operations required by the workspace authority.
/// </summary>
/// <remarks>
/// <para>
/// Implementations return operation results for expected environmental failures and may throw for
/// failures that the implementation cannot translate. Paths supplied by the store are normalized
/// document ids: the file read, stamp, replace, and delete paths, the temporary-file directory, the
/// move source, and the directory move and delete paths. The one exception is the destination of
/// <see cref="MoveAsync(string, string, FileStamp, CancellationToken)"/>, which can retain the
/// caller's display spelling so a renamed file keeps it.
/// </para>
/// <para>
/// Implementations are not required to perform the described work asynchronously; synchronous I/O
/// wrapped in a completed task is acceptable, and callers must not assume that an operation
/// completes off the calling thread.
/// </para>
/// <para>
/// Implementations should await their own asynchronous work with <c>ConfigureAwait(false)</c>: a host
/// can block its calling thread while a store operation runs (for example the manager's synchronous
/// open), and a continuation that captures that context would deadlock.
/// </para>
/// <para>
/// Deletion is permanent unless the implementation chooses otherwise; a host that needs platform
/// deletion behavior such as a trash folder or another recoverable store supplies a
/// <see cref="WorkspaceFileSystemDecorator"/> instead of relying on the default
/// <see cref="LocalWorkspaceFileSystem"/>.
/// </para>
/// <para>
/// A cancellation token is observed before any work starts and again before a mutation: members
/// whose result vocabulary carries a canceled status report it instead of performing the operation,
/// and members whose result cannot carry one throw <see cref="OperationCanceledException"/>.
/// </para>
/// </remarks>
public interface IWorkspaceFileSystem
{
	/// <summary>Reads a file and captures its content stamp.</summary>
	/// <remarks>
	/// An implementation may return decoded <see cref="WorkspaceFileReadResult.Content"/> or raw bytes
	/// in <see cref="WorkspaceFileReadResult.RawBytes"/>. Missing files return
	/// <see cref="FileStamp.Missing"/>. A path that exists as a directory must set
	/// <see cref="WorkspaceFileReadResult.IsDirectory"/> and return the missing-file shape; callers
	/// treat any other result shape as a file.
	/// </remarks>
	/// <param name="path">The file path to read.</param>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>The read content or raw bytes together with the captured stamp.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
	/// <exception cref="OperationCanceledException">The read was canceled.</exception>
	Task<WorkspaceFileReadResult> ReadAsync(
		string path,
		CancellationToken cancellationToken = default);

	/// <summary>Captures the current stamp for a file.</summary>
	/// <remarks>
	/// Capturing a stamp for an existing file reads and hashes its content, so the operation cost is
	/// proportional to the file size. A file that disappears or becomes inaccessible between the
	/// existence probe and the read surfaces the underlying I/O exception. The captured length and
	/// hash describe one byte sequence; a writer that changes the file mid-capture can produce a stamp
	/// that matches neither the old nor the new state, which the next expected-stamp check reports as
	/// a conflict instead of accepting a torn read as current.
	/// </remarks>
	/// <param name="path">The file path to inspect.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>The current stamp, or <see cref="FileStamp.Missing"/> when the file does not exist.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
	/// <exception cref="OperationCanceledException">The capture was canceled.</exception>
	Task<FileStamp> CaptureStampAsync(
		string path,
		CancellationToken cancellationToken = default);

	/// <summary>Writes bytes to a temporary file.</summary>
	/// <param name="directory">The directory in which to create the temporary file.</param>
	/// <param name="content">The bytes to write.</param>
	/// <param name="cancellationToken">Cancels the write.</param>
	/// <returns>The temporary-file descriptor the caller must delete with <see cref="DeleteTemporaryAsync(WorkspaceTemporaryFile)"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="directory"/> is <see langword="null"/>.</exception>
	/// <exception cref="OperationCanceledException">The write was canceled.</exception>
	Task<WorkspaceTemporaryFile> WriteTemporaryAsync(
		string directory,
		ReadOnlyMemory<byte> content,
		CancellationToken cancellationToken = default);

	/// <summary>Conditionally replaces a destination file after validating its expected stamp.</summary>
	/// <remarks>
	/// The destination is re-validated against <paramref name="expectedStamp"/> before the temporary
	/// file is moved, and a canceled token is observed before the destination is touched, so a
	/// <see cref="WorkspaceFileReplacementStatus.Canceled"/> result means the destination was not
	/// replaced. When the expected stamp represents a missing file and a directory occupies the
	/// destination path, the replacement reports
	/// <see cref="WorkspaceFileReplacementStatus.DestinationExists"/> instead of attempting to move the
	/// temporary file onto the directory.
	/// </remarks>
	/// <param name="temporaryFile">The temporary file to move into the destination.</param>
	/// <param name="destinationPath">The file path to replace or create.</param>
	/// <param name="expectedStamp">The destination stamp that must still match.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>The replacement outcome, including the observed destination stamp when it could be captured.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="temporaryFile"/> or <paramref name="destinationPath"/> is <see langword="null"/>.
	/// </exception>
	Task<WorkspaceFileReplacementResult> ReplaceFileAsync(
		WorkspaceTemporaryFile temporaryFile,
		string destinationPath,
		FileStamp expectedStamp,
		CancellationToken cancellationToken = default);

	/// <summary>Moves a file after validating its expected source stamp.</summary>
	/// <remarks>
	/// A move that fails after its destination may have been touched reports
	/// <see cref="WorkspaceFileMoveStatus.MoveStateUnknown"/> when its final state cannot be
	/// established, for example a case-only rename whose rollback move also failed.
	/// </remarks>
	/// <param name="sourcePath">The file path to move.</param>
	/// <param name="destinationPath">The destination path, which may retain the caller's display spelling.</param>
	/// <param name="expectedSourceStamp">The source stamp that must still match.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>The move outcome, including the observed source stamp when a conflict was detected.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="sourcePath"/> or <paramref name="destinationPath"/> is <see langword="null"/>.
	/// </exception>
	Task<WorkspaceFileMoveResult> MoveAsync(
		string sourcePath,
		string destinationPath,
		FileStamp expectedSourceStamp,
		CancellationToken cancellationToken = default);

	/// <summary>Moves a directory without a source-stamp precondition.</summary>
	/// <param name="sourcePath">The directory path to move.</param>
	/// <param name="destinationPath">The destination directory path.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>The move outcome, including the unknown state of a failed case-only rename that could not be rolled back.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="sourcePath"/> or <paramref name="destinationPath"/> is <see langword="null"/>.
	/// </exception>
	Task<WorkspaceFileMoveResult> MoveDirectoryAsync(
		string sourcePath,
		string destinationPath,
		CancellationToken cancellationToken = default);

	/// <summary>Deletes a file after validating its expected stamp.</summary>
	/// <remarks>
	/// The default <see cref="LocalWorkspaceFileSystem"/> deletes permanently; implementations that
	/// choose a recoverable policy such as a trash folder or shell recycle bin document it on their
	/// own type. The document store's delete member follows this behavior.
	/// </remarks>
	/// <param name="path">The file path to delete.</param>
	/// <param name="expectedStamp">The file stamp that must still match.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>The deletion outcome; a path that is absent after a matching stamp reports <see cref="WorkspaceFileDeleteStatus.Deleted"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
	Task<WorkspaceFileDeleteResult> DeleteAsync(
		string path,
		FileStamp expectedStamp,
		CancellationToken cancellationToken = default);

	/// <summary>Deletes a directory recursively.</summary>
	/// <remarks>The default <see cref="LocalWorkspaceFileSystem"/> deletes permanently; the document store's directory-delete member follows this behavior.</remarks>
	/// <param name="path">The directory path to delete.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>The deletion outcome; a path that is absent reports <see cref="WorkspaceFileDeleteStatus.Deleted"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
	Task<WorkspaceFileDeleteResult> DeleteDirectoryAsync(
		string path,
		CancellationToken cancellationToken = default);

	/// <summary>Deletes a temporary file.</summary>
	/// <remarks>
	/// Cleanup is deliberately not cancelable: the caller runs it after a replacement so that a
	/// canceled operation cannot leave the temporary file behind. A missing temporary file is not an
	/// error.
	/// </remarks>
	/// <param name="temporaryFile">The temporary file to delete.</param>
	/// <returns>A task that completes when the deletion finished.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="temporaryFile"/> is <see langword="null"/>.</exception>
	Task DeleteTemporaryAsync(WorkspaceTemporaryFile temporaryFile);
}

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Identifies the text encoding used to decode or persist a workspace document.
/// </summary>
public enum TextEncodingKind
{
	/// <summary>UTF-8 encoding.</summary>
	Utf8,

	/// <summary>Little-endian UTF-16 encoding.</summary>
	Utf16LittleEndian,

	/// <summary>Big-endian UTF-16 encoding.</summary>
	Utf16BigEndian,

	/// <summary>Windows-1252 encoding.</summary>
	Windows1252
}

/// <summary>
/// Identifies the newline convention detected in workspace document content.
/// This value describes the content; it does not cause <see cref="WorkspaceFileCodec.Encode(string, TextFileFormat)"/>
/// to rewrite newline characters.
/// </summary>
public enum TextNewlineStyle
{
	/// <summary>Carriage return followed by line feed.</summary>
	CrLf,

	/// <summary>Line feed.</summary>
	Lf,

	/// <summary>Carriage return.</summary>
	Cr,

	/// <summary>More than one newline style.</summary>
	Mixed,

	/// <summary>No newline characters.</summary>
	None
}

/// <summary>
/// Describes the encoding, byte-order mark, and detected newline format of a workspace file.
/// </summary>
public readonly record struct TextFileFormat(
	TextEncodingKind Encoding,
	bool HasBom,
	TextNewlineStyle NewlineStyle);

/// <summary>
/// Captures the on-disk state used to detect changes to a workspace file.
/// </summary>
/// <remarks>
/// For an existing file, the stamp includes its byte length, last-write time, and content hash.
/// <see cref="Missing"/> represents a file that does not exist.
/// </remarks>
public readonly record struct FileStamp(
	bool Exists,
	long? Length,
	DateTime? LastWriteTimeUtc,
	string? ContentHash)
{
	/// <summary>
	/// Gets the stamp for a file that does not exist.
	/// </summary>
	public static FileStamp Missing => new(false, null, null, null);
}

/// <summary>
/// Specifies format defaults used when opening or creating a workspace document.
/// </summary>
/// <remarks>
/// <see cref="NoBomEncoding"/> is used only when an existing file has no recognized byte-order mark.
/// <see cref="NewFileFormat"/> is used when the requested path does not exist.
/// </remarks>
public readonly record struct WorkspaceDocumentOpenOptions(
	TextEncodingKind NoBomEncoding,
	TextFileFormat NewFileFormat);

/// <summary>
/// Describes a failure returned by a workspace document operation.
/// </summary>
/// <remarks>
/// <see cref="Code"/> is a stable category for the failure. <see cref="Exception"/> may contain the
/// underlying exception when one was available; callers should use <see cref="Message"/> for display
/// or logging rather than depending on an exception being present.
/// </remarks>
public sealed record WorkspaceOperationFailure(
	string Code,
	string Message,
	Exception? Exception = null);

/// <summary>
/// Contains file content or raw bytes and the metadata captured while reading it.
/// </summary>
/// <remarks>
/// A file-system implementation may provide decoded <see cref="Content"/>, raw bytes through
/// <see cref="RawBytes"/>, or both. <see cref="WorkspaceFileCodec.ReadAsync(string, CancellationToken)"/>
/// returns raw bytes for an existing file so the document store can apply its selected no-BOM encoding.
/// A missing file is represented by an empty content string, the default format, and
/// <see cref="FileStamp.Missing"/>.
/// </remarks>
public sealed record WorkspaceFileReadResult(
	string Content,
	TextFileFormat FileFormat,
	FileStamp OnDiskStamp,
	ReadOnlyMemory<byte>? RawBytes = null);

/// <summary>
/// Identifies a temporary encoded file used during an atomic write operation.
/// </summary>
/// <remarks>
/// <see cref="ContentHash"/> is the hash of the bytes written to <see cref="Path"/>. The caller owns
/// cleanup of the temporary path through <see cref="IWorkspaceFileSystem.DeleteTemporaryAsync(WorkspaceTemporaryFile)"/>.
/// </remarks>
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

	/// <summary>The replacement may have occurred, but its final state could not be determined.</summary>
	ReplacementStateUnknown,

	/// <summary>The replacement failed.</summary>
	Failed
}

/// <summary>
/// Contains the outcome of replacing a destination file.
/// </summary>
/// <remarks>
/// <see cref="ObservedOnDiskStamp"/> reports the stamp observed during conflict detection or after
/// an indeterminate replacement when it could be captured. <see cref="Failure"/> explains a failed
/// or indeterminate operation.
/// </remarks>
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

	/// <summary>The move failed.</summary>
	MoveFailed,

	/// <summary>The move was cancelled.</summary>
	Cancelled
}

/// <summary>
/// Contains the outcome of moving a file or directory.
/// </summary>
/// <remarks>
/// <see cref="ObservedOnDiskStamp"/> is populated when a source-stamp conflict is observed.
/// </remarks>
public sealed record WorkspaceFileMoveResult(
	WorkspaceFileMoveStatus Status,
	FileStamp? ObservedOnDiskStamp = null,
	WorkspaceOperationFailure? Failure = null);

/// <summary>
/// Describes the outcome of deleting a file or directory.
/// </summary>
public enum WorkspaceFileDeleteStatus
{
	/// <summary>The file or directory was deleted.</summary>
	Deleted,

	/// <summary>The target changed unexpectedly.</summary>
	ExternalFileConflict,

	/// <summary>The delete operation failed.</summary>
	DeleteFailed,

	/// <summary>The delete operation was cancelled.</summary>
	Cancelled
}

/// <summary>
/// Contains the outcome of deleting a file or directory.
/// </summary>
/// <remarks>
/// A file delete validates its expected stamp before deleting. Directory deletion is recursive and
/// does not use a stamp because the directory operation has no expected-stamp parameter.
/// </remarks>
public sealed record WorkspaceFileDeleteResult(
	WorkspaceFileDeleteStatus Status,
	FileStamp? ObservedOnDiskStamp = null,
	WorkspaceOperationFailure? Failure = null);

/// <summary>
/// Provides asynchronous file-system operations required by the workspace authority.
/// </summary>
/// <remarks>
/// Implementations return operation results for expected environmental failures and may throw for
/// failures that the implementation cannot translate. File paths supplied by the document store are
/// normalized document paths unless a method explicitly receives a display or directory path.
/// </remarks>
public interface IWorkspaceFileSystem
{
	/// <summary>Reads a file and captures its content stamp.</summary>
	/// <remarks>
	/// An implementation may return decoded <see cref="WorkspaceFileReadResult.Content"/> or raw bytes
	/// in <see cref="WorkspaceFileReadResult.RawBytes"/>. Missing files return
	/// <see cref="FileStamp.Missing"/>.
	/// </remarks>
	Task<WorkspaceFileReadResult> ReadAsync(
		string path,
		CancellationToken cancellationToken);

	/// <summary>Captures the current stamp for a file.</summary>
	Task<FileStamp> CaptureStampAsync(
		string path,
		CancellationToken cancellationToken);

	/// <summary>Writes an encoded temporary file.</summary>
	Task<WorkspaceTemporaryFile> WriteTemporaryAsync(
		string directory,
		ReadOnlyMemory<byte> content,
		CancellationToken cancellationToken);

	/// <summary>Conditionally replaces a destination file after validating its expected stamp.</summary>
	Task<WorkspaceFileReplacementResult> ReplaceAsync(
		WorkspaceTemporaryFile temporaryFile,
		string destinationPath,
		FileStamp expectedStamp,
		CancellationToken cancellationToken);

	/// <summary>Moves a file after validating its expected source stamp.</summary>
	Task<WorkspaceFileMoveResult> MoveAsync(
		string sourcePath,
		string destinationPath,
		FileStamp expectedSourceStamp,
		CancellationToken cancellationToken);

	/// <summary>Moves a directory without a source-stamp precondition.</summary>
	Task<WorkspaceFileMoveResult> MoveDirectoryAsync(
		string sourcePath,
		string destinationPath,
		CancellationToken cancellationToken);

	/// <summary>Deletes a file after validating its expected stamp.</summary>
	Task<WorkspaceFileDeleteResult> DeleteAsync(
		string path,
		FileStamp expectedStamp,
		CancellationToken cancellationToken,
		bool useRecycleBin = false);

	/// <summary>Deletes a directory recursively.</summary>
	Task<WorkspaceFileDeleteResult> DeleteDirectoryAsync(
		string path,
		CancellationToken cancellationToken,
		bool useRecycleBin = false);

	/// <summary>Deletes a temporary file.</summary>
	Task DeleteTemporaryAsync(WorkspaceTemporaryFile temporaryFile);
}

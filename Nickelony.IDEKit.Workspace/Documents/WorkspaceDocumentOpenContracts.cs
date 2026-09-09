namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Describes the outcome of opening or loading a workspace document.
/// </summary>
/// <remarks><see cref="AlreadyOpen"/> means the existing tracked snapshot was returned without another load.</remarks>
public enum WorkspaceDocumentOpenStatus
{
	/// <summary>The document was loaded from disk, or created as a new empty document when the path does not exist.</summary>
	Opened,

	/// <summary>The document was already open.</summary>
	AlreadyOpen,

	/// <summary>The path is invalid.</summary>
	InvalidPath,

	/// <summary>
	/// The path does not exist and the open options did not allow creating a new document
	/// (<see cref="WorkspaceDocumentOpenOptions.CreateIfMissing"/> is <see langword="false"/>).
	/// </summary>
	NotFound,

	/// <summary>
	/// The path exists as a directory, not a file. A directory cannot be tracked as a document: every
	/// later write or delete through the store would fail for it, so the path is rejected at open
	/// instead of being tracked as a new empty document.
	/// </summary>
	IsDirectory,

	/// <summary>The document could not be loaded.</summary>
	LoadFailed,

	/// <summary>The operation was canceled.</summary>
	Canceled
}

/// <summary>
/// Specifies format defaults used when opening or creating a workspace document.
/// </summary>
/// <remarks>
/// <see cref="NoBomEncoding"/> is used only when an existing file has no recognized byte-order mark.
/// <see cref="NewFileFormat"/> is used when the requested path does not exist and
/// <see cref="CreateIfMissing"/> allows creating a document for it. Both format values are validated
/// when the store opens the path: an undefined encoding, or a NewFileFormat that combines
/// Windows-1252 with a byte-order mark, is an argument error instead of a value that can only fail
/// much later when the document is committed.
/// </remarks>
/// <param name="NoBomEncoding">The encoding to use when an existing file has no byte-order mark.</param>
/// <param name="NewFileFormat">The format to use when the requested path does not exist.</param>
/// <param name="CreateIfMissing">
/// <see langword="true"/> (the default) to open a missing path as a new empty document;
/// <see langword="false"/> to report <see cref="WorkspaceDocumentOpenStatus.NotFound"/> instead, which
/// headless hosts use to surface a missing file as an error rather than as new content.
/// </param>
public readonly record struct WorkspaceDocumentOpenOptions(
	TextEncodingKind NoBomEncoding,
	TextFileFormat NewFileFormat,
	bool CreateIfMissing = true);

/// <summary>
/// Contains the outcome of opening or loading a workspace document.
/// </summary>
/// <remarks>
/// <see cref="WorkspaceDocumentOpenStatus.Opened"/> and
/// <see cref="WorkspaceDocumentOpenStatus.AlreadyOpen"/> results carry a snapshot; every failure
/// outcome returns none, and a load failure additionally carries a typed
/// <see cref="WorkspaceOperationFailure"/>.
/// </remarks>
/// <param name="Status">The open outcome.</param>
/// <param name="Snapshot">The loaded or already-open snapshot; otherwise, <see langword="null"/>.</param>
/// <param name="Failure">Explains a load failure, when one occurred.</param>
public sealed record WorkspaceDocumentOpenResult(
	WorkspaceDocumentOpenStatus Status,
	WorkspaceDocumentSnapshot? Snapshot,
	WorkspaceOperationFailure? Failure = null);

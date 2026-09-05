using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Identifies one logical incarnation of a workspace document.
/// </summary>
/// <remarks>
/// The key remains stable when the document is renamed or saved at a new path. A document opened
/// after the previous instance was deleted receives a new key.
/// </remarks>
public sealed record WorkspaceDocumentKey(Guid Value);

/// <summary>
/// Captures the authoritative logical and persisted state of a workspace document.
/// </summary>
/// <remarks>
/// <see cref="DocumentId"/> is the normalized path used by the store to identify the document;
/// <see cref="DisplayPath"/> is the supplied path retained for display and for choosing the
/// temporary-file directory during commits. The logical content is in <see cref="Text"/>.
/// <see cref="PersistedVersion"/> identifies the version
/// represented by the persisted content, so <see cref="IsDirty"/> can remain <see langword="true"/>
/// after a later logical edit occurs while an earlier snapshot is being committed.
/// </remarks>
public sealed record WorkspaceDocumentSnapshot(
	WorkspaceDocumentKey DocumentKey,
	string DocumentId,
	string DisplayPath,
	long Version,
	long PersistedVersion,
	bool IsDirty,
	ITextSnapshot Text,
	TextFileFormat FileFormat,
	FileStamp OnDiskStamp)
{
	/// <summary>
	/// Gets a value indicating whether the document currently has an on-disk file stamp.
	/// </summary>
	/// <value><see langword="true"/> when <see cref="OnDiskStamp"/> represents an existing file.</value>
	public bool ExistsOnDisk => OnDiskStamp.Exists;

	/// <summary>
	/// Gets the complete logical document content represented by <see cref="Text"/>.
	/// </summary>
	public string Content => Text.GetText(0, Text.TextLength);
}

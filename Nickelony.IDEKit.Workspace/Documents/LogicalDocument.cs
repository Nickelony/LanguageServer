namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Holds the logical content and persistence state of an open workspace document.
/// </summary>
internal sealed class LogicalDocument
{
	public LogicalDocument(
		WorkspaceDocumentKey documentKey,
		string documentId,
		string displayPath,
		string content,
		TextFileFormat fileFormat,
		FileStamp onDiskStamp,
		TextEncodingKind noBomEncoding)
	{
		DocumentKey = documentKey;
		DocumentId = documentId;
		DisplayPath = displayPath;
		Content = content;
		PersistedContent = content;
		FileFormat = fileFormat;
		PersistedFileFormat = fileFormat;
		OnDiskStamp = onDiskStamp;
		NoBomEncoding = noBomEncoding;
	}

	public WorkspaceDocumentKey DocumentKey { get; }

	public string DocumentId { get; set; }

	public string DisplayPath { get; set; }

	public string Content { get; set; }

	public string PersistedContent { get; set; }

	public TextFileFormat FileFormat { get; set; }

	public TextFileFormat PersistedFileFormat { get; set; }

	public FileStamp OnDiskStamp { get; set; }

	public TextEncodingKind NoBomEncoding { get; }

	public SemaphoreSlim DiskOperationGate { get; } = new(1, 1);

	public bool DeleteOperationActive { get; set; }

	public long Version { get; set; }

	public long PersistedVersion { get; set; }
}

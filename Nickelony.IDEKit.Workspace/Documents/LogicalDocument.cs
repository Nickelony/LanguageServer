namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Holds the logical content and persistence state of an open workspace document.
/// </summary>
internal sealed class LogicalDocument
{
	private string _content;
	private string _persistedContent;
	private TextFileFormat _fileFormat;
	private TextFileFormat _persistedFileFormat;
	private bool? _isDirty;

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
		_content = content;
		_persistedContent = content;
		_fileFormat = fileFormat;
		_persistedFileFormat = fileFormat;
		OnDiskStamp = onDiskStamp;
		NoBomEncoding = noBomEncoding;
	}

	public WorkspaceDocumentKey DocumentKey { get; }

	public string DocumentId { get; set; }

	public string DisplayPath { get; set; }

	public string Content
	{
		get => _content;
		set
		{
			_content = value;
			_isDirty = null;
		}
	}

	public string PersistedContent
	{
		get => _persistedContent;
		set
		{
			_persistedContent = value;
			_isDirty = null;
		}
	}

	public TextFileFormat FileFormat
	{
		get => _fileFormat;
		set
		{
			_fileFormat = value;
			_isDirty = null;
		}
	}

	public TextFileFormat PersistedFileFormat
	{
		get => _persistedFileFormat;
		set
		{
			_persistedFileFormat = value;
			_isDirty = null;
		}
	}

	// The comparison reads the full content, so the result is cached until one of the four state
	// fields changes. Snapshot creation can therefore run many times without repeating the scan.
	public bool IsDirty => _isDirty ??=
		!string.Equals(_content, _persistedContent, StringComparison.Ordinal)
		|| _fileFormat != _persistedFileFormat;

	public FileStamp OnDiskStamp { get; set; }

	public TextEncodingKind NoBomEncoding { get; }

	public SemaphoreSlim DiskOperationGate { get; } = new(1, 1);

	public bool DeleteOperationActive { get; set; }

	// Signals the completion of an in-flight delete so an open request for the same document can
	// wait for the delete to resolve instead of returning an instance that is about to be removed.
	// Set and cleared together with DeleteOperationActive under the store state lock.
	public TaskCompletionSource? DeleteCompletion { get; set; }

	public long Version { get; set; }

	public long PersistedVersion { get; set; }
}

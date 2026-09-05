namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Represents a non-atomic before-and-after change set from a workspace-edit application.
/// The historical type name is retained for compatibility; this type does not provide rollback
/// or all-target atomicity.
/// </summary>
public sealed class TextWorkspaceEditTransaction
{
	/// <summary>
	/// Creates a transaction from the confirmed before-and-after document changes.
	/// </summary>
	/// <param name="documentChanges">The confirmed before-and-after document changes to capture.</param>
	public TextWorkspaceEditTransaction(IReadOnlyList<TextWorkspaceDocumentChange> documentChanges)
	{
		ArgumentNullException.ThrowIfNull(documentChanges);
		DocumentChanges = Array.AsReadOnly([.. documentChanges]);
	}

	/// <summary>
	/// Gets the per-document changes captured in the transaction. The caller-provided collection is
	/// copied into owned read-only storage so later caller mutations cannot leak into the transaction.
	/// </summary>
	public IReadOnlyList<TextWorkspaceDocumentChange> DocumentChanges { get; }

	/// <summary>
	/// Gets a value indicating whether the transaction contains any changes.
	/// </summary>
	public bool HasChanges => DocumentChanges.Count > 0;
}

/// <summary>
/// Represents the requested before and after contents of a document target.
/// </summary>
public sealed class TextWorkspaceDocumentChange
{
	/// <summary>
	/// Creates a document change record.
	/// </summary>
	/// <param name="filePath">The path or identifier of the changed document.</param>
	/// <param name="beforeContent">The content before the requested transformation.</param>
	/// <param name="afterContent">The requested content after the transformation.</param>
	public TextWorkspaceDocumentChange(string filePath, string beforeContent, string afterContent)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(beforeContent);
		ArgumentNullException.ThrowIfNull(afterContent);

		FilePath = filePath;
		BeforeContent = beforeContent;
		AfterContent = afterContent;
	}

	/// <summary>
	/// Gets the document path or target identifier.
	/// </summary>
	public string FilePath { get; }

	/// <summary>
	/// Gets the document content before the requested transformation.
	/// </summary>
	public string BeforeContent { get; }

	/// <summary>
	/// Gets the requested document content after the transformation.
	/// </summary>
	public string AfterContent { get; }
}

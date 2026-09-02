namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Represents a non-atomic before-and-after change set from a workspace-edit application.
/// The historical type name is retained for compatibility; this type does not provide rollback
/// or all-target atomicity.
/// </summary>
/// <param name="documentChanges">The confirmed before-and-after document changes to capture.</param>
public sealed class TextWorkspaceEditTransaction(IReadOnlyList<TextWorkspaceDocumentChange> documentChanges)
{
	/// <summary>
	/// Gets the per-document changes captured in the transaction. The caller-provided collection is
	/// copied into owned read-only storage so later caller mutations cannot leak into the transaction.
	/// </summary>
	public IReadOnlyList<TextWorkspaceDocumentChange> DocumentChanges { get; } = Array.AsReadOnly([.. (documentChanges ?? [])]);

	/// <summary>
	/// Gets a value indicating whether the transaction contains any changes.
	/// </summary>
	public bool HasChanges => DocumentChanges.Count > 0;
}

/// <summary>
/// Represents the requested before and after contents of a document target.
/// </summary>
/// <param name="filePath">The path or identifier of the changed document.</param>
/// <param name="beforeContent">The content before the requested transformation.</param>
/// <param name="afterContent">The requested content after the transformation.</param>
public sealed class TextWorkspaceDocumentChange(string filePath, string beforeContent, string afterContent)
{
	/// <summary>
	/// Gets the document path or target identifier.
	/// </summary>
	public string FilePath { get; } = filePath ?? string.Empty;

	/// <summary>
	/// Gets the document content before the requested transformation.
	/// </summary>
	public string BeforeContent { get; } = beforeContent ?? string.Empty;

	/// <summary>
	/// Gets the requested document content after the transformation.
	/// </summary>
	public string AfterContent { get; } = afterContent ?? string.Empty;
}

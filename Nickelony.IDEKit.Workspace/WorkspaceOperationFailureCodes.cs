namespace Nickelony.IDEKit.Workspace;

/// <summary>
/// Lists the stable failure codes produced by workspace operations.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Documents.WorkspaceOperationFailure.Code"/> carries one of these values for failures produced by
/// the workspace packages. A custom file-system implementation reports its own failures with the codes
/// that describe its operation; reusing these spellings where they fit keeps host handling uniform.
/// </para>
/// <para>
/// The codes are registered in one neutral place for the whole library, including the view failures
/// produced by the view-manager slice, so hosts can match on one list regardless of the slice that
/// produced the failure. The class lives outside the <c>Documents</c> namespace because the codes are
/// shared vocabulary rather than document-authority surface.
/// </para>
/// </remarks>
public static class WorkspaceOperationFailureCodes
{
	/// <summary>The document could not be loaded from disk when it was opened.</summary>
	public const string LoadFailed = "LoadFailed";

	/// <summary>The document could not be written to disk.</summary>
	public const string WriteFailed = "WriteFailed";

	/// <summary>The document could not be read from disk during a reload or conflict resolution.</summary>
	public const string ReadFailed = "ReadFailed";

	/// <summary>A path exists as a directory where the operation requires a file.</summary>
	public const string IsDirectory = "IsDirectory";

	/// <summary>The disk file changed in a way that conflicts with the caller's expectation.</summary>
	public const string ExternalFileConflict = "ExternalFileConflict";

	/// <summary>The file content is not valid for the selected encoding: bytes that cannot be decoded, or characters that cannot be encoded. Reported with the failing operation's own status; hosts that branch on status alone can match this code to distinguish an encoding problem from an I/O problem.</summary>
	public const string InvalidEncoding = "InvalidEncoding";

	/// <summary>A file or directory move did not complete.</summary>
	public const string MoveFailed = "MoveFailed";

	/// <summary>The move failed and its final state could not be established; for example, a case-only rename whose rollback move also failed.</summary>
	public const string MoveStateUnknown = "MoveStateUnknown";

	/// <summary>A file or directory delete did not complete.</summary>
	public const string DeleteFailed = "DeleteFailed";

	/// <summary>A file or directory could not be accessed because of a permission failure.</summary>
	public const string AccessDenied = "AccessDenied";

	/// <summary>A move or save-as destination already exists.</summary>
	public const string DestinationExists = "DestinationExists";

	/// <summary>The replacement may have completed, but its final state could not be established.</summary>
	public const string ReplacementStateUnknown = "ReplacementStateUnknown";

	/// <summary>The operation was canceled before it completed.</summary>
	public const string Canceled = "Canceled";

	/// <summary>A prepared workspace-edit target was rejected without the document being mutated.</summary>
	public const string TargetNotChanged = "TargetNotChanged";

	/// <summary>A prepared workspace-edit target was not attempted because an earlier target for the same document was rejected.</summary>
	public const string TargetSkipped = "TargetSkipped";

	/// <summary>A prepared workspace-edit target returned a replacement outcome outside the document authority's vocabulary, so its final state is unknown.</summary>
	public const string TargetOutcomeUnknown = "TargetOutcomeUnknown";

	/// <summary>A prepared workspace-edit target's replacement delegate threw, so its final state is unknown.</summary>
	public const string TargetApplicationFailed = "TargetApplicationFailed";

	/// <summary>Opening a document with a view failed: the view did not accept the attachment, or the open path threw an unexpected exception.</summary>
	public const string OpenFailed = "OpenFailed";

	/// <summary>A view could not refresh from workspace content.</summary>
	public const string ViewRefreshFailed = "ViewRefreshFailed";

	/// <summary>A view could not acknowledge an applied mutation.</summary>
	public const string ViewAcknowledgeFailed = "ViewAcknowledgeFailed";

	/// <summary>A view could not apply a document identity change.</summary>
	public const string ViewIdentityUpdateFailed = "ViewIdentityUpdateFailed";
}

namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>
/// Describes whether a view may attach to a workspace document.
/// </summary>
internal enum ViewValidation
{
	Valid,
	AlreadyRegistered,
	DuplicateViewId,
	UnavailableForAttachment
}

using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>
/// Binds a view to the workspace snapshot it is currently attached to.
/// </summary>
internal sealed record ViewBinding(
	IWorkspaceDocumentView View,
	WorkspaceDocumentSnapshot Snapshot);

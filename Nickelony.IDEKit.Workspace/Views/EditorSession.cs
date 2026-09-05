using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>
/// Determines whether a leased editor view remains open when its session ends.
/// </summary>
public enum EditorSessionMode
{
	/// <summary>The leased view stays open after the session ends.</summary>
	Persistent,

	/// <summary>An acquired view is closed when the session ends.</summary>
	Transient
}

/// <summary>
/// Configures how an editor view is opened through an <see cref="IEditorViewHost"/>.
/// </summary>
/// <remarks>A persistent session never closes the view merely because the session is disposed.</remarks>
public readonly record struct EditorSessionOptions(
	EditorSessionMode Mode = EditorSessionMode.Persistent);

/// <summary>
/// Represents an acquired editor view that must be released before the caller finishes.
/// </summary>
/// <remarks>
/// Disposing a session is idempotent. Disposal restores the previously active editor view; a
/// transient session closes the view only when this session opened it.
/// </remarks>
public interface IEditorSession : IDisposable
{
	/// <summary>Gets the workspace document key of the leased view.</summary>
	WorkspaceDocumentKey DocumentKey { get; }

	/// <summary>Gets the workspace document identifier of the leased view.</summary>
	string DocumentId { get; }

	/// <summary>Gets the mode that governs how the view is released.</summary>
	EditorSessionMode Mode { get; }

	/// <summary>Gets a value indicating whether the session has not yet been disposed.</summary>
	bool IsActive { get; }
}

/// <summary>
/// Contains the outcome of attaching an editor view through an <see cref="IEditorViewHost"/>.
/// </summary>
/// <remarks><see cref="Session"/> is <see langword="null"/> when no view could be produced.</remarks>
public sealed record EditorSessionOpenResult(
	EditorSessionOpenStatus Status,
	IEditorSession? Session);

/// <summary>
/// Describes how an editor view attach operation completed.
/// </summary>
public enum EditorSessionOpenStatus
{
	/// <summary>A new editor view was opened and leased.</summary>
	Opened,

	/// <summary>An existing editor view was leased without opening a new one.</summary>
	AlreadyOpen,

	/// <summary>No editor view could be produced.</summary>
	Unavailable
}

/// <summary>
/// Opens and leases an editor view for a workspace document snapshot.
/// </summary>
/// <remarks>
/// Implementations may reuse an existing view. The returned session ensures that transient disposal
/// closes only a view newly acquired for that session, not a view owned by another caller.
/// </remarks>
public interface IEditorViewHost
{
	/// <summary>
	/// Opens or reuses an editor view for the snapshot and returns a session for it.
	/// </summary>
	EditorSessionOpenResult Open(
		WorkspaceDocumentSnapshot snapshot,
		EditorSessionOptions options);
}

/// <summary>
/// Releases a leased editor view and restores the previously active editor view.
/// </summary>
/// <remarks>
/// For a transient session that acquired the view, disposal invokes <c>closeView</c> and then invokes
/// <c>activateEditor</c> even if closing reports <see langword="false"/>.
/// Persistent or non-acquired sessions skip the close callback.
/// </remarks>
public sealed class EditorSession : IEditorSession
{
	private readonly WorkspaceDocumentSnapshot _snapshot;
	private readonly bool _acquiredView;
	private readonly Func<bool> _closeView;
	private readonly Action _activateEditor;
	private bool _disposed;

	/// <summary>
	/// Creates a session that closes an acquired transient view and restores the previous view.
	/// </summary>
	/// <param name="snapshot">The workspace document snapshot the session describes.</param>
	/// <param name="mode">Whether the leased view is transient or persistent.</param>
	/// <param name="acquiredView">Whether this session opened the editor view.</param>
	/// <param name="closeView">Closes the leased view; returns whether the view was closed.</param>
	/// <param name="activateEditor">Restores the previously active editor view.</param>
	public EditorSession(
		WorkspaceDocumentSnapshot snapshot,
		EditorSessionMode mode,
		bool acquiredView,
		Func<bool> closeView,
		Action activateEditor)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(closeView);
		ArgumentNullException.ThrowIfNull(activateEditor);

		_snapshot = snapshot;
		_acquiredView = acquiredView;
		_closeView = closeView;
		_activateEditor = activateEditor;

		Mode = mode;
	}

	/// <inheritdoc/>
	public WorkspaceDocumentKey DocumentKey => _snapshot.DocumentKey;

	/// <inheritdoc/>
	public string DocumentId => _snapshot.DocumentId;

	/// <inheritdoc/>
	public EditorSessionMode Mode { get; }

	/// <inheritdoc/>
	public bool IsActive => !_disposed;

	/// <inheritdoc/>
	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;

		if (_acquiredView && Mode == EditorSessionMode.Transient)
			_closeView();

		_activateEditor();
	}
}

using Nickelony.IDEKit.Core.Pathing;
using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>
/// Coordinates view attachment and publication around workspace document authority.
/// </summary>
/// <remarks>
/// <para>
/// The manager tracks its active operations and dispatches every action that touches a view member
/// through the delegate supplied to the constructor. It tracks views by normalized document id and
/// records failed view synchronization as unsynchronized; a successful refresh can clear that state,
/// while unregistering the view always removes it. A commit retries the synchronization of a view
/// whose only outstanding state is such a failure instead of blocking the commit permanently.
/// </para>
/// <para>
/// The manager owns no editor or UI thread. The constructor delegate is the only view-affinity
/// mechanism, and the manager never reads or invokes a view member while holding its own state lock.
/// </para>
/// </remarks>
public sealed partial class WorkspaceDocumentManager : IWorkspaceDocumentManager
{
	private readonly object _stateLock = new();
	private readonly IWorkspaceDocumentStore _store;
	private readonly Func<Action, Task> _dispatchViewAction;

	// The view-keyed collections use reference equality: a view's identity is its instance, and a
	// host type that overrides value equality must not make two distinct views collide. View ids are
	// captured through the dispatch delegate at registration and indexed separately, so the manager
	// never reads a view member while holding the state lock.
	private readonly Dictionary<IWorkspaceDocumentView, string> _views = new(ReferenceEqualityComparer.Instance);
	private readonly Dictionary<IWorkspaceDocumentView, string> _viewIds = new(ReferenceEqualityComparer.Instance);
	private readonly Dictionary<IWorkspaceDocumentView, long> _viewVersions = new(ReferenceEqualityComparer.Instance);
	private readonly Dictionary<string, List<IWorkspaceDocumentView>> _viewsByDocument;
	private readonly Dictionary<string, IWorkspaceDocumentView> _viewsByViewId = new(StringComparer.Ordinal);
	private readonly HashSet<IWorkspaceDocumentView> _unsynchronizedViews = new(ReferenceEqualityComparer.Instance);
	private readonly List<Task> _activeOperations = [];
	private Task? _stopTask;
	private bool _stopping;

	/// <summary>
	/// Initializes a new instance of the <see cref="WorkspaceDocumentManager"/> class.
	/// </summary>
	/// <param name="store">The workspace document store that owns document content and disk operations.</param>
	/// <param name="dispatchViewAction">
	/// The delegate that runs actions touching a view's members where view access is legal, for example a
	/// UI dispatcher or the direct headless adapter <c>action =&gt; { action(); return Task.CompletedTask; }</c>.
	/// The returned task must complete only after the action has run: the manager awaits it before it reads
	/// the results the action captured. A dispatcher that completes asynchronously (for example a queue-based
	/// dispatcher that cannot block its caller) is supported because the manager never blocks while waiting.
	/// </param>
	/// <param name="pathComparison">
	/// The comparison used for document identities tracked by the manager. When omitted, the policy
	/// exposed by <see cref="IWorkspaceDocumentReader.PathComparison"/> is used.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="store"/> or <paramref name="dispatchViewAction"/> is <see langword="null"/>.</exception>
	public WorkspaceDocumentManager(
		IWorkspaceDocumentStore store,
		Func<Action, Task> dispatchViewAction,
		LocalPathComparisonPolicy? pathComparison = null)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(dispatchViewAction);

		_store = store;
		_dispatchViewAction = dispatchViewAction;
		_viewsByDocument = new Dictionary<string, List<IWorkspaceDocumentView>>(
			(pathComparison ?? _store.PathComparison).Comparer);
	}

	/// <inheritdoc />
	public IWorkspaceDocumentReader Documents => _store;
}

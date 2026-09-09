using System.Collections.ObjectModel;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Watches the workspace root for configured file patterns and forwards coalesced changes to the owner.
/// </summary>
/// <remarks>
/// <para>
/// If watcher creation or activation fails, this instance is disposed and recovery requires a replacement watcher.
/// A failed dispatch is retried with bounded exponential backoff started at the debounce delay; when the attempt
/// limit is reached, the pending changes are dropped, the file-system watchers stop, and the owner is notified once
/// so it can dispose this watcher and create a replacement, which reconciles missed changes through its workspace
/// snapshot. The owner may dispose this watcher from within the failure callback.
/// </para>
/// <para>
/// The dispatch callback is awaited under an internal gate and must complete (or observe the lifetime token): a
/// callback that never completes also never escalates to the failure path, so later changes stay queued until
/// disposal. The owner failure callback runs under the watcher's notification lock so no callback can start once
/// disposal returned; it may dispose the watcher, but it must not block waiting for a thread that is entering
/// disposal.
/// </para>
/// <para>
/// Disposal is idempotent and returns without waiting for in-flight dispatch operations; it can be initiated from
/// any context, including the dispatch and failure callbacks. A disposal that races a running failure notification
/// waits for that callback because disposal marks the instance disposed under the notification lock. Changes still
/// waiting in the debounce buffer or in a scheduled retry are dropped, and the cancellation token passed to the
/// dispatch callback is canceled so in-flight work can observe disposal and unwind promptly.
/// </para>
/// </remarks>
public sealed partial class WorkspaceFileWatcher : IDisposable, IAsyncDisposable
{
	// Bounded dispatch retry policy: a failed dispatch is requeued with exponential backoff started at the
	// debounce delay; when the attempt limit is reached the watcher stops watching, drops the pending changes,
	// and notifies the owner once so it can dispose this watcher and create a replacement.
	private const int DispatchFailureMaxAttempts = 3;

	private readonly ILogger _logger;

	// Debounce policy: changes within 250 ms of each other coalesce into one dispatch, but a sustained
	// edit stream still dispatches at least every 2 s (maximum dispatch latency).
	private static readonly TimeSpan s_dispatchDebounce = TimeSpan.FromMilliseconds(250);
	private static readonly TimeSpan s_maxDispatchDelay = TimeSpan.FromSeconds(2.0);

	// Watcher registration state.
	private readonly string _workspaceRootDirectoryPath;
	private readonly ReadOnlyCollection<WorkspaceWatchSpecification> _watchSpecifications;
	private readonly Func<string, WorkspaceWatchSpecification, FileSystemWatcher> _fileSystemWatcherFactory;
	private readonly List<FileSystemWatcher> _watchers = [];
	private readonly object _watchersSyncRoot = new();
	private readonly Action<WorkspaceFileWatcher, Exception?>? _onWatcherFailed;
	private int _watcherFailureReported;

	// Dispatch and disposal lifecycle state. Disposal marks the watcher disposed under the notification lock and
	// then stops it without waiting: the lifetime token is canceled so an in-flight dispatch can observe disposal.
	private readonly Func<FileChangeBatch, CancellationToken, Task> _dispatchAsync;
	private readonly WorkspaceChangeDebouncer _pendingChanges;
	private readonly CancellationTokenSource _lifetimeCts = new();
	private readonly SemaphoreSlim _dispatchGate = new(1, 1);

	// Serializes disposal against the failure notification so the owner callback cannot run after disposal returned.
	private readonly object _notificationSyncRoot = new();

	private volatile bool _isDisposed;

	// 0 = not disposing, 1 = disposing.
	private int _disposeStarted;

	private int _consecutiveDispatchFailures;

	/// <summary>
	/// Gets a value indicating whether any file-system watchers are currently active.
	/// </summary>
	/// <remarks>Internal diagnostic view used by the package tests; not part of the supported surface.</remarks>
	internal bool HasActiveWatchers => ActiveWatcherCount > 0;

	/// <summary>
	/// Gets the number of active file-system watchers.
	/// </summary>
	/// <remarks>Internal diagnostic view used by the package tests; not part of the supported surface.</remarks>
	internal int ActiveWatcherCount
	{
		get
		{
			lock (_watchersSyncRoot)
				return _watchers.Count;
		}
	}

	/// <summary>
	/// Gets a value indicating whether the watcher has been disposed.
	/// A disposed watcher no longer watches the workspace and cannot be restarted.
	/// </summary>
	public bool IsDisposed => _isDisposed;

	/// <summary>
	/// Initializes a new instance of the <see cref="WorkspaceFileWatcher"/> class.
	/// </summary>
	/// <param name="workspaceRootDirectoryPath">The workspace root directory to watch.</param>
	/// <param name="dispatchAsync">The callback that forwards coalesced changes to the owner.</param>
	/// <param name="watchSpecifications">The explicit file patterns that should be watched under the workspace root. The list is copied on assignment.</param>
	/// <param name="onWatcherFailed">The callback that reports a watcher failure to the owner.</param>
	/// <param name="fileSystemWatcherFactory">Creates one file-system watcher for a watch specification, or <see langword="null"/> to use the built-in factory.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	/// <exception cref="ArgumentException">
	/// <paramref name="workspaceRootDirectoryPath"/> is empty or whitespace-only, <paramref name="watchSpecifications"/>
	/// contains no entries, or one of its filters is <see langword="null"/>, empty, whitespace-only, <c>.</c>,
	/// <c>..</c>, rooted, or contains a directory separator.
	/// </exception>
	/// <exception cref="ArgumentNullException"><paramref name="workspaceRootDirectoryPath"/>, <paramref name="dispatchAsync"/>, or <paramref name="watchSpecifications"/> is <see langword="null"/>.</exception>
	public WorkspaceFileWatcher(
		string workspaceRootDirectoryPath,
		Func<FileChangeBatch, CancellationToken, Task> dispatchAsync,
		IReadOnlyList<WorkspaceWatchSpecification> watchSpecifications,
		Action<WorkspaceFileWatcher, Exception?>? onWatcherFailed = null,
		Func<string, WorkspaceWatchSpecification, FileSystemWatcher>? fileSystemWatcherFactory = null,
		ILogger<WorkspaceFileWatcher>? logger = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRootDirectoryPath);
		ArgumentNullException.ThrowIfNull(dispatchAsync);
		ArgumentNullException.ThrowIfNull(watchSpecifications);

		if (watchSpecifications.Count == 0)
			throw new ArgumentException("At least one watch specification is required.", nameof(watchSpecifications));

		for (int i = 0; i < watchSpecifications.Count; i++)
			WorkspaceWatchSpecification.Validate(watchSpecifications[i], nameof(watchSpecifications));

		_logger = logger ?? NullLogger<WorkspaceFileWatcher>.Instance;

		_workspaceRootDirectoryPath = workspaceRootDirectoryPath;
		_dispatchAsync = dispatchAsync;
		_watchSpecifications = Array.AsReadOnly([.. watchSpecifications]);
		_onWatcherFailed = onWatcherFailed;
		_fileSystemWatcherFactory = fileSystemWatcherFactory ?? CreateFileSystemWatcher;

		_pendingChanges = new WorkspaceChangeDebouncer(s_dispatchDebounce, s_maxDispatchDelay, () => _ = DispatchPendingChangesAsync());
	}
}

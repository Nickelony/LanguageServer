namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Forwards workspace file changes when the owner allows it, buffers recoverable delivery failures for replay,
/// and reports unexpected dropped batches with forwarding context.
/// </summary>
/// <remarks>
/// <para>
/// This is a provider-composition helper rather than a host API: it is public so a language package can build its
/// own workspace-change forwarding around it. A host consumes the resulting provider through the Abstractions
/// provider contract instead of talking to this type directly.
/// </para>
/// <para>
/// Buffered changes are replayed only when the caller invokes <see cref="ReplayDeferredAsync"/>; <see cref="DispatchAsync"/> never
/// drains the deferred set, so a host that wants guaranteed ordering must replay the deferred changes before it
/// forwards new ones. A batch that fails a dispatch attempt stays ahead of changes that were buffered while the
/// attempt was in flight, so a re-buffered delete cannot cancel against a newer create for the same path.
/// </para>
/// <para>
/// Disposal is immediate and drops undelivered state: once disposal was requested, an incoming change set is
/// discarded instead of being buffered or forwarded, and buffered changes are not replayed anymore. A host that
/// needs those changes recovered replaces the forwarder and reconciles missed changes through its workspace
/// snapshot.
/// </para>
/// </remarks>
public sealed partial class WorkspaceFileChangeForwarder : IDisposable
{
	// Forwarding prerequisites.
	private readonly Func<bool> _canForwardAccessor;
	private readonly Func<bool> _isDisposedAccessor;
	private readonly Func<CancellationToken, Task<bool>> _ensureStartedAsync;
	private readonly Action _tryMarkTransportUnhealthy;
	private readonly Action<WorkspaceFileForwardingFailure>? _logForwardingFailure;
	private readonly bool _bufferChangesWhileForwardingDisabled;

	// Buffered changes are split into two accumulators: _replayedChanges holds batches that failed a dispatch
	// attempt (they keep their original order and stay ahead of changes that arrived while the attempt was in
	// flight), and _pendingChanges holds changes buffered while forwarding was not allowed. Keeping them apart
	// prevents a re-buffered delete from canceling against a newer create for the same path.
	private readonly WorkspaceChangeAccumulator _pendingChanges = new();
	private readonly WorkspaceChangeAccumulator _replayedChanges = new();
	private readonly SemaphoreSlim _forwardingGate = new(1, 1);
	private readonly object _disposeSyncRoot = new();
	private int _activeOperationCount;
	private bool _disposeRequested;
	private bool _disposed;

	/// <summary>
	/// Initializes a new instance of the <see cref="WorkspaceFileChangeForwarder"/> class.
	/// </summary>
	/// <param name="canForwardAccessor">Reports whether forwarding attempts are currently allowed.</param>
	/// <param name="isDisposedAccessor">Reports whether the owner has been disposed.</param>
	/// <param name="ensureStartedAsync">Starts or validates the underlying transport before forwarding.</param>
	/// <param name="tryMarkTransportUnhealthy">Attempts to mark the current transport as unhealthy after recoverable forwarding failures; a failed attempt is the owner's concern.</param>
	/// <param name="logForwardingFailure">Logs forwarding failures together with batch context.</param>
	/// <param name="bufferChangesWhileForwardingDisabled">Whether changes should be buffered instead of dropped while forwarding is temporarily disallowed.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="canForwardAccessor"/>, <paramref name="isDisposedAccessor"/>, <paramref name="ensureStartedAsync"/>,
	/// or <paramref name="tryMarkTransportUnhealthy"/> is <see langword="null"/>.
	/// </exception>
	public WorkspaceFileChangeForwarder(
		Func<bool> canForwardAccessor,
		Func<bool> isDisposedAccessor,
		Func<CancellationToken, Task<bool>> ensureStartedAsync,
		Action tryMarkTransportUnhealthy,
		Action<WorkspaceFileForwardingFailure>? logForwardingFailure = null,
		bool bufferChangesWhileForwardingDisabled = true)
	{
		ArgumentNullException.ThrowIfNull(canForwardAccessor);
		ArgumentNullException.ThrowIfNull(isDisposedAccessor);
		ArgumentNullException.ThrowIfNull(ensureStartedAsync);
		ArgumentNullException.ThrowIfNull(tryMarkTransportUnhealthy);

		_canForwardAccessor = canForwardAccessor;
		_isDisposedAccessor = isDisposedAccessor;
		_ensureStartedAsync = ensureStartedAsync;
		_tryMarkTransportUnhealthy = tryMarkTransportUnhealthy;
		_logForwardingFailure = logForwardingFailure;
		_bufferChangesWhileForwardingDisabled = bufferChangesWhileForwardingDisabled;
	}
}

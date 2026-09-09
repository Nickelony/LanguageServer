using Nickelony.IDEKit.Core.Requests;

namespace Nickelony.IDEKit.IntelliSense.Completion;

/// <summary>
/// Tracks the request lifetime a completion pipeline uses, shared by a controller's standard
/// request entry point and by hosts that admit requests themselves.
/// </summary>
/// <remarks>
/// <para>
/// A completion controller creates the session and exposes it to its host; the session ends when its
/// owner disposes it. Its operations keep reporting their documented safe defaults afterwards (for
/// example <see cref="BeginRequest"/> reports <c>-1</c>), and the latest request token stays
/// observable through <see cref="CurrentRequestCancellationToken"/>. Admission is decided inside the
/// coordinator's critical section against the session's disposal state, so a request admitted
/// concurrently with disposal is either canceled by the disposal or rejected by it, never left
/// pending; see <see cref="BeginRequest"/> for the admission rules.
/// </para>
/// <para>
/// The session and the controller's standard request pipeline share one request lifetime through the
/// core <see cref="LatestRequestCoordinator"/>: beginning a request here supersedes an in-flight
/// standard request and vice versa, so only the newest request of either kind stays current.
/// </para>
/// <para>
/// This session is the request-lifetime mechanism: it reports whether the request that produced a
/// result is still the newest one. Items that outlive the pipeline - for example an
/// open completion window whose entries are resolved asynchronously - carry their own staleness
/// stamps instead (<see cref="TextCompletionItem.RequestDocumentVersion"/>,
/// <see cref="TextCompletionItem.RequestGeneration"/>,
/// <see cref="TextCompletionItem.WithRequestContext"/>) and are rebased by the host when the
/// document changes under the open window.
/// </para>
/// </remarks>
public sealed class TextCompletionRequestSession : IDisposable
{
	private readonly LatestRequestCoordinator _requests = new();
	private readonly object _sync = new();
	private volatile bool _isDisposed;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextCompletionRequestSession"/> class.
	/// </summary>
	/// <remarks>
	/// A completion controller normally creates the session and exposes it to its host; a host that
	/// owns the complete request lifecycle may create one directly and dispose it when done.
	/// </remarks>
	public TextCompletionRequestSession()
	{
	}

	/// <summary>
	/// Gets the request coordinator behind the session, so a controller's standard request pipeline can
	/// admit its runs as requests of the same session.
	/// </summary>
	/// <remarks>
	/// The coordinator stays usable after the session is disposed, but requests admitted through it
	/// directly bypass the session's disposal gate (admission is not rejected with <c>-1</c>) and the
	/// session reports them as non-current while it is disposed; their token stays observable through
	/// <see cref="CurrentRequestCancellationToken"/>. Use <see cref="BeginRequest"/> so a disposed
	/// session rejects admission with <c>-1</c>.
	/// </remarks>
	public LatestRequestCoordinator Coordinator => _requests;

	/// <summary>
	/// Begins tracking a new completion request and returns its identifier. The previous request's token is
	/// canceled first, so only the newest request can still publish.
	/// </summary>
	/// <remarks>
	/// The new request's cancellation token is observable through
	/// <see cref="CurrentRequestCancellationToken"/>, and its identifier stays current until a newer request
	/// begins or <see cref="InvalidateRequests"/> is called. Admission is evaluated against the disposal
	/// state inside the coordinator's critical section, so a request admitted concurrently with disposal
	/// is either canceled by the disposal or rejected with <c>-1</c>, never left pending, and a rejected
	/// call leaves every other request untouched.
	/// </remarks>
	/// <returns>The identifier of the new request, or <c>-1</c> when the session is disposed.</returns>
	public long BeginRequest()
	{
		long requestId = _requests.BeginRequest(CanAdmit);

		if (requestId == 0)
			return -1;

		// Disposal may have run between the admission predicate and this re-check; the rejected call
		// cancels only its own admission, so it cannot disturb a request admitted through the
		// coordinator by someone else.
		lock (_sync)
		{
			if (!_isDisposed)
				return requestId;
		}

		_requests.CancelPendingRequest(requestId);
		return -1;
	}

	/// <summary>
	/// The admission predicate evaluated inside the coordinator's critical section; it reads the
	/// volatile disposal flag only, so it cannot deadlock against the session lock.
	/// </summary>
	private bool CanAdmit() => !_isDisposed;

	/// <summary>
	/// Determines whether a request identifier is still current: not invalidated and not disposed.
	/// </summary>
	/// <param name="requestToken">The request identifier to check.</param>
	/// <returns><see langword="true"/> when the identifier is current; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// Canceling the request does not make its identifier non-current: a host that drives the pipeline
	/// manually also treats a canceled <see cref="CurrentRequestCancellationToken"/> as a rejection, and
	/// <see cref="InvalidateRequests"/> is the way to reject an outstanding result without canceling it.
	/// Use <see cref="CanPublish"/> for one check that covers both staleness and cancellation.
	/// </remarks>
	public bool IsCurrent(long requestToken)
	{
		lock (_sync)
		{
			return !_isDisposed && _requests.IsCurrent(requestToken);
		}
	}

	/// <summary>
	/// Determines whether a request identifier is still current and its request has not been canceled,
	/// so a result produced for it may be published.
	/// </summary>
	/// <param name="requestToken">The request identifier to check.</param>
	/// <returns>
	/// <see langword="true"/> when the identifier is current and its request's token is not
	/// canceled; otherwise, <see langword="false"/>.
	/// </returns>
	/// <remarks>
	/// This combines the two rejection rules a standard request pipeline applies: the identifier must
	/// still be the newest one and the request must not have been canceled by
	/// <see cref="CancelInFlightRequest"/> or a newer admission. The check is atomic - the identifier
	/// and the token are evaluated together under the coordinator lock - so a concurrent admission can
	/// never make a superseded identifier report publishable. A host that checks only
	/// <see cref="IsCurrent"/> can publish a canceled result and must apply the cancellation check
	/// itself.
	/// </remarks>
	public bool CanPublish(long requestToken)
	{
		lock (_sync)
		{
			return !_isDisposed && _requests.CanPublish(requestToken);
		}
	}

	/// <summary>
	/// Gets the cancellation token of the latest completion request, or <see cref="CancellationToken.None"/> when
	/// there is none. The token is canceled when a newer request begins, when <see cref="CancelInFlightRequest"/> is
	/// called, or when the session is disposed. After cancellation, the property keeps returning the canceled
	/// token of the last request until a newer request begins.
	/// </summary>
	public CancellationToken CurrentRequestCancellationToken
		=> _requests.RequestCancellationToken;

	/// <summary>
	/// Cancels the pending completion request's token, if any, so a cooperative provider can stop early.
	/// </summary>
	/// <remarks>
	/// Canceling only stops the provider call; the request's identifier stays current, and a host that
	/// drives the pipeline manually treats the canceled <see cref="CurrentRequestCancellationToken"/> as a
	/// rejection the same way a standard request pipeline does. Use <see cref="CanPublish"/> for the
	/// combined staleness and cancellation check. Starting a newer request cancels the pending
	/// request's token as well, and the canceled token stays observable through
	/// <see cref="CurrentRequestCancellationToken"/>. The disposal guard is checked before the
	/// coordinator call, so a call that passes it concurrently with <see cref="Dispose"/> still
	/// cancels the pending request; the late cancellation can only reach a request admitted directly
	/// through <see cref="Coordinator"/> after disposal.
	/// </remarks>
	public void CancelInFlightRequest()
	{
		lock (_sync)
		{
			if (_isDisposed)
				return;
		}

		_requests.CancelPendingRequest();
	}

	/// <summary>
	/// Marks outstanding completion requests as stale so completed results are ignored. The pending
	/// request's token is not canceled; use <see cref="CancelInFlightRequest"/> for that. The same
	/// disposal-guard window applies as for <see cref="CancelInFlightRequest"/>.
	/// </summary>
	public void InvalidateRequests()
	{
		lock (_sync)
		{
			if (_isDisposed)
				return;
		}

		_requests.Invalidate();
	}

	/// <summary>
	/// Ends the session: invalidates outstanding requests and cancels the pending request's token. The
	/// method is idempotent, and the canceled token stays observable through
	/// <see cref="CurrentRequestCancellationToken"/>. Disposal is serialized with admission, so a
	/// concurrent request is either canceled by it or rejected with <c>-1</c>.
	/// </summary>
	public void Dispose()
	{
		lock (_sync)
		{
			if (_isDisposed)
				return;

			_isDisposed = true;
		}

		_requests.Invalidate();
		_requests.CancelPendingRequest();
	}
}

namespace Nickelony.IDEKit.Core.Requests;

/// <summary>
/// Coordinates asynchronous operations so that a result is only published while no newer request
/// has been admitted. Starting a newer operation cancels any previously outstanding operation,
/// and each run reports a <see cref="RequestOutcome"/> that classifies how it completed.
/// </summary>
/// <remarks>
/// <para>
/// A superseded operation's result is discarded even when its compute delegate ignored the
/// cancellation token. The guarantee covers same-thread publication only - no newer request was
/// admitted before this run's completion checks - so a request admitted between the checks and the
/// callbacks does not retract a result that is already being published.
/// </para>
/// <para>
/// The coordinator supports two request levels that share one request lifetime: a run, where
/// <see cref="RunAsync"/> awaits the work and publishes the result through a current-state check,
/// and a host-driven request, where <see cref="BeginRequest()"/> admits a request that the caller
/// completes itself (the caller decides admission with <see cref="IsCurrent"/> and treats a canceled
/// <see cref="RequestCancellationToken"/> as a rejection, the same rules a run applies). Both levels
/// supersede each other: a newer request of either kind cancels the outstanding request's token,
/// and only the newest request stays current; admission can be conditioned on a caller-supplied
/// predicate (<see cref="BeginRequest(Func{bool})"/>), and <see cref="CanPublish"/> reports
/// atomically whether a request's result may still be published.
/// </para>
/// <para>
/// A caller token that is already canceled when a run is admitted cancels the run before its
/// compute delegate is invoked.
/// </para>
/// <para>
/// Hosts supply the operation's state and a <c>canApply</c> predicate for current-state checks
/// (for example a version or generation of the caller's state); Core has no knowledge of editor or
/// document state. The predicate runs separately from <c>apply</c> so it can reject a result without
/// side effects and can prepare state that <c>apply</c> consumes.
/// </para>
/// <para>
/// The continuation, including <c>canApply</c> and <c>apply</c>, runs on the caller's captured
/// synchronization context by default, so a context-capturing host (for example a UI thread) also
/// publishes the result on that thread; pass <c>continueOnCapturedContext: false</c> to resume on
/// the thread pool instead and marshal thread-affine work in the host. Await the returned task
/// instead of blocking on it: a blocking wait on a single-threaded context can deadlock, because
/// the continuation must run on that context.
/// </para>
/// </remarks>
public sealed class LatestRequestCoordinator
{
	private readonly object _sync = new();
	private long _latestRequestId;
	private CancellationTokenSource? _currentRequestCancellation;
	private CancellationToken _latestRequestToken;

	/// <summary>
	/// Begins a host-driven request and returns its identifier. The caller runs the work itself,
	/// decides admission with <see cref="IsCurrent"/> and treats a canceled
	/// <see cref="RequestCancellationToken"/> as a rejection - the same rules the run path applies.
	/// </summary>
	/// <remarks>
	/// Starting the request supersedes the outstanding request of either level: its token is canceled
	/// first so a cooperative provider can stop early, and its result is discarded. The new request's
	/// cancellation token is reported by <see cref="RequestCancellationToken"/>.
	/// </remarks>
	/// <returns>A nonzero identifier that identifies the new request.</returns>
	public long BeginRequest()
		=> BeginRequestCore(canAdmit: null);

	/// <summary>
	/// Begins a host-driven request after the supplied admission predicate accepted it. The predicate
	/// decides admission atomically with the supersede, so a rejected call leaves every request
	/// untouched.
	/// </summary>
	/// <param name="canAdmit">
	/// Determines whether the request may be admitted. The predicate runs synchronously under the
	/// coordinator lock - that is what makes the admission decision atomic with the supersede - so it
	/// must be side-effect-free and must not call back into the coordinator. When it returns
	/// <see langword="false"/>, no request is admitted, nothing is superseded or canceled, and any
	/// outstanding request keeps its state.
	/// </param>
	/// <returns>
	/// A nonzero identifier that identifies the new request, or zero when the predicate rejected the
	/// admission. Zero is never a valid request identifier, so a rejected admission cannot be mistaken
	/// for an admitted request.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="canAdmit"/> is <see langword="null"/>.</exception>
	public long BeginRequest(Func<bool> canAdmit)
	{
		ArgumentNullException.ThrowIfNull(canAdmit);

		return BeginRequestCore(canAdmit);
	}

	private long BeginRequestCore(Func<bool>? canAdmit)
	{
		CancellationTokenSource? supersededCancellation;
		CancellationTokenSource requestCancellation;
		long requestId;

		lock (_sync)
		{
			if (canAdmit is not null && !canAdmit())
				return 0;

			requestId = NextRequestId();
			supersededCancellation = _currentRequestCancellation;

			requestCancellation = new CancellationTokenSource();
			_currentRequestCancellation = requestCancellation;
			_latestRequestToken = requestCancellation.Token;
		}

		// Cancel outside the lock: a cancellation callback is caller code and must not run while the
		// coordinator lock is held.
		TryCancelSafely(supersededCancellation);

		return requestId;
	}

	/// <summary>
	/// Advances the request identifier. The identifier documents a nonzero value, so the zero
	/// produced by the wrap after 2^64 requests is skipped and an uninitialized identifier never
	/// matches a live request. Callers must hold the coordinator lock.
	/// </summary>
	private long NextRequestId()
	{
		long requestId = ++_latestRequestId;

		return requestId == 0 ? ++_latestRequestId : requestId;
	}

	/// <summary>
	/// Determines whether the supplied request identifier identifies the most recent request begun
	/// through <see cref="BeginRequest()"/> or <see cref="RunAsync"/>.
	/// </summary>
	/// <param name="requestId">The request identifier to inspect.</param>
	/// <returns>
	/// <see langword="true"/> when <paramref name="requestId"/> is the latest request; otherwise,
	/// <see langword="false"/>. The default identifier value zero is never current, so an
	/// uninitialized identifier cannot pass the check.
	/// </returns>
	public bool IsCurrent(long requestId)
	{
		if (requestId == 0)
			return false;

		lock (_sync)
		{
			return requestId == _latestRequestId;
		}
	}

	/// <summary>
	/// Determines whether a result produced for the supplied request may still be published: the
	/// request is still the latest one and its own cancellation token is not canceled.
	/// </summary>
	/// <param name="requestId">The request identifier to inspect.</param>
	/// <returns>
	/// <see langword="true"/> when <paramref name="requestId"/> is the latest request and its token
	/// is not canceled; otherwise, <see langword="false"/>. The default identifier value zero is
	/// never publishable. The check is atomic: the identifier and the token are evaluated together
	/// under the coordinator lock, so a concurrent admission cannot make a superseded identifier
	/// report publishable.
	/// </returns>
	public bool CanPublish(long requestId)
	{
		if (requestId == 0)
			return false;

		lock (_sync)
		{
			return requestId == _latestRequestId && !_latestRequestToken.IsCancellationRequested;
		}
	}

	/// <summary>
	/// Gets the cancellation token of the most recent request begun through <see cref="BeginRequest()"/> or
	/// <see cref="RunAsync"/>.
	/// </summary>
	/// <remarks>
	/// The token is canceled when a newer request begins or when <see cref="CancelPendingRequest()"/> is
	/// called. Canceling a run's caller token ends the run's work and classifies the run as canceled,
	/// but it does not cancel this token. Canceling only stops the work: the property keeps returning
	/// the canceled token of the last request until a newer request begins, and the token stays usable
	/// for cancellation checks and for late registrations after its request completed.
	/// </remarks>
	/// <value>
	/// The token of the latest request, or <see cref="CancellationToken.None"/> before any request.
	/// </value>
	public CancellationToken RequestCancellationToken
	{
		get
		{
			lock (_sync)
			{
				return _latestRequestToken;
			}
		}
	}

	/// <summary>
	/// Invalidates all outstanding requests so their results are discarded when they complete. This
	/// does not cancel the outstanding work; use <see cref="CancelPendingRequest()"/> to ask it to stop
	/// early.
	/// </summary>
	public void Invalidate()
	{
		lock (_sync)
		{
			_ = NextRequestId();
		}
	}

	/// <summary>
	/// Cancels the outstanding request's token, if any, so a cooperative compute delegate or provider
	/// can stop early. This does not invalidate the request: it is still the latest, and its outcome is
	/// classified when it completes. Use <see cref="Invalidate"/> to discard outstanding results instead.
	/// </summary>
	/// <remarks>
	/// The canceled token stays observable through <see cref="RequestCancellationToken"/> until a newer
	/// request begins. Canceling only stops the work, so a host-driven request keeps its current state
	/// and a host that drives the pipeline itself treats a canceled
	/// <see cref="RequestCancellationToken"/> as a rejection the same way a run is classified as
	/// <see cref="RequestOutcome.Canceled"/>. To cancel only a specific admission - so a caller can
	/// never cancel a request it did not admit - use the identifier overload.
	/// </remarks>
	public void CancelPendingRequest()
		=> CancelPendingRequestCore(requestId: null);

	/// <summary>
	/// Cancels the outstanding request's token only when the supplied identifier is still the latest
	/// request. An identifier that a newer request superseded leaves the newer request's token
	/// untouched.
	/// </summary>
	/// <param name="requestId">
	/// The identifier of the request whose token to cancel; an identifier that is not the latest
	/// request is ignored, and zero (never a valid identifier) does nothing.
	/// </param>
	public void CancelPendingRequest(long requestId)
		=> CancelPendingRequestCore(requestId);

	private void CancelPendingRequestCore(long? requestId)
	{
		CancellationTokenSource? cancellation;

		lock (_sync)
		{
			if (requestId is long id && id != _latestRequestId)
				return;

			cancellation = _currentRequestCancellation;
			_currentRequestCancellation = null;
		}

		TryCancelSafely(cancellation);
	}

	/// <summary>
	/// Requests cancellation and absorbs the two benign races: the source was disposed externally
	/// after the capture, or a cancellation callback faulted so
	/// <see cref="CancellationTokenSource.Cancel()"/> threw an <see cref="AggregateException"/>.
	/// </summary>
	/// <param name="cancellation">The source to cancel, or <see langword="null"/> when no request is outstanding.</param>
	private static void TryCancelSafely(CancellationTokenSource? cancellation)
	{
		if (cancellation is null)
			return;

		try
		{
			cancellation.Cancel();
		}
		catch (ObjectDisposedException)
		{
			// Defensive: no member of the coordinator disposes its sources, but Cancel on a source
			// that some future lifecycle disposed must not escape an unrelated call.
		}
		catch (AggregateException)
		{
			// Cancel runs the callbacks registered on the run's token. A faulting callback belongs
			// to that run, not to this cancel, so the aggregate is swallowed instead of escaping an
			// unrelated call.
		}
	}

	/// <summary>
	/// Runs the supplied compute delegate as the latest request, applying its result only when the
	/// request is still current and the host's <c>canApply</c> predicate accepts it.
	/// </summary>
	/// <remarks>
	/// The latest-request check and the <c>canApply</c> and <c>apply</c> callbacks run after the
	/// compute delegate completes; see the class remarks for the publication window guarantee. A
	/// host that can start requests concurrently with publication should keep its own state check in
	/// <c>canApply</c>. Exceptions thrown by <c>canApply</c> or <c>apply</c> propagate to the caller
	/// unclassified; only the compute delegate's <see cref="OperationCanceledException"/> is
	/// intercepted.
	/// </remarks>
	/// <typeparam name="TState">The type of the host-supplied request state.</typeparam>
	/// <typeparam name="TResult">The type of the computed result.</typeparam>
	/// <param name="state">The state captured for this request.</param>
	/// <param name="computeAsync">
	/// Computes the result. The returned task may observe the supplied
	/// <see cref="CancellationToken"/> to stop early. An <see cref="OperationCanceledException"/>
	/// thrown by the delegate is treated as cancellation, so no result is applied.
	/// </param>
	/// <param name="canApply">
	/// Determines whether the completed result may be applied. It runs after the latest-request and
	/// cancellation checks and receives both the request state and the computed result.
	/// </param>
	/// <param name="apply">Applies the accepted result.</param>
	/// <param name="continueOnCapturedContext">
	/// <see langword="true"/> (the default) to resume the continuation - including
	/// <paramref name="canApply"/> and <paramref name="apply"/> - on the caller's captured
	/// synchronization context, so a UI host publishes on its own thread; <see langword="false"/> to
	/// resume on the thread pool and marshal thread-affine work in the host.
	/// </param>
	/// <param name="cancellationToken">A caller-supplied token linked into the request.</param>
	/// <returns>
	/// The outcome of the run:
	/// <list type="bullet">
	/// <item><see cref="RequestOutcome.Completed"/> when the result was applied;</item>
	/// <item><see cref="RequestOutcome.RejectedByCurrentState"/> when <paramref name="canApply"/> rejected the result;</item>
	/// <item><see cref="RequestOutcome.Canceled"/> when the caller token or
	/// <see cref="CancelPendingRequest()"/> canceled the work;</item>
	/// <item><see cref="RequestOutcome.Superseded"/> when a newer request or
	/// <see cref="Invalidate"/> replaced it.</item>
	/// </list>
	/// A compute failure other than cancellation, or an exception from <paramref name="canApply"/> or
	/// <paramref name="apply"/>, propagates as an exception instead of an outcome.
	/// <para>
	/// Cancellation is observed at checkpoints (before the compute delegate runs, after it returns
	/// or throws, and before publication) rather than during the <paramref name="canApply"/> and
	/// <paramref name="apply"/> callbacks, so a cancellation that arrives while they run does not
	/// preempt publication. The compute delegate receives the run's linked token; the linked source
	/// is disposed when the run completes, so a token retained by delegate-started work stops
	/// observing later cancellation at that point.
	/// </para>
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="computeAsync"/>, <paramref name="canApply"/>, or <paramref name="apply"/> is <see langword="null"/>.
	/// </exception>
	public async Task<RequestOutcome> RunAsync<TState, TResult>(
		TState state,
		Func<TState, CancellationToken, Task<TResult>> computeAsync,
		Func<TState, TResult, bool> canApply,
		Action<TResult> apply,
		bool continueOnCapturedContext = true,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(computeAsync);
		ArgumentNullException.ThrowIfNull(canApply);
		ArgumentNullException.ThrowIfNull(apply);

		long requestId;
		CancellationTokenSource runCancellation;
		CancellationTokenSource? supersededCancellation;

		lock (_sync)
		{
			requestId = NextRequestId();
			supersededCancellation = _currentRequestCancellation;

			runCancellation = new CancellationTokenSource();
			_currentRequestCancellation = runCancellation;
			_latestRequestToken = runCancellation.Token;
		}

		// Cancel outside the lock: a cancellation callback is caller code and must not run while the
		// coordinator lock is held. The coordinator never disposes the run's own source, so the exposed
		// token stays registrable after the run completes; only the linked source (which registers on
		// the caller token) is disposed when the run finishes.
		TryCancelSafely(supersededCancellation);

		CancellationTokenSource? linkedCancellation = cancellationToken.CanBeCanceled
			? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, runCancellation.Token)
			: null;

		CancellationToken effectiveToken = linkedCancellation?.Token ?? runCancellation.Token;

		try
		{
			// A caller token that is already canceled cancels the run before the compute delegate
			// is invoked, and a run that a newer request replaced is superseded even though its
			// compute delegate never started.
			if (effectiveToken.IsCancellationRequested)
				return ClassifyCancellation(requestId);

			TResult result;

			// Only the compute delegate's cancellation is expected here. A cancellation thrown by
			// the host's canApply or apply delegates is a real failure and must propagate.
			try
			{
				result = await computeAsync(state, effectiveToken).ConfigureAwait(continueOnCapturedContext);
			}
			catch (OperationCanceledException)
			{
				return ClassifyCancellation(requestId);
			}

			if (effectiveToken.IsCancellationRequested)
				return ClassifyCancellation(requestId);

			if (!IsLatestRequest(requestId))
				return RequestOutcome.Superseded;

			if (!canApply(state, result))
				return RequestOutcome.RejectedByCurrentState;

			apply(result);
			return RequestOutcome.Completed;
		}
		finally
		{
			// The linked source registers on the caller token, so it is disposed when the run completes.
			// The run's own source is deliberately left undisposed: the coordinator never disposes the
			// exposed source, so the token reported by RequestCancellationToken stays usable for
			// cancellation checks and late registrations - the same lifetime the host-driven path
			// provides.
			linkedCancellation?.Dispose();

			lock (_sync)
			{
				if (ReferenceEquals(_currentRequestCancellation, runCancellation))
					_currentRequestCancellation = null;
			}
		}
	}

	/// <summary>
	/// Classifies a canceled run. A canceled run is only superseded when a newer request or
	/// <see cref="Invalidate"/> replaced it; an owner-driven cancel of the latest run is a plain
	/// cancellation.
	/// </summary>
	private RequestOutcome ClassifyCancellation(long requestId)
	{
		return IsLatestRequest(requestId)
			? RequestOutcome.Canceled
			: RequestOutcome.Superseded;
	}

	private bool IsLatestRequest(long requestId)
	{
		lock (_sync)
		{
			return requestId == _latestRequestId;
		}
	}
}

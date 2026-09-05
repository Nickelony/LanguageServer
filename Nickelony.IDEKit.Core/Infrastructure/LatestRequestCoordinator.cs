namespace Nickelony.IDEKit.Core.Infrastructure;

/// <summary>
/// Coordinates asynchronous operations so that only the most recently requested operation may
/// publish its result. Starting a newer operation cancels any previously outstanding operation.
/// </summary>
/// <remarks>
/// <para>
/// Each <see cref="RunAsync{TState, TResult}(TState, Func{TState, CancellationToken, Task{TResult}}, Func{TState, TResult, bool}, Action{TResult}, CancellationToken)"/>
/// invocation supersedes any operation that is still pending: the older operation's per-request
/// cancellation token is cancelled and, even if its compute delegate ignores cancellation, its
/// result is discarded when it does not belong to the latest request. Hosts supply the operation's
/// state and a <c>canApply</c> predicate for current-state checks (for example a document version
/// or session generation); Core does not know about editor or document state.
/// </para>
/// <para>
/// The continuation, including <c>canApply</c> and <c>apply</c>, runs on the caller's captured
/// synchronization context, matching the editor-UI expectation that published results are applied
/// on the thread that started the request.
/// </para>
/// </remarks>
public sealed class LatestRequestCoordinator
{
	private readonly object _sync = new();
	private int _latestRequestId;
	private CancellationTokenSource? _currentRunCancellation;

	/// <summary>
	/// Invalidates all outstanding requests so their results are discarded when they complete.
	/// </summary>
	public void Invalidate()
	{
		lock (_sync)
		{
			_latestRequestId++;
		}
	}

	/// <summary>
	/// Cancels the currently outstanding request so a cooperative compute delegate can stop early.
	/// </summary>
	public void CancelPendingRequest()
	{
		lock (_sync)
		{
			_currentRunCancellation?.Cancel();
		}
	}

	/// <summary>
	/// Runs the supplied compute delegate as the latest request, applying its result only when the
	/// request is still current and the host's <c>canApply</c> predicate accepts it.
	/// </summary>
	/// <typeparam name="TState">The type of the host-supplied request state.</typeparam>
	/// <typeparam name="TResult">The type of the computed result.</typeparam>
	/// <param name="state">The state captured for this request.</param>
	/// <param name="computeAsync">
	/// Computes the result. The returned task may observe the supplied
	/// <see cref="CancellationToken"/> to stop early.
	/// </param>
	/// <param name="canApply">
	/// Determines whether the completed result may be applied. It runs after the latest-request and
	/// cancellation checks and receives both the request state and the computed result.
	/// </param>
	/// <param name="apply">Applies the accepted result.</param>
	/// <param name="cancellationToken">A caller-supplied token linked into the request.</param>
	/// <returns>
	/// <see langword="true"/> when <paramref name="apply"/> was invoked; otherwise, <see langword="false"/>.
	/// </returns>
	public async Task<bool> RunAsync<TState, TResult>(
		TState state,
		Func<TState, CancellationToken, Task<TResult>> computeAsync,
		Func<TState, TResult, bool> canApply,
		Action<TResult> apply,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(computeAsync);
		ArgumentNullException.ThrowIfNull(canApply);
		ArgumentNullException.ThrowIfNull(apply);

		int requestId;
		CancellationTokenSource runCancellation;

		lock (_sync)
		{
			requestId = ++_latestRequestId;

			// Cancel (do not dispose) the previous run: it may still be reading the token, and
			// disposing it here could race with that work. The finally block disposes the source only
			// when it is still the current run's source.
			_currentRunCancellation?.Cancel();

			runCancellation = new CancellationTokenSource();
			_currentRunCancellation = runCancellation;
		}

		using CancellationTokenSource? linkedCancellation = cancellationToken.CanBeCanceled
			? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, runCancellation.Token)
			: null;

		CancellationToken effectiveToken = linkedCancellation?.Token ?? runCancellation.Token;

		try
		{
			TResult result = await computeAsync(state, effectiveToken).ConfigureAwait(true);

			if (effectiveToken.IsCancellationRequested || !IsLatestRequest(requestId))
				return false;

			if (!canApply(state, result))
				return false;

			apply(result);
			return true;
		}
		catch (OperationCanceledException)
		{
			return false;
		}
		finally
		{
			lock (_sync)
			{
				if (ReferenceEquals(_currentRunCancellation, runCancellation))
				{
					_currentRunCancellation = null;
					runCancellation.Dispose();
				}
			}
		}
	}

	private bool IsLatestRequest(int requestId)
	{
		lock (_sync)
		{
			return requestId == _latestRequestId;
		}
	}
}

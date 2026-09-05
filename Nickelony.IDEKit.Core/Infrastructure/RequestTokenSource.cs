namespace Nickelony.IDEKit.Core.Infrastructure;

/// <summary>
/// Provides request tokens used to identify the latest asynchronous result and invalidate stale ones.
/// </summary>
public sealed class RequestTokenSource
{
	private int _currentToken;

	/// <summary>
	/// Begins a new request and returns its token.
	/// </summary>
	public int Begin()
		=> Interlocked.Increment(ref _currentToken);

	/// <summary>
	/// Invalidates all outstanding requests so their tokens are no longer current.
	/// </summary>
	public void Invalidate()
		=> Interlocked.Increment(ref _currentToken);

	/// <summary>
	/// Determines whether the supplied token belongs to the most recent request.
	/// </summary>
	/// <param name="token">The request token to inspect.</param>
	/// <returns><see langword="true"/> when <paramref name="token"/> is current; otherwise, <see langword="false"/>.</returns>
	public bool IsCurrent(int token)
		=> token == Volatile.Read(ref _currentToken);
}

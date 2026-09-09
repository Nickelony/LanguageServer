using System.Diagnostics;

namespace Nickelony.LanguageServer.Lua.Tests;

/// <summary>
/// Provides poll-until-satisfied helpers for tests that observe a live or asynchronous result that has
/// no completion signal to synchronize on.
/// </summary>
internal static class TestPolling
{
	/// <summary>
	/// The shared timeout for tests that wait on an asynchronous notification or poll for a live result.
	/// </summary>
	internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

	internal static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(150);

	/// <summary>
	/// Repeatedly evaluates <paramref name="action"/> until <paramref name="isSatisfied"/> accepts the
	/// result or <paramref name="timeout"/> elapses, then fails the test with the last observed result.
	/// </summary>
	/// <typeparam name="TResult">The observed result type.</typeparam>
	/// <param name="action">The action that produces one observation.</param>
	/// <param name="isSatisfied">The predicate that accepts a completed observation.</param>
	/// <param name="timeout">The maximum time to keep polling.</param>
	/// <param name="failureMessage">The failure message used when the predicate is never satisfied.</param>
	/// <param name="describeLastResult">Describes the last observed result for the failure message.</param>
	/// <param name="pollInterval">The delay between observations, or <see langword="null"/> for the default.</param>
	/// <returns>The first result accepted by <paramref name="isSatisfied"/>.</returns>
	internal static async Task<TResult> WaitForAsync<TResult>(
		Func<Task<TResult>> action,
		Func<TResult, bool> isSatisfied,
		TimeSpan timeout,
		string failureMessage,
		Func<TResult, string> describeLastResult,
		TimeSpan? pollInterval = null)
	{
		TimeSpan effectivePollInterval = pollInterval ?? DefaultPollInterval;
		Stopwatch stopwatch = Stopwatch.StartNew();
		TResult lastResult = default!;

		while (stopwatch.Elapsed < timeout)
		{
			lastResult = await action().ConfigureAwait(false);

			if (isSatisfied(lastResult))
				return lastResult;

			await Task.Delay(effectivePollInterval).ConfigureAwait(false);
		}

		Assert.Fail(failureMessage + Environment.NewLine + describeLastResult(lastResult));
		return lastResult;
	}
}

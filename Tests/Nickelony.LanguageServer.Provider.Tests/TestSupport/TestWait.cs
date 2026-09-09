namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Shared bounded polling helper for asynchronous assertions: waits until a condition holds or the timeout
/// elapses. Wall-clock budgets stay generous so assertions remain stable under load.
/// </summary>
internal static class TestWait
{
	/// <summary>
	/// Polls <paramref name="predicate"/> every 10 ms until it holds or <paramref name="timeout"/> elapses.
	/// </summary>
	/// <param name="predicate">The condition to observe.</param>
	/// <param name="timeout">The maximum wait time.</param>
	/// <returns><see langword="true"/> when the condition held; otherwise, <see langword="false"/>.</returns>
	public static async Task<bool> ForConditionAsync(Func<bool> predicate, TimeSpan timeout)
	{
		DateTime deadline = DateTime.UtcNow + timeout;

		while (DateTime.UtcNow < deadline)
		{
			if (predicate())
				return true;

			await Task.Delay(10).ConfigureAwait(false);
		}

		return predicate();
	}
}

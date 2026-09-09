namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>
/// Provides the shared condition-polling helper for tests that must wait for asynchronous effects.
/// </summary>
internal static class TestWait
{
	private static readonly TimeSpan s_pollInterval = TimeSpan.FromMilliseconds(10);

	/// <summary>
	/// Polls <paramref name="condition"/> until it becomes <see langword="true"/> or <paramref name="timeout"/> elapses.
	/// </summary>
	/// <param name="condition">The condition to poll.</param>
	/// <param name="timeout">The maximum time to wait for the condition.</param>
	/// <param name="failureMessage">The assertion message used when the timeout elapses.</param>
	public static async Task UntilAsync(Func<bool> condition, TimeSpan timeout, string? failureMessage = null)
	{
		DateTime deadline = DateTime.UtcNow + timeout;

		while (!condition())
		{
			if (DateTime.UtcNow >= deadline)
				Assert.Fail(failureMessage ?? "Condition was not reached within the timeout.");

			await Task.Delay(s_pollInterval).ConfigureAwait(false);
		}
	}
}

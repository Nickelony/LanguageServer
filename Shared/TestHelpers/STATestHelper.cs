using System.Runtime.ExceptionServices;

namespace Nickelony.IDEKit.Testing;

/// <summary>
/// Runs test bodies on dedicated single-threaded apartment threads.
/// </summary>
internal static class STATestHelper
{
	private static readonly TimeSpan s_staThreadTimeout = TimeSpan.FromSeconds(30);

	/// <summary>
	/// Runs the test body on a dedicated STA thread without installing a synchronization context,
	/// using the default timeout.
	/// </summary>
	/// <param name="action">The test body to run.</param>
	public static void RunInSTA(Action action)
		=> RunInSTA(action, s_staThreadTimeout);

	/// <summary>
	/// Runs the test body on a dedicated STA thread without installing a synchronization context.
	/// Exceptions from the test body are rethrown on the calling thread.
	/// </summary>
	/// <param name="action">The test body to run.</param>
	/// <param name="timeout">The maximum time to wait for the test body; timing out does not interrupt the test body.</param>
	/// <exception cref="TimeoutException">The test body does not finish within <paramref name="timeout"/>.</exception>
	public static void RunInSTA(Action action, TimeSpan timeout)
	{
		ArgumentNullException.ThrowIfNull(action);

		ExceptionDispatchInfo? capturedException = null;

		var thread = new Thread(() =>
		{
			try
			{
				action();
			}
			catch (Exception exception)
			{
				capturedException = ExceptionDispatchInfo.Capture(exception);
			}
		});

		thread.SetApartmentState(ApartmentState.STA);
		thread.IsBackground = true;
		thread.Start();

		if (!thread.Join(timeout))
			throw new TimeoutException("The STA test thread did not finish within the allotted time.");

		capturedException?.Throw();
	}
}

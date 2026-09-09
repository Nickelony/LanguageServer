namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Provides the per-subscriber background drain schedule shared by the serialized subscriber sets: at most one drain
/// runs per subscriber at a time, and work enqueued while a drain is running causes exactly one rescheduled drain.
/// </summary>
internal abstract class SerializedSubscriberDrain
{
	private int _drainScheduled;
	private int _isDisposed;

	/// <summary>
	/// Gets a value indicating whether this subscriber was disposed and should stop draining.
	/// </summary>
	protected bool IsDisposed => Volatile.Read(ref _isDisposed) != 0;

	/// <summary>
	/// Gets a value indicating whether work is still pending that requires another scheduled drain.
	/// </summary>
	protected abstract bool HasPendingWork { get; }

	/// <summary>
	/// Drains at most one round of pending work for this subscriber.
	/// </summary>
	/// <returns><see langword="true"/> when a round of work was processed and the drain loop should continue; otherwise, <see langword="false"/>.</returns>
	protected abstract bool TryDrainNext();

	/// <summary>
	/// Marks this subscriber as disposed.
	/// </summary>
	/// <returns><see langword="true"/> when this call performed the transition; otherwise, <see langword="false"/>.</returns>
	protected bool TryMarkDisposed() => Interlocked.Exchange(ref _isDisposed, 1) == 0;

	/// <summary>
	/// Reports one handler failure through the subscriber set's logger while isolating a throwing logger callback.
	/// </summary>
	/// <param name="logHandlerFailure">The logger callback supplied by the subscriber set.</param>
	/// <param name="exception">The handler failure to report.</param>
	protected static void TryLogHandlerFailure(Action<Exception> logHandlerFailure, Exception exception)
	{
		try
		{
			logHandlerFailure(exception);
		}
		catch (Exception)
		{
			// A throwing host logger must not stop the remaining drained payloads from being delivered.
		}
	}

	/// <summary>
	/// Schedules one background drain for this subscriber unless a drain is already scheduled.
	/// </summary>
	protected void TryScheduleDrain()
	{
		if (Interlocked.CompareExchange(ref _drainScheduled, 1, 0) != 0)
			return;

		ThreadPool.QueueUserWorkItem(static state => ((SerializedSubscriberDrain)state!).Drain(), this, preferLocal: false);
	}

	private void Drain()
	{
		try
		{
			try
			{
				while (TryDrainNext())
				{
					if (IsDisposed)
						return;
				}
			}
			catch (Exception)
			{
				// An exception raised by a subscriber set while draining must not escape a thread-pool work item; an
				// unhandled exception on a pool thread terminates the process on .NET Core. Handler failures are
				// already isolated per subscriber inside TryDrainNext, so this guard only protects the drain machinery.
			}
		}
		finally
		{
			Volatile.Write(ref _drainScheduled, 0);

			if (!IsDisposed && HasPendingWork)
				TryScheduleDrain();
		}
	}
}

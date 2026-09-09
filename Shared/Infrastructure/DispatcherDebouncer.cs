using System.Windows.Threading;

namespace Nickelony.IDEKit.Infrastructure;

/// <summary>
/// Runs a single debounced callback on a dispatcher: arming the debouncer replaces any pending
/// callback, and the last armed callback runs once after the configured delay.
/// </summary>
/// <remarks>
/// <para>
/// The debouncer runs its callback on the dispatcher supplied at construction, or on the creating
/// thread's dispatcher when none is supplied, because <see cref="DispatcherTimer"/> fires on its
/// associated dispatcher. <see cref="Arm"/> captures the pending callback, so callers can hand over
/// per-invocation state through the callback closure instead of tracking it in fields of their own.
/// </para>
/// <para>
/// This file is compiled into every package that needs it through a <c>&lt;Compile Include&gt;</c> link.
/// The <c>Nickelony.IDEKit.Infrastructure</c> namespace is intentionally shared by those linked copies
/// instead of following one project's folder-to-namespace convention, so the helper keeps a single
/// identity across packages.
/// </para>
/// </remarks>
internal sealed class DispatcherDebouncer : IDisposable
{
	private readonly DispatcherTimer _timer;
	private Action? _callback;
	private bool _isDisposed;

	/// <summary>
	/// Initializes a new instance of the <see cref="DispatcherDebouncer"/> class.
	/// </summary>
	/// <param name="delay">The debounce delay before an armed callback runs. Must not be negative.</param>
	/// <param name="dispatcher">
	/// The dispatcher the callback runs on, or <see langword="null"/> for the creating thread's dispatcher.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="delay"/> is negative.</exception>
	public DispatcherDebouncer(TimeSpan delay, Dispatcher? dispatcher = null)
	{
		if (delay < TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(delay), delay, "The debounce delay must not be negative.");

		_timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher ?? Dispatcher.CurrentDispatcher)
		{
			Interval = delay
		};

		_timer.Tick += HandleTick;
	}

	/// <summary>
	/// Gets a value indicating whether a callback is armed and waiting for its debounce delay.
	/// </summary>
	public bool IsPending => _timer.IsEnabled;

	/// <summary>
	/// Arms the debouncer with the callback to run after the debounce delay, replacing any callback
	/// armed before it. Arming again restarts the delay.
	/// </summary>
	/// <param name="callback">The callback to run when the delay elapses.</param>
	public void Arm(Action callback)
	{
		if (_isDisposed)
			return;

		_callback = callback;
		_timer.Stop();
		_timer.Start();
	}

	/// <summary>
	/// Cancels the armed callback, if any, so it never runs. Has no effect on an already running callback
	/// or on a disposed debouncer.
	/// </summary>
	public void Cancel()
	{
		if (_isDisposed)
			return;

		_timer.Stop();
		_callback = null;
	}

	/// <summary>
	/// Cancels any armed callback and stops the debouncer. Further <see cref="Arm"/> calls are ignored.
	/// </summary>
	public void Dispose()
	{
		if (_isDisposed)
			return;

		_isDisposed = true;
		_timer.Stop();
		_timer.Tick -= HandleTick;
		_callback = null;
	}

	private void HandleTick(object? sender, EventArgs e)
	{
		_timer.Stop();

		Action? callback = _callback;
		_callback = null;
		callback?.Invoke();
	}
}

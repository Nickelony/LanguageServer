using System.Diagnostics;
using System.Windows.Threading;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

/// <summary>
/// Provides dispatcher pumping helpers for tests that wait for timer or posted dispatcher work.
/// </summary>
internal static class DispatcherTestUtils
{
	/// <summary>
	/// Pumps dispatcher frames until the condition holds or the deadline elapses, then asserts the condition.
	/// </summary>
	/// <param name="condition">The condition to wait for.</param>
	/// <param name="timeout">The optional timeout; defaults to five seconds.</param>
	/// <param name="priority">
	/// The priority of the frame-exit callback. Pumping below <see cref="DispatcherPriority.ContextIdle"/>
	/// lets posted context-idle work run before the frame exits; the default background priority keeps
	/// the original behavior.
	/// </param>
	internal static void PumpUntil(
		Func<bool> condition,
		TimeSpan? timeout = null,
		DispatcherPriority priority = DispatcherPriority.Background)
	{
		var stopwatch = Stopwatch.StartNew();
		TimeSpan effectiveTimeout = timeout ?? TimeSpan.FromSeconds(5.0);

		while (!condition() && stopwatch.Elapsed < effectiveTimeout)
		{
			var frame = new DispatcherFrame();
			Dispatcher.CurrentDispatcher.BeginInvoke(priority, new Action(() => frame.Continue = false));
			Dispatcher.PushFrame(frame);
		}

		Assert.IsTrue(condition(), "The expected dispatcher work did not complete within the allotted time.");
	}

	/// <summary>
	/// Pumps dispatcher frames for the given duration.
	/// </summary>
	/// <param name="duration">How long to keep pumping.</param>
	/// <param name="priority">
	/// The priority of the frame-exit callback. Pumping below <see cref="DispatcherPriority.ContextIdle"/>
	/// lets posted context-idle work run during the pump; the default background priority keeps the
	/// original behavior.
	/// </param>
	internal static void PumpFrames(TimeSpan duration, DispatcherPriority priority = DispatcherPriority.Background)
	{
		var stopwatch = Stopwatch.StartNew();

		while (stopwatch.Elapsed < duration)
		{
			var frame = new DispatcherFrame();
			Dispatcher.CurrentDispatcher.BeginInvoke(priority, new Action(() => frame.Continue = false));
			Dispatcher.PushFrame(frame);
		}
	}
}

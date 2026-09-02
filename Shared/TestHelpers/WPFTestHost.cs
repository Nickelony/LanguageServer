using System.Reflection;
using System.Windows;
using System.Windows.Threading;

namespace Nickelony.IDEKit.Testing;

/// <summary>
/// Provides WPF hosting and reflection helpers for UI tests.
/// </summary>
internal static class WPFTestHost
{
	/// <summary>
	/// Shows the given content in a non-activating host window and pumps initial background-priority
	/// dispatcher work.
	/// </summary>
	/// <param name="content">The content to host.</param>
	/// <returns>The host window; callers must close it.</returns>
	public static Window ShowInHostWindow(FrameworkElement content)
	{
		ArgumentNullException.ThrowIfNull(content);

		var window = new Window
		{
			Content = content,
			Width = 800,
			Height = 600,
			ShowActivated = false,
			ShowInTaskbar = false,
			WindowStyle = WindowStyle.None
		};

		window.Show();
		PumpDispatcher(window.Dispatcher, DispatcherPriority.Background);
		return window;
	}

	/// <summary>
	/// Synchronously schedules a no-op at the given priority, allowing queued dispatcher work at
	/// higher priorities to run first.
	/// </summary>
	/// <param name="dispatcher">The dispatcher to pump.</param>
	/// <param name="priority">The priority at which to schedule the synchronization callback.</param>
	public static void PumpDispatcher(Dispatcher dispatcher, DispatcherPriority priority)
	{
		ArgumentNullException.ThrowIfNull(dispatcher);
		dispatcher.Invoke(priority, new Action(() => { }));
	}

	/// <summary>
	/// Reads a non-public instance field by name.
	/// </summary>
	/// <typeparam name="T">The field type.</typeparam>
	/// <param name="instance">The instance to read from.</param>
	/// <param name="fieldName">The field name.</param>
	/// <returns>The field value cast to <typeparamref name="T"/>.</returns>
	public static T GetPrivateField<T>(object instance, string fieldName)
	{
		ArgumentNullException.ThrowIfNull(instance);
		ArgumentNullException.ThrowIfNull(fieldName);

		FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
			?? throw new InvalidOperationException($"Field '{fieldName}' was not found on '{instance.GetType().Name}'.");

		return (T)(field.GetValue(instance) ?? throw new InvalidOperationException($"Field '{fieldName}' was null."));
	}
}

using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Nickelony.IDEKit.Testing;

/// <summary>
/// Provides WPF/AvalonEdit hosting, element, and reflection helpers for UI tests.
/// </summary>
/// <remarks>
/// Every visual test needs a window station: tests that show a host window (through
/// <see cref="ShowInHostWindow"/>) require an interactive Windows session and cannot run headless.
/// </remarks>
internal static class WPFTestHost
{
	/// <summary>
	/// The design font size unattached elements inherit. Pinning it keeps layout assertions
	/// independent of the machine's text-size setting.
	/// </summary>
	public const double DesignFontSize = 12.0;

	/// <summary>
	/// Creates a text editor with the given document text on the current thread.
	/// </summary>
	/// <param name="text">The document text.</param>
	/// <returns>The created editor; owned by the caller.</returns>
	public static TextEditor CreateEditor(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		return new TextEditor { Document = new TextDocument(text) };
	}

	/// <summary>
	/// Pins the element's font size so layout assertions stay independent of the machine's
	/// text-size setting.
	/// </summary>
	/// <param name="element">The element to pin.</param>
	/// <param name="fontSize">The font size to apply; the design font size by default.</param>
	public static void PinDesignFontSize(DependencyObject element, double fontSize = DesignFontSize)
	{
		ArgumentNullException.ThrowIfNull(element);
		TextBlock.SetFontSize(element, fontSize);
	}

	/// <summary>
	/// Shows the given content in a non-activating host window and pumps initial background-priority
	/// dispatcher work.
	/// </summary>
	/// <param name="content">The content to host.</param>
	/// <returns>The host window; dispose it to close the window.</returns>
	public static HostWindow ShowInHostWindow(FrameworkElement content)
	{
		ArgumentNullException.ThrowIfNull(content);

		var window = new HostWindow
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
	/// Adds the margin to the editor's left margins and shows the editor in a host window.
	/// </summary>
	/// <param name="editor">The editor to host.</param>
	/// <param name="margin">The margin to add to the editor's left margins.</param>
	/// <returns>The host window; dispose it to close the window.</returns>
	public static HostWindow ShowEditorWithMargin(TextEditor editor, UIElement margin)
	{
		ArgumentNullException.ThrowIfNull(editor);
		ArgumentNullException.ThrowIfNull(margin);

		editor.TextArea.LeftMargins.Add(margin);
		return ShowInHostWindow(editor);
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

/// <summary>
/// A non-activating test host window that closes itself when disposed.
/// </summary>
internal sealed class HostWindow : Window, IDisposable
{
	/// <inheritdoc />
	public void Dispose()
	{
		if (IsVisible || IsLoaded)
			Close();
	}
}

using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using System.Windows;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

[STATestClass]
public sealed class CompletionWindowCoordinatorTests
{
	[TestMethod]
	public void Create_WithDefaults_CreatesUntrackedWindowWithHeightCap()
	{
		(CompletionWindowCoordinator coordinator, HostWindow hostWindow) = CreateHostedCoordinator();

		using (hostWindow)
		{
			CompletionWindow window = coordinator.Create();

			// A created but not yet shown window is not tracked.
			Assert.IsFalse(coordinator.IsWindowOpen);
			Assert.IsNull(coordinator.ActiveWindow);

			// The window is created with the shared completion window height cap; the controller sizes the
			// width from the measured items before showing the window, so the initial width is AvalonEdit's
			// metadata (deliberately not pinned here, because this package does not own it).
			Assert.AreEqual(300.0, window.MaxHeight);

			// CompletionWindowBase overrides the window style metadata to None, so no assignment is needed.
			Assert.AreEqual(WindowStyle.None, window.WindowStyle);

			coordinator.Show();

			Assert.IsTrue(coordinator.IsWindowOpen);
			Assert.AreSame(window, coordinator.ActiveWindow);

			coordinator.Close();

			Assert.IsFalse(coordinator.IsWindowOpen);
			Assert.IsNull(coordinator.ActiveWindow);
		}
	}

	[TestMethod]
	public void Create_AppliesTheDocumentedWindowProperties()
	{
		ICSharpCode.AvalonEdit.TextEditor editor = CompletionTestHost.CreateEditor();
		var skin = new CompletionWindowSkin(Brushes.Gray, Brushes.Black, Brushes.White, BorderThickness: 2.0);
		var coordinator = new CompletionWindowCoordinator(editor.TextArea, skin);
		HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		using (hostWindow)
		{
			CompletionWindow window = coordinator.Create();

			// AvalonEdit only sets the window style metadata, so the skin and the coordinator's resize
			// setting are the only source of these values; the border thickness comes from the skin.
			Assert.AreEqual(ResizeMode.NoResize, window.ResizeMode);
			Assert.AreEqual(new Thickness(2.0), window.BorderThickness);
			Assert.AreSame(skin.Background, window.Background);
			Assert.AreSame(skin.Foreground, window.Foreground);
			Assert.AreSame(skin.BorderBrush, window.BorderBrush);
		}
	}

	[TestMethod]
	public void Constructor_NegativeSkinBorderThickness_ThrowsArgumentOutOfRangeException()
	{
		ICSharpCode.AvalonEdit.TextEditor editor = CompletionTestHost.CreateEditor();
		var skin = new CompletionWindowSkin(Brushes.Gray, Brushes.Black, Brushes.White, BorderThickness: -1.0);

		var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => new CompletionWindowCoordinator(editor.TextArea, skin));

		Assert.AreEqual("defaultSkin", exception.ParamName);
	}

	[TestMethod]
	public void IsWindowOpen_TracksWindowUntilItClosesItself()
	{
		(CompletionWindowCoordinator coordinator, HostWindow hostWindow) = CreateHostedCoordinator();

		using (hostWindow)
		{
			CompletionWindow window = coordinator.Create();

			Assert.IsFalse(coordinator.IsWindowOpen);

			coordinator.Show();

			Assert.IsTrue(coordinator.IsWindowOpen);
			Assert.AreSame(window, coordinator.ActiveWindow);
			Assert.IsTrue(window.IsVisible);

			window.Close();

			// A self-closed window is no longer tracked.
			Assert.IsFalse(coordinator.IsWindowOpen);
			Assert.IsNull(coordinator.ActiveWindow);
		}
	}

	[TestMethod]
	public void Close_WithTrackedWindow_ClosesItAndClearsTracking()
	{
		(CompletionWindowCoordinator coordinator, HostWindow hostWindow) = CreateHostedCoordinator();

		using (hostWindow)
		{
			CompletionWindow window = coordinator.Create();
			coordinator.Show();

			coordinator.Close();

			Assert.IsFalse(coordinator.IsWindowOpen);
			Assert.IsNull(coordinator.ActiveWindow);
			Assert.IsFalse(window.IsVisible);
		}
	}

	[TestMethod]
	public void Create_TwiceWithoutShow_ClosesTheUnshownWindowAndCreatesANewOne()
	{
		(CompletionWindowCoordinator coordinator, HostWindow hostWindow) = CreateHostedCoordinator();

		using (hostWindow)
		{
			CompletionWindow first = coordinator.Create();
			bool firstClosed = false;
			first.Closed += (_, _) => firstClosed = true;

			CompletionWindow second = coordinator.Create();

			// The created-but-unshown window is closed by the replacement instead of being stranded
			// untracked, and the replacement stays usable.
			Assert.AreNotSame(first, second);
			Assert.IsTrue(firstClosed, "The created-but-unshown window must be closed by the replacement.");
			Assert.IsFalse(coordinator.IsWindowOpen);
			Assert.IsNull(coordinator.ActiveWindow);

			coordinator.Show();

			Assert.IsTrue(coordinator.IsWindowOpen);
			Assert.AreSame(second, coordinator.ActiveWindow);

			coordinator.Close();
		}
	}

	[TestMethod]
	public void Create_WhileWindowTracked_ClosesAndReportsItExactlyOnce()
	{
		(CompletionWindowCoordinator coordinator, HostWindow hostWindow) = CreateHostedCoordinator();

		using (hostWindow)
		{
			int firstClosedCalls = 0;
			coordinator.WindowClosed += (_, _) => firstClosedCalls++;
			CompletionWindow first = coordinator.Create();
			coordinator.Show();

			// Replacing the tracked window closes it and raises the event for it exactly once.
			CompletionWindow second = coordinator.Create(250.0);

			Assert.IsFalse(first.IsVisible);
			Assert.AreEqual(1, firstClosedCalls, "Replacing a window raises the window-closed event exactly once.");
			Assert.AreEqual(250.0, second.MaxHeight);
			Assert.IsFalse(coordinator.IsWindowOpen);

			coordinator.Show();
			coordinator.Close();
		}
	}

	[TestMethod]
	public void Show_WindowClosesItself_ReportsOnce()
	{
		(CompletionWindowCoordinator coordinator, HostWindow hostWindow) = CreateHostedCoordinator();

		using (hostWindow)
		{
			int closedCalls = 0;
			coordinator.WindowClosed += (_, _) => closedCalls++;
			CompletionWindow window = coordinator.Create();
			coordinator.Show();

			window.Close();

			Assert.AreEqual(1, closedCalls);
			Assert.IsFalse(coordinator.IsWindowOpen);
		}
	}

	[TestMethod]
	public void Close_TrackedWindow_ReportsThroughTheEventExactlyOnce()
	{
		(CompletionWindowCoordinator coordinator, HostWindow hostWindow) = CreateHostedCoordinator();

		using (hostWindow)
		{
			int closedCalls = 0;
			coordinator.WindowClosed += (_, _) => closedCalls++;
			CompletionWindow window = coordinator.Create();
			coordinator.Show();

			coordinator.Close();

			Assert.AreEqual(1, closedCalls);
			Assert.IsFalse(window.IsVisible);

			// Closing again with nothing tracked is a no-op that reports nothing more.
			coordinator.Close();

			Assert.AreEqual(1, closedCalls);
		}
	}

	[TestMethod]
	public void Close_ThrowingSubscriber_PropagatesToTheCloser()
	{
		(CompletionWindowCoordinator coordinator, HostWindow hostWindow) = CreateHostedCoordinator();

		using (hostWindow)
		{
			coordinator.WindowClosed += (_, _) => throw new InvalidOperationException("Subscriber failed.");
			coordinator.Create();
			coordinator.Show();

			// The documented contract: a throwing subscriber propagates to whoever closed the window,
			// exactly like a throwing host callback on the close path, while the window stays untracked.
			var exception = Assert.ThrowsExactly<InvalidOperationException>(() => coordinator.Close());

			Assert.AreEqual("Subscriber failed.", exception.Message);
			Assert.IsFalse(coordinator.IsWindowOpen);
			Assert.IsNull(coordinator.ActiveWindow);
		}
	}

	[TestMethod]
	public void Show_WithoutCreatedWindow_IsANoOp()
	{
		(CompletionWindowCoordinator coordinator, HostWindow hostWindow) = CreateHostedCoordinator();

		using (hostWindow)
		{
			int closedCalls = 0;
			coordinator.WindowClosed += (_, _) => closedCalls++;

			coordinator.Show();

			Assert.IsFalse(coordinator.IsWindowOpen);
			Assert.IsNull(coordinator.ActiveWindow);

			// A second show with the same window still tracked keeps it tracked and reports nothing.
			CompletionWindow window = coordinator.Create();
			coordinator.Show();
			coordinator.Show();

			Assert.IsTrue(coordinator.IsWindowOpen);
			Assert.AreSame(window, coordinator.ActiveWindow);
			Assert.AreEqual(0, closedCalls);

			// Close the created window so the test does not leave a live popup behind.
			coordinator.Close();
			Assert.IsFalse(coordinator.IsWindowOpen);
		}
	}

	[TestMethod]
	public void Close_AfterWindowClosedItself_ReportsNothingMore()
	{
		(CompletionWindowCoordinator coordinator, HostWindow hostWindow) = CreateHostedCoordinator();

		using (hostWindow)
		{
			int closedCalls = 0;
			coordinator.WindowClosed += (_, _) => closedCalls++;
			CompletionWindow window = coordinator.Create();
			coordinator.Show();

			window.Close();
			coordinator.Close();

			Assert.AreEqual(1, closedCalls, "A self-closed window must not be reported a second time by Close.");
			Assert.IsFalse(coordinator.IsWindowOpen);
		}
	}

	[TestMethod]
	public void Show_FailingShow_DoesNotTrackWindowOrRaiseTheEvent()
	{
		(CompletionWindowCoordinator coordinator, HostWindow hostWindow) = CreateHostedCoordinator();

		using (hostWindow)
		{
			int closedCalls = 0;
			coordinator.WindowClosed += (_, _) => closedCalls++;

			// A window that has already been closed cannot be shown again, which models a failing show operation.
			CompletionWindow window = coordinator.Create();
			window.Show();
			window.Close();

			Assert.ThrowsExactly<InvalidOperationException>(() => coordinator.Show());

			// A window that never became visible must not stay tracked and must not report a closure.
			Assert.IsFalse(coordinator.IsWindowOpen);
			Assert.IsNull(coordinator.ActiveWindow);
			Assert.AreEqual(0, closedCalls);
		}
	}

	[TestMethod]
	public void Create_WithNegativeOrNonFiniteMaxHeight_ThrowsArgumentOutOfRangeException()
	{
		(CompletionWindowCoordinator coordinator, HostWindow hostWindow) = CreateHostedCoordinator();

		using (hostWindow)
		{
			Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => coordinator.Create(-1.0));
			Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => coordinator.Create(double.NaN));
		}
	}

	[TestMethod]
	public void Constructor_WithNullBrush_ThrowsArgumentNullException()
	{
		ICSharpCode.AvalonEdit.TextEditor editor = CompletionTestHost.CreateEditor();
		var skin = new CompletionWindowSkin(null!, Brushes.Black, Brushes.White);

		Assert.ThrowsExactly<ArgumentNullException>(() => new CompletionWindowCoordinator(editor.TextArea, skin));
	}

	[TestMethod]
	public void Constructor_WithNullBackgroundBrush_ThrowsArgumentNullException()
	{
		ICSharpCode.AvalonEdit.TextEditor editor = CompletionTestHost.CreateEditor();
		var skin = new CompletionWindowSkin(Brushes.Gray, null!, Brushes.White);

		Assert.ThrowsExactly<ArgumentNullException>(() => new CompletionWindowCoordinator(editor.TextArea, skin));
	}

	[TestMethod]
	public void Constructor_WithNullForegroundBrush_ThrowsArgumentNullException()
	{
		ICSharpCode.AvalonEdit.TextEditor editor = CompletionTestHost.CreateEditor();
		var skin = new CompletionWindowSkin(Brushes.Gray, Brushes.Black, null!);

		Assert.ThrowsExactly<ArgumentNullException>(() => new CompletionWindowCoordinator(editor.TextArea, skin));
	}

	[TestMethod]
	public void Close_OnCreatedNotShownWindow_ClosesWithoutRaisingTheEvent()
	{
		(CompletionWindowCoordinator coordinator, HostWindow hostWindow) = CreateHostedCoordinator();

		using (hostWindow)
		{
			int closedEvents = 0;
			coordinator.WindowClosed += (_, _) => closedEvents++;

			CompletionWindow window = coordinator.Create();

			// A window that was created but never shown does not raise the closure event when it is closed.
			coordinator.Close();

			Assert.IsNull(coordinator.ActiveWindow);
			Assert.AreEqual(0, closedEvents);
			Assert.IsFalse(window.IsVisible);
		}
	}

	private static (CompletionWindowCoordinator Coordinator, HostWindow HostWindow) CreateHostedCoordinator()
	{
		ICSharpCode.AvalonEdit.TextEditor editor = CompletionTestHost.CreateEditor();

		return (CompletionTestHost.CreateCoordinator(editor), WPFTestHost.ShowInHostWindow(editor));
	}
}

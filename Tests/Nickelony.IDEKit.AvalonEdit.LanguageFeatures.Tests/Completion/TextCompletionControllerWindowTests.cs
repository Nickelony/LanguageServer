using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.IntelliSense.Completion;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionTestHost;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

[STATestClass]
public sealed class TextCompletionControllerWindowTests
{
	[TestMethod]
	public void OpenOrRefresh_SizesWindowToContentWithinConfiguredCap()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 5));

				CompletionWindow smallWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				smallWindow.UpdateLayout();

				// The window is content-sized: a single item stays below the cap...
				Assert.IsTrue(smallWindow.ActualHeight > 0.0);
				Assert.IsTrue(smallWindow.ActualHeight < smallWindow.MaxHeight);

				// ...and the effective floor is the content minimum plus the window chrome.
				TextCompletionControllerOptions defaultOptions = TextCompletionControllerOptions.Default;

				Assert.AreEqual(
					defaultOptions.WindowMinContentWidth + defaultOptions.WindowHorizontalChrome,
					smallWindow.Width);

				ICompletionData[] tallItemSet = [.. Enumerable.Range(0, 60).Select(index => CreateItem($"pawn{index}"))];

				// The replacement start moves, so the controller opens a replacement window instead of
				// refreshing the open one in place.
				Assert.IsTrue(hosted.Controller.OpenOrRefresh(tallItemSet, 1, 5));

				CompletionWindow tallWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The replacement completion window was not tracked.");

				Assert.AreNotSame(smallWindow, tallWindow);

				tallWindow.UpdateLayout();

				// A tall item set grows towards the configured cap instead of exceeding it.
				Assert.AreEqual((double)defaultOptions.WindowMaxHeight, tallWindow.MaxHeight);
				Assert.IsTrue(tallWindow.ActualHeight <= tallWindow.MaxHeight);
				Assert.IsTrue(tallWindow.ActualHeight > smallWindow.ActualHeight);
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_ThrowingConfigureWindow_ClosesCreatedWindowAndKeepsStateClosed()
	{
		var editor = new ICSharpCode.AvalonEdit.TextEditor
		{
			Text = "sample"
		};

		HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		using (hostWindow)
		{
			using var controller = CompletionTestHost.CreateController(
				editor,
				hooks: new TextCompletionControllerHooks
				{
					ConfigureWindow = _ => throw new InvalidOperationException("Window configuration failed.")
				});

			Assert.ThrowsExactly<InvalidOperationException>(
				() => controller.OpenOrRefresh([CreateItem("sample")], 0, 6));

			// The created window must not be stranded or tracked after the hook failure.
			Assert.IsNull(controller.WindowCoordinator.ActiveWindow);
			Assert.IsFalse(controller.WindowCoordinator.IsWindowOpen);
			Assert.IsFalse(controller.CurrentPresentation.IsListVisible);
		}
	}

	[TestMethod]
	public void OpenOrRefresh_ThrowingConfigureToolTip_ClosesCreatedWindowAndKeepsStateClosed()
	{
		var editor = new ICSharpCode.AvalonEdit.TextEditor
		{
			Text = "sample"
		};

		HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		using (hostWindow)
		{
			using var controller = CompletionTestHost.CreateController(
				editor,
				hooks: new TextCompletionControllerHooks
				{
					ConfigureToolTip = _ => throw new InvalidOperationException("Tooltip configuration failed.")
				});

			// The tooltip hook runs inside the same rollback scope as the window hook.
			Assert.ThrowsExactly<InvalidOperationException>(
				() => controller.OpenOrRefresh([CreateItem("sample")], 0, 6));

			Assert.IsNull(controller.WindowCoordinator.ActiveWindow);
			Assert.IsFalse(controller.WindowCoordinator.IsWindowOpen);
			Assert.IsFalse(controller.CurrentPresentation.IsListVisible);
		}
	}

	[TestMethod]
	public void OpenOrRefresh_ConfigureWindowHook_RunsAfterSizingSoItCanPinTheWidth()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(hooks: new TextCompletionControllerHooks
			{
				ConfigureWindow = window => window.Width = 333.0
			});

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				// The hook runs after the controller sized the window, so the pinned width survives.
				Assert.AreEqual(333.0, completionWindow.Width);
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_ConfigureWindowHook_CanConfigureTheWindow()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(hooks: new TextCompletionControllerHooks
			{
				ConfigureWindow = window => window.FontSize = 42.0
			});

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				// The successful configuration path leaves the host's customizations in place.
				Assert.AreEqual(42.0, completionWindow.FontSize);
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_CustomMeasureItemWidth_ControlsWindowWidth()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(hooks: new TextCompletionControllerHooks
			{
				MeasureItemWidth = _ => 1000.0
			});

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				TextCompletionControllerOptions options = TextCompletionControllerOptions.Default;

				// The custom measurement exceeds the default floor but is still clamped by the maximum width.
				Assert.AreEqual((double)options.WindowMaxWidth, completionWindow.Width);
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_CustomDisplayInfo_IsUsedForWidthMeasurement()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(
				hooks: new TextCompletionControllerHooks
				{
					GetDisplayInfo = _ => ("sample", "a very long detail that widens the window considerably")
				},
				options: TextCompletionControllerOptions.Default with
				{
					WindowMinContentWidth = 100.0,
					ItemIconWidth = 0.0,
					ItemDetailSpacing = 0.0
				});

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				// The long detail widens the window beyond the 100-pixel content floor, so the measured detail
				// demonstrably drives the width instead of the floor.
				Assert.IsTrue(
					completionWindow.Width > TextCompletionControllerOptions.Default.WindowHorizontalChrome + 100.0);
			}
		}
	}

	[TestMethod]
	public void ScheduleCloseIfEmpty_WhenListIsEmpty_ClosesWindow()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(text: "sample");

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				completionWindow.CompletionList.CompletionData.Clear();
				hosted.Controller.ScheduleCloseIfEmpty();

				DispatcherTestUtils.PumpUntil(() => !hosted.Coordinator.IsWindowOpen);

				Assert.IsFalse(hosted.Controller.CurrentPresentation.IsListVisible);
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_PostedInitialSelection_EmptyFilteredList_ClosesTheWindow()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				// The replacement range spells "sample"; no item matches it, so the posted initial selection
				// empties the filtered list and the automatic close path must close the empty window.
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("zeta")], 0, 6));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				// The initial selection is posted at context-idle priority; pumping below that priority lets
				// the posted close run before the pump exits.
				DispatcherTestUtils.PumpUntil(
					() => !hosted.Coordinator.IsWindowOpen,
					priority: DispatcherPriority.ApplicationIdle);

				Assert.IsFalse(hosted.Controller.CurrentPresentation.IsListVisible);
				Assert.IsFalse(completionWindow.IsVisible);
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_CloseWhenEmptyDisabled_KeepsTheEmptyWindowOpen()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(options: CompletionTestHost.FastOptions with
			{
				CloseWhenEmpty = false
			});

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("zeta")], 0, 6));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				ListBox listBox = completionWindow.CompletionList.ListBox;

				// Wait for the posted context-idle selection to run and filter the list empty; with
				// CloseWhenEmpty disabled the window must stay open instead of closing itself.
				DispatcherTestUtils.PumpUntil(
					() => listBox.Items.Count == 0,
					priority: DispatcherPriority.ApplicationIdle);

				Assert.IsTrue(hosted.Coordinator.IsWindowOpen);
				Assert.IsTrue(hosted.Controller.CurrentPresentation.IsListVisible);
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_ScheduledInitialSelection_SelectsTheBestMatchForTheWindowQuery()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample"), CreateItem("second")], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				// The initial selection is posted at context-idle priority so it applies after the window's
				// layout; pumping below that priority lets the posted selection run before the pump exits.
				DispatcherTestUtils.PumpUntil(
					() => (completionWindow.CompletionList.ListBox.SelectedItem as ICompletionData)?.Text == "sample",
					priority: DispatcherPriority.ApplicationIdle);

				Assert.IsTrue(hosted.Coordinator.IsWindowOpen);
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_PreselectedItem_IsSelectedOverTheBestMatch()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				// The higher priority makes the first item AvalonEdit's best match; the provider's
				// preselection hint on the second item must win over that ranking.
				var items = new ICompletionData[]
				{
					new TextCompletionItemCompletionData(new TextCompletionItem("sample") { Priority = 10.0 }),
					new TextCompletionItemCompletionData(new TextCompletionItem("second") { IsPreselected = true })
				};

				Assert.IsTrue(hosted.Controller.OpenOrRefresh(items, 0, 1));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				// The posted context-idle selection applies the provider's preselection once the window's
				// layout ran; both items match the one-character query, so the preselected one is available.
				DispatcherTestUtils.PumpUntil(
					() => ReferenceEquals(completionWindow.CompletionList.ListBox.SelectedItem, items[1]),
					priority: DispatcherPriority.ApplicationIdle);

				Assert.IsTrue(hosted.Coordinator.IsWindowOpen);
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_CustomDataImplementingThePreselectionInterface_IsSelectedOverTheBestMatch()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				// A host item type opts into preselection through the interface, so the policy is not limited
				// to the package's default completion-data adapter.
				var items = new ICompletionData[]
				{
					new TestCompletionData("sample"),
					new TestCompletionData("second") { IsPreselected = true }
				};

				Assert.IsTrue(hosted.Controller.OpenOrRefresh(items, 0, 1));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				DispatcherTestUtils.PumpUntil(
					() => ReferenceEquals(completionWindow.CompletionList.ListBox.SelectedItem, items[1]),
					priority: DispatcherPriority.ApplicationIdle);

				Assert.IsTrue(hosted.Coordinator.IsWindowOpen);
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_PreselectedItemBeyondTheViewport_IsScrolledIntoView()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				// The window caps at its configured height, so an item at the end of a long list starts outside
				// the visible window; the preselection must scroll it into view instead of selecting an item the
				// user cannot see (AvalonEdit's selected-item setter does not scroll).
				ICompletionData[] items =
				[
					.. Enumerable.Range(0, 60).Select(index => (ICompletionData)new TextCompletionItemCompletionData(
						new TextCompletionItem($"item{index:00}") { IsPreselected = index == 59 }))
				];

				Assert.IsTrue(hosted.Controller.OpenOrRefresh(items, 0, 0));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				ListBox listBox = completionWindow.CompletionList.ListBox;

				DispatcherTestUtils.PumpUntil(
					() => ReferenceEquals(listBox.SelectedItem, items[59]),
					priority: DispatcherPriority.ApplicationIdle);

				completionWindow.UpdateLayout();

				// A realized container for the selected item proves the list scrolled to it: a virtualizing
				// list realizes only the visible items.
				DispatcherTestUtils.PumpUntil(
					() => listBox.ItemContainerGenerator.ContainerFromIndex(59) is not null);
			}
		}
	}

	[TestMethod]
	public void CompletionListClick_InlineBasedItemTemplate_SelectsTheClickedItem()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample"), CreateItem("second")], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				(ListBox listBox, Run run) = PrepareInlineTemplateClick(completionWindow);

				var eventArgs = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
				{
					RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent,
					Source = run
				};

				listBox.RaiseEvent(eventArgs);

				// The click selected the clicked item instead of throwing from the non-visual original source.
				Assert.AreEqual("second", (listBox.SelectedItem as ICompletionData)?.Text);
				Assert.IsFalse(eventArgs.Handled);
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_ActivatableWindowOption_DoesNotInstallTheExplicitClickSelection()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(options: TextCompletionControllerOptions.Default with
			{
				NonActivatingWindow = false
			});

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample"), CreateItem("second")], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				(ListBox listBox, Run run) = PrepareInlineTemplateClick(completionWindow);
				object? selectionBeforeClick = listBox.SelectedItem;

				var eventArgs = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
				{
					RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent,
					Source = run
				};

				listBox.RaiseEvent(eventArgs);

				// With an activatable window the controller installs neither the activation hook nor the
				// explicit click selection; a synthetic click cannot change the selection, so it stays as it
				// was. (The non-activating default selects the clicked item; its click path is pinned by
				// CompletionListClick_InlineBasedItemTemplate_SelectsTheClickedItem.)
				Assert.AreSame(selectionBeforeClick, listBox.SelectedItem);
			}
		}
	}

	[TestMethod]
	public void OpenOrRefresh_BareTextAreaHost_OpensWindow()
	{
		// A TextArea-only host - no TextEditor wrapper - can drive the completion window directly: the
		// controller reads only text-area state (the document, the dispatcher, and the typography), so a
		// custom control that composes its own TextArea is a supported composition point.
		var textArea = new TextArea
		{
			Document = new TextDocument("sample")
		};

		HostWindow hostWindow = WPFTestHost.ShowInHostWindow(textArea);

		using (hostWindow)
		{
			using var controller = new TextCompletionController(textArea, CreateSkin(), FastOptions);

			Assert.IsTrue(controller.OpenOrRefresh([CreateItem("sample")], 0, 5));
			Assert.IsNotNull(controller.WindowCoordinator.ActiveWindow);
		}
	}

	[TestMethod]
	public void OpenOrRefresh_WithoutDocument_ReportsNoOpAndDoesNotThrow()
	{
		// A bare text area can exist without a document; the controller must treat the call as a no-op
		// instead of dereferencing the missing document.
		var textArea = new TextArea
		{
			Document = null
		};

		HostWindow hostWindow = WPFTestHost.ShowInHostWindow(textArea);

		using (hostWindow)
		{
			using var controller = new TextCompletionController(textArea, CreateSkin(), FastOptions);

			Assert.IsNull(textArea.Document);
			Assert.IsFalse(controller.OpenOrRefresh([CreateItem("sample")], 0, 3));
			Assert.IsFalse(controller.WindowCoordinator.IsWindowOpen);

			// Attaching a document restores the normal open path.
			textArea.Document = new TextDocument("sample");

			Assert.IsTrue(controller.OpenOrRefresh([CreateItem("sample")], 0, 3));
			Assert.IsNotNull(controller.WindowCoordinator.ActiveWindow);
		}
	}

	[TestMethod]
	public void OpenOrRefresh_DocumentDetachedBeforeInitialSelection_DoesNotThrow()
	{
		(TextEditor Editor, TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CreateHostedEditorController(text: "sample");

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 3));

				// The posted initial selection must survive a document that detaches before it runs: the
				// window query falls back to the empty query instead of dereferencing the missing document.
				hosted.Editor.TextArea.Document = null;

				DispatcherTestUtils.PumpFrames(TimeSpan.FromMilliseconds(50.0), DispatcherPriority.ApplicationIdle);

				// Reattaching a document restores the normal path before teardown.
				hosted.Editor.TextArea.Document = new TextDocument("sample");

				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 3));
			}
		}
	}

	// Applies the shared inline-based item template used by the click tests and waits for the second item
	// container, returning the list and the clicked Run.
	private static (ListBox ListBox, Run Run) PrepareInlineTemplateClick(CompletionWindow completionWindow)
	{
		ListBox listBox = completionWindow.CompletionList.ListBox;

		// An inline-based item template makes the click's original source a Run, a non-visual ContentElement;
		// resolving the item container must not fall back to a visual-tree walk.
		listBox.ItemTemplate = (DataTemplate)XamlReader.Parse(
			"<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">"
			+ "<TextBlock><Run Text=\"{Binding Text, Mode=OneWay}\" /></TextBlock></DataTemplate>");

		completionWindow.UpdateLayout();

		// Container realization can need more than one layout pass on a loaded machine, so the helper waits
		// for the container instead of trusting a fixed delay.
		DispatcherTestUtils.PumpUntil(() => listBox.ItemContainerGenerator.ContainerFromIndex(1) is not null);

		completionWindow.UpdateLayout();

		var container = listBox.ItemContainerGenerator.ContainerFromIndex(1) as ListBoxItem
			?? throw new InvalidOperationException("The second item container was not realized.");

		TextBlock textBlock = FindVisualDescendant<TextBlock>(container)
			?? throw new InvalidOperationException("The item template's TextBlock was not found.");

		return (listBox, textBlock.Inlines.OfType<Run>().First());
	}

	private static T? FindVisualDescendant<T>(DependencyObject root) where T : DependencyObject
	{
		var queue = new Queue<DependencyObject>();
		queue.Enqueue(root);

		while (queue.Count > 0)
		{
			DependencyObject current = queue.Dequeue();

			if (current is T match)
				return match;

			int childCount = VisualTreeHelper.GetChildrenCount(current);

			for (int i = 0; i < childCount; i++)
				queue.Enqueue(VisualTreeHelper.GetChild(current, i));
		}

		return null;
	}

}

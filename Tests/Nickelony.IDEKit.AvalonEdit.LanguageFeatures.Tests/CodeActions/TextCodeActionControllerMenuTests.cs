using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.IntelliSense.CodeActions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

[STATestClass]
public sealed class TextCodeActionControllerMenuTests
{
	[TestMethod]
	public void TryOpenActions_WithActions_OpensTheMenuWithTheSkinAndItems()
	{
		using var host = new CodeActionTestHost();
		host.RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<TextCodeActionItem>>(
		[
			CodeActionTestHost.CreateItem("Fix it", isPreferred: true),
			CodeActionTestHost.CreateItem("Extract expression", kind: "refactor.extract")
		]);

		using var controller = host.CreateController();

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());

		Assert.IsTrue(controller.TryOpenActions());
		Assert.IsTrue(controller.IsActionsOpen);
		Assert.IsTrue(host.ConfigureMenuInvoked);

		ContextMenu menu = host.Menus[^1];

		Assert.AreSame(host.Editor.TextArea, menu.PlacementTarget);
		Assert.AreSame(Brushes.Black, menu.Background);
		Assert.AreSame(Brushes.White, menu.Foreground);
		Assert.AreSame(Brushes.Gray, menu.BorderBrush);
		Assert.AreEqual(TextCodeActionMenuOptions.Default.MaxHeight, menu.MaxHeight);
		Assert.IsFalse(menu.StaysOpen);
		Assert.AreEqual(2, menu.Items.Count);

		var preferredItem = (MenuItem)menu.Items[0];
		var plainItem = (MenuItem)menu.Items[1];

		Assert.AreEqual("Fix it", ((TextBlock)preferredItem.Header).Text);
		Assert.AreEqual(FontWeights.Bold, ((TextBlock)preferredItem.Header).FontWeight);
		Assert.AreEqual("Extract expression", ((TextBlock)plainItem.Header).Text);
		Assert.AreEqual(FontWeights.Normal, ((TextBlock)plainItem.Header).FontWeight);

		controller.CloseActions();

		Assert.IsFalse(controller.IsActionsOpen);
	}

	[TestMethod]
	public void MenuItemClick_InvokesTheExecuteHookWithTheItem()
	{
		using var host = new CodeActionTestHost();
		var payload = new object();
		host.RequestCodeActionsAsync = (_, _) => Task.FromResult<IReadOnlyList<TextCodeActionItem>>(
			[CodeActionTestHost.CreateItem("Fix it", payload: payload)]);

		using var controller = host.CreateController();

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());
		Assert.IsTrue(controller.TryOpenActions());

		var menuItem = (MenuItem)host.Menus[^1].Items[0];

		menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

		Assert.AreEqual(1, host.Executed.Count);
		Assert.AreSame(payload, host.Executed[0].Payload);
	}

	[TestMethod]
	public void TryOpenActions_Twice_ReplacesTheOpenMenu()
	{
		using var host = new CodeActionTestHost();
		host.RequestCodeActionsAsync = static (_, _) =>
			Task.FromResult<IReadOnlyList<TextCodeActionItem>>([CodeActionTestHost.CreateItem("Fix it")]);

		using var controller = host.CreateController();

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());

		Assert.IsTrue(controller.TryOpenActions());
		Assert.IsTrue(controller.TryOpenActions());

		Assert.AreEqual(2, host.Menus.Count);
		Assert.IsFalse(host.Menus[0].IsOpen);
		Assert.IsTrue(host.Menus[1].IsOpen);
		Assert.IsTrue(controller.IsActionsOpen);
	}

	[TestMethod]
	public void Dispose_WhenMenuOpen_ClosesTheMenu()
	{
		using var host = new CodeActionTestHost();
		host.RequestCodeActionsAsync = static (_, _) =>
			Task.FromResult<IReadOnlyList<TextCodeActionItem>>([CodeActionTestHost.CreateItem("Fix it")]);

		var controller = host.CreateController();

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());
		Assert.IsTrue(controller.TryOpenActions());

		controller.Dispose();

		Assert.IsFalse(controller.IsActionsOpen);
	}

	[TestMethod]
	public void TryOpenActions_WithConfigureMenuItemHook_ConfiguresEveryCreatedItem()
	{
		using var host = new CodeActionTestHost();
		host.ConfigureMenuItem = static (menuItem, item) => menuItem.ToolTip = item.Title;
		host.RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<TextCodeActionItem>>(
		[
			CodeActionTestHost.CreateItem("Fix it", isPreferred: true),
			CodeActionTestHost.CreateItem("Extract expression")
		]);

		using var controller = host.CreateController();

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());
		Assert.IsTrue(controller.TryOpenActions());

		ContextMenu menu = host.Menus[^1];

		Assert.IsTrue(host.ConfigureMenuItemInvoked);
		Assert.AreEqual(2, menu.Items.Count);
		Assert.AreEqual("Fix it", ((MenuItem)menu.Items[0]).ToolTip);
		Assert.AreEqual("Extract expression", ((MenuItem)menu.Items[1]).ToolTip);
	}

	[TestMethod]
	public void TryOpenActions_CustomAnchorOptions_MoveTheMenuByTheConfiguredDelta()
	{
		using var host = new CodeActionTestHost();
		host.RequestCodeActionsAsync = static (_, _) =>
			Task.FromResult<IReadOnlyList<TextCodeActionItem>>([CodeActionTestHost.CreateItem("Fix it")]);

		Point baselineAnchor;

		using (TextCodeActionController baseline = host.CreateController())
		{
			CodeActionTestHost.RunToCompletion(baseline.RefreshAsync());
			Assert.IsTrue(baseline.TryOpenActions());

			ContextMenu baselineMenu = host.Menus[^1];
			baselineAnchor = new Point(baselineMenu.HorizontalOffset, baselineMenu.VerticalOffset);
		}

		var menuOptions = TextCodeActionMenuOptions.Default with
		{
			AnchorXOffset = 12.0,
			CaretAnchorYOffset = 7.0
		};

		using var controller = host.CreateController(menuOptions: menuOptions);

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());
		Assert.IsTrue(controller.TryOpenActions());

		ContextMenu menu = host.Menus[^1];

		// The configured offsets move the menu by the delta to the defaults, independent of the layout math
		// that produced the baseline position.
		Assert.AreEqual(baselineAnchor.X + 10.0, menu.HorizontalOffset, 1e-6);
		Assert.AreEqual(baselineAnchor.Y + 5.0, menu.VerticalOffset, 1e-6);
	}

	[TestMethod]
	public void ExternalMenuClose_UntracksTheMenuAndAllowsReopening()
	{
		using var host = new CodeActionTestHost();
		host.RequestCodeActionsAsync = static (_, _) =>
			Task.FromResult<IReadOnlyList<TextCodeActionItem>>([CodeActionTestHost.CreateItem("Fix it")]);

		using var controller = host.CreateController();
		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());

		Assert.IsTrue(controller.TryOpenActions());
		Assert.IsTrue(controller.IsActionsOpen);

		// The menu closes itself (Escape, focus loss, or a click elsewhere) without the controller asking.
		host.Menus[^1].IsOpen = false;

		// The external close reaches the presenter through the menu's Closed event, which WPF raises on the
		// dispatcher.
		DispatcherTestUtils.PumpUntil(() => !controller.IsActionsOpen);

		// The presenter untracked the closed menu, so the next open simply opens a fresh one.
		Assert.IsTrue(controller.TryOpenActions());
		Assert.IsTrue(controller.IsActionsOpen);
	}

	[TestMethod]
	public void CloseActions_RestoresTheFocusTheEditorHadBeforeTheMenuOpened()
	{
		using var host = new CodeActionTestHost();
		host.RequestCodeActionsAsync = static (_, _) =>
			Task.FromResult<IReadOnlyList<TextCodeActionItem>>([CodeActionTestHost.CreateItem("Fix it")]);

		using var controller = host.CreateController();
		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());

		host.Editor.Focus();
		Assert.IsTrue(controller.TryOpenActions());
		Assert.IsTrue(controller.IsActionsOpen);

		controller.CloseActions();

		Assert.IsFalse(controller.IsActionsOpen);

		// The editor keeps working after the menu closes: keyboard focus returned to the element that had it
		// before the menu opened.
		Assert.IsTrue(host.Editor.IsKeyboardFocusWithin);
	}
}

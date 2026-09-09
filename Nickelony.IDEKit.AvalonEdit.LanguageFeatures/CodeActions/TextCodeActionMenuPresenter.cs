using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.IntelliSense.CodeActions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;

/// <summary>
/// Creates and manages the single code-action menu of a text area: standard WPF menu items over the
/// skinned chrome, anchored at the requested point, with focus restored to the editor after closing.
/// </summary>
/// <remarks>
/// <para>
/// The presenter must be created and used on the thread that owns the text area. Opening a menu
/// replaces an open menu. The menu takes keyboard focus while it is open - standard menu behavior,
/// including arrow-key navigation, Enter to invoke, and Escape to dismiss - and the element that had
/// focus before the menu opened is restored when it closes, so a margin click that opened the menu
/// never moves the caret and the editor keeps working after the menu closes.
/// </para>
/// <para>
/// The item containers are the standard WPF <see cref="MenuItem"/> ones, so the selection highlight
/// follows the system menu highlight like the package's other windows; the skin supplies the menu
/// chrome colors only. The optional item hook receives every created container before the menu opens,
/// so a host can restyle an item without replacing the menu pipeline.
/// </para>
/// </remarks>
internal sealed class TextCodeActionMenuPresenter : IDisposable
{
	private readonly TextArea _textArea;
	private readonly TextCodeActionMenuSkin _skin;
	private readonly double _maxHeight;
	private readonly Action<ContextMenu>? _configureMenu;
	private readonly Action<MenuItem, TextCodeActionItem>? _configureMenuItem;

	private ContextMenu? _menu;
	private IInputElement? _focusedBeforeOpen;
	private bool _isDisposed;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextCodeActionMenuPresenter"/> class.
	/// </summary>
	/// <param name="textArea">The text area the menu belongs to and focus returns to.</param>
	/// <param name="skin">The skin applied to created menus.</param>
	/// <param name="maxHeight">The maximum menu height; a taller item list scrolls.</param>
	/// <param name="configureMenu">The optional host hook invoked before a menu opens.</param>
	/// <param name="configureMenuItem">The optional host hook invoked for every created item.</param>
	public TextCodeActionMenuPresenter(
		TextArea textArea,
		TextCodeActionMenuSkin skin,
		double maxHeight,
		Action<ContextMenu>? configureMenu,
		Action<MenuItem, TextCodeActionItem>? configureMenuItem)
	{
		_textArea = textArea;
		_skin = skin;
		_maxHeight = maxHeight;
		_configureMenu = configureMenu;
		_configureMenuItem = configureMenuItem;
	}

	/// <summary>
	/// Gets a value indicating whether a menu created by this presenter is open.
	/// </summary>
	public bool IsOpen => _menu is not null;

	/// <summary>
	/// Opens a menu for the supplied actions, replacing an open menu.
	/// </summary>
	/// <param name="items">The actions to present, in order.</param>
	/// <param name="placementTarget">The element the anchor is relative to.</param>
	/// <param name="anchor">The menu's top-left position, relative to <paramref name="placementTarget"/>.</param>
	/// <param name="invoked">The callback invoked with the action whose menu item was clicked.</param>
	public void Open(
		IReadOnlyList<TextCodeActionItem> items,
		UIElement placementTarget,
		Point anchor,
		Action<TextCodeActionItem> invoked)
	{
		if (_isDisposed)
			return;

		Close();

		var menu = new ContextMenu
		{
			PlacementTarget = placementTarget,
			Placement = PlacementMode.RelativePoint,
			HorizontalOffset = anchor.X,
			VerticalOffset = anchor.Y,
			MaxHeight = _maxHeight,
			StaysOpen = false,
			Background = _skin.Background,
			Foreground = _skin.Foreground,
			BorderBrush = _skin.BorderBrush
		};

		for (int index = 0; index < items.Count; index++)
		{
			TextCodeActionItem item = items[index];

			// The header is a text element (not a string) so action titles are never parsed for
			// access keys, and a preferred action stands out without changing what the item does.
			var header = new TextBlock { Text = item.Title };

			if (item.IsPreferred)
				header.FontWeight = FontWeights.Bold;

			var menuItem = new MenuItem { Header = header };
			menuItem.Click += (_, _) => invoked(item);
			_configureMenuItem?.Invoke(menuItem, item);
			menu.Items.Add(menuItem);
		}

		_configureMenu?.Invoke(menu);

		menu.Closed += Menu_Closed;
		_focusedBeforeOpen = Keyboard.FocusedElement;
		_menu = menu;

		try
		{
			menu.IsOpen = true;
		}
		catch
		{
			// A failed show must not leave a menu tracked that never became visible.
			Menu_Closed(menu, EventArgs.Empty);
			throw;
		}
	}

	/// <summary>
	/// Closes the open menu, if any.
	/// </summary>
	public void Close()
	{
		ContextMenu? menu = _menu;

		if (menu is null)
			return;

		menu.IsOpen = false;

		// Closing normally raises Closed, which untracks the menu; a menu that never became visible
		// (or was closed externally) is untracked defensively so it cannot leak into the next open.
		if (ReferenceEquals(_menu, menu))
			Menu_Closed(menu, EventArgs.Empty);
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		if (_isDisposed)
			return;

		_isDisposed = true;
		Close();
	}

	private void Menu_Closed(object? sender, EventArgs e)
	{
		ContextMenu? menu = _menu;

		if (menu is null || (sender is ContextMenu closedMenu && !ReferenceEquals(closedMenu, menu)))
			return;

		menu.Closed -= Menu_Closed;
		_menu = null;
		RestoreFocus();
	}

	private void RestoreFocus()
	{
		IInputElement? previousFocus = _focusedBeforeOpen;
		_focusedBeforeOpen = null;

		if (!_textArea.IsVisible)
			return;

		if (previousFocus is UIElement element && element.IsVisible && element.IsEnabled && element.Focusable)
			Keyboard.Focus(element);
		else
			_textArea.Focus();
	}
}

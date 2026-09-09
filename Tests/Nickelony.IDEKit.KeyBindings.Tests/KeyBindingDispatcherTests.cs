using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Nickelony.IDEKit.KeyBindings.Tests;

[STATestClass]
public class KeyBindingDispatcherTests
{
	private static CommandCatalog<TestCommand> CreateCatalog()
		=> new([
			new CommandDescriptor<TestCommand>(TestCommand.Save, nameof(TestCommand.Save), isRemappable: true, isHostReserved: false,
				new KeyCombo(Key.S, ModifierKeys.Control)),
			new CommandDescriptor<TestCommand>(TestCommand.Undo, nameof(TestCommand.Undo), isRemappable: true, isHostReserved: false,
				new KeyCombo(Key.Z, ModifierKeys.Control))
		]);

	private static IKeyBindingService<TestCommand> CreateService()
		=> new KeyBindingService<TestCommand>(CreateCatalog(), new KeyBindingOverrideCollection(), _ => true);

	private static KeyEventArgs CreateKeyDown(Key key, params Key[] downModifierKeys)
		=> new(new FakeKeyboardDevice([key, .. downModifierKeys]), new FakePresentationSource(), 0, key)
		{
			RoutedEvent = Keyboard.KeyDownEvent
		};

	[TestMethod]
	public void TryHandleKeyDown_BoundAndExecutable_ExecutesAndReturnsTrue()
	{
		var executedCommands = new List<TestCommand>();
		var dispatcher = new KeyBindingDispatcher<TestCommand>(
			CreateService(),
			_ => true,
			executedCommands.Add);

		KeyEventArgs args = CreateKeyDown(Key.S, Key.LeftCtrl);

		bool handled = dispatcher.TryHandleKeyDown(args);

		Assert.IsTrue(handled);
		Assert.AreEqual(1, executedCommands.Count);
		Assert.AreEqual(TestCommand.Save, executedCommands[0]);
	}

	[TestMethod]
	public void TryHandleKeyDown_BoundButCannotExecute_ReturnsFalseAndDoesNotExecute()
	{
		var executedCommands = new List<TestCommand>();
		var dispatcher = new KeyBindingDispatcher<TestCommand>(
			CreateService(),
			_ => false,
			executedCommands.Add);

		KeyEventArgs args = CreateKeyDown(Key.S, Key.LeftCtrl);

		bool handled = dispatcher.TryHandleKeyDown(args);

		Assert.IsFalse(handled);
		Assert.AreEqual(0, executedCommands.Count);
	}

	[TestMethod]
	public void TryHandleKeyDown_UnboundKeyCombo_ReturnsFalseAndDoesNotExecute()
	{
		var executedCommands = new List<TestCommand>();
		var dispatcher = new KeyBindingDispatcher<TestCommand>(
			CreateService(),
			_ => true,
			executedCommands.Add);

		KeyEventArgs args = CreateKeyDown(Key.Q, Key.LeftCtrl);

		bool handled = dispatcher.TryHandleKeyDown(args);

		Assert.IsFalse(handled);
		Assert.AreEqual(0, executedCommands.Count);
	}

	[TestMethod]
	public void TryHandleKeyDown_ModifierOnlyKey_ReturnsFalseAndDoesNotExecute()
	{
		var executedCommands = new List<TestCommand>();
		var dispatcher = new KeyBindingDispatcher<TestCommand>(
			CreateService(),
			_ => true,
			executedCommands.Add);

		KeyEventArgs args = CreateKeyDown(Key.LeftCtrl);

		bool handled = dispatcher.TryHandleKeyDown(args);

		Assert.IsFalse(handled);
		Assert.AreEqual(0, executedCommands.Count);
	}

	private sealed class FakeKeyboardDevice : KeyboardDevice
	{
		private readonly HashSet<Key> _downKeys;

		public FakeKeyboardDevice(params Key[] downKeys)
			: base(InputManager.Current)
		{
			_downKeys = [.. downKeys];
		}

		protected override KeyStates GetKeyStatesFromSystem(Key key)
			=> _downKeys.Contains(key) ? KeyStates.Down : KeyStates.None;
	}

	private sealed class FakePresentationSource : PresentationSource
	{
		public override Visual RootVisual { get; set; } = new ContainerVisual();

		public override bool IsDisposed => false;

		protected override CompositionTarget GetCompositionTargetCore()
			=> null!;
	}
}

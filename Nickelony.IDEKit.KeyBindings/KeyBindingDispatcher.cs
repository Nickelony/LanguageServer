using System.Windows.Input;

namespace Nickelony.IDEKit.KeyBindings;

/// <summary>
/// Routes WPF key-down events to the command bound to the pressed key combo when
/// that command can currently execute.
/// </summary>
/// <typeparam name="TCommandId">The command identity type.</typeparam>
public sealed class KeyBindingDispatcher<TCommandId>
	where TCommandId : notnull
{
	private readonly IKeyBindingService<TCommandId> _keyBindings;
	private readonly Func<TCommandId, bool> _canExecuteCommand;
	private readonly Action<TCommandId> _executeCommand;

	/// <summary>
	/// Initializes a new instance of the <see cref="KeyBindingDispatcher{TCommandId}"/> class.
	/// </summary>
	/// <param name="keyBindings">The binding service used to resolve key combos to commands.</param>
	/// <param name="canExecuteCommand">The delegate that determines whether a resolved command may currently execute.</param>
	/// <param name="executeCommand">The delegate that invokes the resolved command.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="keyBindings"/>, <paramref name="canExecuteCommand"/>, or <paramref name="executeCommand"/> is
	/// <see langword="null"/>.
	/// </exception>
	public KeyBindingDispatcher(
		IKeyBindingService<TCommandId> keyBindings,
		Func<TCommandId, bool> canExecuteCommand,
		Action<TCommandId> executeCommand)
	{
		ArgumentNullException.ThrowIfNull(keyBindings);
		ArgumentNullException.ThrowIfNull(canExecuteCommand);
		ArgumentNullException.ThrowIfNull(executeCommand);

		_keyBindings = keyBindings;
		_canExecuteCommand = canExecuteCommand;
		_executeCommand = executeCommand;
	}

	/// <summary>
	/// Tries to dispatch <paramref name="e"/> to the command bound to the pressed key combo.
	/// </summary>
	/// <remarks>
	/// The caller should mark the event handled only when this method returns <see langword="true"/>.
	/// </remarks>
	/// <param name="e">The WPF key-down event to inspect.</param>
	/// <returns>
	/// <see langword="true"/> when a key combo was recognized, the command can execute, and the command was invoked;
	/// otherwise, <see langword="false"/> for modifier-only keystrokes, unbound key combos, and commands that
	/// cannot currently execute.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="e"/> is <see langword="null"/>.</exception>
	public bool TryHandleKeyDown(KeyEventArgs e)
	{
		ArgumentNullException.ThrowIfNull(e);

		KeyCombo? keyCombo = KeyCombo.FromKeyEventArgs(e);

		if (keyCombo is null)
			return false;

		if (!_keyBindings.TryGetCommand(keyCombo.Value, out TCommandId? command) || !_canExecuteCommand(command))
		{
			return false;
		}

		_executeCommand(command);
		return true;
	}
}

using System.Windows.Input;

namespace Nickelony.IDEKit.KeyBindings;

/// <summary>
/// Routes WPF key-down events to the command bound to the pressed key combo when
/// that command can currently execute.
/// </summary>
public sealed class KeyBindingDispatcher<TCommandId>
	where TCommandId : notnull
{
	private readonly IKeyBindingService<TCommandId> _keyBindings;
	private readonly Func<TCommandId, bool> _canExecuteCommand;
	private readonly Action<TCommandId> _executeCommand;

	/// <summary>
	/// Creates a dispatcher over a key binding service with command execution hooks.
	/// </summary>
	/// <param name="keyBindings">The binding service used to resolve key combos to commands.</param>
	/// <param name="canExecuteCommand">Determines whether a resolved command may currently execute.</param>
	/// <param name="executeCommand">Invokes the resolved command.</param>
	/// <exception cref="ArgumentNullException">A parameter is <see langword="null"/>.</exception>
	public KeyBindingDispatcher(
		IKeyBindingService<TCommandId> keyBindings,
		Func<TCommandId, bool> canExecuteCommand,
		Action<TCommandId> executeCommand)
	{
		_keyBindings = keyBindings ?? throw new ArgumentNullException(nameof(keyBindings));
		_canExecuteCommand = canExecuteCommand ?? throw new ArgumentNullException(nameof(canExecuteCommand));
		_executeCommand = executeCommand ?? throw new ArgumentNullException(nameof(executeCommand));
	}

	/// <summary>
	/// Tries to dispatch <paramref name="e"/> to the command bound to the pressed key combo.
	/// Returns <see langword="true"/> only when a key combo was recognized, the command can
	/// execute, and the command was invoked; the caller should mark the event handled in that case.
	/// Returns <see langword="false"/> for modifier-only keystrokes, unbound key combos,
	/// and commands that cannot currently execute.
	/// </summary>
	/// <param name="e">The WPF key-down event to inspect.</param>
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

using System.Windows.Input;

namespace Nickelony.IDEKit.KeyBindings;

/// <summary>
/// Value type representing a WPF key combo composed of a <see cref="Key"/> and
/// <see cref="ModifierKeys"/>. Provides value equality, display text, and a factory method
/// from WPF key events.
/// </summary>
public readonly record struct KeyCombo
{
	/// <summary>
	/// Creates a key combo from a primary <paramref name="key"/> and its <paramref name="modifiers"/>.
	/// </summary>
	/// <param name="key">The primary key. Must not be <see cref="Key.None"/>.</param>
	/// <param name="modifiers">The modifier flags, such as Control, Shift, Alt, and Windows.</param>
	public KeyCombo(Key key, ModifierKeys modifiers)
	{
		if (key == Key.None)
			throw new ArgumentException("A bindable key combo must include a non-None key.", nameof(key));

		Key = key;
		Modifiers = modifiers;
	}

	/// <summary>
	/// The primary key. Instances created through the public constructor never contain
	/// <see cref="Key.None"/>; the <see langword="default"/> value of this record struct does.
	/// </summary>
	public Key Key { get; }

	/// <summary>
	/// The modifier flags associated with the combo.
	/// Display text renders Ctrl, Shift, Alt, and Windows flags.
	/// </summary>
	public ModifierKeys Modifiers { get; }

	/// <summary>
	/// Creates a <see cref="KeyCombo"/> from a WPF <see cref="KeyEventArgs"/>.
	/// Normalizes <see cref="Key.System"/> to <see cref="KeyEventArgs.SystemKey"/> and reads modifiers
	/// from the event's keyboard device rather than the global <see cref="Keyboard.Modifiers"/>.
	/// Returns <see langword="null"/> when the resulting key is <see cref="Key.None"/> or
	/// the combination is a modifier-only keystroke.
	/// </summary>
	/// <param name="e">The WPF key event whose key and currently pressed modifier keys are read.</param>
	/// <exception cref="ArgumentNullException"><paramref name="e"/> is <see langword="null"/>.</exception>
	public static KeyCombo? FromKeyEventArgs(KeyEventArgs e)
	{
		ArgumentNullException.ThrowIfNull(e);

		Key key = e.Key == Key.System ? e.SystemKey : e.Key;

		if (key == Key.None)
			return null;

		// Modifier keys alone are not bindable combos.
		if (key == Key.LeftCtrl || key == Key.RightCtrl ||
			key == Key.LeftAlt || key == Key.RightAlt ||
			key == Key.LeftShift || key == Key.RightShift ||
			key == Key.LWin || key == Key.RWin)
		{
			return null;
		}

		ModifierKeys modifiers = ModifierKeys.None;

		if (e.KeyboardDevice.IsKeyDown(Key.LeftCtrl) || e.KeyboardDevice.IsKeyDown(Key.RightCtrl))
			modifiers |= ModifierKeys.Control;

		if (e.KeyboardDevice.IsKeyDown(Key.LeftShift) || e.KeyboardDevice.IsKeyDown(Key.RightShift))
			modifiers |= ModifierKeys.Shift;

		if (e.KeyboardDevice.IsKeyDown(Key.LeftAlt) || e.KeyboardDevice.IsKeyDown(Key.RightAlt))
			modifiers |= ModifierKeys.Alt;

		if (e.KeyboardDevice.IsKeyDown(Key.LWin) || e.KeyboardDevice.IsKeyDown(Key.RWin))
			modifiers |= ModifierKeys.Windows;

		return new KeyCombo(key, modifiers);
	}

	/// <summary>
	/// Returns display text suitable for menu and toolbar presentation.
	/// Control, Shift, Alt, and Windows flags are formatted in that order; other modifier bits
	/// are omitted. The text is calculated from <see cref="Key"/> and <see cref="Modifiers"/>
	/// rather than persisted.
	/// </summary>
	public string GetDisplayText()
	{
		string keyText = Key switch
		{
			Key.D0 => "0",
			Key.D1 => "1",
			Key.D2 => "2",
			Key.D3 => "3",
			Key.D4 => "4",
			Key.D5 => "5",
			Key.D6 => "6",
			Key.D7 => "7",
			Key.D8 => "8",
			Key.D9 => "9",
			Key.OemPlus => "+",
			Key.OemMinus => "-",
			Key.OemQuestion => "/",
			Key.OemComma => ",",
			Key.OemPeriod => ".",
			Key.OemSemicolon => ";",
			Key.OemOpenBrackets => "[",
			Key.OemCloseBrackets => "]",
			Key.OemPipe => "\\",
			Key.OemQuotes => "\"",
			Key.OemTilde => "`",
			_ => Key.ToString()
		};

		if (Modifiers == ModifierKeys.None)
			return keyText;

		string modifierText = string.Empty;

		if ((Modifiers & ModifierKeys.Control) != ModifierKeys.None)
			modifierText += "Ctrl+";

		if ((Modifiers & ModifierKeys.Shift) != ModifierKeys.None)
			modifierText += "Shift+";

		if ((Modifiers & ModifierKeys.Alt) != ModifierKeys.None)
			modifierText += "Alt+";

		if ((Modifiers & ModifierKeys.Windows) != ModifierKeys.None)
			modifierText += "Win+";

		return modifierText + keyText;
	}
}

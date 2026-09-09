using System.Diagnostics.CodeAnalysis;

namespace Nickelony.IDEKit.KeyBindings;

/// <summary>
/// Provides runtime key binding lookup, display text, validation, mutation operations,
/// and a <see cref="BindingsChanged"/> notification.
/// </summary>
/// <typeparam name="TCommandId">The command identity type.</typeparam>
public interface IKeyBindingService<TCommandId> : IDisposable
	where TCommandId : notnull
{
	/// <summary>
	/// Raised after the runtime maps are rebuilt by a reset or reset-all operation.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Both operations invoke the persistence callback but do not inspect its return value.
	/// </para>
	/// <para>
	/// Consumers can use this notification to refresh command presentation.
	/// </para>
	/// </remarks>
	event EventHandler? BindingsChanged;

	/// <summary>
	/// Looks up the <typeparamref name="TCommandId"/> currently bound to a key combo.
	/// </summary>
	/// <remarks>Each combo in the published runtime maps identifies at most one command.</remarks>
	/// <param name="shortcut">The key combo to look up.</param>
	/// <param name="command">The command bound to the combo when the method returns <see langword="true"/>; otherwise, the <see langword="default"/> command value.</param>
	/// <returns><see langword="true"/> when the combo is bound to a command; otherwise, <see langword="false"/>.</returns>
	bool TryGetCommand(KeyCombo shortcut, [NotNullWhen(true)] out TCommandId? command);

	/// <summary>
	/// Returns the current bindings for a command.
	/// </summary>
	/// <param name="command">The command whose current bindings are returned.</param>
	/// <returns>The current bindings for the command; an empty list when the command has no bindings (explicitly unbound) or is not cataloged.</returns>
	IReadOnlyList<KeyCombo> GetBindings(TCommandId command);

	/// <summary>
	/// Returns the display text for a command's current bindings.
	/// </summary>
	/// <remarks>Multiple bindings are joined with <c> / </c>.</remarks>
	/// <param name="command">The command whose display text is returned.</param>
	/// <param name="fallbackDisplayText">The text returned when the command has no bindings.</param>
	/// <returns>The display text for the command's bindings, or <paramref name="fallbackDisplayText"/> when the command has no bindings.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="fallbackDisplayText"/> is <see langword="null"/>.</exception>
	string GetDisplayText(TCommandId command, string fallbackDisplayText = "");

	/// <summary>
	/// Validates a proposed binding set for a command without mutating state.
	/// </summary>
	/// <remarks>
	/// The result accounts for command remapping policy, repeated combos within the set, and conflicts
	/// with the current bindings of other commands.
	/// </remarks>
	/// <param name="command">The command the proposed binding set applies to.</param>
	/// <param name="bindings">The proposed binding set to validate.</param>
	/// <returns>The validation outcome for the proposed binding set.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="bindings"/> is <see langword="null"/>.</exception>
	KeyBindingValidationResult Validate(TCommandId command, IReadOnlyList<KeyCombo> bindings);

	/// <summary>
	/// Removes the override for a single command, falling back to catalog defaults.
	/// </summary>
	/// <remarks>
	/// The persistence callback is invoked, but its return value is not inspected. The
	/// in-memory override collection and runtime maps are updated and the change event is
	/// raised even when the callback reports failure.
	/// </remarks>
	/// <param name="command">The command whose override is removed.</param>
	void Reset(TCommandId command);

	/// <summary>
	/// Removes all overrides for the active workspace, falling back to catalog defaults.
	/// </summary>
	/// <remarks>
	/// The persistence callback is invoked, but its return value is not inspected. The
	/// in-memory override collection and runtime maps are updated and the change event is
	/// raised even when the callback reports failure.
	/// </remarks>
	void ResetAll();
}

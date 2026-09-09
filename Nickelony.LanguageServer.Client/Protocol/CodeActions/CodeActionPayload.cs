using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents one usable code-action entry returned by a language server.
/// </summary>
/// <remarks>
/// The tolerant response converter keeps every entry that has a usable title and at least one of an edit or a
/// command, so the host owns the <c>workspace/executeCommand</c> policy. An action that carries both keeps its
/// edit and its command because a host may execute the command after applying the edit. The edit stays a
/// <see cref="WorkspaceEditResponse"/> so resource operations remain representable for the provider to fail the
/// affected action closed. The opaque <c>data</c> member is preserved for hosts that implement
/// <c>codeAction/resolve</c>.
/// </remarks>
public sealed class CodeActionPayload
{
	/// <summary>
	/// Gets the human-readable action title.
	/// </summary>
	public string? Title { get; init; }

	/// <summary>
	/// Gets the protocol action kind (for example <c>quickfix</c>), or <see langword="null"/> when the
	/// server omitted it.
	/// </summary>
	public string? Kind { get; init; }

	/// <summary>
	/// Gets a value indicating whether the server marks this action as preferred, or
	/// <see langword="null"/> when the server omitted the flag.
	/// </summary>
	public bool? IsPreferred { get; init; }

	/// <summary>
	/// Gets the workspace edit the action applies, or <see langword="null"/> when the action carries only a command.
	/// </summary>
	public WorkspaceEditResponse? Edit { get; init; }

	/// <summary>
	/// Gets the command the server attached to the action, or <see langword="null"/> when the action carries no
	/// command. An action may carry both an edit and a command; the command is expected to run after the edit is
	/// applied.
	/// </summary>
	public CodeActionCommandPayload? Command { get; init; }

	/// <summary>
	/// Gets the opaque server state the action carries for <c>codeAction/resolve</c>, or <see langword="null"/>
	/// when the server sent none. The element is a detached clone and stays valid after its source document is
	/// disposed; hosts that resolve actions should send it back unchanged.
	/// </summary>
	public JsonElement? Data { get; init; }

	/// <summary>
	/// Gets the diagnostics the action addresses, when the server attached them to the action.
	/// </summary>
	public IReadOnlyList<DiagnosticPayload>? Diagnostics { get; init; }

	/// <summary>
	/// Gets the disabled state the server attached to the action, or <see langword="null"/> when the action is
	/// enabled. A disabled action stays representable so hosts can grey it out or withhold it.
	/// </summary>
	public CodeActionDisabledPayload? Disabled { get; init; }
}

/// <summary>
/// Represents the command of a code action: the identifier to execute plus its optional title and arguments.
/// </summary>
/// <param name="Title">The command title, or <see langword="null"/> when the server omitted it.</param>
/// <param name="Command">The command identifier to execute.</param>
/// <param name="Arguments">The command arguments in wire order, or <see langword="null"/> when the server sent none.</param>
public readonly record struct CodeActionCommandPayload(
	[property: JsonPropertyName("title")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string? Title,
	[property: JsonPropertyName("command")]
	string? Command,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	IReadOnlyList<JsonElement>? Arguments);

/// <summary>
/// Marks a code action as disabled together with the reason the server supplied.
/// </summary>
/// <param name="Reason">The user-facing reason the action is disabled.</param>
public readonly record struct CodeActionDisabledPayload(string? Reason);

namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Represents a single code action (quick fix or refactoring) offered for a document range.
/// </summary>
/// <remarks>
/// <para>
/// The action's changes are carried as a <see cref="TextWorkspaceEdit"/> so a consumer can apply
/// every document change as one atomic operation. Actions that a server expresses as a command
/// instead of an edit cannot be represented by the shared edit model; providers omit them rather
/// than offering an action that cannot run.
/// </para>
/// <para>
/// Record equality compares the <see cref="Edit"/> snapshot by reference; compare the edit
/// element-wise when structural equality is required.
/// </para>
/// </remarks>
public sealed record TextCodeAction
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextCodeAction"/> record.
	/// </summary>
	/// <param name="title">The human-readable action title.</param>
	/// <param name="kind">
	/// The protocol action kind (for example <c>quickfix</c> or <c>refactor.rewrite</c>), or
	/// <see langword="null"/> when the server did not specify one. A blank value is treated as absent.
	/// </param>
	/// <param name="isPreferred">Whether the server marks this action as preferred among otherwise equivalent actions.</param>
	/// <param name="edit">The workspace edit the action applies.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="title"/> or <paramref name="edit"/> is <see langword="null"/>.
	/// </exception>
	public TextCodeAction(string title, string? kind, bool isPreferred, TextWorkspaceEdit edit)
	{
		ArgumentNullException.ThrowIfNull(title);
		ArgumentNullException.ThrowIfNull(edit);

		Title = title;
		Kind = string.IsNullOrWhiteSpace(kind) ? null : kind.Trim();
		IsPreferred = isPreferred;
		Edit = edit;
	}

	/// <summary>
	/// Gets the human-readable action title.
	/// </summary>
	public string Title { get; }

	/// <summary>
	/// Gets the protocol action kind (for example <c>quickfix</c> or <c>refactor.rewrite</c>), or
	/// <see langword="null"/> when the server did not specify one.
	/// </summary>
	public string? Kind { get; }

	/// <summary>
	/// Gets a value indicating whether the server marks this action as preferred among otherwise
	/// equivalent actions.
	/// </summary>
	public bool IsPreferred { get; }

	/// <summary>
	/// Gets the workspace edit the action applies.
	/// </summary>
	public TextWorkspaceEdit Edit { get; }
}

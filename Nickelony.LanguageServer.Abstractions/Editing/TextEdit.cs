namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Represents a single text replacement inside a document.
/// </summary>
/// <remarks>
/// The range uses line and column numbers so the edit can be passed between components without depending on a
/// specific editor.
/// </remarks>
public sealed class TextEdit
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextEdit"/> class.
	/// </summary>
	/// <param name="range">The range to replace.</param>
	/// <param name="newText">The replacement text.</param>
	public TextEdit(TextDocumentRange range, string newText)
	{
		ArgumentNullException.ThrowIfNull(range);
		ArgumentNullException.ThrowIfNull(newText);

		Range = range;
		NewText = newText;
	}

	/// <summary>
	/// Gets the range to replace.
	/// </summary>
	public TextDocumentRange Range { get; }

	/// <summary>
	/// Gets the replacement text.
	/// </summary>
	public string NewText { get; }
}

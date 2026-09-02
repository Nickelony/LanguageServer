namespace Nickelony.IDEKit.Core.Formatting;

/// <summary>
/// Trims trailing whitespace from each line in a document.
/// </summary>
public sealed class TrimTrailingWhitespaceFormatter : ITextDocumentFormatter
{
	/// <summary>
	/// Gets the shared instance of the formatter.
	/// </summary>
	public static TrimTrailingWhitespaceFormatter Instance { get; } = new();

	private TrimTrailingWhitespaceFormatter()
	{ }

	/// <inheritdoc/>
	public string FormatDocument(string content)
	{
		string[] lines = content.Replace("\r", string.Empty).Split('\n');

		for (int i = 0; i < lines.Length; i++)
			lines[i] = lines[i].TrimEnd();

		return string.Join(Environment.NewLine, lines);
	}
}

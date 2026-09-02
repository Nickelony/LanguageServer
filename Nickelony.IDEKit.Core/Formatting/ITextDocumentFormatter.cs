namespace Nickelony.IDEKit.Core.Formatting;

/// <summary>
/// Formats the content of a text document.
/// </summary>
public interface ITextDocumentFormatter
{
	/// <summary>
	/// Formats the supplied document content.
	/// </summary>
	/// <param name="content">The document content to format.</param>
	/// <returns>The formatted document content.</returns>
	string FormatDocument(string content);
}

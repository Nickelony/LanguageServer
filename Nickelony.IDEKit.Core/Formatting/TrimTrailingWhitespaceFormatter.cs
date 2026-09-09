using Nickelony.IDEKit.Core.Text;
using System.Text;

namespace Nickelony.IDEKit.Core.Formatting;

/// <summary>
/// Trims trailing spaces and tabs from each line while preserving the original line
/// terminators.
/// </summary>
/// <remarks>
/// <para>
/// LF, CRLF, and lone CR are recognized as line terminators, and a final unterminated line is trimmed
/// without adding a terminator. Only spaces and tabs are removed; other Unicode whitespace
/// characters, such as a non-breaking space at the end of a line, are preserved so content that
/// relies on them is not altered silently. An already-trimmed document is returned unchanged (the same
/// instance), so callers can detect a no-op by reference.
/// </para>
/// <para>
/// Some formats give trailing whitespace meaning (Markdown hard line breaks are two trailing
/// spaces, for example); documents in those formats must not be trimmed, so a host selects this
/// formatter per document type instead of applying it unconditionally.
/// </para>
/// </remarks>
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
		ArgumentNullException.ThrowIfNull(content);

		if (!HasTrailingWhitespace(content))
			return content;

		var builder = new StringBuilder(content.Length);
		var enumerator = new TextLineEnumerator(content);

		while (enumerator.MoveNext())
		{
			ReadOnlySpan<char> line = enumerator.Content;
			builder.Append(EndsInSpaceOrTab(line) ? line.TrimEnd(" \t") : line);
			builder.Append(enumerator.Delimiter);
		}

		return builder.ToString();
	}

	/// <summary>
	/// Determines whether any line ends in a space or tab, so an already-trimmed document can be
	/// returned without rebuilding it.
	/// </summary>
	private static bool HasTrailingWhitespace(string content)
	{
		var enumerator = new TextLineEnumerator(content);

		while (enumerator.MoveNext())
		{
			if (EndsInSpaceOrTab(enumerator.Content))
				return true;
		}

		return false;
	}

	/// <summary>
	/// Determines whether the line ends in a space or tab, the only characters this formatter trims.
	/// </summary>
	private static bool EndsInSpaceOrTab(ReadOnlySpan<char> line)
		=> line.Length > 0 && WhitespaceScan.IsIndentationCharacter(line[^1]);
}

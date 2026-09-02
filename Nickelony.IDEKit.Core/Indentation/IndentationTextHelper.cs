using System.Text;

namespace Nickelony.IDEKit.Core.Indentation;

/// <summary>
/// Represents one line produced by <see cref="IndentationTextHelper.SplitLines"/>.
/// </summary>
/// <param name="Content">The line content without its delimiter.</param>
/// <param name="Delimiter">The line terminator that follows the content, if any.</param>
/// <param name="StartOffset">The zero-based offset of the line within the source text.</param>
public readonly record struct IndentationTextLine(string Content, string Delimiter, int StartOffset);

/// <summary>
/// Provides language-independent helpers for computing and manipulating indentation text.
/// </summary>
public static class IndentationTextHelper
{
	/// <summary>
	/// Creates the indentation unit that should be appended for one extra indent level.
	/// </summary>
	/// <remarks>
	/// When converting to spaces and neither <paramref name="indentationSize"/> nor
	/// <paramref name="tabSize"/> is positive, the unit falls back to 4 spaces.
	/// </remarks>
	/// <param name="convertTabsToSpaces">Whether indentation should use spaces instead of tabs.</param>
	/// <param name="indentationSize">The number of columns per indent level.</param>
	/// <param name="tabSize">The tab size used when the indentation size is not configured.</param>
	/// <returns>The indentation unit text.</returns>
	public static string CreateIndentationUnit(bool convertTabsToSpaces, int indentationSize, int tabSize)
	{
		if (!convertTabsToSpaces)
			return "\t";

		int size = indentationSize > 0
			? indentationSize
			: tabSize > 0
				? tabSize
				: 4;

		return new string(' ', size);
	}

	/// <summary>
	/// Gets the leading whitespace prefix for the supplied line.
	/// </summary>
	/// <param name="lineText">The line text to inspect.</param>
	/// <returns>The leading whitespace of the line.</returns>
	public static string GetLeadingWhitespace(string lineText)
	{
		return string.IsNullOrEmpty(lineText)
			? string.Empty
			: lineText[..GetLeadingWhitespaceLength(lineText)];
	}

	/// <summary>
	/// Gets the number of leading whitespace characters in the supplied text.
	/// </summary>
	/// <param name="text">The text to inspect.</param>
	/// <returns>The number of leading whitespace characters.</returns>
	public static int GetLeadingWhitespaceLength(string text)
	{
		int length = 0;

		while (length < text.Length && char.IsWhiteSpace(text[length]) && text[length] != '\r' && text[length] != '\n')
			length++;

		return length;
	}

	/// <summary>
	/// Removes a single indent level from the supplied indentation.
	/// </summary>
	/// <param name="indentation">The current indentation.</param>
	/// <param name="indentationUnit">The indentation unit for one level.</param>
	/// <returns>The indentation with one level removed.</returns>
	public static string RemoveSingleIndentLevel(string indentation, string indentationUnit)
	{
		if (string.IsNullOrEmpty(indentation))
			return string.Empty;

		if (indentationUnit == "\t" && indentation[^1] == '\t')
			return indentation[..^1];

		int removeLength = indentationUnit.Length > 0
			? Math.Min(indentationUnit.Length, indentation.Length)
			: 1;

		return indentation[..^removeLength];
	}

	/// <summary>
	/// Builds the indentation for a line at the given relative indent level.
	/// </summary>
	/// <param name="currentLineIndentation">The base indentation of the surrounding line.</param>
	/// <param name="indentationUnit">The indentation unit for one level.</param>
	/// <param name="indentLevel">The relative indent level above the base indentation.</param>
	/// <returns>The composed indentation text.</returns>
	public static string BuildIndentation(string currentLineIndentation, string indentationUnit, int indentLevel)
	{
		if (indentLevel <= 0)
			return currentLineIndentation;

		var builder = new StringBuilder(currentLineIndentation.Length + indentationUnit.Length * indentLevel);
		builder.Append(currentLineIndentation);

		for (int i = 0; i < indentLevel; i++)
			builder.Append(indentationUnit);

		return builder.ToString();
	}

	/// <summary>
	/// Determines whether the supplied text contains a line break.
	/// </summary>
	/// <param name="text">The text to inspect.</param>
	/// <returns><see langword="true"/> if the text contains a line break; otherwise, <see langword="false"/>.</returns>
	public static bool ContainsLineBreak(string text)
		=> text.IndexOfAny(['\r', '\n']) >= 0;

	/// <summary>
	/// Splits the supplied text into lines, preserving each line's delimiter and source offset.
	/// </summary>
	/// <param name="text">The text to split.</param>
	/// <returns>The lines of the text, including a final empty line when the text ends with a line break.</returns>
	public static IReadOnlyList<IndentationTextLine> SplitLines(string text)
	{
		var lines = new List<IndentationTextLine>();
		int lineStart = 0;
		int index = 0;

		while (index < text.Length)
		{
			if (text[index] == '\r' || text[index] == '\n')
			{
				int delimiterStart = index;

				if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
					index++;

				lines.Add(new IndentationTextLine(
					text[lineStart..delimiterStart],
					text[delimiterStart..(index + 1)],
					lineStart));

				index++;
				lineStart = index;
				continue;
			}

			index++;
		}

		lines.Add(new IndentationTextLine(text[lineStart..], string.Empty, lineStart));
		return lines;
	}
}

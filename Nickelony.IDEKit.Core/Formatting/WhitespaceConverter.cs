using Nickelony.IDEKit.Core.Text;
using System.Text;

namespace Nickelony.IDEKit.Core.Formatting;

/// <summary>
/// Converts between spaces and tabs in text while preserving the original line terminators.
/// </summary>
/// <remarks>
/// Space-to-tab conversion changes leading indentation; tab-to-space conversion expands tabs
/// wherever they occur. Tab stops are computed from UTF-16 code units, so a surrogate pair counts as
/// two columns and text containing wide or zero-width characters is approximated. When nothing
/// converts (no tab to expand, or no leading space that reaches a tab stop), the original instance
/// is returned, matching <see cref="TrimTrailingWhitespaceFormatter"/>.
/// </remarks>
public static class WhitespaceConverter
{
	/// <summary>
	/// Converts leading space indentation to tabs using the specified tab size.
	/// </summary>
	/// <remarks>
	/// Only leading spaces and tabs are converted; other whitespace (for example a form feed or a
	/// non-breaking space) is treated as content and ends the indentation scan. Existing tabs,
	/// partial groups of spaces that do not reach a tab stop, and all non-indentation content are
	/// preserved.
	/// </remarks>
	/// <param name="input">The text to convert.</param>
	/// <param name="tabSize">The number of spaces per tab stop.</param>
	/// <returns>The converted text.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="input"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="tabSize"/> is less than or equal to zero.</exception>
	public static string ConvertIndentationToTabs(string input, int tabSize)
	{
		ArgumentNullException.ThrowIfNull(input);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tabSize);

		return TransformLines(input, tabSize, expandTabs: false);
	}

	/// <summary>
	/// Expands every tab in the text to as many spaces as needed to reach the next tab stop.
	/// </summary>
	/// <remarks>
	/// Unlike <see cref="ConvertIndentationToTabs"/>, which changes leading indentation only, this method
	/// expands tabs wherever they occur, including inside line content.
	/// </remarks>
	/// <param name="input">The text to convert.</param>
	/// <param name="tabSize">The number of spaces per tab stop.</param>
	/// <returns>The converted text.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="input"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="tabSize"/> is less than or equal to zero.</exception>
	public static string ExpandTabs(string input, int tabSize)
	{
		ArgumentNullException.ThrowIfNull(input);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tabSize);

		return TransformLines(input, tabSize, expandTabs: true);
	}

	private static string TransformLines(string input, int tabSize, bool expandTabs)
	{
		var builder = new StringBuilder(input.Length);
		var enumerator = new TextLineEnumerator(input);
		bool changed = false;

		while (enumerator.MoveNext())
		{
			if (expandTabs)
				AppendExpandedTabs(builder, enumerator.Content, tabSize, ref changed);
			else
				AppendLineIndentationAsTabs(builder, enumerator.Content, tabSize, ref changed);

			builder.Append(enumerator.Delimiter);
		}

		// Return the original instance when nothing converted, so callers can detect a no-op by
		// reference like they can with TrimTrailingWhitespaceFormatter. The flag tracks the only two
		// conversion sites, so a no-op input never pays for building or comparing the output text.
		return changed ? builder.ToString() : input;
	}

	private static void AppendLineIndentationAsTabs(StringBuilder builder, ReadOnlySpan<char> line, int tabSize, ref bool changed)
	{
		int indentLength = 0;

		while (indentLength < line.Length && (line[indentLength] == ' ' || line[indentLength] == '\t'))
			indentLength++;

		ReadOnlySpan<char> indentation = line[..indentLength];
		int column = 0;

		for (int i = 0; i < indentation.Length;)
		{
			if (indentation[i] == '\t')
			{
				builder.Append('\t');

				// An existing tab advances to the next tab stop rather than by tabSize columns.
				column = ((column / tabSize) + 1) * tabSize;
				i++;

				continue;
			}

			int runStart = i;

			while (i < indentation.Length && indentation[i] == ' ')
				i++;

			int spaceCount = i - runStart;

			// Replace groups of spaces that reach a tab stop with a single tab.
			// Remaining spaces that would not reach a tab stop stay as spaces.
			while (spaceCount > 0)
			{
				int spacesToNextStop = tabSize - (column % tabSize);

				if (spaceCount < spacesToNextStop)
					break;

				builder.Append('\t');
				changed = true;
				column += spacesToNextStop;
				spaceCount -= spacesToNextStop;
			}

			if (spaceCount > 0)
			{
				builder.Append(' ', spaceCount);
				column += spaceCount;
			}
		}

		builder.Append(line[indentLength..]);
	}

	private static void AppendExpandedTabs(StringBuilder builder, ReadOnlySpan<char> line, int tabSize, ref bool changed)
	{
		if (line.IndexOf('\t') < 0)
		{
			builder.Append(line);
			return;
		}

		int column = 0;

		foreach (char character in line)
		{
			if (character == '\t')
			{
				// Expand each tab only as far as the next tab stop for this line.
				int spaces = tabSize - (column % tabSize);
				builder.Append(' ', spaces);
				changed = true;
				column += spaces;
			}
			else
			{
				builder.Append(character);
				column++;
			}
		}
	}
}

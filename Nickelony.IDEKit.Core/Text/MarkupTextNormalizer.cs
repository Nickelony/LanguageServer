namespace Nickelony.IDEKit.Core.Text;

/// <summary>
/// Normalizes markup text for display in controls that present documentation as plain text.
/// </summary>
public static class MarkupTextNormalizer
{
	/// <summary>
	/// Removes standalone Markdown fence lines while preserving inline backticks and code content.
	/// </summary>
	/// <param name="text">The text to normalize.</param>
	/// <returns>The normalized text, or <see langword="null"/> when the input is blank.</returns>
	public static string? NormalizeForPlainText(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
			return null;

		string[] lines = [.. text
			.Replace("\r", string.Empty, StringComparison.Ordinal)
			.Split('\n')
			.Select(line => line.TrimEnd())];

		var normalizedLines = new List<string>(lines.Length);

		for (int i = 0; i < lines.Length; i++)
		{
			if (IsFenceLine(lines[i]))
				continue;

			normalizedLines.Add(lines[i]);
		}

		string normalized = string.Join(Environment.NewLine, normalizedLines).Trim();
		return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
	}

	private static bool IsFenceLine(string line)
	{
		string trimmedLine = line.Trim();

		if (!trimmedLine.StartsWith("```", StringComparison.Ordinal))
			return false;

		if (trimmedLine.Length == 3)
			return true;

		for (int i = 3; i < trimmedLine.Length; i++)
		{
			char character = trimmedLine[i];

			if (!char.IsLetterOrDigit(character) && character != '_' && character != '-' && character != '.')
				return false;
		}

		return true;
	}
}

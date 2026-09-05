using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Computes minimal single-range document changes using common-prefix and common-suffix scans.
/// </summary>
public static class TextIncrementalEditCalculator
{
	/// <summary>
	/// Computes the minimal replacement that transforms one document snapshot into another.
	/// </summary>
	/// <param name="oldText">The previously synchronized document content; <see langword="null"/> is treated as empty.</param>
	/// <param name="newText">The updated document content; <see langword="null"/> is treated as empty.</param>
	/// <returns>The replacement range and text, expressed as UTF-16 offsets.</returns>
	public static TextIncrementalChange Compute(string? oldText, string? newText)
	{
		oldText ??= string.Empty;
		newText ??= string.Empty;

		int prefixLength = ComputeCommonPrefixLength(oldText, newText);
		int suffixLength = ComputeCommonSuffixLength(oldText, newText, prefixLength);
		int oldEnd = oldText.Length - suffixLength;
		int newEnd = newText.Length - suffixLength;
		string replacement = newEnd > prefixLength ? newText[prefixLength..newEnd] : string.Empty;

		return new(new TextRange(prefixLength, oldEnd - prefixLength), replacement);
	}

	private static int ComputeCommonPrefixLength(string first, string second)
	{
		int maximum = Math.Min(first.Length, second.Length);
		int index = 0;

		while (index < maximum && first[index] == second[index])
			index++;

		return index;
	}

	private static int ComputeCommonSuffixLength(string first, string second, int prefixLength)
	{
		int maximum = Math.Min(first.Length, second.Length) - prefixLength;
		int index = 0;

		while (index < maximum && first[first.Length - 1 - index] == second[second.Length - 1 - index])
			index++;

		return index;
	}
}

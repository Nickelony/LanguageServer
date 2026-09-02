namespace Nickelony.IDEKit.Core.Navigation;

/// <summary>
/// Identifies a navigable position in an editor document: the logical file, the caret offset, the
/// selection range, and an optional preferred display line for scroll restoration.
/// </summary>
/// <param name="FilePath">The logical path of the document the location belongs to.</param>
/// <param name="CaretOffset">The zero-based caret offset within the document.</param>
/// <param name="SelectionStart">The zero-based start offset of the selection.</param>
/// <param name="SelectionLength">The length of the selection in UTF-16 code units.</param>
/// <param name="PreferredLine">The one-based line the host should prefer when restoring the view, when known.</param>
public readonly record struct NavigationLocation(
	string FilePath,
	int CaretOffset,
	int SelectionStart,
	int SelectionLength,
	int? PreferredLine)
{
	/// <summary>
	/// Determines whether this location represents the same logical position as
	/// <paramref name="other"/>, ignoring the preferred display line.
	/// </summary>
	public bool IsEquivalentTo(NavigationLocation other)
	{
		return string.Equals(FilePath, other.FilePath, StringComparison.OrdinalIgnoreCase)
			&& CaretOffset == other.CaretOffset
			&& SelectionStart == other.SelectionStart
			&& SelectionLength == other.SelectionLength;
	}
}

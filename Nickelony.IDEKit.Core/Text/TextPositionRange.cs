namespace Nickelony.IDEKit.Core.Text;

/// <summary>
/// Represents a text range using zero-based line and character positions.
/// </summary>
/// <remarks>
/// <para>
/// This is a plain pair of <see cref="TextPosition"/> values: no bounds check is performed and no
/// ordering is enforced, so a range whose end precedes its start is stored as supplied. Conversions
/// on <see cref="TextLineMap"/> clamp values that fall outside the document.
/// </para>
/// <para>
/// Callers interpret the range as half-open: <see cref="Start"/> is included and <see cref="End"/> is
/// excluded.
/// </para>
/// </remarks>
/// <param name="Start">The zero-based position where the range starts; the endpoint is inclusive when the range is ordered.</param>
/// <param name="End">The zero-based position where the range ends; the endpoint is exclusive when the range is ordered.</param>
public readonly record struct TextPositionRange(TextPosition Start, TextPosition End)
{
	/// <summary>
	/// Gets a value indicating whether the range is empty (its endpoints are equal).
	/// </summary>
	/// <remarks>A reversed range is not empty; it contains no positions but still spans two endpoints.</remarks>
	public bool IsEmpty => Start == End;

	/// <summary>
	/// Determines whether the range contains the supplied position.
	/// </summary>
	/// <remarks>
	/// Positions are compared lexicographically by line and then by character. The range is
	/// half-open (start inclusive, end exclusive); because no ordering is enforced, an empty or
	/// reversed range contains no position.
	/// </remarks>
	/// <param name="position">The position to test.</param>
	/// <returns><see langword="true"/> when the position lies within the range.</returns>
	public bool Contains(TextPosition position)
		=> ComparePositions(Start, position) <= 0 && ComparePositions(position, End) < 0;

	private static int ComparePositions(TextPosition left, TextPosition right)
		=> (left.Line, left.Character).CompareTo((right.Line, right.Character));
}

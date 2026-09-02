namespace Nickelony.IDEKit.Core.FindReplace;

/// <summary>
/// Describes a single find-and-replace match within a document for presentation in search results.
/// </summary>
public sealed class FindReplaceItem
{
	/// <summary>
	/// Gets the one-based line number of the match.
	/// </summary>
	public int LineNumber { get; }

	/// <summary>
	/// Gets the full text of the line containing the match.
	/// </summary>
	public string LineText { get; }

	/// <summary>
	/// Gets the matched text segment.
	/// </summary>
	public string MatchSegmentText { get; }

	/// <summary>
	/// Gets the zero-based index of this match among the matches of <see cref="MatchSegmentText"/>
	/// on the line.
	/// </summary>
	public int MatchSegmentIndex { get; }

	/// <summary>
	/// Creates a find-and-replace match item.
	/// </summary>
	/// <param name="lineNumber">The one-based line number of the match.</param>
	/// <param name="lineText">The full text of the line containing the match.</param>
	/// <param name="matchSegmentText">The matched text segment.</param>
	/// <param name="matchSegmentIndex">The zero-based index of this match on its line.</param>
	public FindReplaceItem(int lineNumber, string lineText, string matchSegmentText, int matchSegmentIndex)
	{
		LineNumber = lineNumber;
		LineText = lineText;
		MatchSegmentText = matchSegmentText;
		MatchSegmentIndex = matchSegmentIndex;
	}
}

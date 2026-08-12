namespace Nickelony.LanguageServer.Abstractions.Infrastructure;

/// <summary>
/// Normalizes one-based document range coordinates for stable cross-boundary use.
/// </summary>
internal static class TextDocumentRangeNormalizer
{
	/// <summary>
	/// Clamps coordinates to one and ensures that the end coordinate does not precede the start coordinate.
	/// </summary>
	/// <param name="startLineNumber">The one-based start line number.</param>
	/// <param name="startColumnNumber">The one-based start column number.</param>
	/// <param name="endLineNumber">The one-based end line number.</param>
	/// <param name="endColumnNumber">The one-based end column number.</param>
	/// <returns>
	/// The normalized start and end coordinates as <c>(StartLineNumber, StartColumnNumber, EndLineNumber, EndColumnNumber)</c>.
	/// </returns>
	public static (int StartLineNumber, int StartColumnNumber, int EndLineNumber, int EndColumnNumber) Normalize(
		int startLineNumber,
		int startColumnNumber,
		int endLineNumber,
		int endColumnNumber)
	{
		int safeStartLineNumber = Math.Max(1, startLineNumber);
		int safeStartColumnNumber = Math.Max(1, startColumnNumber);
		int safeEndLineNumber = Math.Max(1, endLineNumber);
		int safeEndColumnNumber = Math.Max(1, endColumnNumber);

		if (safeEndLineNumber < safeStartLineNumber
			|| (safeEndLineNumber == safeStartLineNumber && safeEndColumnNumber < safeStartColumnNumber))
		{
			safeEndLineNumber = safeStartLineNumber;
			safeEndColumnNumber = safeStartColumnNumber;
		}

		return (safeStartLineNumber, safeStartColumnNumber, safeEndLineNumber, safeEndColumnNumber);
	}
}

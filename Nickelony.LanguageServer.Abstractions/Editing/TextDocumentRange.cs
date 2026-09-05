namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Represents a text range using one-based line and column numbers.
/// </summary>
/// <remarks>
/// Values less than one are changed to one. If the end is before the start, it is changed to the start.
/// </remarks>
public sealed class TextDocumentRange
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextDocumentRange"/> class.
	/// </summary>
	/// <param name="startLineNumber">The one-based start line number.</param>
	/// <param name="startColumnNumber">The one-based start column number.</param>
	/// <param name="endLineNumber">The one-based end line number.</param>
	/// <param name="endColumnNumber">The one-based end column number.</param>
	public TextDocumentRange(int startLineNumber, int startColumnNumber, int endLineNumber, int endColumnNumber)
	{
		(int safeStartLineNumber, int safeStartColumnNumber, int safeEndLineNumber, int safeEndColumnNumber) =
			TextDocumentRangeNormalizer.Normalize(startLineNumber, startColumnNumber, endLineNumber, endColumnNumber);

		StartLineNumber = safeStartLineNumber;
		StartColumnNumber = safeStartColumnNumber;
		EndLineNumber = safeEndLineNumber;
		EndColumnNumber = safeEndColumnNumber;
	}

	/// <summary>
	/// Gets the one-based start line number.
	/// </summary>
	public int StartLineNumber { get; }

	/// <summary>
	/// Gets the one-based start column number.
	/// </summary>
	public int StartColumnNumber { get; }

	/// <summary>
	/// Gets the one-based end line number.
	/// </summary>
	public int EndLineNumber { get; }

	/// <summary>
	/// Gets the one-based end column number.
	/// </summary>
	public int EndColumnNumber { get; }
}

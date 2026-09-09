namespace Nickelony.IDEKit.IntelliSense.Infrastructure;

/// <summary>
/// Validates snapshot-relative offsets shared by the request records.
/// </summary>
/// <remarks>
/// The request records validate their offset when it is passed, so an out-of-range position is
/// rejected at the offending call instead of surfacing later inside a provider.
/// </remarks>
internal static class SnapshotOffsetValidation
{
	/// <summary>
	/// Validates that the offset addresses a position within the document text.
	/// </summary>
	/// <param name="documentText">The document snapshot text the offset refers to.</param>
	/// <param name="offset">The zero-based offset to validate; the text length itself is a valid position.</param>
	/// <param name="parameterName">The parameter name reported by the exception.</param>
	/// <param name="offsetDescription">
	/// A short description used in the exception message (for example <c>caret</c> or <c>hovered</c>).
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The offset is negative or greater than the document text length.
	/// </exception>
	internal static void Validate(string documentText, int offset, string parameterName, string offsetDescription)
		=> Validate(documentText.Length, offset, parameterName, offsetDescription);

	/// <summary>
	/// Validates that the offset addresses a position within a document of the supplied length. Use
	/// this overload when the document text is not materialized, so the rule and its message stay in
	/// one place.
	/// </summary>
	/// <param name="textLength">The length of the document text the offset refers to.</param>
	/// <param name="offset">The zero-based offset to validate; the text length itself is a valid position.</param>
	/// <param name="parameterName">The parameter name reported by the exception.</param>
	/// <param name="offsetDescription">
	/// A short description used in the exception message (for example <c>caret</c> or <c>hovered</c>).
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The offset is negative or greater than the document text length.
	/// </exception>
	internal static void Validate(int textLength, int offset, string parameterName, string offsetDescription)
	{
		if (offset < 0 || offset > textLength)
		{
			throw new ArgumentOutOfRangeException(
				parameterName,
				offset,
				$"The {offsetDescription} offset ({offset}) is outside the document text (length {textLength}).");
		}
	}

	/// <summary>
	/// Validates that an ordered range's start offset is not after its end offset.
	/// </summary>
	/// <param name="startOffset">The zero-based start offset of the range.</param>
	/// <param name="endOffset">The zero-based end offset of the range.</param>
	/// <param name="startOffsetParameterName">The start-offset parameter name reported by the exception.</param>
	/// <param name="rangeDescription">
	/// A short description of the range used in the exception message (for example <c>selection</c>).
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="startOffset"/> is greater than <paramref name="endOffset"/>.
	/// </exception>
	internal static void ValidateOrderedRange(int startOffset, int endOffset, string startOffsetParameterName, string rangeDescription)
	{
		if (startOffset > endOffset)
		{
			throw new ArgumentOutOfRangeException(
				startOffsetParameterName,
				startOffset,
				$"The {rangeDescription} start offset must not be after its end offset.");
		}
	}
}

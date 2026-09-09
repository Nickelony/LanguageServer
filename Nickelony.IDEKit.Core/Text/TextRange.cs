namespace Nickelony.IDEKit.Core.Text;

/// <summary>
/// Represents a contiguous range of text identified by a zero-based UTF-16 offset and length.
/// </summary>
public readonly record struct TextRange
{
	/// <summary>
	/// Gets the zero-based UTF-16 offset of the start of the range.
	/// </summary>
	public int Offset { get; }

	/// <summary>
	/// Gets the length of the range in UTF-16 code units.
	/// </summary>
	public int Length { get; }

	/// <summary>
	/// Gets the zero-based UTF-16 offset of the first character after the range.
	/// </summary>
	/// <remarks>
	/// The value never exceeds <see cref="int.MaxValue"/>: the constructor rejects a negative offset
	/// or length and any range whose end cannot be represented.
	/// </remarks>
	public int EndOffset => Offset + Length;

	/// <summary>
	/// Gets a value indicating whether this range is empty.
	/// </summary>
	public bool IsEmpty => Length == 0;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextRange"/> struct.
	/// </summary>
	/// <param name="offset">The zero-based UTF-16 start offset.</param>
	/// <param name="length">The length in UTF-16 code units.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// An argument is negative, or the resulting end offset exceeds <see cref="int.MaxValue"/>.
	/// </exception>
	public TextRange(int offset, int length)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(offset);
		ArgumentOutOfRangeException.ThrowIfNegative(length);

		// EndOffset is computed as offset + length, so reject a range whose end cannot be represented.
		if (offset > int.MaxValue - length)
			throw new ArgumentOutOfRangeException(nameof(length), "The range end exceeds the maximum supported offset.");

		Offset = offset;
		Length = length;
	}

	/// <summary>
	/// Returns the text represented by this range from the given source text.
	/// </summary>
	/// <param name="source">The source text to slice.</param>
	/// <returns>The text within this range.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The range extends beyond the end of <paramref name="source"/>; the message identifies the
	/// offending range component.
	/// </exception>
	public string GetText(string source)
	{
		ArgumentNullException.ThrowIfNull(source);

		// The source is the argument that cannot satisfy this range, so the exception names it;
		// each message identifies the offending range component and its value.
		if (Offset > source.Length)
			throw new ArgumentOutOfRangeException(nameof(source), $"The range offset ({Offset}) is beyond the end of the source text (length {source.Length}).");

		if (Length > source.Length - Offset)
			throw new ArgumentOutOfRangeException(nameof(source), $"The range end ({EndOffset}) is beyond the end of the source text (length {source.Length}).");

		return source.Substring(Offset, Length);
	}

	/// <summary>
	/// Returns the text represented by this range from the given snapshot.
	/// </summary>
	/// <param name="snapshot">The snapshot to slice.</param>
	/// <returns>The text within this range.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The range extends beyond the end of the snapshot text; the message identifies the offending
	/// range component.
	/// </exception>
	public string GetText(ITextSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);

		if (Offset > snapshot.TextLength)
			throw new ArgumentOutOfRangeException(nameof(snapshot), $"The range offset ({Offset}) is beyond the end of the snapshot text (length {snapshot.TextLength}).");

		if (Length > snapshot.TextLength - Offset)
			throw new ArgumentOutOfRangeException(nameof(snapshot), $"The range end ({EndOffset}) is beyond the end of the snapshot text (length {snapshot.TextLength}).");

		return snapshot.GetText(Offset, Length);
	}

	/// <summary>
	/// Returns the range in half-open interval notation, for example <c>[4..10)</c>.
	/// </summary>
	/// <returns>A string in the form <c>[Offset..EndOffset)</c>.</returns>
	public override string ToString()
		=> $"[{Offset}..{EndOffset})";
}

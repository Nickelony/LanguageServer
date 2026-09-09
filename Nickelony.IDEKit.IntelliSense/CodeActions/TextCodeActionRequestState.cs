using Nickelony.IDEKit.IntelliSense.Infrastructure;

namespace Nickelony.IDEKit.IntelliSense.CodeActions;

/// <summary>
/// Describes the document snapshot a code-action request is made against: the state the host's
/// request-state builder returns for a <see cref="TextCodeActionContext"/>.
/// </summary>
/// <remarks>
/// The host maps the editor context to the range its provider is asked about and returns that
/// snapshot here; the standard pipeline passes it unchanged to the request delegate. The offsets are zero-based and
/// validated against <see cref="DocumentText"/>: both lie within the document and the start offset
/// is not after the end offset. Returning <see langword="null"/> from the builder instead vetoes the
/// request for that context, which clears the host's request indicator until the next context change.
/// </remarks>
public readonly record struct TextCodeActionRequestState
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextCodeActionRequestState"/> record struct.
	/// </summary>
	/// <param name="documentText">The document text snapshot the offsets refer to.</param>
	/// <param name="startOffset">The zero-based start offset of the requested range.</param>
	/// <param name="endOffset">The zero-based end offset of the requested range.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="documentText"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// An offset is negative or greater than the document text length, or
	/// <paramref name="startOffset"/> is greater than <paramref name="endOffset"/>.
	/// </exception>
	public TextCodeActionRequestState(string documentText, int startOffset, int endOffset)
	{
		ArgumentNullException.ThrowIfNull(documentText);
		SnapshotOffsetValidation.Validate(documentText, startOffset, nameof(startOffset), "start");
		SnapshotOffsetValidation.Validate(documentText, endOffset, nameof(endOffset), "end");
		SnapshotOffsetValidation.ValidateOrderedRange(startOffset, endOffset, nameof(startOffset), "range");

		this.DocumentText = documentText;
		this.StartOffset = startOffset;
		this.EndOffset = endOffset;
	}

	/// <summary>Gets the document text snapshot the offsets refer to.</summary>
	public string DocumentText { get; }

	/// <summary>Gets the zero-based start offset of the requested range.</summary>
	public int StartOffset { get; }

	/// <summary>Gets the zero-based end offset of the requested range.</summary>
	public int EndOffset { get; }
}

using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.AvalonEdit.Documents;

/// <summary>
/// An immutable <see cref="ITextSnapshot"/> of an AvalonEdit <see cref="TextDocument"/>, including its text
/// and optional file name.
/// </summary>
/// <remarks>
/// <para>
/// The snapshot wraps the AvalonEdit document snapshot created by
/// <see cref="TextDocument.CreateSnapshot()"/>, which is captured in constant time and is safe to read from a
/// background thread. Capture it on the document's owner thread, then hand it to a parser or request that
/// reads it elsewhere.
/// </para>
/// <para>
/// Line metadata requires a full text scan and is materialized from the captured text on first access,
/// so a consumer that only reads text or characters never pays for it, and the first line-metadata
/// access on a large document costs a scan proportional to its size. The snapshot is not a live view
/// and does not observe later document changes.
/// </para>
/// </remarks>
public sealed class TextDocumentSnapshot : ITextSnapshot
{
	private readonly string? _fileName;
	private readonly ITextSource _snapshot;

	private StringTextSnapshot? _lineSnapshot;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextDocumentSnapshot"/> class.
	/// </summary>
	/// <remarks>
	/// Capturing reads the document's file name and creates its snapshot, so call this on the document's
	/// owner thread. The returned snapshot itself can be read from any thread. The captured file name is
	/// the value of <see cref="TextDocument.FileName"/> as the host set it on the document; this package
	/// never writes that property, so a host that tracks document identity elsewhere must keep the
	/// document's file name in sync for snapshots to report it.
	/// </remarks>
	/// <param name="document">The AvalonEdit <see cref="TextDocument"/> to capture.</param>
	/// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
	public TextDocumentSnapshot(TextDocument document)
	{
		ArgumentNullException.ThrowIfNull(document);

		_fileName = document.FileName;
		_snapshot = document.CreateSnapshot();
	}

	/// <inheritdoc/>
	public string? FileName => _fileName;

	/// <inheritdoc/>
	public int TextLength => _snapshot.TextLength;

	/// <inheritdoc/>
	public int LineCount => GetLineSnapshot().LineCount;

	/// <inheritdoc/>
	public char GetCharAt(int offset)
	{
		// The wrapper enforces the ITextSnapshot argument contract; the underlying AvalonEdit
		// snapshot throws different exception types for some invalid ranges.
		ArgumentOutOfRangeException.ThrowIfNegative(offset);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(offset, _snapshot.TextLength);

		return _snapshot.GetCharAt(offset);
	}

	/// <inheritdoc/>
	public string GetText(int offset, int length)
	{
		// The wrapper enforces the ITextSnapshot argument contract; the underlying AvalonEdit
		// snapshot throws different exception types for some invalid ranges (for example an
		// overflow for a negative length).
		ArgumentOutOfRangeException.ThrowIfNegative(offset);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, _snapshot.TextLength);
		ArgumentOutOfRangeException.ThrowIfNegative(length);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(length, _snapshot.TextLength - offset);

		return _snapshot.GetText(offset, length);
	}

	/// <inheritdoc/>
	public ITextLine GetLineByOffset(int offset)
		=> GetLineSnapshot().GetLineByOffset(offset);

	/// <inheritdoc/>
	public ITextLine GetLineByNumber(int lineNumber)
		=> GetLineSnapshot().GetLineByNumber(lineNumber);

	/// <inheritdoc/>
	public IReadOnlyList<ITextLine> Lines => GetLineSnapshot().Lines;

	/// <summary>
	/// Gets the line metadata of the snapshot, materializing it from the captured text on first access.
	/// </summary>
	/// <remarks>
	/// A racing materialization on another thread is harmless: both instances describe the same immutable
	/// captured text, and the last published instance wins.
	/// </remarks>
	private StringTextSnapshot GetLineSnapshot()
	{
		StringTextSnapshot? lineSnapshot = _lineSnapshot;

		if (lineSnapshot is not null)
			return lineSnapshot;

		lineSnapshot = new StringTextSnapshot(_snapshot.Text, _fileName);
		_lineSnapshot = lineSnapshot;

		return lineSnapshot;
	}
}

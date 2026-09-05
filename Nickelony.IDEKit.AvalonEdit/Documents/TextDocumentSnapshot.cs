using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.AvalonEdit.Documents;

/// <summary>
/// An immutable <see cref="ITextSnapshot"/> of an AvalonEdit <see cref="TextDocument"/>,
/// including its text and optional file name.
/// </summary>
public sealed class TextDocumentSnapshot : ITextSnapshot
{
	private readonly StringTextSnapshot _snapshot;

	/// <summary>
	/// Captures the current text and optional file name from an AvalonEdit <see cref="TextDocument"/>.
	/// </summary>
	/// <remarks>The AvalonEdit <see cref="TextDocument"/> must be accessed from its owner thread.</remarks>
	/// <param name="document">The AvalonEdit <see cref="TextDocument"/> to capture.</param>
	/// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">
	/// The current thread is not the owner thread of the AvalonEdit <see cref="TextDocument"/>.
	/// </exception>
	public TextDocumentSnapshot(TextDocument document)
	{
		ArgumentNullException.ThrowIfNull(document);
		_snapshot = new StringTextSnapshot(document.Text, document.FileName);
	}

	/// <inheritdoc/>
	public string? FileName => _snapshot.FileName;

	/// <inheritdoc/>
	public int TextLength => _snapshot.TextLength;

	/// <inheritdoc/>
	public int LineCount => _snapshot.LineCount;

	/// <inheritdoc/>
	public char GetCharAt(int offset)
		=> _snapshot.GetCharAt(offset);

	/// <inheritdoc/>
	public string GetText(int offset, int length)
		=> _snapshot.GetText(offset, length);

	/// <inheritdoc/>
	public ITextLine GetLineByOffset(int offset)
		=> _snapshot.GetLineByOffset(offset);

	/// <inheritdoc/>
	public ITextLine GetLineByNumber(int lineNumber)
		=> _snapshot.GetLineByNumber(lineNumber);

	/// <inheritdoc/>
	public IEnumerable<ITextLine> Lines => _snapshot.Lines;
}

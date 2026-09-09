using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Indentation;

/// <summary>
/// Represents one line produced by <see cref="IndentationOperations.SplitLines"/>.
/// </summary>
/// <remarks>
/// The content and delimiter are kept as text so the original document can be rebuilt exactly;
/// unlike <see cref="ITextLine"/>, this shape carries the delimiter and
/// does not compute line lengths or line numbers. A <see langword="default"/> instance carries
/// <see langword="null"/> text members and a zero <see cref="StartOffset"/> despite the annotations.
/// </remarks>
/// <param name="Content">The line content without its delimiter.</param>
/// <param name="Delimiter">The line terminator that follows the content, if any.</param>
/// <param name="StartOffset">The zero-based offset of the line within the source text.</param>
public readonly record struct IndentationTextLine(string Content, string Delimiter, int StartOffset);

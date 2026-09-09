namespace Nickelony.IDEKit.Core.Text;

/// <summary>
/// Represents a zero-based line and character position in text.
/// </summary>
/// <remarks>
/// This is a plain coordinate pair: no bounds check is performed, and conversions on
/// <see cref="TextLineMap"/> clamp values that fall outside the document. The line and
/// character are zero-based indexes; one-based line numbers are exposed as
/// <see cref="ITextLine.LineNumber"/>.
/// </remarks>
/// <param name="Line">The zero-based line index.</param>
/// <param name="Character">The zero-based character index within the line.</param>
public readonly record struct TextPosition(int Line, int Character);

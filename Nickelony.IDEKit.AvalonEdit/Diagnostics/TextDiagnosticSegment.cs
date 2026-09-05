namespace Nickelony.IDEKit.AvalonEdit.Diagnostics;

/// <summary>
/// Describes a document range and its diagnostic severity.
/// </summary>
/// <remarks>
/// Offsets are zero-based UTF-16 code-unit positions.
/// When rendered, the range is clamped to the document.
/// An empty or reversed range becomes a segment of length 1 UTF-16 code unit when the document is non-empty.
/// </remarks>
/// <param name="StartOffset">The zero-based inclusive start offset of the requested range.</param>
/// <param name="EndOffset">The zero-based exclusive end offset of the requested range.</param>
/// <param name="Severity">The diagnostic severity used to choose the underline style.</param>
public readonly record struct TextDiagnosticSegment(int StartOffset, int EndOffset, TextDiagnosticSeverity Severity);

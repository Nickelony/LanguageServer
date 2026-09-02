namespace Nickelony.IDEKit.AvalonEdit.Diagnostics;

/// <summary>
/// Identifies a document range and its severity for diagnostic underlining.
/// </summary>
/// <remarks>
/// Offsets are measured in UTF-16 code units. The renderer clamps the range and expands it to one
/// character when it is empty or reversed.
/// </remarks>
/// <param name="StartOffset">The zero-based inclusive start offset of the segment.</param>
/// <param name="EndOffset">The zero-based exclusive end offset of the segment.</param>
/// <param name="Severity">The severity used to select the underline style.</param>
public readonly record struct TextDiagnosticSegment(int StartOffset, int EndOffset, TextDiagnosticSeverity Severity);

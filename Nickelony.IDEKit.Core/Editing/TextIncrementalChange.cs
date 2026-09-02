using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Describes one minimal replacement that transforms one document content value into another.
/// </summary>
/// <param name="Range">The zero-based UTF-16 range in the original content.</param>
/// <param name="NewText">The replacement text.</param>
public readonly record struct TextIncrementalChange(TextRange Range, string NewText);

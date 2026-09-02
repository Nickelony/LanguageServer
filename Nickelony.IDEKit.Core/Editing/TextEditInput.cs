using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Describes a text replacement using a zero-based UTF-16 source range.
/// </summary>
/// <param name="Range">The source range to replace.</param>
/// <param name="NewText">The replacement text.</param>
public sealed record TextEditInput(TextRange Range, string NewText);

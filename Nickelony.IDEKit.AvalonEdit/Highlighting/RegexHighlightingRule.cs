using System.Text.RegularExpressions;

namespace Nickelony.IDEKit.AvalonEdit.Highlighting;

/// <summary>
/// Represents a highlighting rule that applies a <see cref="RegexHighlightingStyle"/> to text
/// matched by a regular expression.
/// </summary>
/// <param name="Regex">The regular expression used to match highlighted text.</param>
/// <param name="Style">The style applied to each match.</param>
public sealed record RegexHighlightingRule(Regex Regex, RegexHighlightingStyle Style);

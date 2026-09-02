using System.Text.RegularExpressions;

namespace Nickelony.IDEKit.AvalonEdit.Highlighting;

/// <summary>
/// Declares a highlighting rule that matches a regular expression and applies a
/// <see cref="RegexHighlightingStyle"/> to the matched text.
/// </summary>
/// <param name="Regex">The regular expression that identifies the highlighted text.</param>
/// <param name="Style">The style applied to the matched text.</param>
public sealed record RegexHighlightingRule(Regex Regex, RegexHighlightingStyle Style);

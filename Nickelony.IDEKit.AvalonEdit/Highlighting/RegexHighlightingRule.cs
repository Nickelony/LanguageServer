using System.Text.RegularExpressions;

namespace Nickelony.IDEKit.AvalonEdit.Highlighting;

/// <summary>
/// Represents a highlighting rule that applies a <see cref="RegexHighlightingStyle"/> to text
/// matched by a regular expression.
/// </summary>
/// <remarks>
/// <para>
/// AvalonEdit evaluates the regular expression per line while the editor renders, so avoid patterns that can
/// backtrack catastrophically; construct the <see cref="Regex"/> with a bounded match timeout so a pathological
/// pattern cannot stall the UI thread. When the timeout fires during rendering, the resulting
/// <see cref="RegexMatchTimeoutException"/> is raised inside AvalonEdit's colorizer on the UI thread, where no
/// layer above the engine catches it, so a timeout trades a stall for that failure mode.
/// </para>
/// <para>
/// A pattern must never be able to match empty text. AvalonEdit's highlight engine cannot advance past a
/// zero-length match and throws when the earliest match at a scan position is empty, so avoid constructs such
/// as <c>$</c>, <c>\b</c>, empty-capable alternations, and optional repetitions that can match nothing.
/// <see cref="RegexHighlightingDefinition"/> rejects patterns its probe detects when it builds the rule set;
/// a pattern that can match empty only on text the probe lacks fails inside the engine during rendering
/// instead.
/// </para>
/// </remarks>
public sealed class RegexHighlightingRule
{
	/// <summary>
	/// Initializes a new instance of the <see cref="RegexHighlightingRule"/> class.
	/// </summary>
	/// <param name="pattern">The regular expression used to match highlighted text.</param>
	/// <param name="style">The style applied to each match.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="pattern"/> or <paramref name="style"/> is <see langword="null"/>.
	/// </exception>
	public RegexHighlightingRule(Regex pattern, RegexHighlightingStyle style)
	{
		ArgumentNullException.ThrowIfNull(pattern);
		ArgumentNullException.ThrowIfNull(style);

		Pattern = pattern;
		Style = style;
	}

	/// <summary>
	/// Gets the regular expression used to match highlighted text.
	/// </summary>
	public Regex Pattern { get; }

	/// <summary>
	/// Gets the style applied to each match.
	/// </summary>
	public RegexHighlightingStyle Style { get; }
}

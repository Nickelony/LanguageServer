using System.Text.RegularExpressions;

namespace Nickelony.IDEKit.AvalonEdit.Highlighting;

/// <summary>
/// Represents a delimiter-based highlighting span (for example a block comment or a long string) that is
/// highlighted from a begin match to an end match.
/// </summary>
/// <remarks>
/// <para>
/// The span covers the text from the begin match through the end match and stays highlighted on the
/// following lines until the end matches, so it can express multiline constructs that a per-line rule
/// cannot. A span whose <see cref="End"/> is <see langword="null"/> extends to the end of the document
/// once its begin matches; an end pattern that never matches keeps the span open to the document end.
/// </para>
/// <para>
/// <see cref="SpanStyle"/> colors the whole span, including the delimiter matches unless
/// <see cref="BeginStyle"/> or <see cref="EndStyle"/> override them for their own matches.
/// </para>
/// <para>
/// The begin and end patterns must never match empty text; the definition rejects them when the rule set
/// is built, because AvalonEdit's highlight engine cannot advance past a zero-length match.
/// </para>
/// </remarks>
public sealed class RegexHighlightingSpan
{
	/// <summary>
	/// Initializes a new instance of the <see cref="RegexHighlightingSpan"/> class.
	/// </summary>
	/// <param name="begin">The regular expression that starts the span.</param>
	/// <param name="end">The regular expression that closes the span, or <see langword="null"/> for a span that never closes.</param>
	/// <param name="spanStyle">The style applied to the whole span.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="begin"/> or <paramref name="spanStyle"/> is <see langword="null"/>.
	/// </exception>
	public RegexHighlightingSpan(
		Regex begin,
		Regex? end,
		RegexHighlightingStyle spanStyle)
	{
		ArgumentNullException.ThrowIfNull(begin);
		ArgumentNullException.ThrowIfNull(spanStyle);

		Begin = begin;
		End = end;
		SpanStyle = spanStyle;
	}

	/// <summary>
	/// Gets the regular expression that starts the span.
	/// </summary>
	public Regex Begin { get; }

	/// <summary>
	/// Gets the regular expression that closes the span, or <see langword="null"/> for a span that never closes.
	/// </summary>
	public Regex? End { get; }

	/// <summary>
	/// Gets the style applied to the whole span.
	/// </summary>
	public RegexHighlightingStyle SpanStyle { get; }

	/// <summary>
	/// Gets the style applied to the begin match instead of the span style, when supplied.
	/// </summary>
	public RegexHighlightingStyle? BeginStyle { get; init; }

	/// <summary>
	/// Gets the style applied to the end match instead of the span style, when supplied.
	/// </summary>
	public RegexHighlightingStyle? EndStyle { get; init; }
}

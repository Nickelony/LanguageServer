namespace Nickelony.IDEKit.Core.Comments;

/// <summary>
/// Specifies which string-literal styles are recognized while scanning for comments,
/// so that a delimiter inside string content is not treated as a comment start.
/// Members are flags and combine for languages that support several quote characters,
/// e.g. <c>DoubleQuoted | SingleQuoted | BacktickQuoted</c> for JavaScript.
/// The backtick and triple-quoted styles describe multi-line strings.
/// </summary>
[Flags]
public enum StringLiteralStyle
{
	/// <summary>
	/// No string awareness; any delimiter occurrence starts a comment.
	/// This matches INI-like formats and languages without string literals.
	/// </summary>
	None = 0,

	/// <summary>
	/// Double-quoted strings with backslash escapes, as in the C family (C, C++, C#, Java).
	/// </summary>
	DoubleQuoted = 1 << 0,

	/// <summary>
	/// Single-quoted strings with backslash escapes, as in Python, JavaScript, and Lua.
	/// </summary>
	SingleQuoted = 1 << 1,

	/// <summary>
	/// Backtick-quoted strings, as in JavaScript template literals and Go raw strings.
	/// These strings may span lines.
	/// </summary>
	BacktickQuoted = 1 << 2,

	/// <summary>
	/// Multi-line strings delimited by a run of three or more double quotes,
	/// as in C# raw string literals and other raw or multi-line string syntaxes.
	/// The opening quote run is the delimiter; the string closes on a run of at least that length,
	/// so content that must contain a three-quote sequence uses a longer opener (C#).
	/// Backslash escapes are not processed in this scanning mode.
	/// </summary>
	TripleDoubleQuoted = 1 << 3,

	/// <summary>
	/// Multi-line strings delimited by a run of three or more single quotes, as in Python.
	/// Backslash escapes are not processed; Python's escaped quotes inside such strings are not modeled.
	/// </summary>
	TripleSingleQuoted = 1 << 4,

	/// <summary>
	/// Multi-line strings delimited by Lua-style long brackets: <c>[[ ... ]]</c>,
	/// <c>[=[ ... ]=]</c>, and so on, where the opener and closer carry the same number
	/// of equals signs. These strings may span lines and process no escape sequences.
	/// </summary>
	LongBracketQuoted = 1 << 5,
}

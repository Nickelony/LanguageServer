namespace Nickelony.LanguageServer.Lua;

/// <summary>
/// Parses typed Lua language-server responses into shared text-model types (completion items, hover info,
/// definition locations, reference locations, workspace edits, signature help, formatting edits, document
/// symbols, and code actions).
/// </summary>
internal static partial class LuaLanguageServerResponseParser
{
	/// <summary>
	/// Determines whether a protocol range is ordered - its end is at or after its start.
	/// </summary>
	/// <remarks>
	/// Range consumers validate the raw protocol coordinates with this rule before clamping, because clamping
	/// can collapse an inverted range into a zero-length range that would otherwise pass as valid.
	/// </remarks>
	/// <param name="start">The range start position.</param>
	/// <param name="end">The range end position.</param>
	/// <returns><see langword="true"/> when the range is ordered.</returns>
	internal static bool IsOrderedRange(ProtocolPosition start, ProtocolPosition end)
		=> start.Line < end.Line || (start.Line == end.Line && start.Character <= end.Character);
}

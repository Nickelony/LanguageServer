namespace Nickelony.LanguageServer.Lua;

/// <summary>
/// Defines the Lua-specific language-service contract used by the Lua editor to provide IntelliSense features.
/// </summary>
/// <remarks>
/// This interface extends <see cref="ILanguageServerIntelliSenseProvider"/> with Lua-specific concerns such as
/// semantic tokens. The generic document lifecycle, diagnostics, completion, hover, definition, and signature-help
/// contracts are defined on the base interface.
///
/// Implementations may raise callbacks from background threads. Consumers that access UI controls must marshal those
/// callbacks to the UI thread. Disposal prevents new callbacks, but a callback already in progress may finish.
/// </remarks>
public interface ILuaIntelliSenseProvider : ILanguageServerIntelliSenseProvider
{
	/// <summary>
	/// Occurs when semantic tokens for a document have changed.
	/// </summary>
	/// <remarks>
	/// This callback may be raised from a background thread. UI consumers must marshal to the UI thread before touching
	/// controls. Handlers for one event invocation run serially on the raising thread, and a failing handler does not
	/// prevent later handlers from running. The semantic-token list contains the decoded result for that notification,
	/// and each token's modifier list is a read-only snapshot. Disposal prevents new callbacks, but a callback already in progress
	/// may finish.
	/// </remarks>
	event Action<string, IReadOnlyList<LuaSemanticToken>>? SemanticTokensUpdated;

	/// <summary>
	/// Gets the latest semantic tokens known for a document.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <returns>A read-only snapshot of the semantic tokens currently cached for the document.</returns>
	IReadOnlyList<LuaSemanticToken> GetSemanticTokens(string filePath);
}

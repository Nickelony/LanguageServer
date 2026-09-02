using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.LanguageServer.Lua;

/// <summary>
/// Normalizes the visible insert and replace ranges that participate in completion-item duplicate detection.
/// </summary>
/// <param name="InsertRange">The insert range, when present.</param>
/// <param name="ReplaceRange">The replace range, when present.</param>
internal readonly record struct LuaCompletionTextEditIdentity(TextRange? InsertRange, TextRange? ReplaceRange)
{
	/// <summary>
	/// Creates a normalized text-edit identity from a parsed completion text edit.
	/// </summary>
	/// <param name="textEdit">The parsed completion text edit.</param>
	/// <returns>The normalized identity.</returns>
	internal static LuaCompletionTextEditIdentity Create(TextCompletionTextEdit? textEdit)
	{
		return textEdit is not { } value
			? default
			: new(value.InsertRange, value.ReplaceRange);
	}
}

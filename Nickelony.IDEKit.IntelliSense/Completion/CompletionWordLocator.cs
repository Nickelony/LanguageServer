using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.IntelliSense.Completion;

/// <summary>
/// Locates the word being typed at a caret offset within a document snapshot.
/// </summary>
/// <param name="snapshot">The document snapshot to inspect.</param>
/// <param name="caretOffset">The zero-based caret offset.</param>
/// <returns>The word being typed and the range it occupies.</returns>
public delegate CompletionWordInfo CompletionWordLocator(ITextSnapshot snapshot, int caretOffset);

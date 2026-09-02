using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.IntelliSense.Completion;

/// <summary>
/// Describes the word being typed at a caret offset and the range it occupies.
/// </summary>
/// <remarks>
/// The <see cref="Word"/> is used to filter completion items and the <see cref="Range"/> defines
/// the document span replaced when a completion is committed. Languages with word or
/// replacement-range rules that differ from the kernel default supply their own
/// <see cref="CompletionWordInfo"/> through a <see cref="CompletionWordLocator"/> or per call.
/// </remarks>
/// <param name="Word">The word being typed at the caret, or an empty string when there is none.</param>
/// <param name="Range">The zero-based range the word occupies, used as the completion replacement range.</param>
public readonly record struct CompletionWordInfo(string Word, TextRange Range);

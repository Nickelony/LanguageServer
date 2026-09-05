using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.IntelliSense.Completion;

/// <summary>
/// Computes shared completion-session decisions from a provider, a document snapshot, and the word
/// being typed at the caret.
/// </summary>
/// <remarks>
/// The kernel owns the generic completion-session pipeline - provider invocation, current-word filtering,
/// word and replacement-range handling, and the open / close / no-op decision - so language coordinators stay
/// thin and do not duplicate session logic. Languages
/// with custom word or replacement-range rules supply their own <see cref="CompletionWordLocator"/> to
/// the constructor or a per-call <see cref="CompletionWordInfo"/>, and may inject a language-specific
/// item filter. The kernel does not retain per-request state; callers own any supersession or cancellation
/// policy around the returned decision. Custom locators and filters must provide their own thread-safety
/// when a kernel is shared between callers.
/// </remarks>
public sealed class CompletionSessionKernel
{
	private readonly CompletionWordLocator _wordLocator;
	private readonly Func<IReadOnlyList<TextCompletionItem>, string, IReadOnlyList<TextCompletionItem>> _filter;

	/// <summary>
	/// Initializes a new instance of the <see cref="CompletionSessionKernel"/> class.
	/// </summary>
	/// <param name="wordLocator">
	/// The strategy that locates the word being typed and its replacement range, or <see langword="null"/> to use
	/// the default identifier-word locator (letters, digits, and underscores before the caret).
	/// </param>
	/// <param name="filter">
	/// The strategy that reduces completion items to those matching the word being typed, or <see langword="null"/>
	/// to use <see cref="TextCompletionFilter.FilterByWord"/>.
	/// </param>
	public CompletionSessionKernel(
		CompletionWordLocator? wordLocator = null,
		Func<IReadOnlyList<TextCompletionItem>, string, IReadOnlyList<TextCompletionItem>>? filter = null)
	{
		_wordLocator = wordLocator ?? LocateDefaultWord;
		_filter = filter ?? TextCompletionFilter.FilterByWord;
	}

	/// <summary>
	/// Computes a completion session decision using the provider synchronously.
	/// </summary>
	/// <param name="snapshot">The document snapshot the caret refers to.</param>
	/// <param name="caretOffset">The zero-based caret offset within <paramref name="snapshot"/>.</param>
	/// <param name="provider">The completion provider to invoke.</param>
	/// <param name="trigger">The reason the completion request was raised.</param>
	/// <param name="wordInfo">
	/// An optional explicit word and replacement range; when omitted, the configured
	/// <see cref="CompletionWordLocator"/> computes them from <paramref name="snapshot"/> and
	/// <paramref name="caretOffset"/>.
	/// </param>
	/// <returns>
	/// An opening decision with the filtered items and replacement range, or
	/// <see cref="TextCompletionSessionDecision.None"/> when the provider returns no items or filtering removes
	/// every item.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="snapshot"/> or <paramref name="provider"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="caretOffset"/> is negative or greater than the snapshot text length.
	/// </exception>
	public TextCompletionSessionDecision GetDecision(
		ITextSnapshot snapshot,
		int caretOffset,
		ITextCompletionProvider provider,
		TextCompletionTrigger trigger = TextCompletionTrigger.Automatic,
		CompletionWordInfo? wordInfo = null)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(provider);

		var word = wordInfo ?? _wordLocator(snapshot, caretOffset);
		var context = new TextCompletionContext(snapshot.GetText(0, snapshot.TextLength), caretOffset, trigger);

		return CreateDecision(provider.GetCompletionItems(context), word);
	}

	/// <summary>
	/// Computes a completion session decision, running the provider off the calling thread.
	/// </summary>
	/// <remarks>
	/// The provider contract permits background execution for CPU-bound catalog scans. The word and
	/// replacement range are computed synchronously before the provider runs. Because the provider
	/// contract has no cancellation parameter, <paramref name="cancellationToken"/> can prevent
	/// queued work from starting but cannot interrupt a provider that has already started. Callers
	/// that supersede requests should combine cancellation with their own request identity check
	/// after awaiting the result.
	/// </remarks>
	/// <param name="snapshot">The document snapshot the caret refers to.</param>
	/// <param name="caretOffset">The zero-based caret offset within <paramref name="snapshot"/>.</param>
	/// <param name="provider">The completion provider to invoke.</param>
	/// <param name="trigger">The reason the completion request was raised.</param>
	/// <param name="wordInfo">
	/// An optional explicit word and replacement range; when omitted, the configured
	/// <see cref="CompletionWordLocator"/> computes them from <paramref name="snapshot"/> and
	/// <paramref name="caretOffset"/>.
	/// </param>
	/// <param name="cancellationToken">The cancellation token used to cancel queued provider work before it starts.</param>
	/// <returns>
	/// An opening decision with the filtered items and replacement range, or
	/// <see cref="TextCompletionSessionDecision.None"/> when the provider returns no items or filtering removes
	/// every item.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="snapshot"/> or <paramref name="provider"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="caretOffset"/> is negative or greater than the snapshot text length.
	/// </exception>
	public async Task<TextCompletionSessionDecision> GetDecisionAsync(
		ITextSnapshot snapshot,
		int caretOffset,
		ITextCompletionProvider provider,
		TextCompletionTrigger trigger = TextCompletionTrigger.Automatic,
		CompletionWordInfo? wordInfo = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(provider);

		var word = wordInfo ?? _wordLocator(snapshot, caretOffset);
		var context = new TextCompletionContext(snapshot.GetText(0, snapshot.TextLength), caretOffset, trigger);

		IReadOnlyList<TextCompletionItem> items = await Task.Run(
			() => provider.GetCompletionItems(context),
			cancellationToken).ConfigureAwait(false);

		return CreateDecision(items, word);
	}

	private TextCompletionSessionDecision CreateDecision(
		IReadOnlyList<TextCompletionItem> items,
		CompletionWordInfo wordInfo)
	{
		IReadOnlyList<TextCompletionItem> filtered = _filter(items, wordInfo.Word);

		return filtered.Count == 0
			? TextCompletionSessionDecision.None
			: TextCompletionSessionDecision.Open(filtered, wordInfo.Range.Offset, wordInfo.Range.EndOffset);
	}

	private static CompletionWordInfo LocateDefaultWord(ITextSnapshot snapshot, int caretOffset)
	{
		caretOffset = Math.Clamp(caretOffset, 0, snapshot.TextLength);

		int start = caretOffset;

		while (start > 0 && IsIdentifierCharacter(snapshot.GetCharAt(start - 1)))
			start--;

		string word = snapshot.GetText(start, caretOffset - start);

		return new CompletionWordInfo(word, new TextRange(start, caretOffset - start));
	}

	private static bool IsIdentifierCharacter(char character)
		=> char.IsLetterOrDigit(character) || character == '_';
}

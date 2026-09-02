using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.AvalonEdit.Documents;

/// <summary>
/// Wires an <see cref="IncrementalLineStateCache{TState}"/> to an AvalonEdit
/// <see cref="TextDocument"/>, translating document changes into neutral edit deltas.
/// </summary>
/// <typeparam name="TState">The parser continuation state type carried across lines.</typeparam>
public sealed class DocumentLineStateCache<TState> : IDisposable
{
	private readonly TextDocument _document;
	private readonly IncrementalLineStateCache<TState> _cache;

	/// <summary>
	/// Initializes a new instance of the <see cref="DocumentLineStateCache{TState}"/> class.
	/// </summary>
	/// <param name="document">The AvalonEdit document whose line-start states should be cached.</param>
	/// <param name="transition">
	/// Computes the continuation state for a line given its text and the previous line's state.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="document"/> or <paramref name="transition"/> is null.
	/// </exception>
	public DocumentLineStateCache(TextDocument document, Func<string, TState, TState> transition)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentNullException.ThrowIfNull(transition);

		_document = document;
		_cache = new IncrementalLineStateCache<TState>(new TextDocumentSnapshot(document), transition);
		_document.Changed += Document_Changed;
	}

	/// <summary>
	/// Gets the parser continuation state that applies at the start of the specified one-based line.
	/// </summary>
	/// <param name="lineNumber">The one-based document line number. Values outside the document are clamped.</param>
	/// <returns>The cached or computed line-start parser state.</returns>
	public TState GetLineStartState(int lineNumber)
		=> _cache.GetLineStartState(lineNumber);

	/// <summary>
	/// Unsubscribes from the document so the cache stops tracking edits.
	/// </summary>
	/// <remarks>Disposal does not clear states already held by the cache.</remarks>
	public void Dispose()
		=> _document.Changed -= Document_Changed;

	private void Document_Changed(object? sender, DocumentChangeEventArgs e)
	{
		_cache.ApplyChange(
			new TextDocumentSnapshot(_document),
			new TextIncrementalChange(new TextRange(e.Offset, e.RemovalLength), e.InsertedText.Text));
	}
}

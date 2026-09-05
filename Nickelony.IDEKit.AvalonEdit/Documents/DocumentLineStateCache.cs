using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.AvalonEdit.Documents;

/// <summary>
/// Tracks parser continuation states at line starts for an AvalonEdit <see cref="TextDocument"/>
/// and invalidates affected states when the document changes.
/// </summary>
/// <remarks>
/// Use this class when an editor feature scans a document one line at a time and carries state across line boundaries.
/// For example, a syntax highlighter can cache whether each line starts inside a block comment or string.
/// Edits invalidate the affected suffix, and states are recomputed lazily as needed.
/// </remarks>
/// <typeparam name="TState">The parser continuation state carried from one line to the next.</typeparam>
public sealed class DocumentLineStateCache<TState> : IDisposable
{
	private readonly TextDocument _document;
	private readonly IncrementalLineStateCache<TState> _cache;

	/// <summary>
	/// Creates a line-state cache for <paramref name="document"/> using <paramref name="transition"/>.
	/// </summary>
	/// <param name="document">The AvalonEdit <see cref="TextDocument"/> whose line-start states are tracked.</param>
	/// <param name="transition">
	/// Computes the next line's state from the current line's text and start state.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="document"/> or <paramref name="transition"/> is <see langword="null"/>.
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
	/// Gets the parser continuation state at the start of the specified one-based line.
	/// </summary>
	/// <param name="lineNumber">
	/// The one-based line number. Values less than or equal to 1 return <see langword="default"/>.
	/// Values beyond the document's final line are clamped to the final line, if one exists.
	/// </param>
	/// <returns>
	/// The cached or computed line-start state;
	/// <see langword="default"/> for line numbers at or below 1 or an empty snapshot.
	/// </returns>
	public TState GetLineStartState(int lineNumber)
		=> _cache.GetLineStartState(lineNumber);

	/// <summary>
	/// Stops tracking edits to the AvalonEdit <see cref="TextDocument"/> by unsubscribing from
	/// <see cref="TextDocument.Changed"/>.
	/// </summary>
	/// <remarks>Cached states remain available but are not updated by later edits.</remarks>
	public void Dispose()
		=> _document.Changed -= Document_Changed;

	private void Document_Changed(object? sender, DocumentChangeEventArgs e)
	{
		_cache.ApplyChange(
			new TextDocumentSnapshot(_document),
			new TextIncrementalChange(new TextRange(e.Offset, e.RemovalLength), e.InsertedText.Text));
	}
}

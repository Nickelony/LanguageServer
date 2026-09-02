using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.IntelliSense.DocumentSymbols;

/// <summary>
/// Represents a symbol (outline entry) found in a document.
/// </summary>
/// <remarks>
/// Aligned with the LSP <c>DocumentSymbol</c> shape but protocol-independent: ranges use neutral
/// zero-based UTF-16 offsets (<see cref="TextRange"/>) rather than protocol line and column
/// coordinates, and the symbol kind is <see cref="TextDocumentSymbolKind"/>. Nested symbols are
/// carried in <see cref="Children"/>, and an optional host payload rides in <see cref="Data"/>.
/// </remarks>
public sealed class TextDocumentSymbol
{
	private readonly IReadOnlyList<TextDocumentSymbol> _children;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextDocumentSymbol"/> class.
	/// </summary>
	/// <param name="name">The display name of the symbol.</param>
	/// <param name="kind">The semantic category of the symbol.</param>
	/// <param name="selectionRange">
	/// The zero-based range of the symbol name, or <see langword="null"/> when unknown.
	/// </param>
	/// <param name="range">
	/// The zero-based full range of the symbol, or <see langword="null"/> to fall back to
	/// <paramref name="selectionRange"/>.
	/// </param>
	/// <param name="detail">An optional short detail line shown beside the name.</param>
	/// <param name="children">The nested symbols of this symbol, or <see langword="null"/> when there are none.</param>
	/// <param name="data">An optional host-specific payload (for example a navigation discriminator).</param>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	public TextDocumentSymbol(
		string name,
		TextDocumentSymbolKind kind,
		TextRange? selectionRange = null,
		TextRange? range = null,
		string? detail = null,
		IReadOnlyList<TextDocumentSymbol>? children = null,
		object? data = null)
	{
		ArgumentNullException.ThrowIfNull(name);

		Name = name;
		Kind = kind;
		SelectionRange = selectionRange;
		Range = range ?? selectionRange;
		Detail = string.IsNullOrWhiteSpace(detail) ? null : detail;
		Data = data;

		_children = children is { Count: > 0 } ? [.. children] : [];
	}

	/// <summary>
	/// Gets the display name of the symbol.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// Gets the semantic category of the symbol.
	/// </summary>
	public TextDocumentSymbolKind Kind { get; }

	/// <summary>
	/// Gets the zero-based range of the symbol name, or <see langword="null"/> when unknown.
	/// </summary>
	public TextRange? SelectionRange { get; }

	/// <summary>
	/// Gets the zero-based full range of the symbol, falling back to <see cref="SelectionRange"/>.
	/// </summary>
	public TextRange? Range { get; }

	/// <summary>
	/// Gets the optional short detail line shown beside the name.
	/// </summary>
	public string? Detail { get; }

	/// <summary>
	/// Gets the owned immutable snapshot of nested symbols.
	/// </summary>
	public IReadOnlyList<TextDocumentSymbol> Children => _children;

	/// <summary>
	/// Gets the optional host-specific payload (for example a navigation discriminator).
	/// </summary>
	public object? Data { get; }

	/// <summary>
	/// Gets a value indicating whether this symbol has nested children.
	/// </summary>
	public bool HasChildren => _children.Count > 0;
}

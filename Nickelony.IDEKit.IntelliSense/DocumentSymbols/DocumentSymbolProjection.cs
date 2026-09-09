using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.IntelliSense.DocumentSymbols;

/// <summary>
/// Describes how items of one data type are projected into <see cref="TextDocumentSymbol"/> entries.
/// </summary>
/// <remarks>
/// The projection is a plain selector bundle with no validation of its own; the
/// <see cref="DocumentSymbolOutlineBuilder"/> methods validate that <see cref="NameSelector"/> and
/// <see cref="KindSelector"/> are supplied, while the remaining selectors are optional. A
/// <c>default</c> instance carries no selectors and is rejected by the builder.
/// </remarks>
/// <typeparam name="TItem">The item data type.</typeparam>
/// <param name="NameSelector">Selects the display name of an item.</param>
/// <param name="KindSelector">Selects the semantic category of an item.</param>
/// <param name="DataSelector">Selects the optional host payload of an item.</param>
/// <param name="RangeSelector">Selects the optional full range of an item.</param>
/// <param name="SelectionRangeSelector">Selects the optional name range of an item.</param>
/// <param name="DetailSelector">
/// Selects the optional detail line of an item; blank values are treated as absent.
/// </param>
public readonly record struct DocumentSymbolProjection<TItem>(
	Func<TItem, string> NameSelector,
	Func<TItem, TextDocumentSymbolKind> KindSelector,
	Func<TItem, object?>? DataSelector = null,
	Func<TItem, TextRange?>? RangeSelector = null,
	Func<TItem, TextRange?>? SelectionRangeSelector = null,
	Func<TItem, string?>? DetailSelector = null);

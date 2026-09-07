namespace Nickelony.IDEKit.IntelliSense.DocumentSymbols;

/// <summary>
/// Builds <see cref="TextDocumentSymbol"/> trees from flat or grouped node data.
/// </summary>
/// <remarks>
/// Group roots are created as <see cref="TextDocumentSymbolKind.Module"/> symbols and their nodes
/// as <see cref="TextDocumentSymbolKind.Variable"/> children. The builder does not assign ranges,
/// details, or UI state; callers can carry host data through the optional <c>dataSelector</c>.
/// </remarks>
public static class DocumentSymbolTreeBuilder
{
	/// <summary>
	/// Builds a flat list of variable symbols from the given data.
	/// </summary>
	/// <typeparam name="TNode">The node data type.</typeparam>
	/// <param name="nodes">The node data items.</param>
	/// <param name="textSelector">Selects the display name of a node.</param>
	/// <param name="dataSelector">Selects the optional host payload of a node.</param>
	/// <returns>The built document symbols, with one variable symbol per node and no range or children.</returns>
	/// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
	public static IReadOnlyList<TextDocumentSymbol> BuildFlatNodes<TNode>(
		IReadOnlyList<TNode> nodes,
		Func<TNode, string> textSelector,
		Func<TNode, object?>? dataSelector = null)
	{
		ArgumentNullException.ThrowIfNull(nodes);
		ArgumentNullException.ThrowIfNull(textSelector);

		var result = new List<TextDocumentSymbol>(nodes.Count);

		foreach (TNode node in nodes)
			result.Add(CreateSymbol(textSelector(node), dataSelector?.Invoke(node)));

		return result;
	}

	/// <summary>
	/// Builds a grouped list of symbols, with one module root and variable children per group.
	/// </summary>
	/// <typeparam name="TGroup">The group data type.</typeparam>
	/// <typeparam name="TNode">The node data type.</typeparam>
	/// <param name="groups">The group data items.</param>
	/// <param name="headerSelector">Selects the display name of a group root.</param>
	/// <param name="nodesSelector">Selects the node data items of a group.</param>
	/// <param name="textSelector">Selects the display name of a node.</param>
	/// <param name="dataSelector">Selects the optional host payload of a node.</param>
	/// <returns>The built document symbols, with module roots carrying their variable nodes as children.</returns>
	/// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
	public static IReadOnlyList<TextDocumentSymbol> BuildGroupedNodes<TGroup, TNode>(
		IReadOnlyList<TGroup> groups,
		Func<TGroup, string> headerSelector,
		Func<TGroup, IReadOnlyList<TNode>> nodesSelector,
		Func<TNode, string> textSelector,
		Func<TNode, object?>? dataSelector = null)
	{
		ArgumentNullException.ThrowIfNull(groups);
		ArgumentNullException.ThrowIfNull(headerSelector);
		ArgumentNullException.ThrowIfNull(nodesSelector);
		ArgumentNullException.ThrowIfNull(textSelector);

		var result = new List<TextDocumentSymbol>(groups.Count);

		foreach (TGroup group in groups)
		{
			IReadOnlyList<TNode> nodes = nodesSelector(group);
			ArgumentNullException.ThrowIfNull(nodes);

			var children = new List<TextDocumentSymbol>(nodes.Count);

			foreach (TNode node in nodes)
				children.Add(CreateSymbol(textSelector(node), dataSelector?.Invoke(node)));

			result.Add(new TextDocumentSymbol(headerSelector(group), TextDocumentSymbolKind.Module, children: children));
		}

		return result;
	}

	private static TextDocumentSymbol CreateSymbol(string text, object? data)
		=> new(text, TextDocumentSymbolKind.Variable, data: data);
}

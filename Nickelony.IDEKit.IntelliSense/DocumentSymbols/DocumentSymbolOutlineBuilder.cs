namespace Nickelony.IDEKit.IntelliSense.DocumentSymbols;

/// <summary>
/// Projects flat or grouped item data into <see cref="TextDocumentSymbol"/> outline entries.
/// </summary>
/// <remarks>
/// <para>
/// The builder is document- and range-agnostic: it creates outline entries from a
/// <see cref="DocumentSymbolProjection{TItem}"/> and produces either a flat list
/// (<see cref="BuildFlatOutline{TItem}"/>) or one group root with its item children per group
/// (<see cref="BuildGroupedOutline{TGroup, TItem}"/>). Ranges, selection ranges, and the detail
/// line are projected when the corresponding selectors are supplied; deeper nesting than the one
/// grouping level is not produced, and callers that need it construct
/// <see cref="TextDocumentSymbol"/> instances directly.
/// </para>
/// <para>
/// The caller chooses every name and kind through the projections. Output and children follow the
/// order of the supplied groups and items. Returned lists are fresh caller-owned snapshots.
/// </para>
/// </remarks>
public static class DocumentSymbolOutlineBuilder
{
	/// <summary>
	/// Builds a flat outline from the given items.
	/// </summary>
	/// <typeparam name="TItem">The item data type.</typeparam>
	/// <param name="items">The item data to project.</param>
	/// <param name="projection">The projection applied to every item.</param>
	/// <returns>A new caller-owned list with one symbol per item and no children.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="items"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// The projection does not supply its name or kind selector.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The projection's name selector returned <see langword="null"/>.
	/// </exception>
	public static IReadOnlyList<TextDocumentSymbol> BuildFlatOutline<TItem>(
		IReadOnlyList<TItem> items,
		DocumentSymbolProjection<TItem> projection)
	{
		ArgumentNullException.ThrowIfNull(items);
		ValidateProjection(projection, nameof(projection));

		var result = new List<TextDocumentSymbol>(items.Count);

		for (int i = 0; i < items.Count; i++)
			result.Add(CreateSymbol(items[i], projection, "the item", i));

		return result;
	}

	/// <summary>
	/// Builds a grouped outline with one group root and its item children per group.
	/// </summary>
	/// <typeparam name="TGroup">The group data type.</typeparam>
	/// <typeparam name="TItem">The item data type.</typeparam>
	/// <param name="groups">The group data to project.</param>
	/// <param name="groupProjection">The projection applied to every group root.</param>
	/// <param name="groupItemsSelector">
	/// Selects the item data of a group. The selector must not return <see langword="null"/>.
	/// </param>
	/// <param name="itemProjection">The projection applied to every item.</param>
	/// <returns>
	/// A new caller-owned list with one root per group, carrying the group's projected items as
	/// children.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="groups"/> or <paramref name="groupItemsSelector"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// A projection does not supply its name or kind selector.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// <paramref name="groupItemsSelector"/> returned <see langword="null"/> for a group, or a projection's
	/// name selector returned <see langword="null"/>.
	/// </exception>
	public static IReadOnlyList<TextDocumentSymbol> BuildGroupedOutline<TGroup, TItem>(
		IReadOnlyList<TGroup> groups,
		DocumentSymbolProjection<TGroup> groupProjection,
		Func<TGroup, IReadOnlyList<TItem>> groupItemsSelector,
		DocumentSymbolProjection<TItem> itemProjection)
	{
		ArgumentNullException.ThrowIfNull(groups);
		ValidateProjection(groupProjection, nameof(groupProjection));
		ArgumentNullException.ThrowIfNull(groupItemsSelector);
		ValidateProjection(itemProjection, nameof(itemProjection));

		var result = new List<TextDocumentSymbol>(groups.Count);

		for (int i = 0; i < groups.Count; i++)
		{
			TGroup group = groups[i];
			IReadOnlyList<TItem> items = groupItemsSelector(group);

			if (items is null)
			{
				throw new InvalidOperationException(
					$"The items selector returned null for the group at index {i}.");
			}

			var children = new List<TextDocumentSymbol>(items.Count);

			for (int j = 0; j < items.Count; j++)
				children.Add(CreateSymbol(items[j], itemProjection, "an item of the group", i));

			result.Add(CreateSymbol(group, groupProjection, "the group", i, children));
		}

		return result;
	}

	private static void ValidateProjection<TItem>(DocumentSymbolProjection<TItem> projection, string paramName)
	{
		if (projection.NameSelector is null)
			throw new ArgumentException("The projection must supply a name selector.", paramName);

		if (projection.KindSelector is null)
			throw new ArgumentException("The projection must supply a kind selector.", paramName);
	}

	private static TextDocumentSymbol CreateSymbol<TItem>(
		TItem item,
		DocumentSymbolProjection<TItem> projection,
		string subject,
		int index,
		IReadOnlyList<TextDocumentSymbol>? children = null)
	{
		string name = projection.NameSelector(item);

		if (name is null)
		{
			throw new InvalidOperationException(
				$"The projection name selector returned null for {subject} at index {index}.");
		}

		return new(
			name,
			projection.KindSelector(item),
			projection.RangeSelector?.Invoke(item),
			projection.SelectionRangeSelector?.Invoke(item))
		{
			Detail = projection.DetailSelector?.Invoke(item),
			Children = children,
			Data = projection.DataSelector?.Invoke(item)
		};
	}
}

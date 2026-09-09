namespace Nickelony.LanguageServer.Lua;

/// <summary>
/// Caches the latest version-fenced payload of a tracked document, such as its diagnostics or semantic tokens.
/// </summary>
/// <remarks>
/// <para>
/// Items are stored as a read-only snapshot detached from the source collection. The cached version follows
/// <see cref="LuaDocumentVersionPolicy.TryAccept"/>: a payload with an unknown version <c>0</c> is accepted
/// without advancing the cached version, a stale positive version is rejected, and a newer positive version
/// replaces the cached stamp. An accepted unversioned payload still replaces the cached items, so an
/// out-of-order unversioned push can overwrite newer content without touching the stamp. The fence is
/// enforced by the cache itself, independent of caller-side checks, so every store path keeps its own guard.
/// </para>
/// <para>
/// <see cref="SourceContent"/> carries the content snapshot the items' offsets refer to when the producer
/// supplies one (the diagnostics path does, so its offsets can be mapped back to positions later).
/// </para>
/// </remarks>
/// <typeparam name="TItem">The cached item type.</typeparam>
internal sealed class VersionFencedPayloadCache<TItem>
{
	private static readonly IReadOnlyList<TItem> s_emptyItems = Array.AsReadOnly(Array.Empty<TItem>());

	/// <summary>
	/// Gets the currently cached items.
	/// </summary>
	internal IReadOnlyList<TItem> Items { get; private set; } = s_emptyItems;

	/// <summary>
	/// Gets the synchronized document version associated with the cached items.
	/// </summary>
	internal int Version { get; private set; }

	/// <summary>
	/// Gets the content snapshot the cached items' offsets refer to, or <see langword="null"/> when the
	/// producer supplied none.
	/// </summary>
	internal string? SourceContent { get; private set; }

	/// <summary>
	/// Clears the cached payload, including the source-content snapshot.
	/// </summary>
	internal void Clear()
	{
		Items = s_emptyItems;
		Version = 0;
		SourceContent = null;
	}

	/// <summary>
	/// Drops the synchronized version stamp while preserving the cached payload, so the next stored payload
	/// is accepted regardless of the version it reports.
	/// </summary>
	internal void ResetVersionStamp()
		=> Version = 0;

	/// <summary>
	/// Stores a payload when its version is not stale relative to the current cache.
	/// </summary>
	/// <param name="version">The synchronized document version associated with the payload.</param>
	/// <param name="items">The items to cache, or <see langword="null"/> to cache an empty list.</param>
	/// <param name="sourceContent">
	/// The content snapshot the items' offsets refer to, or <see langword="null"/> when the items carry no
	/// offsets or the producer supplied none.
	/// </param>
	/// <returns><see langword="true"/> when the payload was stored; otherwise, <see langword="false"/>.</returns>
	internal bool TryStore(int version, IReadOnlyList<TItem>? items, string? sourceContent = null)
	{
		if (!LuaDocumentVersionPolicy.TryAccept(Version, version, out int acceptedVersion))
			return false;

		Version = acceptedVersion;
		Items = CreateReadOnlySnapshot(items);
		SourceContent = sourceContent;
		return true;
	}

	private static IReadOnlyList<TItem> CreateReadOnlySnapshot(IReadOnlyList<TItem>? items)
	{
		return items is null || items.Count == 0
			? s_emptyItems
			: Array.AsReadOnly([.. items]);
	}
}

namespace Nickelony.IDEKit.AvalonEdit.Bookmarks;

/// <summary>
/// Provides extension methods for persisting bookmarks managed by a <see cref="BookmarkCoordinator"/>.
/// </summary>
public static class BookmarkStoreExtensions
{
	/// <summary>
	/// Saves the coordinator's current one-based bookmark line numbers through the supplied store.
	/// </summary>
	/// <param name="coordinator">The coordinator whose bookmarks are persisted.</param>
	/// <param name="store">The store used to save the bookmarks.</param>
	/// <param name="filePath">The path of the document the bookmarks belong to.</param>
	/// <returns><see langword="true"/> when the store reports success; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="coordinator"/>, <paramref name="store"/>, or <paramref name="filePath"/> is <see langword="null"/>.
	/// </exception>
	public static bool SaveBookmarks(this BookmarkCoordinator coordinator, IBookmarkStore store, string filePath)
	{
		ArgumentNullException.ThrowIfNull(coordinator);
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(filePath);

		IReadOnlyList<int> lineNumbers = [.. coordinator.GetBookmarkedLines().Select(line => line.LineNumber)];

		return store.Save(filePath, lineNumbers);
	}

	/// <summary>
	/// Restores the stored one-based bookmark line numbers into the coordinator.
	/// </summary>
	/// <remarks>
	/// Replaces existing bookmarks. Line numbers outside the document are ignored,
	/// and the coordinator's change callback is not invoked.
	/// </remarks>
	/// <param name="coordinator">The coordinator whose bookmarks are restored.</param>
	/// <param name="store">The store used to restore the bookmarks.</param>
	/// <param name="filePath">The path of the document the bookmarks belong to.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="coordinator"/>, <paramref name="store"/>, or <paramref name="filePath"/> is <see langword="null"/>.
	/// </exception>
	public static void RestoreBookmarks(this BookmarkCoordinator coordinator, IBookmarkStore store, string filePath)
	{
		ArgumentNullException.ThrowIfNull(coordinator);
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(filePath);

		coordinator.Restore(store.Restore(filePath));
	}
}

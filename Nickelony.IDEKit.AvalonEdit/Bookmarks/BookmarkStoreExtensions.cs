namespace Nickelony.IDEKit.AvalonEdit.Bookmarks;

/// <summary>
/// Saves and restores <see cref="BookmarkCoordinator"/> bookmarks through an
/// <see cref="IBookmarkStore"/>.
/// </summary>
public static class BookmarkStoreExtensions
{
	/// <summary>
	/// Saves the coordinator's bookmarked line numbers through the supplied store.
	/// </summary>
	/// <param name="coordinator">The coordinator whose bookmarks are persisted.</param>
	/// <param name="store">The store used to persist the bookmarks.</param>
	/// <param name="filePath">The path of the document the bookmarks belong to.</param>
	/// <returns><see langword="true"/> when the bookmarks were persisted; otherwise, <see langword="false"/>.</returns>
	public static bool SaveBookmarks(this BookmarkCoordinator coordinator, IBookmarkStore store, string filePath)
	{
		ArgumentNullException.ThrowIfNull(coordinator);
		ArgumentNullException.ThrowIfNull(store);

		IReadOnlyList<int> lineNumbers = [.. coordinator.GetBookmarkedLines().Select(line => line.LineNumber)];

		return store.Save(filePath, lineNumbers);
	}

	/// <summary>
	/// Restores the coordinator's bookmarks from the supplied store.
	/// </summary>
	/// <param name="coordinator">The coordinator whose bookmarks are restored.</param>
	/// <param name="store">The store used to load the bookmarks.</param>
	/// <param name="filePath">The path of the document the bookmarks belong to.</param>
	public static void RestoreBookmarks(this BookmarkCoordinator coordinator, IBookmarkStore store, string filePath)
	{
		ArgumentNullException.ThrowIfNull(coordinator);
		ArgumentNullException.ThrowIfNull(store);

		coordinator.Restore(store.Restore(filePath));
	}
}

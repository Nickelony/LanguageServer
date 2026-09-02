namespace Nickelony.IDEKit.AvalonEdit.Bookmarks;

/// <summary>
/// Persists the bookmarked line numbers of a document to a host-chosen store.
/// </summary>
/// <remarks>
/// The store contract is storage-agnostic: implementations may write a sidecar file next to the
/// document (see <see cref="BookmarkSidecarStore"/>), a settings file, a workspace store, or a
/// database. The <see cref="BookmarkCoordinator"/> itself performs no persistence; hosts supply
/// a store when saving or restoring.
/// </remarks>
public interface IBookmarkStore
{
	/// <summary>
	/// Saves the bookmarked one-based line numbers for the document at the supplied path.
	/// </summary>
	/// <param name="filePath">The path of the document the bookmarks belong to.</param>
	/// <param name="bookmarkedLineNumbers">The one-based line numbers to persist.</param>
	/// <returns><see langword="true"/> when the bookmarks were persisted; otherwise, <see langword="false"/>.</returns>
	bool Save(string filePath, IReadOnlyList<int> bookmarkedLineNumbers);

	/// <summary>
	/// Restores the bookmarked one-based line numbers for the document at the supplied path.
	/// </summary>
	/// <param name="filePath">The path of the document the bookmarks belong to.</param>
	/// <returns>The restored line numbers, or an empty list when none are stored.</returns>
	IReadOnlyList<int> Restore(string filePath);
}

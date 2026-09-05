namespace Nickelony.IDEKit.AvalonEdit.Bookmarks;

/// <summary>
/// Provides persistence operations for bookmarked line numbers.
/// </summary>
/// <remarks>
/// The <see cref="BookmarkCoordinator"/> keeps bookmarks in memory; implementations decide how they are persisted.
/// </remarks>
public interface IBookmarkStore
{
	/// <summary>
	/// Saves one-based bookmark line numbers for the document at <paramref name="filePath"/>.
	/// </summary>
	/// <param name="filePath">The document path.</param>
	/// <param name="bookmarkedLineNumbers">The one-based line numbers to save.</param>
	/// <returns><see langword="true"/> if the save operation succeeds; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="bookmarkedLineNumbers"/> is <see langword="null"/>.
	/// </exception>
	bool Save(string filePath, IReadOnlyList<int> bookmarkedLineNumbers);

	/// <summary>
	/// Restores one-based bookmark line numbers for the document at <paramref name="filePath"/>.
	/// </summary>
	/// <param name="filePath">The document path.</param>
	/// <returns>The stored one-based line numbers, or an empty list if none are available.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> is <see langword="null"/>.
	/// </exception>
	IReadOnlyList<int> Restore(string filePath);
}

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
	/// <remarks>
	/// <see langword="false"/> reports a save that did not persist the numbers: a blank or unusable path,
	/// or a failed file operation. Implementations may report additional
	/// reasons, so callers must not treat <see langword="false"/> as one specific failure.
	/// </remarks>
	/// <param name="filePath">The document path.</param>
	/// <param name="bookmarkedLineNumbers">The one-based line numbers to save.</param>
	/// <returns><see langword="true"/> when the save operation succeeds; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="bookmarkedLineNumbers"/> is <see langword="null"/>.
	/// </exception>
	bool Save(string filePath, IReadOnlyList<int> bookmarkedLineNumbers);

	/// <summary>
	/// Attempts to load the stored one-based bookmark line numbers for the document at <paramref name="filePath"/>.
	/// </summary>
	/// <remarks>
	/// <see langword="true"/> reports a load that produced a result: the stored numbers, or an empty
	/// list when nothing was stored. <see langword="false"/> reports a load that could not be
	/// performed: a blank or unusable path, or a failed read, so a caller can tell a storage failure
	/// apart from "nothing was saved". Implementations may report additional reasons, so callers must
	/// not treat <see langword="false"/> as one specific failure.
	/// </remarks>
	/// <param name="filePath">The document path.</param>
	/// <param name="bookmarkLineNumbers">
	/// Receives the stored one-based line numbers; empty when none were stored.
	/// </param>
	/// <returns><see langword="true"/> when the load produced a result; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> is <see langword="null"/>.
	/// </exception>
	bool TryLoad(string filePath, out IReadOnlyList<int> bookmarkLineNumbers);
}

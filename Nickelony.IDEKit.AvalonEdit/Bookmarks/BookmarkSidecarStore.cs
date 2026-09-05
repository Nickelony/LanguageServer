using Nickelony.IDEKit.Core.Persistence;

namespace Nickelony.IDEKit.AvalonEdit.Bookmarks;

/// <summary>
/// Persists bookmarked line numbers in a sidecar file associated with each document.
/// </summary>
public sealed class BookmarkSidecarStore : IBookmarkStore
{
	private readonly string _sidecarExtension;

	/// <summary>
	/// Initializes a store that appends the specified suffix to each document path.
	/// </summary>
	/// <param name="sidecarExtension">
	/// The suffix appended to document paths. Defaults to <c>.bkmrk</c>.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="sidecarExtension"/> is <see langword="null"/>.</exception>
	public BookmarkSidecarStore(string sidecarExtension = ".bkmrk")
	{
		ArgumentNullException.ThrowIfNull(sidecarExtension);
		_sidecarExtension = sidecarExtension;
	}

	/// <inheritdoc/>
	public bool Save(string filePath, IReadOnlyList<int> bookmarkedLineNumbers)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(bookmarkedLineNumbers);

		return SidecarLineFile.Save(filePath, bookmarkedLineNumbers, _sidecarExtension);
	}

	/// <inheritdoc/>
	public IReadOnlyList<int> Restore(string filePath)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		return [.. SidecarLineFile.Restore(filePath, _sidecarExtension)];
	}
}

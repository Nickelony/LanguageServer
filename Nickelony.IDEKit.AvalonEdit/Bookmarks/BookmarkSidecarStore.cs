using Nickelony.IDEKit.Core.Persistence;

namespace Nickelony.IDEKit.AvalonEdit.Bookmarks;

/// <summary>
/// Persists bookmarks to a <c>.bkmrk</c> sidecar file whose path is formed by appending the
/// extension to the document path.
/// </summary>
/// <remarks>
/// This is the default <see cref="IBookmarkStore"/> for hosts that want file-local bookmark
/// persistence. Hosts that persist bookmarks elsewhere (settings, workspace store, database)
/// implement <see cref="IBookmarkStore"/> directly and never use this type.
/// </remarks>
public sealed class BookmarkSidecarStore : IBookmarkStore
{
	private readonly string _sidecarExtension;

	/// <summary>
	/// Initializes a new instance of the <see cref="BookmarkSidecarStore"/> class.
	/// </summary>
	/// <param name="sidecarExtension">
	/// The text appended to the document path, or <see langword="null"/> to use the default <c>.bkmrk</c>.
	/// </param>
	public BookmarkSidecarStore(string? sidecarExtension = null)
		=> _sidecarExtension = sidecarExtension ?? ".bkmrk";

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

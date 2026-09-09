using Nickelony.IDEKit.Core.Persistence;

namespace Nickelony.IDEKit.AvalonEdit.Bookmarks;

/// <summary>
/// Persists bookmarked line numbers in a sidecar file associated with each document.
/// </summary>
/// <remarks>
/// <para>
/// The sidecar extension is supplied by the host, so sidecar naming stays a host decision.
/// </para>
/// <para>
/// The numbers are persisted sorted and de-duplicated; they are matched against the document's lines
/// when a coordinator restores them, so edits between a save and a restore can silently move a
/// bookmark to a different line.
/// </para>
/// <para>
/// Saves and loads perform synchronous file I/O on the calling thread, so a host should not call them
/// from a per-change handler.
/// </para>
/// </remarks>
public sealed class BookmarkSidecarStore : IBookmarkStore
{
	private readonly string _sidecarExtension;

	/// <summary>
	/// Initializes a new instance of the <see cref="BookmarkSidecarStore"/> class.
	/// </summary>
	/// <param name="sidecarExtension">
	/// The non-blank, usable extension appended to document paths, for example <c>.bkmrk</c>.
	/// A blank or unusable extension is rejected here, so an invalid configuration fails at construction
	/// instead of making every save and restore throw.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="sidecarExtension"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="sidecarExtension"/> is empty, whitespace-only, or does not form a usable file-name
	/// extension (for example <c>.</c>, <c>a/b</c>, or <c>x\y</c>).
	/// </exception>
	public BookmarkSidecarStore(string sidecarExtension)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sidecarExtension);

		SidecarLineFile.ValidateExtension(sidecarExtension);

		_sidecarExtension = sidecarExtension;
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Entries below one are ignored by <see cref="SidecarLineFile.Save"/>, so a set whose entries are
	/// all ignored deletes the sidecar file instead of writing one.
	/// </remarks>
	public bool Save(string filePath, IReadOnlyList<int> bookmarkedLineNumbers)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(bookmarkedLineNumbers);

		return SidecarLineFile.Save(filePath, bookmarkedLineNumbers, _sidecarExtension);
	}

	/// <inheritdoc/>
	public bool TryLoad(string filePath, out IReadOnlyList<int> bookmarkLineNumbers)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		return SidecarLineFile.TryRestore(filePath, _sidecarExtension, out bookmarkLineNumbers);
	}
}

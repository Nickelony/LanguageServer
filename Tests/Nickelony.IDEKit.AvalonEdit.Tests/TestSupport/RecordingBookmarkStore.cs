using Nickelony.IDEKit.AvalonEdit.Bookmarks;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

/// <summary>
/// An <see cref="IBookmarkStore"/> test double that records the calls made through it and returns
/// configured results, so the coordinator-level persistence extensions can be pinned without the
/// file system.
/// </summary>
internal sealed class RecordingBookmarkStore : IBookmarkStore
{
	/// <summary>
	/// Gets the save calls in call order, each with the path and the line numbers the coordinator supplied.
	/// </summary>
	public List<(string FilePath, IReadOnlyList<int> LineNumbers)> Saves { get; } = [];

	/// <summary>
	/// Gets the load calls in call order.
	/// </summary>
	public List<string> LoadedPaths { get; } = [];

	/// <summary>
	/// Gets or sets a value indicating whether <see cref="Save"/> reports success. Defaults to <see langword="true"/>.
	/// </summary>
	public bool SaveResult { get; set; } = true;

	/// <summary>
	/// Gets or sets a value indicating whether <see cref="TryLoad"/> reports a result.
	/// Defaults to <see langword="true"/>.
	/// </summary>
	public bool TryLoadResult { get; set; } = true;

	/// <summary>
	/// Gets or sets the line numbers <see cref="TryLoad"/> reports. Defaults to an empty list.
	/// </summary>
	public IReadOnlyList<int> LoadResult { get; set; } = [];

	/// <inheritdoc/>
	public bool Save(string filePath, IReadOnlyList<int> bookmarkedLineNumbers)
	{
		Saves.Add((filePath, [.. bookmarkedLineNumbers]));

		return SaveResult;
	}

	/// <inheritdoc/>
	public bool TryLoad(string filePath, out IReadOnlyList<int> bookmarkLineNumbers)
	{
		LoadedPaths.Add(filePath);

		bookmarkLineNumbers = LoadResult;
		return TryLoadResult;
	}
}

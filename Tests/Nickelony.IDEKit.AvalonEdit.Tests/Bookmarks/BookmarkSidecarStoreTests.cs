using Nickelony.IDEKit.AvalonEdit.Bookmarks;
using System.IO;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

[TestClass]
public sealed class BookmarkSidecarStoreTests
{
	private readonly TempDirectoryScope _tempDirectory = new("BookmarkSidecarStoreTests");

	private string CreateTempPath()
		=> _tempDirectory.CreatePath("document.txt");

	[TestCleanup]
	public void Cleanup()
		=> _tempDirectory.Dispose();

	[TestMethod]
	public void Save_ThenLoad_RoundTripsLineNumbersThroughStore()
	{
		string filePath = CreateTempPath();
		var store = new BookmarkSidecarStore(".bkmrk");

		bool saved = store.Save(filePath, [2, 7]);
		Assert.IsTrue(saved);

		Assert.IsTrue(store.TryLoad(filePath, out IReadOnlyList<int> loaded));
		Assert.AreSequenceEqual([2, 7], [.. loaded]);
	}

	[TestMethod]
	public void Save_EmptySet_DeletesSidecar()
	{
		string filePath = CreateTempPath();
		var store = new BookmarkSidecarStore(".bkmrk");

		store.Save(filePath, [1]);
		store.Save(filePath, []);

		Assert.IsFalse(File.Exists(filePath + ".bkmrk"));
		Assert.IsTrue(store.TryLoad(filePath, out IReadOnlyList<int> emptyAfterDelete));
		Assert.IsEmpty(emptyAfterDelete);
	}

	[TestMethod]
	public void Save_OnlyIgnoredEntries_DeletesSidecar()
	{
		string filePath = CreateTempPath();
		var store = new BookmarkSidecarStore(".bkmrk");

		store.Save(filePath, [1, 2]);
		Assert.IsTrue(File.Exists(filePath + ".bkmrk"));

		// Entries below one are ignored by the underlying writer, so a set that filters to nothing
		// deletes the sidecar instead of writing one.
		store.Save(filePath, [0, -3]);

		Assert.IsFalse(File.Exists(filePath + ".bkmrk"));
		Assert.IsTrue(store.TryLoad(filePath, out IReadOnlyList<int> emptyAfterIgnored));
		Assert.IsEmpty(emptyAfterIgnored);
	}

	[TestMethod]
	public void TryLoad_NormalizesSkippedDuplicatesAndOrdering()
	{
		string filePath = CreateTempPath();
		var store = new BookmarkSidecarStore(".bkmrk");

		// The sidecar is written raw so the load normalization is exercised directly: a skipped
		// non-integer, a duplicate, and unordered entries.
		File.WriteAllLines(filePath + ".bkmrk", ["7", "not-a-number", "3", "3", "1", "0"]);

		Assert.IsTrue(store.TryLoad(filePath, out IReadOnlyList<int> loaded));
		Assert.AreSequenceEqual([1, 3, 7], [.. loaded]);
	}

	[TestMethod]
	public void BlankOrNullExtension_IsRejected()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new BookmarkSidecarStore(null!));
		Assert.ThrowsExactly<ArgumentException>(() => new BookmarkSidecarStore(string.Empty));
		Assert.ThrowsExactly<ArgumentException>(() => new BookmarkSidecarStore("   "));
	}

	[TestMethod]
	public void UnusableExtensionForms_AreRejected()
	{
		Assert.ThrowsExactly<ArgumentException>(() => new BookmarkSidecarStore("."));
		Assert.ThrowsExactly<ArgumentException>(() => new BookmarkSidecarStore("a/b"));
		Assert.ThrowsExactly<ArgumentException>(() => new BookmarkSidecarStore("x\\y"));
	}

	[TestMethod]
	public void CustomExtension_IsHonored()
	{
		string filePath = CreateTempPath();
		var store = new BookmarkSidecarStore(".markers");

		store.Save(filePath, [3]);

		Assert.IsTrue(File.Exists(filePath + ".markers"));
		Assert.IsFalse(File.Exists(filePath + ".bkmrk"));
	}

	[TestMethod]
	public void Save_UnwritablePath_ReturnsFalseAndTryLoadReportsNothingStored()
	{
		string filePath = CreateTempPath();
		string blockerPath = Path.Combine(Path.GetDirectoryName(filePath)!, "blocker");

		File.WriteAllText(blockerPath, "not a directory");

		// 'blocker' is a file, so the sidecar path below it cannot be written.
		string blockedPath = Path.Combine(blockerPath, "document.txt");
		var store = new BookmarkSidecarStore(".bkmrk");

		Assert.IsFalse(store.Save(blockedPath, [1]));

		// No sidecar exists at the blocked path, so the load reports success with an empty list.
		Assert.IsTrue(store.TryLoad(blockedPath, out IReadOnlyList<int> blockedLines));
		Assert.IsEmpty(blockedLines);
	}

	[TestMethod]
	public void TryLoad_MissingSidecar_ReportsSuccessWithEmptyList()
	{
		string filePath = CreateTempPath();
		var store = new BookmarkSidecarStore(".bkmrk");

		Assert.IsTrue(store.TryLoad(filePath, out IReadOnlyList<int> loaded));
		Assert.IsEmpty(loaded);
	}

	[TestMethod]
	public void TryLoad_EmptyFileAndOnlyIgnoredEntries_ReportSuccessWithEmptyList()
	{
		string filePath = CreateTempPath();
		var store = new BookmarkSidecarStore(".bkmrk");

		File.WriteAllText(filePath + ".bkmrk", string.Empty);

		Assert.IsTrue(store.TryLoad(filePath, out IReadOnlyList<int> emptyFile));
		Assert.IsEmpty(emptyFile);

		File.WriteAllLines(filePath + ".bkmrk", ["0", string.Empty, "x"]);

		Assert.IsTrue(store.TryLoad(filePath, out IReadOnlyList<int> ignoredEntries));
		Assert.IsEmpty(ignoredEntries);
	}

	[TestMethod]
	public void TryLoad_SidecarPathOccupiedByDirectory_ReportsFailure()
	{
		string filePath = CreateTempPath();
		Directory.CreateDirectory(filePath + ".bkmrk");
		var store = new BookmarkSidecarStore(".bkmrk");

		Assert.IsFalse(store.TryLoad(filePath, out IReadOnlyList<int> loaded));
		Assert.IsEmpty(loaded);
	}
}

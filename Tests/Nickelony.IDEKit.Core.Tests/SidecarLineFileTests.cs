namespace Nickelony.IDEKit.Core.Persistence.Tests;

/// <summary>
/// Tests persisting and restoring line numbers with <see cref="SidecarLineFile"/>.
/// </summary>
[TestClass]
public sealed class SidecarLineFileTests
{
	private static string CreateTempPath(out string directory)
	{
		directory = Path.Combine(Path.GetTempPath(), "SidecarLineFileTests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		return Path.Combine(directory, "document.txt");
	}

	[TestCleanup]
	public void Cleanup()
	{
		// Best-effort removal of test temporary directories.
		foreach (string dir in Directory.GetDirectories(Path.GetTempPath(), "SidecarLineFileTests-*"))
		{
			try
			{
				Directory.Delete(dir, recursive: true);
			}
			catch (IOException)
			{ }
			catch (UnauthorizedAccessException)
			{ }
		}
	}

	[TestMethod]
	public void Save_ThenRestore_RoundTripsLineNumbers()
	{
		string filePath = CreateTempPath(out _);

		bool saved = SidecarLineFile.Save(filePath, [1, 5, 9]);
		Assert.IsTrue(saved);

		IReadOnlyList<int> restored = SidecarLineFile.Restore(filePath);
		CollectionAssert.AreEqual(new[] { 1, 5, 9 }, restored.ToArray());
	}

	[TestMethod]
	public void Save_DuplicatesAndUnsortedInput_AreNormalized()
	{
		string filePath = CreateTempPath(out _);

		SidecarLineFile.Save(filePath, [9, 1, 5, 1]);

		IReadOnlyList<int> restored = SidecarLineFile.Restore(filePath);
		CollectionAssert.AreEqual(new[] { 1, 5, 9 }, restored.ToArray());
	}

	[TestMethod]
	public void Save_EmptySet_DeletesExistingSidecar()
	{
		string filePath = CreateTempPath(out _);
		SidecarLineFile.Save(filePath, [1, 2]);

		Assert.IsTrue(File.Exists(SidecarLineFile.GetSidecarPath(filePath)));

		SidecarLineFile.Save(filePath, []);

		Assert.IsFalse(File.Exists(SidecarLineFile.GetSidecarPath(filePath)));
		Assert.AreEqual(0, SidecarLineFile.Restore(filePath).Count);
	}

	[TestMethod]
	public void Save_EmptyFilePath_ReturnsFalse()
	{
		bool saved = SidecarLineFile.Save(string.Empty, [1]);

		Assert.IsFalse(saved);
	}

	[TestMethod]
	public void Restore_MissingSidecar_ReturnsEmpty()
	{
		string filePath = CreateTempPath(out _);

		IReadOnlyList<int> restored = SidecarLineFile.Restore(filePath);

		Assert.AreEqual(0, restored.Count);
	}

	[TestMethod]
	public void Restore_InvalidEntries_AreSkipped()
	{
		string filePath = CreateTempPath(out _);
		File.WriteAllLines(SidecarLineFile.GetSidecarPath(filePath), ["1", "abc", "2", "-3", "0"]);

		IReadOnlyList<int> restored = SidecarLineFile.Restore(filePath);

		// Invalid and non-positive entries are ignored.
		CollectionAssert.AreEqual(new[] { 1, 2 }, restored.ToArray());
	}

	[TestMethod]
	public void GetSidecarPath_DefaultExtension_AppendsSidecar()
	{
		Assert.AreEqual("doc.txt.sidecar", SidecarLineFile.GetSidecarPath("doc.txt"));
	}

	[TestMethod]
	public void GetSidecarPath_CustomExtension_IsHonored()
	{
		Assert.AreEqual("doc.txt.bkmrk", SidecarLineFile.GetSidecarPath("doc.txt", ".bkmrk"));
	}
}

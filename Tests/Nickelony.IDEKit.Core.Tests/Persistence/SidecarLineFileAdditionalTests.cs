using System.Text;

namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Pins the failure-reporting and normalization behaviors added to <see cref="SidecarLineFile"/>.
/// </summary>
[TestClass]
public sealed class SidecarLineFileAdditionalTests
{
	[TestMethod]
	public void TryRestore_MissingSidecar_SucceedsWithNoLines()
	{
		string documentPath = Path.Combine(
			Path.GetTempPath(),
			"sidecar-wave-" + Guid.NewGuid().ToString("N") + ".txt");

		Assert.IsTrue(SidecarLineFile.TryRestore(documentPath, ".bkmrk", out IReadOnlyList<int> lineNumbers));
		Assert.AreEqual(0, lineNumbers.Count);
	}

	[TestMethod]
	public void TryRestore_BlankPath_ReportsFailure()
	{
		Assert.IsFalse(SidecarLineFile.TryRestore("   ", ".bkmrk", out IReadOnlyList<int> lineNumbers));
		Assert.AreEqual(0, lineNumbers.Count);
	}

	[TestMethod]
	public void Save_DegenerateSet_DeletesExistingSidecarAndReportsSuccess()
	{
		string directory = Path.Combine(Path.GetTempPath(), "sidecar-wave-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);

		string documentPath = Path.Combine(directory, "doc.txt");

		try
		{
			Assert.IsTrue(SidecarLineFile.Save(documentPath, [3, 7], ".bkmrk"));

			string sidecarPath = SidecarLineFile.GetSidecarPath(documentPath, ".bkmrk");

			Assert.IsTrue(File.Exists(sidecarPath));

			// A set that filters to nothing deletes the healthy sidecar and reports success; this
			// pins the documented consequence of a degenerate caller set.
			Assert.IsTrue(SidecarLineFile.Save(documentPath, [0, -1], ".bkmrk"));
			Assert.IsFalse(File.Exists(sidecarPath));
			Assert.AreEqual(0, SidecarLineFile.Restore(documentPath, ".bkmrk").Count);
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[TestMethod]
	public void TryRestore_DirectoryOccupiesTheSidecarPath_ReportsFailure()
	{
		string directory = Path.Combine(Path.GetTempPath(), "sidecar-wave-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);

		string documentPath = Path.Combine(directory, "doc.txt");

		try
		{
			// A directory at the sidecar path can never hold saved line numbers, so the restore reports
			// a storage failure instead of "nothing was saved".
			Directory.CreateDirectory(SidecarLineFile.GetSidecarPath(documentPath, ".bkmrk"));

			Assert.IsFalse(SidecarLineFile.TryRestore(documentPath, ".bkmrk", out IReadOnlyList<int> lineNumbers));
			Assert.AreEqual(0, lineNumbers.Count);
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[TestMethod]
	public void TryRestore_LockedSidecar_ReportsFailure()
	{
		if (!OperatingSystem.IsWindows())
		{
			Assert.Inconclusive("The locked-file read failure is Windows-specific.");
		}

		string directory = Path.Combine(Path.GetTempPath(), "sidecar-wave-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);

		string documentPath = Path.Combine(directory, "doc.txt");

		try
		{
			Assert.IsTrue(SidecarLineFile.Save(documentPath, [1, 2], ".bkmrk"));

			using var lockedSidecar = new FileStream(
				SidecarLineFile.GetSidecarPath(documentPath, ".bkmrk"),
				FileMode.Open,
				FileAccess.Read,
				FileShare.None);

			// The unreadable sidecar is a storage failure, not "nothing was saved".
			Assert.IsFalse(SidecarLineFile.TryRestore(documentPath, ".bkmrk", out IReadOnlyList<int> lineNumbers));
			Assert.AreEqual(0, lineNumbers.Count);
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[TestMethod]
	public void Restore_ByteOrderMarkAndLoneCrTerminator_AreAccepted()
	{
		string directory = Path.Combine(Path.GetTempPath(), "sidecar-wave-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);

		string documentPath = Path.Combine(directory, "doc.txt");

		try
		{
			// The read honors a byte-order mark and accepts any line terminator, including a lone CR.
			File.WriteAllText(
				SidecarLineFile.GetSidecarPath(documentPath, ".bkmrk"),
				"4\r2",
				new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

			IReadOnlyList<int> restored = SidecarLineFile.Restore(documentPath, ".bkmrk");

			CollectionAssert.AreEqual(new[] { 2, 4 }, restored.ToArray());
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}
}

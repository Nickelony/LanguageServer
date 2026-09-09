namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Provides best-effort cleanup for temporary test directories.
/// </summary>
internal static class TestDirectory
{
	/// <summary>
	/// Deletes a temporary directory tree, ignoring the I/O failure a lingering watcher handle can cause during
	/// teardown; cleanup must not fail a test that already ran.
	/// </summary>
	/// <param name="directoryPath">The directory to delete.</param>
	public static void DeleteBestEffort(string directoryPath)
	{
		try
		{
			Directory.Delete(directoryPath, recursive: true);
		}
		catch (IOException)
		{
			// Temp cleanup is best-effort: a lingering handle from a watcher teardown must not fail the test.
		}
	}
}

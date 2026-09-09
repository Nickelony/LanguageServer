namespace Nickelony.LanguageServer.Lua.Tests;

/// <summary>
/// Deletes temporary workspace directories without failing a test run when a lingering file handle,
/// a locked directory, or an antivirus scan briefly blocks the recursive delete.
/// </summary>
internal static class TestTempDirectories
{
	/// <summary>
	/// Deletes <paramref name="directoryPath"/> recursively when it exists, swallowing the file-system
	/// exceptions a best-effort teardown should not surface.
	/// </summary>
	/// <param name="directoryPath">The temporary directory to delete.</param>
	internal static void Delete(string directoryPath)
	{
		try
		{
			if (Directory.Exists(directoryPath))
				Directory.Delete(directoryPath, recursive: true);
		}
		catch (IOException)
		{ }
		catch (UnauthorizedAccessException)
		{ }
	}
}

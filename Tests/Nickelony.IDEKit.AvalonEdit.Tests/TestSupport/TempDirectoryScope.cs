using System.IO;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

/// <summary>
/// Creates one temporary directory for a test class and deletes it on cleanup.
/// </summary>
/// <param name="name">The directory name prefix, typically the test class name.</param>
internal sealed class TempDirectoryScope(string name) : IDisposable
{
	private string? _directory;

	/// <summary>
	/// Gets a path for the given file name inside the scope's directory, creating the directory on
	/// first use.
	/// </summary>
	/// <param name="fileName">The file name to place inside the directory.</param>
	/// <returns>The absolute path of the file inside the scope's directory.</returns>
	public string CreatePath(string fileName)
	{
		_directory ??= CreateDirectory();
		return Path.Combine(_directory, fileName);
	}

	/// <summary>
	/// Deletes the scope's directory when one was created.
	/// </summary>
	/// <remarks>Cleanup is best-effort: a locked directory does not fail the test.</remarks>
	public void Dispose()
	{
		if (_directory is null)
			return;

		try
		{
			Directory.Delete(_directory, recursive: true);
		}
		catch (IOException)
		{
			// Best-effort cleanup: a locked directory can be left behind without failing the test.
		}
		catch (UnauthorizedAccessException)
		{
			// Best-effort cleanup: an access failure can be left behind without failing the test.
		}
	}

	private string CreateDirectory()
	{
		string directory = Path.Combine(
			Path.GetTempPath(),
			name + "-" + Guid.NewGuid().ToString("N"));

		Directory.CreateDirectory(directory);
		return directory;
	}
}

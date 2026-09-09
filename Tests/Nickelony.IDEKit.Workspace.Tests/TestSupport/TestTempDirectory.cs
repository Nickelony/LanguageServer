namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>
/// Creates a unique directory under the system temporary path and deletes it recursively on
/// <see cref="Dispose"/>.
/// </summary>
internal sealed class TestTempDirectory : IDisposable
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TestTempDirectory"/> class.
	/// </summary>
	public TestTempDirectory()
	{
		Path = System.IO.Path.Combine(
			System.IO.Path.GetTempPath(),
			$"idekit-workspace-{Guid.NewGuid():N}");
		Directory.CreateDirectory(Path);
	}

	/// <summary>
	/// Gets the created directory path.
	/// </summary>
	public string Path { get; }

	/// <inheritdoc />
	public void Dispose()
	{
		// Cleanup is best effort: a locked or read-only leftover must not mask the test failure the
		// fixture is unwinding from. A later run uses a fresh unique directory instead.
		try
		{
			if (Directory.Exists(Path))
				Directory.Delete(Path, recursive: true);
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}
}

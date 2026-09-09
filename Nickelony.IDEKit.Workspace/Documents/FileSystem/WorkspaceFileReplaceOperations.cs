namespace Nickelony.IDEKit.Workspace.Documents.FileSystem;

/// <summary>
/// Supplies the raw file operations used by <see cref="LocalWorkspaceFileSystem.ReplaceFileAsync"/>:
/// <c>Replace</c> replaces an existing destination file with the temporary file, and <c>Move</c>
/// moves the temporary file to a missing destination path.
/// </summary>
/// <remarks>
/// The default operations use <see cref="File.Replace(string, string, string)"/> for an existing
/// destination and <see cref="File.Move(string, string)"/> for a missing one. The type is internal:
/// the test suite substitutes the operations to create the destination between the stamp check and
/// the replacement, which makes the conflict-classification race deterministic instead of
/// inspection-only.
/// </remarks>
internal sealed record WorkspaceFileReplaceOperations(
	Action<string, string> Replace,
	Action<string, string> Move)
{
	/// <summary>
	/// Gets the operations that act on the local file system.
	/// </summary>
	public static WorkspaceFileReplaceOperations Default { get; } = new(
		static (sourcePath, targetPath) => File.Replace(sourcePath, targetPath, null),
		// overwrite: false keeps a concurrently created destination intact on every platform
		// instead of replacing it silently.
		static (sourcePath, targetPath) => File.Move(sourcePath, targetPath, overwrite: false));
}

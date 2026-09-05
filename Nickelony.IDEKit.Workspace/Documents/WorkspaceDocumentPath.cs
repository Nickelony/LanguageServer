namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Normalizes, compares, and rebases workspace document paths.
/// </summary>
/// <remarks>
/// The normalized full path is the store's document id. Path comparison and dictionary equality are
/// case-insensitive on Windows and ordinal on other platforms.
/// </remarks>
internal static class WorkspaceDocumentPath
{
	public static string GetDirectoryPrefix(string directoryId)
		=> directoryId.EndsWith(Path.DirectorySeparatorChar)
			? directoryId
			: directoryId + Path.DirectorySeparatorChar;

	public static StringComparison GetPathComparison()
		=> OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

	public static string RebasePath(
		string documentId,
		string sourceDirectoryId,
		string destinationDirectoryId)
		=> Path.Combine(destinationDirectoryId, Path.GetRelativePath(sourceDirectoryId, documentId));

	public static bool TryNormalizePath(string? filePath, out string documentId)
	{
		documentId = string.Empty;

		if (string.IsNullOrWhiteSpace(filePath))
			return false;

		try
		{
			documentId = Path.GetFullPath(filePath)
				.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			return true;
		}
		catch (ArgumentException)
		{
			return false;
		}
		catch (IOException)
		{
			return false;
		}
		catch (NotSupportedException)
		{
			return false;
		}
	}

	public static StringComparer GetPathComparer()
		=> OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}

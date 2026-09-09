using Nickelony.IDEKit.Core.Pathing;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Normalizes and rebases workspace document paths.
/// </summary>
/// <remarks>
/// The normalized full path is the store's document id. A trailing directory separator is trimmed so
/// one location cannot have two spellings, and identity comparison is selected separately through
/// <see cref="LocalPathComparisonPolicy"/>.
/// </remarks>
internal static class WorkspaceDocumentPath
{
	public static string GetDirectoryPrefix(string directoryId)
		=> directoryId.EndsWith(Path.DirectorySeparatorChar)
			? directoryId
			: directoryId + Path.DirectorySeparatorChar;

	// Returns the part of a descendant document id below a directory id. The validated prefix is
	// sliced with the configured comparison instead of using Path.GetRelativePath, which compares
	// ordinally on Unix; a case-divergent descendant id on a case-insensitive volume would otherwise
	// produce a '..'-laden part and a non-normalized destination id.
	public static string GetRelativePathUnderDirectory(
		string documentId,
		string directoryId,
		StringComparison comparison)
	{
		string prefix = GetDirectoryPrefix(directoryId);
		return documentId.StartsWith(prefix, comparison)
			? documentId[prefix.Length..]
			: Path.GetRelativePath(directoryId, documentId);
	}

	public static string RebasePath(
		string documentId,
		string sourceDirectoryId,
		string destinationDirectoryId,
		StringComparison comparison)
		=> Path.Combine(
			destinationDirectoryId,
			GetRelativePathUnderDirectory(documentId, sourceDirectoryId, comparison));

	/// <summary>
	/// Tries to normalize a path into a document id. Returns <see langword="false"/> for a null,
	/// blank, relative, or unnormalizable path; otherwise the result is the fully qualified path
	/// with trailing directory separators trimmed.
	/// </summary>
	public static bool TryNormalizePath(string? filePath, out string documentId)
	{
		documentId = string.Empty;

		if (string.IsNullOrWhiteSpace(filePath))
			return false;

		// Document ids are identity paths: resolving a relative path against the process current
		// directory would bind document identity to ambient process state that a host or a headless
		// server cannot control. A relative path is invalid input.
		if (!Path.IsPathFullyQualified(filePath))
			return false;

		try
		{
			// Canonicalize trailing separators so one location has one identity: Path.GetFullPath keeps
			// a trailing separator, which would otherwise make "dir" and "dir\" two identities for the
			// same location. Root paths, including volume roots and UNC share roots, are returned
			// unchanged by the trim.
			documentId = Path.TrimEndingDirectorySeparator(Path.GetFullPath(filePath));
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
}

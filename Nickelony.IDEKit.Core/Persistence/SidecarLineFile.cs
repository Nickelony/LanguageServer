namespace Nickelony.IDEKit.Core.Persistence;

/// <summary>
/// Persists ordered line numbers to a sidecar file next to a document.
/// </summary>
/// <remarks>
/// This is the un-opinionated file kernel for line-marker persistence (bookmarks, breakpoints,
/// folded regions). The sidecar file name, the serialization format, and the delete-on-empty
/// behavior are conventions; callers that need different conventions compose this kernel with
/// their own path provider and codec rather than forking it.
/// </remarks>
public static class SidecarLineFile
{
	/// <summary>
	/// Saves the supplied line numbers to a sidecar file. When the set is empty, an existing
	/// sidecar file is deleted.
	/// </summary>
	/// <param name="filePath">The path of the document the sidecar belongs to.</param>
	/// <param name="lineNumbers">The one-based line numbers to persist.</param>
	/// <param name="sidecarExtension">The sidecar file extension, or <see langword="null"/> to use the default <c>.sidecar</c>.</param>
	/// <returns>
	/// <see langword="true"/> when the sidecar file was written or deleted, or when there was nothing to persist;
	/// <see langword="false"/> when the file path is blank or the file operation fails with an I/O or authorization error.
	/// </returns>
	public static bool Save(string filePath, IEnumerable<int> lineNumbers, string? sidecarExtension = null)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(lineNumbers);

		if (string.IsNullOrWhiteSpace(filePath))
			return false;

		List<int> orderedLines = lineNumbers.Distinct().OrderBy(line => line).ToList();
		string sidecarPath = GetSidecarPath(filePath, sidecarExtension);

		try
		{
			if (orderedLines.Count > 0)
			{
				File.WriteAllLines(sidecarPath, orderedLines.Select(line => line.ToString(System.Globalization.CultureInfo.InvariantCulture)));
			}
			else if (File.Exists(sidecarPath))
			{
				File.Delete(sidecarPath);
			}

			return true;
		}
		catch (IOException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}

	/// <summary>
	/// Restores the line numbers persisted in the sidecar file for the supplied document path.
	/// Unparseable entries are skipped; a missing or unreadable sidecar yields an empty list.
	/// </summary>
	/// <param name="filePath">The path of the document the sidecar belongs to.</param>
	/// <param name="sidecarExtension">The sidecar file extension, or <see langword="null"/> to use the default <c>.sidecar</c>.</param>
	/// <returns>The restored one-based line numbers.</returns>
	public static IReadOnlyList<int> Restore(string filePath, string? sidecarExtension = null)
	{
		ArgumentNullException.ThrowIfNull(filePath);

		var lineNumbers = new List<int>();

		if (string.IsNullOrWhiteSpace(filePath))
			return lineNumbers;

		string sidecarPath = GetSidecarPath(filePath, sidecarExtension);

		if (!File.Exists(sidecarPath))
			return lineNumbers;

		try
		{
			foreach (string line in File.ReadAllLines(sidecarPath))
			{
				if (int.TryParse(line, out int lineNumber) && lineNumber >= 1)
					lineNumbers.Add(lineNumber);
			}
		}
		catch (IOException)
		{
			lineNumbers.Clear();
		}
		catch (UnauthorizedAccessException)
		{
			lineNumbers.Clear();
		}

		return lineNumbers;
	}

	/// <summary>
	/// Gets the sidecar file path for the supplied document path.
	/// </summary>
	/// <param name="filePath">The path of the document the sidecar belongs to.</param>
	/// <param name="sidecarExtension">The sidecar file extension, or <see langword="null"/> to use the default <c>.sidecar</c>.</param>
	/// <returns>The sidecar file path.</returns>
	public static string GetSidecarPath(string filePath, string sidecarExtension = ".sidecar")
	{
		ArgumentNullException.ThrowIfNull(filePath);
		return filePath + sidecarExtension;
	}
}

using System.Buffers;
using System.Globalization;
using System.Text;

namespace Nickelony.IDEKit.Core.Persistence;

/// <summary>
/// Persists ordered line numbers to a sidecar file next to a document.
/// </summary>
/// <remarks>
/// <para>
/// This is the file kernel for line-marker persistence. Callers own what the line numbers mean
/// (for example bookmarks or breakpoints), and the sidecar extension is a required caller input:
/// entries are saved one per line as one-based line numbers (invariant culture, a single <c>\n</c>
/// line terminator), and an empty set deletes the sidecar instead of writing an empty file. Save
/// reports a blank document path as a failed operation and <see cref="TryRestore"/> as a failure
/// too, while <see cref="GetSidecarPath"/> rejects it with an exception because it cannot build a
/// path. A path that ends in a directory separator names a directory rather than a file, so it is
/// rejected the same way instead of silently placing the sidecar inside that directory.
/// </para>
/// <para>
/// Files are written as UTF-8 without a byte-order mark, and the content is byte-identical on every
/// platform; reads honor a byte-order mark and otherwise assume UTF-8. All I/O is synchronous and
/// blocking, and the methods take no cancellation token.
/// </para>
/// <para>
/// A successful save replaces the sidecar through a unique temporary file in the same directory, so
/// a failed write cannot truncate the previous contents; the temporary file is deleted when the
/// replacement fails. The replacement does not flush the file to disk before the move, and it does
/// not preserve the previous file's access control list or attributes.
/// </para>
/// <para>
/// No cross-process coordination is provided: two processes that save the same sidecar are
/// last-writer-wins, a reader can observe either the old or the new contents, and a save that a
/// destination another process holds open without sharing rejects is reported as a failed
/// operation.
/// </para>
/// </remarks>
public static class SidecarLineFile
{
	/// <summary>
	/// The characters rejected in a sidecar extension on every platform, so validation is not
	/// platform-dependent. Control characters are rejected in addition to this set.
	/// </summary>
	private static readonly SearchValues<char> s_reservedExtensionCharacters = SearchValues.Create(":*?\"<>|\\/");

	/// <summary>
	/// Saves the supplied line numbers to a sidecar file.
	/// </summary>
	/// <remarks>
	/// <para>
	/// An empty set deletes the sidecar instead of writing an empty file; deleting a missing sidecar
	/// is not an error, while a directory at the sidecar path is reported as a failed operation. Line
	/// numbers below 1 are ignored, so a set that filters to nothing also deletes the sidecar and
	/// reports success - a caller bug that passes a degenerate set therefore removes a healthy
	/// sidecar.
	/// </para>
	/// <para>
	/// Duplicate numbers are written once, in ascending order, so the file content depends on the set
	/// of numbers, not on the caller's sequence.
	/// </para>
	/// <para>
	/// A path that the file system rejects is reported as a failed operation instead of throwing.
	/// A path that ends in a directory separator is reported as a failed operation as well: it names
	/// a directory, so a sidecar for it would be misplaced. The extension is validated before the
	/// path is inspected, so an invalid extension throws even when the path would fail the save.
	/// </para>
	/// </remarks>
	/// <param name="filePath">The path of the document the sidecar belongs to.</param>
	/// <param name="lineNumbers">The one-based line numbers to persist.</param>
	/// <param name="sidecarExtension">
	/// The sidecar file extension, with or without a leading dot (for example <c>".bkmrk"</c>).
	/// </param>
	/// <returns>
	/// Either:
	/// <list type="bullet">
	/// <item><see langword="true"/> when the sidecar file was written or deleted, or when there was nothing to persist;</item>
	/// <item><see langword="false"/> when the file path is blank or unusable, or the file operation fails with an I/O, authorization, or path error.</item>
	/// </list>
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/>, <paramref name="lineNumbers"/>, or <paramref name="sidecarExtension"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="sidecarExtension"/> does not form a usable file extension.</exception>
	public static bool Save(string filePath, IEnumerable<int> lineNumbers, string sidecarExtension)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(lineNumbers);
		ValidateExtension(sidecarExtension);

		if (string.IsNullOrWhiteSpace(filePath) || Path.EndsInDirectorySeparator(filePath))
			return false;

		List<int> orderedLines = [.. lineNumbers.Where(lineNumber => lineNumber >= 1).Distinct().OrderBy(lineNumber => lineNumber)];
		string sidecarPath = GetSidecarPath(filePath, sidecarExtension);

		if (orderedLines.Count > 0)
			return TryReplaceSidecar(sidecarPath, orderedLines);

		try
		{
			// Deleting a missing sidecar is a no-op, so no existence check is needed; a directory at
			// the sidecar path fails and is reported as a failed operation, matching the write path.
			File.Delete(sidecarPath);
			return true;
		}
		catch (Exception exception) when (IsRecoverableFileError(exception))
		{
			return false;
		}
	}

	/// <summary>
	/// Replaces the sidecar through a temporary file so a failed write cannot truncate the previous
	/// contents. The temporary file is removed when the replacement fails.
	/// </summary>
	private static bool TryReplaceSidecar(string sidecarPath, IReadOnlyList<int> orderedLines)
	{
		// A unique temporary name keeps concurrent saves from sharing a scratch file.
		string temporaryPath = sidecarPath + "." + Path.GetRandomFileName() + ".tmp";

		try
		{
			File.WriteAllText(temporaryPath, BuildContent(orderedLines));
			File.Move(temporaryPath, sidecarPath, overwrite: true);

			return true;
		}
		catch (Exception exception) when (IsRecoverableFileError(exception))
		{
			TryDeleteTemporaryFile(temporaryPath);
			return false;
		}
	}

	/// <summary>
	/// Builds the file content: one invariant-culture line number per line, each terminated by
	/// <c>\n</c>, so the file is byte-identical on every platform.
	/// </summary>
	private static string BuildContent(IReadOnlyList<int> orderedLines)
	{
		var builder = new StringBuilder(orderedLines.Count * 8);

		foreach (int lineNumber in orderedLines)
			builder.Append(lineNumber.ToString(CultureInfo.InvariantCulture)).Append('\n');

		return builder.ToString();
	}

	private static void TryDeleteTemporaryFile(string temporaryPath)
	{
		try
		{
			File.Delete(temporaryPath);
		}
		catch (Exception exception) when (IsRecoverableFileError(exception))
		{ }
	}

	/// <summary>
	/// Determines whether a file operation failed because of the path, the file system, or missing
	/// permissions, which callers report as a failed operation instead of an exception.
	/// </summary>
	private static bool IsRecoverableFileError(Exception exception)
		=> exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

	/// <summary>
	/// Restores the line numbers persisted in the sidecar file for the supplied document path.
	/// </summary>
	/// <remarks>
	/// Entries that are not positive integers are skipped. The returned line numbers are distinct and
	/// ordered ascending, so a hand-edited sidecar restores the same shape that <see cref="Save"/>
	/// writes; parsing accepts invariant-culture digits and any line terminator. A missing, unreadable,
	/// unusable, or directory-naming sidecar path yields an empty list.
	/// </remarks>
	/// <param name="filePath">The path of the document the sidecar belongs to.</param>
	/// <param name="sidecarExtension">
	/// The sidecar file extension, with or without a leading dot (for example <c>".bkmrk"</c>).
	/// </param>
	/// <returns>The restored one-based line numbers.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="sidecarExtension"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="sidecarExtension"/> does not form a usable file extension.</exception>
	public static IReadOnlyList<int> Restore(string filePath, string sidecarExtension)
		=> TryRestore(filePath, sidecarExtension, out IReadOnlyList<int> lineNumbers) ? lineNumbers : [];

	/// <summary>
	/// Attempts to restore the line numbers persisted in the sidecar file for the supplied document
	/// path and reports a storage failure separately from an empty sidecar.
	/// </summary>
	/// <remarks>
	/// Entries that are not positive integers are skipped. The returned line numbers are distinct and
	/// ordered ascending, so a hand-edited sidecar restores the same shape that <see cref="Save"/>
	/// writes; parsing accepts invariant-culture digits and any line terminator. A missing sidecar
	/// reports success with an empty list; a blank or unusable file path, a path occupied by a
	/// directory, or a file operation that fails with an I/O, authorization, or path error, reports
	/// <see langword="false"/>, so the caller can tell a storage failure apart from "nothing was
	/// saved".
	/// </remarks>
	/// <param name="filePath">The path of the document the sidecar belongs to.</param>
	/// <param name="sidecarExtension">
	/// The sidecar file extension, with or without a leading dot (for example <c>".bkmrk"</c>).
	/// </param>
	/// <param name="lineNumbers">Receives the restored one-based line numbers; empty when none were stored.</param>
	/// <returns>
	/// <see langword="true"/> when the sidecar was read or does not exist as a file;
	/// <see langword="false"/> when the file path is blank or unusable, a directory occupies the
	/// sidecar path, or the file operation failed.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="sidecarExtension"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="sidecarExtension"/> does not form a usable file extension.</exception>
	public static bool TryRestore(string filePath, string sidecarExtension, out IReadOnlyList<int> lineNumbers)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ValidateExtension(sidecarExtension);

		lineNumbers = [];

		if (string.IsNullOrWhiteSpace(filePath) || Path.EndsInDirectorySeparator(filePath))
			return false;

		string sidecarPath = GetSidecarPath(filePath, sidecarExtension);

		if (!File.Exists(sidecarPath))
			return !Directory.Exists(sidecarPath);

		var restoredLines = new SortedSet<int>();

		try
		{
			foreach (string line in File.ReadAllLines(sidecarPath))
			{
				if (int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out int lineNumber) && lineNumber >= 1)
					restoredLines.Add(lineNumber);
			}
		}
		catch (Exception exception) when (IsRecoverableFileError(exception))
		{
			// File.ReadAllLines materialized the file before the loop, so the set is still empty here;
			// an unreadable sidecar is reported as a failure with no line numbers.
			return false;
		}

		lineNumbers = [.. restoredLines];
		return true;
	}

	/// <summary>
	/// Gets the sidecar file path for the supplied document path.
	/// </summary>
	/// <remarks>
	/// The extension is validated before the path is inspected, matching <see cref="Save"/> and
	/// <see cref="Restore"/>: when both arguments are invalid, the extension determines the reported
	/// exception.
	/// </remarks>
	/// <param name="filePath">The path of the document the sidecar belongs to.</param>
	/// <param name="sidecarExtension">
	/// The sidecar file extension, with or without a leading dot (for example <c>".bkmrk"</c>).
	/// </param>
	/// <returns>The sidecar file path.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="sidecarExtension"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="filePath"/> is blank or ends in a directory separator (so it names a
	/// directory), or <paramref name="sidecarExtension"/> does not form a usable file extension.
	/// </exception>
	public static string GetSidecarPath(string filePath, string sidecarExtension)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ValidateExtension(sidecarExtension);

		if (string.IsNullOrWhiteSpace(filePath))
			throw new ArgumentException("The document path must not be blank.", nameof(filePath));

		if (Path.EndsInDirectorySeparator(filePath))
			throw new ArgumentException("The document path must name a file, not a directory (it ends in a directory separator).", nameof(filePath));

		string extension = sidecarExtension.Trim();
		return filePath + (extension[0] == '.' ? extension : "." + extension);
	}

	/// <summary>
	/// Validates a sidecar file extension without building a path.
	/// </summary>
	/// <remarks>
	/// Use this to reject an unusable extension at a configuration boundary, so the failure surfaces
	/// when the configuration is read instead of when a save or restore builds its sidecar path. The
	/// rule is platform-independent: the same extensions are accepted on every operating system.
	/// </remarks>
	/// <param name="sidecarExtension">The extension to validate, with or without a leading dot.</param>
	/// <exception cref="ArgumentNullException"><paramref name="sidecarExtension"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="sidecarExtension"/> is blank or does not form a usable file extension.
	/// </exception>
	public static void ValidateExtension(string sidecarExtension)
	{
		ArgumentNullException.ThrowIfNull(sidecarExtension);

		string extension = sidecarExtension.Trim();

		if (!IsUsableExtension(extension))
		{
			throw new ArgumentException(
				"The sidecar extension must be a file-name extension such as \".bkmrk\": it must not be blank, must not end with a dot, and must not contain path separators, control characters, or the characters ':', '*', '?', '\"', '<', '>', or '|'.",
				nameof(sidecarExtension));
		}
	}

	/// <summary>
	/// Determines whether a trimmed sidecar extension is usable: it must not resolve to the document
	/// itself or to a different directory. A trailing dot is rejected because some file systems strip
	/// it from a path's final component, so an extension of <c>bkmrk.</c> would resolve to the same
	/// file as <c>bkmrk</c> (and an extension of <c>.</c> to the document itself).
	/// </summary>
	/// <param name="extension">The trimmed extension to validate.</param>
	/// <returns><see langword="true"/> when the extension can be appended to a document path.</returns>
	private static bool IsUsableExtension(string extension)
	{
		if (extension.TrimStart('.').Length == 0 || extension.EndsWith('.'))
			return false;

		if (extension.AsSpan().IndexOfAny(s_reservedExtensionCharacters) >= 0)
			return false;

		foreach (char character in extension)
		{
			if (char.IsControl(character))
				return false;
		}

		return true;
	}
}

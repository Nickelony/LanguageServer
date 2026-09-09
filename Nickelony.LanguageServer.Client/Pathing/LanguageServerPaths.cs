using Nickelony.IDEKit.Core.Pathing;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Normalizes local paths and file URIs so the language server and host editor use a consistent document identity.
/// </summary>
public static class LanguageServerPaths
{
	private const string UseCaseSensitiveLocalPathsSwitch = "Nickelony.LanguageServer.Client.UseCaseSensitiveLocalPaths";
	private const string UseCaseInsensitiveLocalPathsSwitch = "Nickelony.LanguageServer.Client.UseCaseInsensitiveLocalPaths";

	private static readonly LocalPathComparisonPolicy s_localPathComparison = DetermineLocalPathComparison();

	/// <summary>
	/// Gets a value indicating whether normalized local-path identity should treat character casing as significant.
	/// The default follows the shared local-path comparison policy: case-insensitive on Windows and macOS,
	/// and ordinal on other platforms. Hosts may override the default through the
	/// <c>Nickelony.LanguageServer.Client.UseCaseSensitiveLocalPaths</c> and
	/// <c>Nickelony.LanguageServer.Client.UseCaseInsensitiveLocalPaths</c> AppContext switches.
	/// Configure a switch before this type is first accessed.
	/// </summary>
	public static bool UsesCaseSensitiveLocalPaths { get; } = !s_localPathComparison.IgnoreCase;

	/// <summary>
	/// Gets the comparer used for normalized local-path dictionary keys.
	/// </summary>
	public static StringComparer LocalPathComparer { get; } = s_localPathComparison.Comparer;

	/// <summary>
	/// Gets the string comparison used for normalized local-path equality checks.
	/// </summary>
	public static StringComparison LocalPathComparison { get; } = s_localPathComparison.Comparison;

	/// <summary>
	/// Converts a local file path into a normalized file URI for language-server requests.
	/// </summary>
	/// <param name="filePath">The local file path to convert.</param>
	/// <returns>The absolute file URI.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="filePath"/> is empty or whitespace-only, or the path is invalid on the current platform.</exception>
	/// <exception cref="PathTooLongException">The resolved absolute path exceeds the platform-specific maximum length.</exception>
	/// <exception cref="NotSupportedException">The path contains a colon that is not part of a volume identifier.</exception>
	/// <exception cref="UriFormatException">The resolved path cannot be represented as a file URI.</exception>
	/// <remarks>
	/// <para>
	/// The file URI is built textually from an explicitly escaped path: the <see cref="Uri"/> DOS-path parser
	/// decodes a literal percent-hex sequence in a file name (<c>a%41b.txt</c> would become <c>aAb.txt</c>), so the
	/// percent, hash, and question-mark characters are escaped before the URI is parsed and path-to-URI-to-path
	/// round trips stay lossless.
	/// </para>
	/// <para>
	/// The URI shape is chosen from the normalized path form: a UNC path keeps its two leading separators and
	/// becomes the URI authority (<c>file://server/share/...</c>), while drive-letter and rooted POSIX paths are
	/// authority-less and carry exactly three slashes (<c>file:///C:/...</c>, <c>file:///home/...</c>). Concatenating
	/// a rooted POSIX path verbatim would produce four slashes, which the runtime parses as a UNC authority and
	/// would corrupt the document identity on non-Windows hosts.
	/// </para>
	/// <para>Callers that need non-throwing normalization can use <see cref="TryNormalizeLocalPath"/> first.</para>
	/// </remarks>
	public static string CreateFileUri(string filePath)
	{
		ArgumentNullException.ThrowIfNull(filePath);

		return BuildFileUri(NormalizeLocalPath(filePath));
	}

	/// <summary>
	/// Builds the absolute file URI for one normalized local path.
	/// </summary>
	/// <param name="normalizedPath">The normalized absolute local path.</param>
	/// <returns>The absolute file URI.</returns>
	/// <remarks>
	/// Internal so the platform-shaped URI construction can be tested with POSIX and UNC inputs on every platform;
	/// the production entry point is <see cref="CreateFileUri"/>.
	/// </remarks>
	internal static string BuildFileUri(string normalizedPath)
	{
		string pathText = normalizedPath;

		// Extended-length prefixes do not form a usable URI authority; strip them so the remaining path
		// selects the drive-letter or UNC shape.
		if (pathText.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
			pathText = @"\\" + pathText[8..];
		else if (pathText.StartsWith(@"\\?\", StringComparison.Ordinal))
			pathText = pathText[4..];

		string escapedPath = pathText
			.Replace('\\', '/')
			.Replace("%", "%25", StringComparison.Ordinal)
			.Replace("#", "%23", StringComparison.Ordinal)
			.Replace("?", "%3F", StringComparison.Ordinal);

		string uriText = escapedPath.StartsWith("//", StringComparison.Ordinal)
			? "file:" + escapedPath
			: "file:///" + escapedPath.TrimStart('/');

		return new Uri(uriText).AbsoluteUri;
	}

	/// <summary>
	/// Reports whether two normalized local paths identify the same logical path on the current host.
	/// </summary>
	/// <param name="left">The first normalized local path.</param>
	/// <param name="right">The second normalized local path.</param>
	/// <returns><see langword="true"/> when both paths should be treated as identical; otherwise, <see langword="false"/>.</returns>
	public static bool AreLocalPathsEqual(string? left, string? right)
		=> string.Equals(left, right, LocalPathComparison);

	/// <summary>
	/// Normalizes a local path into the absolute form used by the language server.
	/// </summary>
	/// <param name="filePath">The path to normalize.</param>
	/// <returns>The normalized absolute path.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="filePath"/> is empty or whitespace-only, or the path is invalid on the current platform.</exception>
	/// <exception cref="PathTooLongException">The resolved absolute path exceeds the platform-specific maximum length.</exception>
	/// <exception cref="NotSupportedException">The path contains a colon that is not part of a volume identifier.</exception>
	/// <remarks>
	/// Relative paths are anchored to <see cref="Environment.CurrentDirectory"/>. Callers that need non-throwing
	/// normalization can use <see cref="TryNormalizeLocalPath"/>. The returned path preserves the input casing, so it
	/// is a display and protocol value rather than a case-insensitive identity key: compare normalized paths with
	/// <see cref="AreLocalPathsEqual"/>, <see cref="LocalPathComparer"/>, or <see cref="LocalPathComparison"/> when
	/// path identity is required. The path is also not Unicode-normalized: on macOS the same file reached through
	/// different Unicode spellings (NFC and NFD) becomes two distinct paths, so hosts that need canonical identity
	/// must normalize the text before calling.
	/// </remarks>
	public static string NormalizeLocalPath(string filePath)
	{
		ArgumentNullException.ThrowIfNull(filePath);

		if (string.IsNullOrWhiteSpace(filePath))
			throw new ArgumentException("File path must not be empty.", nameof(filePath));

		string sanitizedFilePath = Path.DirectorySeparatorChar == Path.AltDirectorySeparatorChar
			? filePath
			: filePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

		return Path.TrimEndingDirectorySeparator(Path.GetFullPath(sanitizedFilePath));
	}

	/// <summary>
	/// Normalizes a file URI into the absolute local-path form used by the language server client.
	/// </summary>
	/// <param name="uri">The file URI to normalize.</param>
	/// <returns>The normalized absolute local path.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The URI does not resolve to a valid local path on the current platform.</exception>
	/// <exception cref="PathTooLongException">The resolved absolute path exceeds the platform-specific maximum length.</exception>
	/// <remarks>Callers that need non-throwing normalization can use <see cref="TryGetLocalPath"/> for URI input.</remarks>
	public static string NormalizeLocalPath(Uri uri)
	{
		ArgumentNullException.ThrowIfNull(uri);

		if (!uri.IsAbsoluteUri || !uri.IsFile)
			throw new ArgumentException("The URI must be an absolute file URI.", nameof(uri));

		return NormalizeLocalPath(uri.LocalPath);
	}

	/// <summary>
	/// Validates and normalizes a workspace root directory list.
	/// </summary>
	/// <param name="workspaceRootDirectoryPaths">The workspace root directories to normalize.</param>
	/// <returns>The normalized root directory paths in caller order.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="workspaceRootDirectoryPaths"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="workspaceRootDirectoryPaths"/> contains a <see langword="null"/>, empty, whitespace-only, or
	/// duplicate entry. Duplicate detection follows the configured local-path identity
	/// (<see cref="LocalPathComparer"/>).
	/// </exception>
	/// <remarks>
	/// An empty list is valid and models a folderless session without a workspace root. Relative entries are anchored
	/// to <see cref="Environment.CurrentDirectory"/>; callers should pass absolute local directory paths. The
	/// returned paths preserve the caller's casing, so compare them with <see cref="AreLocalPathsEqual"/> when path
	/// identity is required.
	/// </remarks>
	public static string[] NormalizeWorkspaceRoots(IReadOnlyList<string> workspaceRootDirectoryPaths)
	{
		ArgumentNullException.ThrowIfNull(workspaceRootDirectoryPaths);

		string[] normalizedWorkspaceRootDirectoryPaths = new string[workspaceRootDirectoryPaths.Count];
		var seenWorkspaceRootDirectoryPaths = new HashSet<string>(LocalPathComparer);

		for (int i = 0; i < workspaceRootDirectoryPaths.Count; i++)
		{
			string workspaceRootDirectoryPath = workspaceRootDirectoryPaths[i];

			if (string.IsNullOrWhiteSpace(workspaceRootDirectoryPath))
				throw new ArgumentException("Workspace root directory paths must not be null, empty, or whitespace-only.", nameof(workspaceRootDirectoryPaths));

			string normalizedWorkspaceRootDirectoryPath = NormalizeLocalPath(workspaceRootDirectoryPath);

			if (!seenWorkspaceRootDirectoryPaths.Add(normalizedWorkspaceRootDirectoryPath))
				throw new ArgumentException($"The workspace root directory list contains the duplicate path '{normalizedWorkspaceRootDirectoryPath}'.", nameof(workspaceRootDirectoryPaths));

			normalizedWorkspaceRootDirectoryPaths[i] = normalizedWorkspaceRootDirectoryPath;
		}

		return normalizedWorkspaceRootDirectoryPaths;
	}

	/// <summary>
	/// Tries to normalize a local path without throwing for invalid input. A <see langword="null"/> path yields
	/// <see langword="false"/> like any other invalid input.
	/// </summary>
	/// <param name="filePath">The path to normalize.</param>
	/// <param name="normalizedFilePath">The normalized absolute path when successful.</param>
	/// <returns><see langword="true"/> when normalization succeeded; otherwise, <see langword="false"/>.</returns>
	public static bool TryNormalizeLocalPath(string? filePath, out string normalizedFilePath)
	{
		normalizedFilePath = string.Empty;

		if (string.IsNullOrWhiteSpace(filePath))
			return false;

		try
		{
			normalizedFilePath = NormalizeLocalPath(filePath);
			return true;
		}
		catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or UriFormatException)
		{
			return false;
		}
	}

	/// <summary>
	/// Tries to extract and normalize a local file path from a file URI.
	/// </summary>
	/// <param name="uriText">The file URI text.</param>
	/// <param name="filePath">The normalized local file path when successful.</param>
	/// <returns><see langword="true"/> when a local file path was resolved; otherwise, <see langword="false"/>.</returns>
	public static bool TryGetLocalPath(string? uriText, out string filePath)
	{
		filePath = string.Empty;

		if (string.IsNullOrWhiteSpace(uriText)
			|| !Uri.TryCreate(uriText, UriKind.Absolute, out Uri? uri)
			|| uri?.IsFile != true)
		{
			return false;
		}

		try
		{
			filePath = NormalizeLocalPath(uri);
			return true;
		}
		catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or UriFormatException)
		{
			return false;
		}
	}

	private static LocalPathComparisonPolicy DetermineLocalPathComparison()
	{
		if (AppContext.TryGetSwitch(UseCaseSensitiveLocalPathsSwitch, out bool useCaseSensitiveLocalPaths)
			&& useCaseSensitiveLocalPaths)
		{
			return LocalPathComparisonPolicy.CaseSensitive;
		}

		if (AppContext.TryGetSwitch(UseCaseInsensitiveLocalPathsSwitch, out bool useCaseInsensitiveLocalPaths)
			&& useCaseInsensitiveLocalPaths)
		{
			return LocalPathComparisonPolicy.CaseInsensitive;
		}

		return LocalPathComparisonPolicy.ForCurrentPlatform;
	}
}

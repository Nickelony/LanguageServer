namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class LanguageServerPathsTests
{
	[TestMethod]
	public void CreateFileUri_AndTryGetLocalPath_RoundTripNormalizedPath()
	{
		string expectedPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Path Helper", "test file.ext"));
		string rawPath = expectedPath.Replace('\\', '/');

		string uri = LanguageServerPaths.CreateFileUri(rawPath);

		Assert.IsTrue(LanguageServerPaths.TryGetLocalPath(uri, out string filePath));
		Assert.AreEqual(expectedPath, filePath);
	}

	[TestMethod]
	public void CreateFileUri_WithPercentSequenceInFileName_RoundTripsTheLiteralName()
	{
		string expectedPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Path Helper", "a%41b.txt"));

		string uri = LanguageServerPaths.CreateFileUri(expectedPath);

		// The literal percent sequence must stay an escaped literal instead of decoding back to "aAb.txt".
		Assert.IsTrue(uri.Contains("a%2541b.txt", StringComparison.Ordinal));
		Assert.IsTrue(LanguageServerPaths.TryGetLocalPath(uri, out string filePath));
		Assert.AreEqual(expectedPath, filePath);
	}

	[TestMethod]
	public void NormalizeLocalPath_TrimsTrailingDirectorySeparatorForNonRootPath()
	{
		string rawPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Path Helper", "Folder")) + Path.DirectorySeparatorChar;
		string normalizedPath = LanguageServerPaths.NormalizeLocalPath(rawPath);

		Assert.AreEqual(Path.TrimEndingDirectorySeparator(Path.GetFullPath(rawPath)), normalizedPath);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows | OperatingSystems.OSX)]
	public void AreLocalPathsEqual_OnCaseInsensitivePlatform_TreatsCasingVariantsAsEqual()
	{
		string directoryPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Path Helper"));
		string lowerCasePath = LanguageServerPaths.NormalizeLocalPath(Path.Combine(directoryPath, "case.ext"));
		string upperCasePath = LanguageServerPaths.NormalizeLocalPath(Path.Combine(directoryPath, "CASE.ext"));

		Assert.IsFalse(LanguageServerPaths.UsesCaseSensitiveLocalPaths);
		Assert.IsTrue(LanguageServerPaths.AreLocalPathsEqual(lowerCasePath, upperCasePath));
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Linux)]
	public void AreLocalPathsEqual_OnCaseSensitivePlatform_TreatsCasingVariantsAsDistinct()
	{
		string directoryPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Path Helper"));
		string lowerCasePath = LanguageServerPaths.NormalizeLocalPath(Path.Combine(directoryPath, "case.ext"));
		string upperCasePath = LanguageServerPaths.NormalizeLocalPath(Path.Combine(directoryPath, "CASE.ext"));

		Assert.IsTrue(LanguageServerPaths.UsesCaseSensitiveLocalPaths);
		Assert.IsFalse(LanguageServerPaths.AreLocalPathsEqual(lowerCasePath, upperCasePath));
	}

	[TestMethod]
	public void NormalizeLocalPath_Uri_HandlesUncPath()
	{
		if (!OperatingSystem.IsWindows())
			Assert.Inconclusive("The test pins Windows UNC path normalization.");

		Uri uri = new("file://server/share/folder/test.ext");
		string expectedPath = Path.GetFullPath(@"\\server\share\folder\test.ext");

		string normalizedPath = LanguageServerPaths.NormalizeLocalPath(uri);

		Assert.AreEqual(expectedPath, normalizedPath);
	}

	[TestMethod]
	public void TryGetLocalPath_ReturnsFalseForNonFileUri()
	{
		Assert.IsFalse(LanguageServerPaths.TryGetLocalPath("https://example.com/test.ext", out string filePath));
		Assert.AreEqual(string.Empty, filePath);
	}

	[TestMethod]
	public void TryNormalizeLocalPath_ReturnsFalseForBlankInput()
	{
		Assert.IsFalse(LanguageServerPaths.TryNormalizeLocalPath(" ", out string normalizedPath));
		Assert.AreEqual(string.Empty, normalizedPath);
	}

	[TestMethod]
	public void NormalizeLocalPath_Uri_RejectsNonFileUri()
	{
		Assert.ThrowsExactly<ArgumentException>(() => LanguageServerPaths.NormalizeLocalPath(new Uri("https://example.com/test.ext")));
		Assert.ThrowsExactly<ArgumentException>(() => LanguageServerPaths.NormalizeLocalPath(new Uri("relative/path.ext", UriKind.Relative)));
	}

	[TestMethod]
	public void NormalizeLocalPath_Uri_FileUriWithDriveLetter_ReturnsLocalPath()
	{
		if (!OperatingSystem.IsWindows())
			Assert.Inconclusive("The test pins Windows drive-letter URI normalization.");

		Uri uri = new("file:///C:/test/folder/test.ext");
		string expectedPath = Path.GetFullPath(@"C:\test\folder\test.ext");

		Assert.AreEqual(expectedPath, LanguageServerPaths.NormalizeLocalPath(uri));
	}

	[TestMethod]
	public void BuildFileUri_PosixAbsolutePath_UsesAuthorityLessThreeSlashShape()
	{
		// A rooted POSIX path must not be concatenated verbatim: four slashes would parse as a UNC authority on
		// non-Windows hosts, which corrupts every document URI.
		Assert.AreEqual("file:///home/user/workspace/main.lua", LanguageServerPaths.BuildFileUri("/home/user/workspace/main.lua"));
	}

	[TestMethod]
	public void BuildFileUri_PosixAbsolutePath_IsNotParsedAsUncAuthority()
		=> Assert.AreEqual(string.Empty, new Uri(LanguageServerPaths.BuildFileUri("/home/user/x")).Host);

	[TestMethod]
	public void BuildFileUri_UncPath_BecomesTheUriAuthority()
	{
		Assert.AreEqual("file://server/share/dir/file.lua", LanguageServerPaths.BuildFileUri(@"\\server\share\dir\file.lua"));
	}

	[TestMethod]
	public void BuildFileUri_DrivePath_UsesThreeSlashes()
	{
		Assert.AreEqual("file:///C:/temp/a.txt", LanguageServerPaths.BuildFileUri(@"C:\temp\a.txt"));
	}

	[TestMethod]
	public void BuildFileUri_ExtendedLengthPrefixes_AreStrippedBeforeBuilding()
	{
		Assert.AreEqual("file:///C:/temp/a.txt", LanguageServerPaths.BuildFileUri(@"\\?\C:\temp\a.txt"));
		Assert.AreEqual("file://server/share/x.lua", LanguageServerPaths.BuildFileUri(@"\\?\UNC\server\share\x.lua"));
	}

	[TestMethod]
	public void BuildFileUri_EscapesPercentHashAndQuestionMarkCharacters()
	{
		Assert.AreEqual("file:///home/user/a%23b%3Fc%25d.txt", LanguageServerPaths.BuildFileUri("/home/user/a#b?c%d.txt"));
	}

	[TestMethod]
	public void CreateFileUri_WithHashAndQuestionMarkCharacters_RoundTripsThroughTryGetLocalPath()
	{
		string expectedPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Path Helper", "a#b?c.txt"));

		string uri = LanguageServerPaths.CreateFileUri(expectedPath);

		Assert.IsTrue(uri.Contains("a%23b%3Fc.txt", StringComparison.Ordinal), uri);
		Assert.IsTrue(LanguageServerPaths.TryGetLocalPath(uri, out string filePath));
		Assert.AreEqual(expectedPath, filePath);
	}

	[TestMethod]
	public void CreateFileUri_WithNonAsciiFileNameFileName_RoundTripsThroughTryGetLocalPath()
	{
		string expectedPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Path Helper", "日本語 test.txt"));

		string uri = LanguageServerPaths.CreateFileUri(expectedPath);

		Assert.IsTrue(LanguageServerPaths.TryGetLocalPath(uri, out string filePath));
		Assert.AreEqual(expectedPath, filePath);
	}

	[TestMethod]
	public void NormalizeWorkspaceRoots_EmptyList_ModelsAFolderlessSession()
	{
		Assert.AreEqual(0, LanguageServerPaths.NormalizeWorkspaceRoots([]).Length);
	}
}

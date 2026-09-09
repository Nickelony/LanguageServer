namespace Nickelony.LanguageServer.Lua;

/// <summary>
/// Describes the workspace file patterns and configuration paths the Lua provider mirrors to the language server.
/// </summary>
internal static class LuaWorkspaceConventions
{
	internal const string LuaConfigurationFilePrefix = ".luarc.";

	internal const string LuaConfigurationFilePattern = LuaConfigurationFilePrefix + "*";

	internal const string LuaFilePattern = "*.lua";

	/// <summary>
	/// The watch specifications mirrored to the language server: every Lua file in the workspace tree
	/// and every LuaLS configuration file in the workspace root.
	/// </summary>
	/// <remarks>
	/// LuaLS loads its settings from the workspace-level configuration file, <c>.luarc.json</c> by
	/// default; a custom file can be supplied through its <c>--configpath</c> command-line flag, which
	/// this watcher does not cover and this provider cannot pass (it exposes no server-argument
	/// surface). The watcher mirrors configuration files in the root only, and the
	/// prefix pattern covers the <c>.luarc.jsonc</c> form. Every <c>*.lua</c> file is watched
	/// recursively because the server serves on-disk files, including files in vendored library folders.
	/// </remarks>
	internal static readonly IReadOnlyList<WorkspaceWatchSpecification> WatchSpecifications =
	[
		new WorkspaceWatchSpecification(LuaFilePattern, IncludeSubdirectories: true),
		new WorkspaceWatchSpecification(LuaConfigurationFilePattern, IncludeSubdirectories: false)
	];

	/// <summary>
	/// Reports whether a normalized workspace path should refresh the language-server settings.
	/// </summary>
	/// <remarks>
	/// The comparison uses <see cref="LanguageServerPaths.LocalPathComparison"/>, so configuration
	/// detection follows the same path policy as document identity.
	/// </remarks>
	/// <param name="normalizedPath">The normalized path of the changed file.</param>
	/// <returns><see langword="true"/> when the path is a LuaLS configuration file.</returns>
	internal static bool IsConfigurationPath(string normalizedPath)
		=> Path.GetFileName(normalizedPath).StartsWith(LuaConfigurationFilePrefix, LanguageServerPaths.LocalPathComparison);
}

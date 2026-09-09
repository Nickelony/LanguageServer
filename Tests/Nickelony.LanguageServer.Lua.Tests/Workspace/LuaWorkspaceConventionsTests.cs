namespace Nickelony.LanguageServer.Lua.Tests;

[TestClass]
public sealed class LuaWorkspaceConventionsTests
{
	[TestMethod]
	public void WatchSpecifications_CoverLuaFilesRecursivelyAndRootConfigurationFiles()
	{
		IReadOnlyList<WorkspaceWatchSpecification> specifications = LuaWorkspaceConventions.WatchSpecifications;

		Assert.AreEqual(2, specifications.Count);

		// LuaLS serves every on-disk Lua file in the workspace tree, so the Lua pattern is recursive.
		Assert.AreEqual("*.lua", specifications[0].Filter);
		Assert.IsTrue(specifications[0].IncludeSubdirectories);

		// LuaLS loads its settings from the workspace-level configuration file, so the configuration
		// pattern stays in the workspace root.
		Assert.AreEqual(".luarc.*", specifications[1].Filter);
		Assert.IsFalse(specifications[1].IncludeSubdirectories);
	}

	[TestMethod]
	public void IsConfigurationPath_MatchesTheLuarcPrefixForms()
	{
		string workspaceRoot = Path.GetTempPath();

		Assert.IsTrue(LuaWorkspaceConventions.IsConfigurationPath(Path.Combine(workspaceRoot, ".luarc.json")));

		// The prefix rule also covers the JSONC form and any other suffix LuaLS may be pointed at.
		Assert.IsTrue(LuaWorkspaceConventions.IsConfigurationPath(Path.Combine(workspaceRoot, ".luarc.jsonc")));

		// The predicate is a superset of watcher delivery: it accepts the prefix in any directory, while
		// the root-only watch specification is what decides which paths actually reach it.
		Assert.IsTrue(LuaWorkspaceConventions.IsConfigurationPath(Path.Combine(workspaceRoot, "scripts", ".luarc.json")));

		Assert.IsFalse(LuaWorkspaceConventions.IsConfigurationPath(Path.Combine(workspaceRoot, "luarc.json")));
		Assert.IsFalse(LuaWorkspaceConventions.IsConfigurationPath(Path.Combine(workspaceRoot, "config.luarc.json")));

		if (!LanguageServerPaths.UsesCaseSensitiveLocalPaths)
		{
			Assert.IsTrue(LuaWorkspaceConventions.IsConfigurationPath(Path.Combine(workspaceRoot, ".LUARC.JSON")),
				"Configuration detection follows the local-path comparison policy, which is case-insensitive on this host.");
		}
	}
}

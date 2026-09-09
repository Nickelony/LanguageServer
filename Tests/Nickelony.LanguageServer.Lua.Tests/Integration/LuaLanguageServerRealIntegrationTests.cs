using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Completion;
using Nickelony.IDEKit.IntelliSense.DocumentSymbols;
using Nickelony.IDEKit.IntelliSense.Hover;
using Nickelony.IDEKit.IntelliSense.Navigation;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Reflection;

namespace Nickelony.LanguageServer.Lua.Tests;

/// <summary>
/// Live integration tests for the Lua language server.
/// Tests are inconclusive when no archive is configured or the expected executable is missing after extraction.
/// A malformed archive fails during setup. The tests launch a real server process and cover diagnostics,
/// semantic tokens, completion, hover, document symbols, definition and reference navigation, rename,
/// workspace file changes, restart after a server crash, and shutdown.
/// </summary>
[TestClass]
public sealed class LuaLanguageServerRealIntegrationTests
{
	private static readonly TimeSpan s_integrationTimeout = TimeSpan.FromSeconds(20);
	private static readonly TimeSpan s_pollInterval = TimeSpan.FromMilliseconds(150);

	[TestMethod]
	[TestCategory("Integration")]
	public async Task Provider_WithBundledLuaLanguageServer_HandlesLiveWorkflowWorkspaceChangesAndShutdown()
	{
		using var session = new RealLuaLanguageServerTestSession();

		string filePath = Path.Combine(session.WorkspaceRoot, "Scripts", "test.lua");
		string generatedLibraryDirectoryPath = Path.Combine(session.WorkspaceRoot, ".generated");
		string generatedStubFilePath = Path.Combine(generatedLibraryDirectoryPath, "Generated.lua");

		const string initialContent = "local stable_local =\r\nreturn stable_local\r\n";
		const string updatedContent = "local stable_local = 1\r\nlocal updated_local = stable_local + 1\r\nreturn updated_local\r\nupd";
		const string libraryAwareContent = updatedContent + "\r\ngen";

		Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? session.WorkspaceRoot);
		File.WriteAllText(filePath, initialContent);

		// The host writes generated Lua stubs into the workspace's .generated folder and declares the
		// folder as a LuaLS library in .luarc.json (written below), which LuaLS reads and re-reads on change.
		using var provider = new LuaLanguageServerIntelliSenseProvider(
			[session.WorkspaceRoot],
			session.ExecutablePath);

		provider.OpenDocument(filePath, initialContent);

		await WaitForConditionAsync(
			() => provider.GetDiagnostics(filePath).Count > 0,
			s_integrationTimeout,
			"Expected bundled LuaLS to publish diagnostics for the syntax error in the opened document.");

		provider.UpdateDocument(filePath, updatedContent);

		await WaitForConditionAsync(
			() => provider.GetSemanticTokens(filePath).Any(token => token.Line >= 2),
			s_integrationTimeout,
			"Expected semantic tokens to reflect the updated live document content.");

		IReadOnlyList<TextCompletionItem> completionItems = await TestPolling.WaitForAsync(
			() => provider.GetCompletionItemsAsync(filePath, updatedContent, 3, 3),
			items => items.Any(item => string.Equals(item.Label, "updated_local", StringComparison.Ordinal)),
			s_integrationTimeout,
			"Expected bundled LuaLS to return completion items for the updated local variable.",
			items => "Last completion labels: " + string.Join(", ", items.Select(item => item.Label)));

		Assert.IsTrue(completionItems.Any(item => string.Equals(item.Label, "updated_local", StringComparison.Ordinal)));

		TextHoverInfo? hover = await TestPolling.WaitForAsync(
			() => provider.GetHoverAsync(filePath, updatedContent, 2, 8),
			hoverInfo => hoverInfo is not null,
			s_integrationTimeout,
			"Expected bundled LuaLS to return hover information for the updated document.",
			_ => "No hover information was returned.");

		Assert.IsNotNull(hover);

		Assert.IsFalse(string.IsNullOrWhiteSpace(hover.Content));

		IReadOnlyList<TextDocumentSymbol> documentSymbols = await TestPolling.WaitForAsync(
			() => provider.GetDocumentSymbolsAsync(filePath, updatedContent),
			symbols => symbols.Any(symbol => string.Equals(symbol.Name, "updated_local", StringComparison.Ordinal)),
			s_integrationTimeout,
			"Expected bundled LuaLS to return document symbols for the updated document.",
			symbols => "Last symbol names: " + string.Join(", ", symbols.Select(symbol => symbol.Name)));

		TextDocumentSymbol localSymbol = documentSymbols.First(symbol => string.Equals(symbol.Name, "updated_local", StringComparison.Ordinal));

		Assert.AreEqual(TextDocumentSymbolKind.Variable, localSymbol.Kind);
		Assert.IsNotNull(localSymbol.SelectionRange);
		Assert.IsTrue(localSymbol.SelectionRange.Value.EndOffset <= updatedContent.Length);

		Directory.CreateDirectory(generatedLibraryDirectoryPath);

		File.WriteAllText(generatedStubFilePath,
			"---@meta\r\n" +
			"function generated_function() end\r\n");

		File.WriteAllText(Path.Combine(session.WorkspaceRoot, ".luarc.json"),
			"{\r\n\t\"workspace.library\": [\".generated\"]\r\n}\r\n");

		provider.UpdateDocument(filePath, libraryAwareContent);

		IReadOnlyList<TextCompletionItem> libraryItems = await TestPolling.WaitForAsync(
			() => provider.GetCompletionItemsAsync(filePath, libraryAwareContent, 4, 3),
			items => items.Any(item => item.Label.StartsWith("generated_function", StringComparison.Ordinal)),
			s_integrationTimeout,
			"Expected a newly forwarded symbol from the generated library folder to appear in completions after the live workspace change.",
			items => "Last completion labels: " + string.Join(", ", items.Select(item => item.Label)));

		Assert.IsTrue(libraryItems.Any(item => item.Label.StartsWith("generated_function", StringComparison.Ordinal)));

		int processId = GetRequiredServerProcessId(provider);

		provider.Dispose();

		await WaitForProcessExitAsync(
			processId,
			s_integrationTimeout,
			"Expected disposing the provider to stop the live Lua language-server process.");
	}

	[TestMethod]
	[TestCategory("Integration")]
	public async Task Provider_WithBundledLuaLanguageServer_RestartsAfterLiveServerCrashAndResumesRequests()
	{
		using var session = new RealLuaLanguageServerTestSession();

		string filePath = Path.Combine(session.WorkspaceRoot, "Scripts", "restart.lua");

		const string initialContent = "local restart_probe = 1\r\nres";
		const string restartedContent = "local restart_probe = 1\r\nlocal after_restart = restart_probe + 1\r\naft";

		Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? session.WorkspaceRoot);
		File.WriteAllText(filePath, initialContent);

		using var provider = new LuaLanguageServerIntelliSenseProvider([session.WorkspaceRoot], session.ExecutablePath);

		provider.OpenDocument(filePath, initialContent);

		IReadOnlyList<TextCompletionItem> initialItems = await TestPolling.WaitForAsync(
			() => provider.GetCompletionItemsAsync(filePath, initialContent, 1, 3),
			items => items.Any(item => string.Equals(item.Label, "restart_probe", StringComparison.Ordinal)),
			s_integrationTimeout,
			"Expected bundled LuaLS to return the initial completion before restart.",
			items => "Last completion labels: " + string.Join(", ", items.Select(item => item.Label)));

		Assert.IsTrue(initialItems.Any(item => string.Equals(item.Label, "restart_probe", StringComparison.Ordinal)));

		LanguageServerClient client = GetRequiredClient(provider);
		long initialGeneration = client.TransportGeneration;
		Process initialProcess = GetRequiredServerProcess(client);
		int initialProcessId = initialProcess.Id;

		initialProcess.Kill(entireProcessTree: true);

		await WaitForProcessExitAsync(
			initialProcessId,
			s_integrationTimeout,
			"Expected the live Lua language-server process to exit after the simulated crash.");

		await WaitForConditionAsync(
			() => !client.IsReady,
			s_integrationTimeout,
			"Expected the client to observe the live LuaLS transport disconnect.");

		provider.UpdateDocument(filePath, restartedContent);

		IReadOnlyList<TextCompletionItem> restartedItems = await TestPolling.WaitForAsync(
			() => provider.GetCompletionItemsAsync(filePath, restartedContent, 2, 3),
			items => items.Any(item => string.Equals(item.Label, "after_restart", StringComparison.Ordinal)),
			s_integrationTimeout,
			"Expected the provider to restart the live server and resume completions after the crash.",
			items => "Last completion labels: " + string.Join(", ", items.Select(item => item.Label)));

		Assert.IsTrue(restartedItems.Any(item => string.Equals(item.Label, "after_restart", StringComparison.Ordinal)));

		long restartedGeneration = client.TransportGeneration;
		int restartedProcessId = GetRequiredServerProcessId(provider);

		Assert.IsTrue(restartedGeneration > initialGeneration);
		Assert.AreNotEqual(initialProcessId, restartedProcessId);

		provider.Dispose();

		await WaitForProcessExitAsync(
			restartedProcessId,
			s_integrationTimeout,
			"Expected the restarted live Lua language-server process to stop on provider disposal.");
	}

	[TestMethod]
	[TestCategory("Integration")]
	public async Task Provider_WithBundledLuaLanguageServer_ResolvesDefinitionAndReferencesForLocalSymbol()
	{
		using var session = new RealLuaLanguageServerTestSession();

		string filePath = Path.Combine(session.WorkspaceRoot, "Scripts", "navigation.lua");
		const string content =
			"local tracked_value = 1\r\n" +
			"local combined = tracked_value + tracked_value\r\n" +
			"return combined\r\n";

		Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? session.WorkspaceRoot);
		File.WriteAllText(filePath, content);

		using var provider = new LuaLanguageServerIntelliSenseProvider([session.WorkspaceRoot], session.ExecutablePath);

		provider.OpenDocument(filePath, content);

		await WaitForConditionAsync(
			() => provider.SupportsReferences,
			s_integrationTimeout,
			"Expected the bundled Lua language server to advertise reference support.");

		TextDefinitionLocation? definition = await TestPolling.WaitForAsync(
			() => provider.GetDefinitionAsync(filePath, content, 1, 19),
			definitionLocation => definitionLocation is not null,
			s_integrationTimeout,
			"Expected the bundled Lua language server to resolve the local symbol definition.",
			_ => "No definition location was returned.");

		Assert.IsNotNull(definition);
		Assert.IsTrue(string.Equals(filePath, definition.DocumentId, StringComparison.OrdinalIgnoreCase));
		Assert.AreEqual(new TextPosition(0, 6), definition.TargetRange.Start);

		IReadOnlyList<TextReferenceLocation> references = await TestPolling.WaitForAsync(
			() => provider.GetReferencesAsync(new TextReferenceRequest(filePath, content, 1, 19)),
			referenceLocations => referenceLocations.Count >= 3,
			s_integrationTimeout,
			"Expected the bundled Lua language server to return declaration and usage references for the local symbol.",
			locations => "Last reference count: " + locations.Count);

		Assert.AreEqual(3, references.Count(location => string.Equals(location.FilePath, filePath, StringComparison.OrdinalIgnoreCase)));
		Assert.IsTrue(references.Any(location => location.Range.Start.Line == 0 && location.Range.Start.Character == 6));
		Assert.AreEqual(2, references.Count(location => location.Range.Start.Line == 1));
	}

	[TestMethod]
	[TestCategory("Integration")]
	public async Task Provider_WithBundledLuaLanguageServer_ReturnsWorkspaceEditForRename()
	{
		using var session = new RealLuaLanguageServerTestSession();

		string filePath = Path.Combine(session.WorkspaceRoot, "Scripts", "rename.lua");
		const string content =
			"local tracked_value = 1\r\n" +
			"local result = tracked_value + 2\r\n" +
			"return tracked_value, result\r\n";

		Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? session.WorkspaceRoot);
		File.WriteAllText(filePath, content);

		using var provider = new LuaLanguageServerIntelliSenseProvider([session.WorkspaceRoot], session.ExecutablePath);

		provider.OpenDocument(filePath, content);

		await WaitForConditionAsync(
			() => provider.SupportsRename,
			s_integrationTimeout,
			"Expected the bundled Lua language server to advertise rename support.");

		TextWorkspaceEdit? workspaceEdit = await TestPolling.WaitForAsync(
			() => provider.RenameSymbolAsync(new TextRenameRequest(filePath, content, 0, 8, "renamed_value")),
			edit => edit?.HasEdits == true,
			s_integrationTimeout,
			"Expected the bundled Lua language server to return a rename workspace edit for the local symbol.",
			_ => "No rename workspace edit was returned.");

		Assert.IsNotNull(workspaceEdit);
		Assert.IsTrue(workspaceEdit.HasEdits);
		Assert.AreEqual(1, workspaceEdit.DocumentEdits.Count);

		TextDocumentEdit documentEdit = workspaceEdit.DocumentEdits[0];

		Assert.IsTrue(string.Equals(filePath, documentEdit.FilePath, StringComparison.OrdinalIgnoreCase));
		Assert.IsTrue(documentEdit.TextEdits.Count >= 3);
		Assert.IsTrue(documentEdit.TextEdits.All(edit => edit.NewText == "renamed_value"));
	}

	/// <summary>
	/// Reaches the provider's real client for the live crash test. Deliberate direct seam:
	/// the test must compare transport generations across a real server crash and restart, and no
	/// public surface exposes the client; the seam only runs against a client created by the provider's
	/// public constructor in these skipped-unless-configured integration tests. The provider framework
	/// owns the client field, so the field lookup walks the provider's base types.
	/// </summary>
	private static LanguageServerClient GetRequiredClient(LuaLanguageServerIntelliSenseProvider provider)
	{
		FieldInfo? field = null;

		for (Type? type = typeof(LuaLanguageServerIntelliSenseProvider); type is not null && field is null; type = type.BaseType)
			field = type.GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

		if (field is null)
			throw new InvalidOperationException("Private field '_client' was not found.");

		return (LanguageServerClient)(field.GetValue(provider)
			?? throw new InvalidOperationException("The live integration test expected a real language-server client instance."));
	}

	/// <summary>
	/// Reaches the active transport session's process to simulate a server crash and to observe process
	/// exit after disposal. Deliberate direct seam: process identity is transport-internal and has no
	/// public surface; the seam walks the client's internal architecture through the shared test-access
	/// helper.
	/// </summary>
	private static Process GetRequiredServerProcess(LanguageServerClient client)
		=> LuaLanguageServerIntelliSenseProviderTestAccess.GetActiveServerProcess(client)
			?? throw new InvalidOperationException("The live language-server client has no active transport session process.");

	private static int GetRequiredServerProcessId(LuaLanguageServerIntelliSenseProvider provider)
		=> GetRequiredServerProcess(GetRequiredClient(provider)).Id;

	private static async Task WaitForConditionAsync(
		Func<bool> predicate,
		TimeSpan timeout,
		string failureMessage)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();

		while (stopwatch.Elapsed < timeout)
		{
			if (predicate())
				return;

			await Task.Delay(s_pollInterval).ConfigureAwait(false);
		}

		Assert.Fail(failureMessage);
	}

	private static async Task WaitForProcessExitAsync(int processId, TimeSpan timeout, string failureMessage)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();

		while (stopwatch.Elapsed < timeout)
		{
			if (!TryGetProcess(processId, out Process? process))
				return;

			using (process)
			{
				if (process.HasExited)
					return;
			}

			await Task.Delay(s_pollInterval).ConfigureAwait(false);
		}

		Assert.Fail(failureMessage);
	}

	private static bool TryGetProcess(int processId, [NotNullWhen(true)] out Process? process)
	{
		try
		{
			process = Process.GetProcessById(processId);
			return true;
		}
		catch (ArgumentException)
		{
			process = null;
			return false;
		}
	}

	private sealed class RealLuaLanguageServerTestSession : IDisposable
	{
		private readonly string _extractionRoot;

		public RealLuaLanguageServerTestSession()
		{
			string? configuredArchivePath = Environment.GetEnvironmentVariable("NICKELONY_LUA_LANGUAGE_SERVER_ARCHIVE");

			string archivePath = configuredArchivePath is not null && File.Exists(configuredArchivePath)
				? configuredArchivePath
				: TryFindRepositoryFile(Path.Combine("Tests", "TestAssets", "LuaLS.zip"))
				?? throw new AssertInconclusiveException(
					"LuaLS integration fixture is not configured. Set NICKELONY_LUA_LANGUAGE_SERVER_ARCHIVE " +
					"or provide Tests/TestAssets/LuaLS.zip.");

			_extractionRoot = Path.Combine(Path.GetTempPath(), "LuaLsExtract_" + Guid.NewGuid().ToString("N"));
			WorkspaceRoot = Path.Combine(Path.GetTempPath(), "LuaLsWorkspace_" + Guid.NewGuid().ToString("N"));

			try
			{
				ZipFile.ExtractToDirectory(archivePath, _extractionRoot);
				Directory.CreateDirectory(WorkspaceRoot);

				string executableName = OperatingSystem.IsWindows() ? "lua-language-server.exe" : "lua-language-server";
				string executablePath = Path.Combine(_extractionRoot, "bin", executableName);

				if (!File.Exists(executablePath))
				{
					throw new AssertInconclusiveException(
						$"The configured LuaLS archive was found, but bin/{executableName} was missing after extraction.");
				}

				ExecutablePath = executablePath;
			}
			catch
			{
				// A constructor that throws never reaches Dispose, so the temp roots are cleaned up here:
				// a malformed archive or a missing binary cannot leak them.
				TestTempDirectories.Delete(_extractionRoot);
				TestTempDirectories.Delete(WorkspaceRoot);
				throw;
			}
		}

		public string ExecutablePath { get; }
		public string WorkspaceRoot { get; }

		public void Dispose()
		{
			TestTempDirectories.Delete(WorkspaceRoot);
			TestTempDirectories.Delete(_extractionRoot);
		}
	}

	private static string? TryFindRepositoryFile(string relativePath)
	{
		for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
		{
			string candidatePath = Path.Combine(current.FullName, relativePath);

			if (File.Exists(candidatePath))
				return candidatePath;
		}

		return null;
	}
}

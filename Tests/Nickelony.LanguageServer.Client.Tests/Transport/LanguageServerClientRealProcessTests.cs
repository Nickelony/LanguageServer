using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>
/// Exercises the real process transport: a compiled fake language server, hosted by the dotnet executable,
/// answers the handshake over stdio, writes a stderr marker, and is shut down through the graceful
/// shutdown/exit handshake.
/// </summary>
[TestClass]
public sealed class LanguageServerClientRealProcessTests
{
	[TestMethod]
	public async Task StartAsync_WithRealProcess_CompletesHandshakeCapturesStderrAndShutsDown()
	{
		string? fakeServerAssemblyPath = GetFakeServerAssemblyPath();

		if (fakeServerAssemblyPath is null)
			Assert.Inconclusive("The fake server build output was not found; build the test project first.");

		using var loggerScope = new TestLoggerScope(LogLevel.Debug);
		var options = new LanguageServerClientOptions(() => new { })
		{
			InitializeTimeout = TimeSpan.FromSeconds(30),
			ShutdownRequestTimeout = TimeSpan.FromSeconds(10),
			RequireTextDocumentSynchronization = false,
			ServerArguments = [fakeServerAssemblyPath]
		};

		var client = new LanguageServerClient(
			[Path.GetTempPath()],
			GetDotnetExecutableName(),
			options,
			loggerScope.CreateLogger<LanguageServerClient>());

		try
		{
			// The dotnet host runs the compiled fake server, so process spawning, stream wiring, framing, and
			// stderr capture all run on real pipes in this test.
			bool ready = await client.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);

			Assert.IsTrue(ready, $"The real process handshake should complete. Logs: {string.Join("; ", loggerScope.Logs)}");
			Assert.IsTrue(client.IsReady);
			Assert.AreEqual(1L, client.TransportGeneration);
			Assert.AreEqual(TextDocumentSyncKind.None, client.TextDocumentSyncKind);
			Assert.IsNull(client.LastStartupException);

			await WaitForLogAsync(loggerScope, "fake-server stderr marker", TimeSpan.FromSeconds(10)).ConfigureAwait(false);
		}
		finally
		{
			await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
		}

		Assert.IsFalse(client.IsReady);
		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => client.StartAsync(CancellationToken.None)).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task StartAsync_WhenTheServerExitsBeforeTheHandshake_ReturnsFalseAndKeepsTheStderrContext()
	{
		string? fakeServerAssemblyPath = GetFakeServerAssemblyPath();

		if (fakeServerAssemblyPath is null)
			Assert.Inconclusive("The fake server build output was not found; build the test project first.");

		using var loggerScope = new TestLoggerScope(LogLevel.Debug);
		var options = new LanguageServerClientOptions(() => new { })
		{
			InitializeTimeout = TimeSpan.FromSeconds(30),
			RequireTextDocumentSynchronization = false,
			ServerArguments = [fakeServerAssemblyPath, "--exit-immediately"]
		};

		var client = new LanguageServerClient(
			[Path.GetTempPath()],
			GetDotnetExecutableName(),
			options,
			loggerScope.CreateLogger<LanguageServerClient>());

		try
		{
			bool ready = await client.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);

			Assert.IsFalse(ready);
			Assert.IsFalse(client.IsReady);
			Assert.IsNotNull(client.LastStartupException);

			// The crash output written before the exit must still be captured for diagnostics.
			await WaitForLogAsync(loggerScope, "fake-server exiting immediately by request", TimeSpan.FromSeconds(10)).ConfigureAwait(false);
		}
		finally
		{
			await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
		}
	}

	[TestMethod]
	public async Task StartAsync_WithMissingExecutable_ReturnsFalseAndReportsTheFailure()
	{
		string missingExecutablePath = Path.Combine(Path.GetTempPath(), $"missing-language-server-{Guid.NewGuid():N}.exe");
		using var loggerScope = new TestLoggerScope(LogLevel.Debug);

		var client = new LanguageServerClient(
			[Path.GetTempPath()],
			missingExecutablePath,
			new LanguageServerClientOptions(() => new { }),
			loggerScope.CreateLogger<LanguageServerClient>());

		try
		{
			bool ready = await client.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

			Assert.IsFalse(ready);
			Assert.IsFalse(client.IsReady);
			Assert.IsNotNull(client.LastStartupException);
		}
		finally
		{
			await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
		}
	}

	private static async Task WaitForLogAsync(TestLoggerScope loggerScope, string expectedText, TimeSpan timeout)
	{
		DateTime deadline = DateTime.UtcNow + timeout;

		while (DateTime.UtcNow < deadline)
		{
			foreach (string log in loggerScope.Logs)
			{
				if (log.Contains(expectedText, StringComparison.Ordinal))
					return;
			}

			await Task.Delay(50).ConfigureAwait(false);
		}

		Assert.Fail($"Expected a log entry containing '{expectedText}'.");
	}

	/// <summary>
	/// Gets the path of the compiled fake server assembly injected at build time, or <see langword="null"/> when the
	/// build output is missing.
	/// </summary>
	/// <returns>The fake server assembly path, or <see langword="null"/>.</returns>
	private static string? GetFakeServerAssemblyPath()
	{
		string? path = typeof(LanguageServerClientRealProcessTests).Assembly
			.GetCustomAttributes<AssemblyMetadataAttribute>()
			.FirstOrDefault(attribute => attribute.Key == "FakeServerAssemblyPath")?.Value;

		return path is { Length: > 0 } && File.Exists(path) ? path : null;
	}

	/// <summary>
	/// Gets the dotnet host command used to run the fake server assembly.
	/// </summary>
	/// <returns>The dotnet host command name.</returns>
	private static string GetDotnetExecutableName() => "dotnet";
}

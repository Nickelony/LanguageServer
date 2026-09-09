using System.Runtime.CompilerServices;

namespace Nickelony.LanguageServer.Lua.Tests;

public sealed partial class LuaLanguageServerIntelliSenseProviderTests
{
	// Weak-keyed so a disposed provider (and its watcher captures) does not stay rooted for the
	// process lifetime; both tables synchronize internally, so no external lock is needed.
	private static readonly ConditionalWeakTable<LuaLanguageServerIntelliSenseProvider, WorkspaceWatcherTestContext> s_workspaceWatcherContexts = new();
	private static readonly ConditionalWeakTable<WorkspaceFileWatcher, Action<WorkspaceFileWatcher, Exception?>> s_workspaceWatcherFailureCallbacks = new();

	private sealed class WorkspaceWatcherTestContext
	{
		// One capture per watched root; the comparer mirrors provider path normalization.
		private readonly Dictionary<string, WorkspaceWatcherRootCapture> _rootCaptures = new(LanguageServerPaths.LocalPathComparer);

		public required IReadOnlyList<string> WorkspaceRootDirectoryPaths { get; init; }

		public required FakeLanguageServerClient Client { get; init; }

		public WorkspaceFileWatcher Create(
			string workspaceRootDirectoryPath,
			Func<FileChangeBatch, CancellationToken, Task> dispatchAsync,
			Action<WorkspaceFileWatcher, Exception?> onWatcherFailed)
		{
			if (!_rootCaptures.TryGetValue(workspaceRootDirectoryPath, out WorkspaceWatcherRootCapture? capture))
			{
				capture = new WorkspaceWatcherRootCapture { WorkspaceRootDirectoryPath = workspaceRootDirectoryPath };
				_rootCaptures.Add(workspaceRootDirectoryPath, capture);
			}

			WorkspaceFileWatcher watcher = new(
				workspaceRootDirectoryPath,
				dispatchAsync,
				LuaWorkspaceConventions.WatchSpecifications,
				onWatcherFailed);

			capture.Dispatch = dispatchAsync;
			capture.CurrentWatcher = watcher;

			s_workspaceWatcherFailureCallbacks.Add(watcher, onWatcherFailed);

			return watcher;
		}

		// Returns the captured root when the provider watches exactly one root; multi-root tests use the
		// per-root accessors instead.
		public WorkspaceWatcherRootCapture? GetSingleRoot()
		{
			WorkspaceWatcherRootCapture? singleCapture = null;

			foreach (WorkspaceWatcherRootCapture capture in _rootCaptures.Values)
			{
				if (singleCapture is not null)
					return null;

				singleCapture = capture;
			}

			return singleCapture;
		}

		public WorkspaceWatcherRootCapture? GetRoot(string workspaceRootDirectoryPath)
			=> _rootCaptures.TryGetValue(workspaceRootDirectoryPath, out WorkspaceWatcherRootCapture? capture) ? capture : null;
	}

	private sealed class WorkspaceWatcherRootCapture
	{
		public required string WorkspaceRootDirectoryPath { get; init; }

		public WorkspaceFileWatcher? CurrentWatcher { get; set; }

		public Func<FileChangeBatch, CancellationToken, Task>? Dispatch { get; set; }

		public WorkspaceFileWatcher? GetActiveWatcher()
			=> CurrentWatcher is { IsDisposed: false } watcher ? watcher : null;
	}

	private static LuaLanguageServerIntelliSenseProvider CreateProviderWithWatcherCapture(
		string workspaceRootDirectoryPath,
		FakeLanguageServerClient client,
		out WorkspaceWatcherTestContext watcherContext,
		LuaLanguageServerOptions? options = null)
		=> CreateProviderWithWatcherCapture([workspaceRootDirectoryPath], client, out watcherContext, options);

	private static LuaLanguageServerIntelliSenseProvider CreateProviderWithWatcherCapture(
		IReadOnlyList<string> workspaceRootDirectoryPaths,
		FakeLanguageServerClient client,
		out WorkspaceWatcherTestContext watcherContext,
		LuaLanguageServerOptions? options = null)
	{
		watcherContext = new WorkspaceWatcherTestContext
		{
			WorkspaceRootDirectoryPaths = workspaceRootDirectoryPaths,
			Client = client
		};
		var provider = new LuaLanguageServerIntelliSenseProvider(
			workspaceRootDirectoryPaths,
			client,
			workspaceFileWatcherFactory: watcherContext.Create,
			luaOptions: options);

		s_workspaceWatcherContexts.Add(provider, watcherContext);
		return provider;
	}

	private static PublishDiagnosticsParams CreateDiagnostics(string filePath, int? version, int startCharacter, int endCharacter, string message) => new(
		new Uri(filePath).AbsoluteUri,
		version,
		[
			new DiagnosticPayload(
				new ProtocolRangePayload(
					new ProtocolPosition(0, startCharacter),
					new ProtocolPosition(0, endCharacter)),
				DiagnosticSeverity.Warning,
				message,
				null,
				null)
		]);

	private static WorkspaceFileWatcher? GetWorkspaceWatcher(LuaLanguageServerIntelliSenseProvider provider)
	{
		if (!s_workspaceWatcherContexts.TryGetValue(provider, out WorkspaceWatcherTestContext? context))
			return null;

		return context.GetSingleRoot()?.GetActiveWatcher();
	}

	private static WorkspaceFileWatcher? GetWorkspaceWatcher(LuaLanguageServerIntelliSenseProvider provider, string workspaceRootDirectoryPath)
	{
		if (!s_workspaceWatcherContexts.TryGetValue(provider, out WorkspaceWatcherTestContext? context))
			return null;

		return context.GetRoot(workspaceRootDirectoryPath)?.GetActiveWatcher();
	}

	private static string? GetSingleWorkspaceRoot(LuaLanguageServerIntelliSenseProvider provider)
		=> s_workspaceWatcherContexts.TryGetValue(provider, out WorkspaceWatcherTestContext? context) && context.WorkspaceRootDirectoryPaths.Count == 1
			? context.WorkspaceRootDirectoryPaths[0]
			: null;

	// Simulates the watcher reporting a failure through the callback the provider registered with it, without
	// reaching into the client package internals.
	private static void SimulateWatcherFailure(WorkspaceFileWatcher watcher, Exception? exception)
	{
		if (!s_workspaceWatcherFailureCallbacks.TryGetValue(watcher, out Action<WorkspaceFileWatcher, Exception?>? onWatcherFailed))
			throw new AssertFailedException("Expected the provider to register a watcher failure callback for the watcher.");

		onWatcherFailed(watcher, exception);
	}

	private static CancellationTokenSource GetProviderDisposeCancellationTokenSource(LuaLanguageServerIntelliSenseProvider provider)
		=> LuaLanguageServerIntelliSenseProviderTestAccess.GetProviderDisposeCancellationTokenSource(provider);

	private static int CountSentMethods(FakeLanguageServerClient client, string method)
	{
		int count = 0;
		string[] methods = client.GetSentMethodNames();

		for (int i = 0; i < methods.Length; i++)
		{
			if (string.Equals(methods[i], method, StringComparison.Ordinal))
				count++;
		}

		return count;
	}

	// The workspace watcher is created during the provider's lazy startup and its dispatch delegate is
	// only observable through the test factory, so tests that dispatch watcher batches before issuing
	// their own first request drive one explicit startup through this helper. The helper forces a
	// successful start (the watcher creation is what the test needs, not the transport state),
	// restores the client state, and clears the sent-message log so the test observes only its own
	// traffic. It is a no-op when the provider already started.
	private static Task StartProviderAndCaptureWorkspaceWatcherAsync(
		LuaLanguageServerIntelliSenseProvider provider,
		FakeLanguageServerClient client)
	{
		string workspaceRootDirectoryPath = GetSingleWorkspaceRoot(provider)
			?? throw new InvalidOperationException("The provider was not created with a single-root workspace watcher capture.");

		return StartProviderAndCaptureWorkspaceWatcherAsync(provider, client, workspaceRootDirectoryPath);
	}

	private static async Task StartProviderAndCaptureWorkspaceWatcherAsync(
		LuaLanguageServerIntelliSenseProvider provider,
		FakeLanguageServerClient client,
		string workspaceRootDirectoryPath)
	{
		if (!s_workspaceWatcherContexts.TryGetValue(provider, out WorkspaceWatcherTestContext? context))
			throw new InvalidOperationException("The provider was not created with a workspace watcher capture.");

		if (context.GetRoot(workspaceRootDirectoryPath)?.Dispatch is not null)
			return;

		bool originalIsReady = client.IsReady;
		bool originalStartResult = client.StartResult;

		try
		{
			client.IsReady = true;
			client.StartResult = true;
			await provider.GetHoverAsync(
				Path.Combine(workspaceRootDirectoryPath, ".watcher-bootstrap.lua"),
				"local bootstrap = true",
				0,
				0).ConfigureAwait(false);
		}
		finally
		{
			client.IsReady = originalIsReady;
			client.StartResult = originalStartResult;
		}

		if (context.GetRoot(workspaceRootDirectoryPath)?.Dispatch is null)
			throw new InvalidOperationException("The workspace watcher factory did not capture a dispatch delegate during the startup request.");

		client.ClearSentMessages();
	}

	private static Task DispatchWorkspaceFileChangesAsync(
		LuaLanguageServerIntelliSenseProvider provider,
		FileChangeBatch batch,
		CancellationToken cancellationToken)
	{
		if (!s_workspaceWatcherContexts.TryGetValue(provider, out WorkspaceWatcherTestContext? context)
			|| context.GetSingleRoot()?.Dispatch is not { } dispatch)
		{
			throw new InvalidOperationException(
				"The workspace watcher was not captured yet; call StartProviderAndCaptureWorkspaceWatcherAsync before dispatching changes.");
		}

		return dispatch(batch, cancellationToken);
	}

	private static Task DispatchWorkspaceFileChangesAsync(
		LuaLanguageServerIntelliSenseProvider provider,
		string workspaceRootDirectoryPath,
		FileChangeBatch batch,
		CancellationToken cancellationToken)
	{
		if (!s_workspaceWatcherContexts.TryGetValue(provider, out WorkspaceWatcherTestContext? context)
			|| context.GetRoot(workspaceRootDirectoryPath)?.Dispatch is not { } dispatch)
		{
			throw new InvalidOperationException(
				$"The workspace watcher for '{workspaceRootDirectoryPath}' was not captured yet; call StartProviderAndCaptureWorkspaceWatcherAsync with that root before dispatching changes.");
		}

		return dispatch(batch, cancellationToken);
	}
}

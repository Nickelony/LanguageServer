using System.Collections.Concurrent;

namespace Nickelony.LanguageServer.Lua;

/// <summary>
/// Implements the Lua IntelliSense provider by synchronizing text documents with LuaLS and caching its diagnostics and semantic tokens.
/// </summary>
/// <remarks>
/// The provider owns the language-server client supplied to its constructor and the workspace watcher created by the
/// provider framework; dispose the provider when the consumer no longer needs it. The disposal order and callback
/// rules are defined by the base class.
/// </remarks>
public sealed partial class LuaLanguageServerIntelliSenseProvider : LanguageServerIntelliSenseProviderBase<LuaDocumentState>, ILuaLanguageServerIntelliSenseProvider
{
	private readonly LuaLanguageServerOptions _options;
	private readonly Func<string, Func<FileChangeBatch, CancellationToken, Task>, Action<WorkspaceFileWatcher, Exception?>, WorkspaceFileWatcher>? _workspaceFileWatcherFactoryOverride;

	private readonly ConcurrentDictionary<string, CancellationTokenSource> _semanticTokenRequests = new(LanguageServerPaths.LocalPathComparer);

	private volatile bool _semanticTokenRequestAdmissionClosed;
	private LuaDocumentStore? _documentStore;

	private EventHandler<SemanticTokensUpdatedEventArgs>? _semanticTokensUpdated;

	/// <inheritdoc/>
	public event EventHandler<SemanticTokensUpdatedEventArgs>? SemanticTokensUpdated
	{
		add => AddAdmittedCallback(ref _semanticTokensUpdated, value);
		remove => RemoveCallback(ref _semanticTokensUpdated, value);
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="LuaLanguageServerIntelliSenseProvider"/> class.
	/// </summary>
	/// <param name="workspaceRootDirectoryPaths">
	/// The root directories of the current Lua script workspace. The first entry is the primary root; every
	/// entry is watched for external changes. Entries may be nested; duplicates are rejected by local-path
	/// identity after normalization.
	/// </param>
	/// <param name="serverExecutablePath">The LuaLS executable path, or <see langword="null"/> when unavailable.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	/// <exception cref="ArgumentException">
	/// <paramref name="workspaceRootDirectoryPaths"/> is empty or contains an empty, whitespace-only, or duplicate entry.
	/// </exception>
	/// <exception cref="ArgumentNullException"><paramref name="workspaceRootDirectoryPaths"/> is <see langword="null"/>.</exception>
	public LuaLanguageServerIntelliSenseProvider(IReadOnlyList<string> workspaceRootDirectoryPaths, string? serverExecutablePath, ILogger<LuaLanguageServerIntelliSenseProvider>? logger = null)
		: this(workspaceRootDirectoryPaths, serverExecutablePath, LuaLanguageServerOptions.Default, logger)
	{ }

	/// <summary>
	/// Initializes a new instance of the <see cref="LuaLanguageServerIntelliSenseProvider"/> class with custom LuaLS settings.
	/// </summary>
	/// <param name="workspaceRootDirectoryPaths">
	/// The root directories of the current Lua script workspace. The first entry is the primary root; every
	/// entry is watched for external changes. Entries may be nested; duplicates are rejected by local-path
	/// identity after normalization.
	/// </param>
	/// <param name="serverExecutablePath">The LuaLS executable path, or <see langword="null"/> when unavailable.</param>
	/// <param name="options">The LuaLS settings overrides to apply for this workspace.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	/// <exception cref="ArgumentException">
	/// <paramref name="workspaceRootDirectoryPaths"/> is empty or contains an empty, whitespace-only, or duplicate entry.
	/// </exception>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="workspaceRootDirectoryPaths"/> or <paramref name="options"/> is <see langword="null"/>.
	/// </exception>
	public LuaLanguageServerIntelliSenseProvider(IReadOnlyList<string> workspaceRootDirectoryPaths, string? serverExecutablePath, LuaLanguageServerOptions options, ILogger<LuaLanguageServerIntelliSenseProvider>? logger = null)
		: this(workspaceRootDirectoryPaths,
			CreateClient(workspaceRootDirectoryPaths, serverExecutablePath, ValidateOptions(options)),
			logger: logger,
			luaOptions: options)
	{ }

	/// <summary>
	/// Initializes a new instance of the <see cref="LuaLanguageServerIntelliSenseProvider"/> class for testing and dependency injection.
	/// </summary>
	/// <param name="workspaceRootDirectoryPaths">
	/// The root directories of the current Lua script workspace, in caller order. Entries may be nested;
	/// duplicates are rejected by local-path identity after normalization.
	/// </param>
	/// <param name="client">The language server client, or <see langword="null"/> when unavailable.</param>
	/// <param name="providerOptions">The provider tunables, or <see langword="null"/> for the framework defaults.</param>
	/// <param name="workspaceFileWatcherFactory">Overrides workspace watcher creation, or <see langword="null"/> for the framework default.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	/// <param name="luaOptions">The LuaLS settings overrides, or <see langword="null"/> for the defaults.</param>
	/// <remarks>
	/// Ownership of <paramref name="client"/> transfers to the provider. The client is disposed when this provider is
	/// disposed; when construction fails after the transfer, the base class disposes the client as construction
	/// unwinds.
	/// </remarks>
	internal LuaLanguageServerIntelliSenseProvider(IReadOnlyList<string> workspaceRootDirectoryPaths, ILanguageServerClient? client,
		LanguageServerProviderOptions? providerOptions = null,
		Func<string, Func<FileChangeBatch, CancellationToken, Task>, Action<WorkspaceFileWatcher, Exception?>, WorkspaceFileWatcher>? workspaceFileWatcherFactory = null,
		ILogger<LuaLanguageServerIntelliSenseProvider>? logger = null,
		LuaLanguageServerOptions? luaOptions = null)
		: base(workspaceRootDirectoryPaths,
			client,
			providerOptions,
			logger)
	{
		_options = luaOptions ?? LuaLanguageServerOptions.Default;
		_workspaceFileWatcherFactoryOverride = workspaceFileWatcherFactory;

		if (Client is not null)
			Client.SemanticTokensRefreshRequested += HandleSemanticTokensRefreshRequested;
	}

	/// <inheritdoc/>
	protected override WorkspaceFileWatcher CreateWorkspaceFileWatcher(
		string workspaceRootDirectoryPath,
		Func<FileChangeBatch, CancellationToken, Task> dispatchAsync,
		Action<WorkspaceFileWatcher, Exception?> onWatcherFailed)
	{
		return _workspaceFileWatcherFactoryOverride?.Invoke(workspaceRootDirectoryPath, dispatchAsync, onWatcherFailed)
			?? base.CreateWorkspaceFileWatcher(workspaceRootDirectoryPath, dispatchAsync, onWatcherFailed);
	}

	/// <inheritdoc/>
	public IReadOnlyList<SemanticToken> GetSemanticTokens(string filePath)
	{
		ArgumentNullException.ThrowIfNull(filePath);

		if (IsDisposed)
			return [];

		if (!LanguageServerPaths.TryNormalizeLocalPath(filePath, out string normalizedFilePath))
		{
			Logger.LogDebug("Lua semantic-token read for '{FilePath}' was dropped because the path is not a usable local path.", filePath);
			return [];
		}

		return DocumentStore.GetSemanticTokens(normalizedFilePath);
	}

	/// <summary>
	/// Gets the tracked-document store as the typed Lua store.
	/// </summary>
	/// <remarks>
	/// The instance is created by <see cref="CreateTrackedDocumentStore"/> and stashed during the base
	/// constructor, so no downcast of the base property is needed.
	/// </remarks>
	private LuaDocumentStore DocumentStore => _documentStore!;

	private static LanguageServerClient? CreateClient(IReadOnlyList<string> workspaceRootDirectoryPaths, string? serverExecutablePath, LuaLanguageServerOptions options)
	{
		// The base constructor validates the roots only after this factory has run, so validate them
		// here first: an invalid root list must fail before a client exists. The empty list is
		// rejected explicitly because the shared path helper accepts it for folderless client
		// sessions, while this provider layer requires at least one root.
		string[] normalizedRoots = LanguageServerPaths.NormalizeWorkspaceRoots(workspaceRootDirectoryPaths);

		if (normalizedRoots.Length == 0)
			throw new ArgumentException("At least one workspace root directory path is required.", nameof(workspaceRootDirectoryPaths));

		if (string.IsNullOrWhiteSpace(serverExecutablePath))
			return null;

		return new LanguageServerClient(workspaceRootDirectoryPaths, serverExecutablePath, new LanguageServerClientOptions(
			() => LuaLanguageServerSettingsFactory.Create(options))
		{
			ClientCapabilitiesProvider = _ => LuaLanguageServerClientCapabilitiesFactory.Create(),
			InitializationOptionsProvider = _ => LuaLanguageServerInitializationOptionsFactory.Create()
		});
	}

	private static LuaLanguageServerOptions ValidateOptions(LuaLanguageServerOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		return options;
	}

	/// <summary>
	/// Resolves the normalized document path a parse closure needs for same-document filtering or edit
	/// targeting.
	/// </summary>
	/// <remarks>
	/// The request pipeline performs the same normalization before invoking a parse closure, so the raw
	/// input path is only the fallback for a request that never reaches its closure.
	/// </remarks>
	/// <param name="filePath">The caller-supplied document path.</param>
	/// <returns>The normalized path, or the raw path when normalization fails.</returns>
	private static string ResolveDocumentFilePath(string filePath)
		=> LanguageServerPaths.TryNormalizeLocalPath(filePath, out string normalizedFilePath) ? normalizedFilePath : filePath;

	private void RaiseSemanticTokensUpdated(string filePath, IReadOnlyList<SemanticToken> semanticTokens)
		=> RaiseSubscribers(
			() => _semanticTokensUpdated,
			handler => ((EventHandler<SemanticTokensUpdatedEventArgs>)handler)(this, new SemanticTokensUpdatedEventArgs(filePath, semanticTokens)),
			"Lua semantic-token subscriber");
}

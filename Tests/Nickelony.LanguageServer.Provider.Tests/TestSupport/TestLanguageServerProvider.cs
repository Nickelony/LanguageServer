using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Completion;
using Nickelony.IDEKit.IntelliSense.Diagnostics;
using Nickelony.IDEKit.IntelliSense.DocumentSymbols;
using Nickelony.IDEKit.IntelliSense.Hover;
using Nickelony.IDEKit.IntelliSense.Navigation;
using Nickelony.IDEKit.IntelliSense.Signatures;
using System.Collections.Concurrent;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Minimal provider for framework tests: implements every hook with deterministic test behavior and records the
/// hook invocations the tests assert against. Requests are routed through the framework request pipeline so its
/// document synchronization and dispatch behavior is exercised.
/// </summary>
internal class TestLanguageServerProvider : LanguageServerIntelliSenseProviderBase<TestDocumentState>
{
	private readonly ConcurrentDictionary<string, IReadOnlyList<TextDiagnostic>> _diagnostics = new(LanguageServerPaths.LocalPathComparer);
	private readonly WorkspaceFileWatcherFactory? _workspaceFileWatcherFactory;
	private TestDocumentStore? _documentStore;

	/// <summary>
	/// Initializes a new instance of the <see cref="TestLanguageServerProvider"/> class.
	/// </summary>
	/// <param name="workspaceRootDirectoryPaths">The workspace root directories.</param>
	/// <param name="client">The language-server client, or <see langword="null"/> when unavailable.</param>
	/// <param name="options">The provider tunables, or <see langword="null"/> for the defaults.</param>
	/// <param name="workspaceFileWatcherFactory">A workspace file watcher factory, used for testing.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	public TestLanguageServerProvider(
		IReadOnlyList<string> workspaceRootDirectoryPaths,
		ILanguageServerClient? client,
		LanguageServerProviderOptions? options = null,
		WorkspaceFileWatcherFactory? workspaceFileWatcherFactory = null,
		ILogger? logger = null)
		: base(workspaceRootDirectoryPaths, client, options, logger)
	{
		_workspaceFileWatcherFactory = workspaceFileWatcherFactory;
	}

	/// <inheritdoc/>
	protected override WorkspaceFileWatcher CreateWorkspaceFileWatcher(
		string workspaceRootDirectoryPath,
		Func<FileChangeBatch, CancellationToken, Task> dispatchAsync,
		Action<WorkspaceFileWatcher, Exception?> onWatcherFailed)
	{
		return _workspaceFileWatcherFactory is null
			? base.CreateWorkspaceFileWatcher(workspaceRootDirectoryPath, dispatchAsync, onWatcherFailed)
			: _workspaceFileWatcherFactory(workspaceRootDirectoryPath, dispatchAsync, onWatcherFailed);
	}

	/// <summary>
	/// Gets the versions of documents whose post-synchronization hook ran.
	/// </summary>
	public ConcurrentQueue<int> SynchronizedDocumentVersions { get; } = new();

	/// <summary>
	/// Gets the file paths reported through the tracked-document invalidation hook.
	/// </summary>
	public ConcurrentQueue<string> InvalidatedPaths { get; } = new();

	/// <summary>
	/// Gets the file paths reported through the rename hook.
	/// </summary>
	public ConcurrentQueue<string> RenamedPaths { get; } = new();

	/// <summary>
	/// Gets or sets a factory for the diagnostics the diagnostics hook returns; when unset, the hook returns a
	/// single warning diagnostic whose message carries the tracked document version.
	/// </summary>
	public Func<string, IReadOnlyList<TextDiagnostic>>? DiagnosticsFactory { get; set; }

	/// <summary>
	/// Gets the number of <see cref="OnDisposing"/> invocations.
	/// </summary>
	public int OnDisposingCallCount { get; private set; }

	/// <summary>
	/// Gets a value indicating whether the owned client was already disposed when <see cref="OnDisposing"/> ran.
	/// </summary>
	public bool? ClientWasDisposedAtOnDisposing { get; private set; }

	/// <summary>
	/// Gets the position of the most recent request sent through the position-request pipeline.
	/// </summary>
	public ProtocolPosition? LastRequestedPosition { get; private set; }

	/// <summary>
	/// Gets or sets a value indicating whether the post-synchronization hook throws, for containment tests.
	/// </summary>
	public bool ThrowOnSynchronizedHook { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the tracked-diagnostics hook throws, for containment tests.
	/// </summary>
	public bool ThrowOnTrackedDiagnostics { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the configuration-path probe throws, for containment tests.
	/// </summary>
	public bool ThrowOnConfigurationPathProbe { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the settings-payload hook throws, for containment tests.
	/// </summary>
	public bool ThrowOnSettingsPayload { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the startup-failure hook throws, for containment tests.
	/// </summary>
	public bool ThrowOnStartupFailureHook { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the missing-client failure hook throws, for containment tests.
	/// </summary>
	public bool ThrowOnMissingClientFailureHook { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the rename hook throws, for containment tests.
	/// </summary>
	public bool ThrowOnRenamedHook { get; set; }

	/// <inheritdoc/>
	protected override void OnDisposing()
	{
		OnDisposingCallCount++;
		ClientWasDisposedAtOnDisposing = (Client as FakeLanguageServerClient)?.IsDisposed;
	}

	/// <summary>
	/// Sends a request through the position-based request pipeline and records the clamped position.
	/// </summary>
	/// <param name="filePath">The absolute local file path of the document.</param>
	/// <param name="content">The current document content.</param>
	/// <param name="line">The zero-based line index.</param>
	/// <param name="column">The zero-based column index.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The documented fallback value of the request.</returns>
	public Task<TextHoverInfo?> GetHoverAtPositionAsync(string filePath, string content, int line, int column, CancellationToken cancellationToken = default)
	{
		return SendDocumentPositionRequestAsync<object?, TextHoverInfo?>(
			filePath, content, line, column, "test/positionHover",
			buildParameters: (textDocument, position) =>
			{
				LastRequestedPosition = position;
				return textDocument;
			},
			parseResponse: static _ => null,
			fallbackValue: null,
			cancellationToken);
	}

	/// <summary>
	/// Sends a request through the document-request pipeline with a caller-supplied capability gate.
	/// </summary>
	/// <param name="filePath">The absolute local file path of the document.</param>
	/// <param name="content">The current document content.</param>
	/// <param name="supportsRequest">The capability gate to evaluate.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The documented fallback value of the request.</returns>
	public Task<TextHoverInfo?> SendGatedRequestAsync(string filePath, string content, Func<ILanguageServerClient, bool> supportsRequest,
		CancellationToken cancellationToken = default)
	{
		return SendDocumentRequestAsync<object?, TextHoverInfo?>(
			filePath, content, "test/gatedRequest",
			supportsRequest,
			buildParameters: static textDocument => textDocument,
			parseResponse: static _ => null,
			fallbackValue: null,
			cancellationToken);
	}

	/// <summary>
	/// Sends a request through the document-request pipeline and returns the parser result, so tests can verify
	/// that a real response reaches the response parser instead of only the documented fallback value.
	/// </summary>
	/// <param name="filePath">The absolute local file path of the document.</param>
	/// <param name="content">The current document content.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The parsed response text, or the documented fallback value.</returns>
	public Task<string?> SendParsedRequestAsync(string filePath, string content, CancellationToken cancellationToken = default)
	{
		return SendDocumentRequestAsync<object?, string?>(
			filePath, content, "test/parsedRequest",
			supportsRequest: static _ => true,
			buildParameters: static textDocument => textDocument,
			parseResponse: static response => response?.ToString(),
			fallbackValue: null,
			cancellationToken);
	}

	/// <summary>
	/// Sends a request whose expected response payload is a non-nullable struct, so tests can verify that a
	/// dispatcher fallback returns the caller's fallback value instead of a parsed default struct.
	/// </summary>
	/// <param name="filePath">The absolute local file path of the document.</param>
	/// <param name="content">The current document content.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The parsed response text, or the documented fallback value.</returns>
	public Task<string?> SendStructResponseRequestAsync(string filePath, string content, CancellationToken cancellationToken = default)
	{
		return SendDocumentRequestAsync<TestStructResponse, string?>(
			filePath, content, "test/structResponse",
			supportsRequest: static _ => true,
			buildParameters: static textDocument => textDocument,
			parseResponse: static response => $"parsed:{response.Value}",
			fallbackValue: "fallback",
			cancellationToken);
	}

	/// <summary>
	/// Gets the current tracked snapshot for one document path, for tests that drive internal framework seams.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <returns>The tracked snapshot, or <see langword="null"/> when the document is not tracked.</returns>
	public DocumentSnapshot? GetTrackedSnapshot(string filePath)
		=> _documentStore!.GetDocumentSnapshot(filePath);

	/// <inheritdoc/>
	protected override string ProviderDisplayName => "Test";

	/// <inheritdoc/>
	protected override string LanguageId => "test";

	/// <inheritdoc/>
	protected override IReadOnlyList<WorkspaceWatchSpecification> CreateWatchSpecifications()
		=> [new WorkspaceWatchSpecification("*.test", IncludeSubdirectories: true)];

	/// <inheritdoc/>
	protected override TrackedDocumentStore<TestDocumentState> CreateTrackedDocumentStore()
	{
		_documentStore = new TestDocumentStore();
		return _documentStore;
	}

	/// <inheritdoc/>
	protected override bool IsConfigurationPath(string normalizedPath)
	{
		if (ThrowOnConfigurationPathProbe)
			throw new InvalidOperationException("Simulated configuration-path probe failure.");

		return Path.GetFileName(normalizedPath).StartsWith(".test", StringComparison.Ordinal);
	}

	/// <inheritdoc/>
	protected override object CreateSettingsPayload()
	{
		if (ThrowOnSettingsPayload)
			throw new InvalidOperationException("Simulated settings-payload failure.");

		return new TestSettingsPayload(true);
	}

	/// <inheritdoc/>
	protected override LanguageServerStartupFailure CreateStartupFailure(bool isPermanentFailure)
	{
		if (ThrowOnStartupFailureHook)
			throw new InvalidOperationException("Simulated startup-failure hook failure.");

		return isPermanentFailure
			? new LanguageServerStartupFailure("The test language server failed to start repeatedly.", IsPersistent: true)
			: new LanguageServerStartupFailure("The test language server failed to start.", IsPersistent: false);
	}

	/// <inheritdoc/>
	protected override LanguageServerStartupFailure CreateMissingClientFailure()
	{
		if (ThrowOnMissingClientFailureHook)
			throw new InvalidOperationException("Simulated missing-client failure hook failure.");

		return new("The test language server executable is unavailable.", IsPersistent: true);
	}

	/// <inheritdoc/>
	protected override IReadOnlyList<TextDiagnostic> GetTrackedDiagnostics(string normalizedFilePath)
	{
		if (ThrowOnTrackedDiagnostics)
			throw new InvalidOperationException("Simulated tracked-diagnostics failure.");

		return _diagnostics.TryGetValue(normalizedFilePath, out IReadOnlyList<TextDiagnostic>? diagnostics) ? diagnostics : [];
	}

	/// <inheritdoc/>
	protected override void InvalidateTrackedDocumentSynchronization(string filePath)
		=> _documentStore!.Invalidate(filePath);

	/// <inheritdoc/>
	protected override IReadOnlyList<TextDiagnostic>? HandleDiagnosticsPayload(
		string filePath,
		PublishDiagnosticsParams parameters,
		DocumentSnapshot document)
	{
		IReadOnlyList<TextDiagnostic> diagnostics = DiagnosticsFactory?.Invoke(filePath)
			?? [new TextDiagnostic(TextDiagnosticSeverity.Warning, $"test:{document.Version}", 0, 0)];

		_diagnostics[filePath] = diagnostics;
		return diagnostics;
	}

	/// <inheritdoc/>
	protected override Task OnDocumentSynchronizedAsync(DocumentSnapshot document, CancellationToken cancellationToken)
	{
		if (ThrowOnSynchronizedHook)
			throw new InvalidOperationException("Simulated synchronization-hook failure.");

		SynchronizedDocumentVersions.Enqueue(document.Version);
		return Task.CompletedTask;
	}

	/// <inheritdoc/>
	protected override void OnTrackedDocumentInvalidated(string filePath)
		=> InvalidatedPaths.Enqueue(filePath);

	/// <inheritdoc/>
	protected override void OnDocumentRenamed(string filePath)
	{
		RenamedPaths.Enqueue(filePath);

		if (ThrowOnRenamedHook)
			throw new InvalidOperationException("Simulated rename-hook failure.");
	}

	/// <inheritdoc/>
	public override Task<IReadOnlyList<TextCompletionItem>> GetCompletionItemsAsync(string filePath, string content,
		int line, int column, char? triggerCharacter = null, CancellationToken cancellationToken = default)
		=> Task.FromResult<IReadOnlyList<TextCompletionItem>>([]);

	/// <inheritdoc/>
	public override Task<TextHoverInfo?> GetHoverAsync(string filePath, string content,
		int line, int column, CancellationToken cancellationToken = default)
	{
		return SendDocumentRequestAsync<object?, TextHoverInfo?>(
			filePath, content, "test/hover",
			supportsRequest: static _ => true,
			buildParameters: static textDocument => textDocument,
			parseResponse: static _ => null,
			fallbackValue: null,
			cancellationToken);
	}

	/// <inheritdoc/>
	public override Task<TextDefinitionLocation?> GetDefinitionAsync(string filePath, string content,
		int line, int column, CancellationToken cancellationToken = default)
		=> Task.FromResult<TextDefinitionLocation?>(null);

	/// <inheritdoc/>
	public override Task<TextSignatureHelp?> GetSignatureHelpAsync(string filePath, string content,
		int line, int column, TextSignatureHelpContext? context = null, CancellationToken cancellationToken = default)
		=> Task.FromResult<TextSignatureHelp?>(null);

	/// <inheritdoc/>
	public override Task<IReadOnlyList<TextDocumentSymbol>> GetDocumentSymbolsAsync(string filePath, string content,
		CancellationToken cancellationToken = default)
		=> Task.FromResult<IReadOnlyList<TextDocumentSymbol>>([]);

	/// <inheritdoc/>
	public override Task<IReadOnlyList<TextCodeAction>> GetCodeActionsAsync(TextCodeActionRequest request,
		CancellationToken cancellationToken = default)
		=> Task.FromResult<IReadOnlyList<TextCodeAction>>([]);

	/// <inheritdoc/>
	public override Task<IReadOnlyList<TextReferenceLocation>> GetReferencesAsync(TextReferenceRequest request,
		CancellationToken cancellationToken = default)
		=> Task.FromResult<IReadOnlyList<TextReferenceLocation>>([]);

	/// <inheritdoc/>
	public override Task<TextWorkspaceEdit?> RenameSymbolAsync(TextRenameRequest request,
		CancellationToken cancellationToken = default)
		=> Task.FromResult<TextWorkspaceEdit?>(null);

	/// <inheritdoc/>
	public override Task<TextWorkspaceEdit?> FormatDocumentAsync(TextFormatRequest request,
		CancellationToken cancellationToken = default)
		=> Task.FromResult<TextWorkspaceEdit?>(null);
}

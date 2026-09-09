using Nickelony.IDEKit.IntelliSense.Diagnostics;

namespace Nickelony.LanguageServer.Lua;

public sealed partial class LuaLanguageServerIntelliSenseProvider
{
	/// <inheritdoc/>
	protected override string ProviderDisplayName => "Lua";

	/// <inheritdoc/>
	protected override string LanguageId => "lua";

	/// <inheritdoc/>
	protected override IReadOnlyList<WorkspaceWatchSpecification> CreateWatchSpecifications()
		=> LuaWorkspaceConventions.WatchSpecifications;

	/// <inheritdoc/>
	protected override TrackedDocumentStore<LuaDocumentState> CreateTrackedDocumentStore()
	{
		// The framework owns the store instance returned here; stash it so the provider can use the
		// typed store without downcasting the base property on every access. The hook runs during the
		// base constructor, before any member can read the field.
		_documentStore = new LuaDocumentStore();
		return _documentStore;
	}

	/// <inheritdoc/>
	protected override bool IsConfigurationPath(string normalizedPath)
		=> LuaWorkspaceConventions.IsConfigurationPath(normalizedPath);

	/// <inheritdoc/>
	protected override object CreateSettingsPayload()
		=> LuaLanguageServerSettingsFactory.Create(_options);

	/// <inheritdoc/>
	protected override LanguageServerStartupFailure CreateStartupFailure(bool isPermanentFailure)
	{
		return isPermanentFailure
			? new LanguageServerStartupFailure(
				"The configured Lua language server failed to start repeatedly, so Lua IntelliSense is now disabled until the provider is recreated.",
				true)
			: new LanguageServerStartupFailure(
				"The configured Lua language server failed to start. Lua IntelliSense will remain unavailable until the language server can be started successfully. The provider retries automatically when Lua IntelliSense is requested again.",
				false);
	}

	/// <inheritdoc/>
	protected override LanguageServerStartupFailure CreateMissingClientFailure()
		=> new(
			"The Lua language server executable is unavailable, so Lua IntelliSense is disabled until the provider is recreated with a valid executable path.",
			IsPersistent: true);

	/// <inheritdoc/>
	protected override IReadOnlyList<TextDiagnostic> GetTrackedDiagnostics(string normalizedFilePath)
		=> DocumentStore.GetDiagnostics(normalizedFilePath);

	/// <inheritdoc/>
	protected override void InvalidateTrackedDocumentSynchronization(string filePath)
		=> DocumentStore.InvalidateServerSynchronization(filePath);

	/// <inheritdoc/>
	protected override void OnTrackedDocumentInvalidated(string filePath)
		=> CancelSemanticTokenRequest(filePath);

	/// <inheritdoc/>
	protected override void OnDocumentRenamed(string filePath)
		=> RaiseSemanticTokensUpdated(filePath, DocumentStore.GetSemanticTokens(filePath));

	/// <inheritdoc/>
	protected override void OnDisposing()
	{
		if (Client is not null)
			Client.SemanticTokensRefreshRequested -= HandleSemanticTokensRefreshRequested;

		// Closes request admission and cancels every in-flight semantic-token request; refreshes that
		// already passed the provider's checks abort through their linked token.
		CancelAllSemanticTokenRequests();
	}
}

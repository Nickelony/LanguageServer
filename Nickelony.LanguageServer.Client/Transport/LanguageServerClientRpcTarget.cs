using StreamJsonRpc;
using System.Text.Json;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Exposes the host callbacks required by the language server over JSON-RPC.
/// </summary>
internal sealed class LanguageServerClientRpcTarget
{
	/// <summary>
	/// Provides session lookup and transport-generation fencing for server callbacks.
	/// </summary>
	private readonly LanguageServerCapabilityStore _capabilityStore;

	/// <summary>
	/// Queues semantic-token refresh and diagnostics callbacks for subscriber dispatch.
	/// </summary>
	private readonly LanguageServerDiagnosticsRouter _diagnosticsRouter;

	/// <summary>
	/// Builds configuration responses from the cached settings snapshot.
	/// </summary>
	private readonly LanguageServerProtocolForwarder _protocolForwarder;

	/// <summary>
	/// The logger for server callback diagnostics.
	/// </summary>
	private readonly ILogger _logger;

	/// <summary>
	/// The workspace folder descriptors advertised to the language server.
	/// </summary>
	private readonly IReadOnlyList<WorkspaceFolder> _workspaceFolders;

	/// <summary>
	/// Captures the transport generation that delivered the callback.
	/// </summary>
	private readonly long _transportGeneration;

	/// <summary>
	/// Initializes a new instance of the <see cref="LanguageServerClientRpcTarget"/> class.
	/// </summary>
	/// <param name="capabilityStore">Provides session lookup and transport-generation fencing.</param>
	/// <param name="diagnosticsRouter">Queues semantic-token refresh and diagnostics callbacks.</param>
	/// <param name="protocolForwarder">Builds configuration responses from the cached settings snapshot.</param>
	/// <param name="transportGeneration">The transport generation associated with the callback target.</param>
	/// <param name="workspaceFolders">The workspace folder descriptors advertised to the language server.</param>
	/// <param name="logger">The logger instance for server callback diagnostics.</param>
	internal LanguageServerClientRpcTarget(LanguageServerCapabilityStore capabilityStore,
		LanguageServerDiagnosticsRouter diagnosticsRouter, LanguageServerProtocolForwarder protocolForwarder,
		long transportGeneration, IReadOnlyList<WorkspaceFolder> workspaceFolders, ILogger logger)
	{
		_capabilityStore = capabilityStore;
		_diagnosticsRouter = diagnosticsRouter;
		_protocolForwarder = protocolForwarder;
		_logger = logger;
		_workspaceFolders = workspaceFolders;
		_transportGeneration = transportGeneration;
	}

	/// <summary>
	/// Supplies configuration sections requested by the language server.
	/// </summary>
	/// <param name="parameters">The requested configuration sections.</param>
	/// <returns>The requested configuration objects.</returns>
	[JsonRpcMethod("workspace/configuration", UseSingleObjectParameterDeserialization = true)]
	public object?[] WorkspaceConfiguration(WorkspaceConfigurationParams parameters)
	{
		if (!_capabilityStore.CanAcceptServerCallbacksForGeneration(_transportGeneration))
			return new object?[(parameters.Items ?? []).Length];

		return _protocolForwarder.BuildConfigurationResponse(parameters);
	}

	/// <summary>
	/// Returns the workspace folders advertised to the language server.
	/// </summary>
	/// <returns>The current workspace folder array.</returns>
	[JsonRpcMethod("workspace/workspaceFolders")]
	public WorkspaceFolder[] WorkspaceFolders()
	{
		if (!_capabilityStore.CanAcceptServerCallbacksForGeneration(_transportGeneration))
			return [];

		return [.. _workspaceFolders];
	}

	/// <summary>
	/// Acknowledges a semantic tokens refresh request and notifies the owner.
	/// </summary>
	/// <returns>A completed task that resolves to <see langword="null"/>.</returns>
	[JsonRpcMethod("workspace/semanticTokens/refresh")]
	public Task<object?> RefreshSemanticTokensAsync()
	{
		if (!_capabilityStore.CanAcceptServerCallbacksForGeneration(_transportGeneration))
			return Task.FromResult<object?>(null);

		_diagnosticsRouter.QueueSemanticTokensRefreshCallback();

		return Task.FromResult<object?>(null);
	}

	/// <summary>
	/// Ignores dynamic capability registration because the client advertises it as unsupported.
	/// </summary>
	/// <param name="parameters">The capability registration payload.</param>
	/// <returns><see langword="null"/>.</returns>
	[JsonRpcMethod("client/registerCapability", UseSingleObjectParameterDeserialization = true)]
	public object? RegisterCapability(CapabilityRegistrationParams parameters)
	{
		if (parameters.Registrations is null || parameters.Registrations.Length == 0)
			return null;

		return IgnoreUnsupportedDynamicCapability(
			"client/registerCapability",
			DescribeCapabilityRegistrations(parameters.Registrations));
	}

	/// <summary>
	/// Ignores dynamic capability unregistration because the client advertises it as unsupported.
	/// </summary>
	/// <param name="parameters">The capability unregistration payload.</param>
	/// <returns><see langword="null"/>.</returns>
	[JsonRpcMethod("client/unregisterCapability", UseSingleObjectParameterDeserialization = true)]
	public object? UnregisterCapability(CapabilityUnregistrationParams parameters)
	{
		if (parameters.Unregistrations is null || parameters.Unregistrations.Length == 0)
			return null;

		return IgnoreUnsupportedDynamicCapability(
			"client/unregisterCapability",
			DescribeCapabilityUnregistrations(parameters.Unregistrations));
	}

	/// <summary>
	/// Logs and ignores a dynamic capability request that this client deliberately does not support.
	/// </summary>
	/// <param name="method">The JSON-RPC method name.</param>
	/// <param name="requestedCapabilities">The formatted requested capability names.</param>
	/// <returns><see langword="null"/>.</returns>
	private object? IgnoreUnsupportedDynamicCapability(string method, string requestedCapabilities)
	{
		if (!_capabilityStore.IsCurrentTransportGeneration(_transportGeneration))
		{
			_logger.LogDebug(
				"Ignoring unsupported dynamic capability request '{Method}' from stale language server transport generation {Generation}. Requested capabilities: {Capabilities}",
				method,
				_transportGeneration,
				requestedCapabilities);

			return null;
		}

		_logger.LogWarning(
			"Ignoring unsupported dynamic capability request '{Method}' on transport generation {Generation} because the client advertises dynamicRegistration = false. Requested capabilities: {Capabilities}",
			method,
			_transportGeneration,
			requestedCapabilities);

		return null;
	}

	/// <summary>
	/// Formats one capability-registration payload array for diagnostic logging.
	/// </summary>
	/// <param name="registrations">The capability registrations to describe.</param>
	/// <returns>The comma-separated method list.</returns>
	private static string DescribeCapabilityRegistrations(CapabilityRegistrationPayload[] registrations)
	{
		return string.Join(", ",
			Array.ConvertAll(registrations, static registration =>
				string.IsNullOrWhiteSpace(registration.Method) ? "<unknown>" : registration.Method));
	}

	/// <summary>
	/// Formats one capability-unregistration payload array for diagnostic logging.
	/// </summary>
	/// <param name="unregistrations">The capability unregistrations to describe.</param>
	/// <returns>The comma-separated method list.</returns>
	private static string DescribeCapabilityUnregistrations(CapabilityUnregistrationPayload[] unregistrations)
	{
		return string.Join(", ",
			Array.ConvertAll(unregistrations, static unregistration =>
				string.IsNullOrWhiteSpace(unregistration.Method) ? "<unknown>" : unregistration.Method));
	}

	/// <summary>
	/// Acknowledges work-done progress creation requests without creating a client-side progress sink.
	/// </summary>
	/// <param name="parameters">The protocol payload ignored by the host.</param>
	/// <returns><see langword="null"/>.</returns>
	[JsonRpcMethod("window/workDoneProgress/create", UseSingleObjectParameterDeserialization = true)]
	public object? CreateWorkDoneProgress(JsonElement parameters)
	{
		LogIgnoredUnsupportedCallback("window/workDoneProgress/create", "this client does not expose a client-side progress sink");
		return null;
	}

	/// <summary>
	/// Queues diagnostics published by the language server.
	/// </summary>
	/// <param name="parameters">The diagnostics notification payload.</param>
	[JsonRpcMethod("textDocument/publishDiagnostics", UseSingleObjectParameterDeserialization = true)]
	public void PublishDiagnostics(PublishDiagnosticsParams parameters)
	{
		if (!_capabilityStore.CanAcceptServerCallbacksForGeneration(_transportGeneration))
			return;

		_diagnosticsRouter.RaiseDiagnosticsPublished(_transportGeneration, parameters);
	}

	/// <summary>
	/// Logs a non-modal server message through the host logger.
	/// </summary>
	/// <param name="parameters">The window message payload.</param>
	[JsonRpcMethod("window/logMessage", UseSingleObjectParameterDeserialization = true)]
	public void LogMessage(WindowMessageParams parameters)
		=> LogServerMessage("window/logMessage", parameters);

	/// <summary>
	/// Logs a server message through the host logger.
	/// </summary>
	/// <param name="parameters">The window message payload.</param>
	[JsonRpcMethod("window/showMessage", UseSingleObjectParameterDeserialization = true)]
	public void ShowMessage(WindowMessageParams parameters)
		=> LogServerMessage("window/showMessage", parameters);

	/// <summary>
	/// Ignores telemetry events that the host does not surface.
	/// </summary>
	/// <param name="parameters">The protocol payload ignored by the host.</param>
	[JsonRpcMethod("telemetry/event", UseSingleObjectParameterDeserialization = true)]
	public void TelemetryEvent(JsonElement parameters)
		=> LogIgnoredUnsupportedCallback("telemetry/event", "this client does not surface server telemetry events");

	/// <summary>
	/// Ignores generic progress notifications that the host does not surface.
	/// </summary>
	/// <param name="parameters">The protocol payload ignored by the host.</param>
	[JsonRpcMethod("$/progress", UseSingleObjectParameterDeserialization = true)]
	public void Progress(JsonElement parameters)
		=> LogIgnoredUnsupportedCallback("$/progress", "this client does not surface generic progress notifications");

	/// <summary>
	/// Ignores nonstandard hello notifications that some language servers emit during startup.
	/// </summary>
	/// <param name="parameters">The protocol payload ignored by the host.</param>
	[JsonRpcMethod("$/hello", UseSingleObjectParameterDeserialization = true)]
	public void Hello(JsonElement parameters)
		=> LogIgnoredUnsupportedCallback("$/hello", "this client does not surface nonstandard startup notifications");

	/// <summary>
	/// Logs one unsupported server callback without surfacing it to the host.
	/// </summary>
	/// <param name="method">The callback method name.</param>
	/// <param name="reason">Why the callback is ignored.</param>
	private void LogIgnoredUnsupportedCallback(string method, string reason)
	{
		if (!_capabilityStore.IsCurrentTransportGeneration(_transportGeneration))
		{
			_logger.LogDebug(
				"Ignoring unsupported server callback '{Method}' from stale language server transport generation {Generation}. Reason: {Reason}",
				method,
				_transportGeneration,
				reason);

			return;
		}

		_logger.LogDebug(
			"Ignoring unsupported server callback '{Method}' on transport generation {Generation}. Reason: {Reason}",
			method,
			_transportGeneration,
			reason);
	}

	/// <summary>
	/// Logs a window message or log message notification from the language server.
	/// </summary>
	/// <param name="method">The originating method name.</param>
	/// <param name="parameters">The message payload.</param>
	private void LogServerMessage(string method, WindowMessageParams parameters)
	{
		string? messageText = parameters.Message;

		if (string.IsNullOrWhiteSpace(messageText))
			return;

		// A missing severity is a log message (the least intrusive protocol level); an unknown numeric
		// value falls through to the same case.
		MessageType messageType = parameters.Type ?? MessageType.Log;

		switch (messageType)
		{
			case MessageType.Error:
				_logger.LogError("[LS {Method}] {Message}", method, messageText);
				break;

			case MessageType.Warning:
				_logger.LogWarning("[LS {Method}] {Message}", method, messageText);
				break;

			case MessageType.Information:
				_logger.LogInformation("[LS {Method}] {Message}", method, messageText);
				break;

			default:
				_logger.LogDebug("[LS {Method}] {Message}", method, messageText);
				break;
		}
	}
}

using System.Text.Json;

namespace Nickelony.LanguageServer.Lua.Tests;

internal sealed class FakeLanguageServerClient : ILanguageServerClient
{
	private static readonly JsonSerializerOptions s_responseDeserializationOptions = new()
	{
		// The real client deserializes with case-insensitive property matching, so the fake accepts
		// the same payload casing a live server produces.
		PropertyNameCaseInsensitive = true
	};

	private readonly object _syncRoot = new();
	private readonly List<(string Method, JsonElement Parameters)> _sentNotifications = [];
	private readonly List<(string Method, JsonElement Parameters)> _sentRequests = [];
	private readonly List<string> _sentMethodNames = [];
	private readonly Queue<JsonElement> _semanticTokensFullResponses = [];
	private readonly Queue<(TaskCompletionSource<bool> Gate, JsonElement? Response)> _availableSemanticTokensFullRequestGates = new();
	private readonly Queue<TaskCompletionSource<bool>> _pendingSemanticTokensFullRequestGates = new();
	private TaskCompletionSource<bool>? _hoverRequestGate;
	private TaskCompletionSource<bool>? _openNotificationGate;
	private TaskCompletionSource<bool>? _startGate;
	private TaskCompletionSource<bool>? _changeNotificationGate;
	private TaskCompletionSource<bool>? _watchedFilesNotificationGate;
	private long _lastIssuedTransportGeneration;
	private bool _isDisposed;
	private bool _capabilitySnapshotPublished;
	private TextDocumentSyncKind _configuredTextDocumentSyncKind = TextDocumentSyncKind.Incremental;
	private bool _supportsCompletionResolve;
	private bool _supportsDocumentSymbols = true;
	private bool _supportsCodeActions = true;
	private bool _supportsReferences = true;
	private bool _supportsRename = true;
	private bool _supportsFormatting = true;
	private bool _supportsHover = true;
	private bool _supportsDefinition = true;
	private bool _supportsSignatureHelp = true;
	private bool _supportsSemanticTokensFull = true;
	private bool _supportsSemanticTokensDelta;
	private readonly TaskCompletionSource<bool> _changeNotificationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource<bool> _closeNotificationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public bool IsReady { get; set; } = true;
	public long TransportGeneration { get; private set; }
	public bool StartResult { get; set; } = true;

	/// <summary>
	/// Gets or sets the startup failure the fake reports. Defaults to <see langword="null"/>; tests that
	/// exercise startup-failure paths assign their own exception.
	/// </summary>
	public Exception? LastStartupException { get; set; }

	public JsonElement CodeActionsResponse { get; set; }
	public JsonElement CompletionResponse { get; set; }
	public JsonElement CompletionResolveResponse { get; set; }
	public JsonElement DefinitionResponse { get; set; }
	public JsonElement DocumentSymbolsResponse { get; set; }
	public JsonElement FormattingResponse { get; set; }

	/// <summary>
	/// Gets or sets the hover response. Defaults to a JSON <c>null</c> payload, the "no hover content"
	/// answer that tests driving document synchronization through hover requests rely on; tests that
	/// assert hover content assign their own payload.
	/// </summary>
	public JsonElement HoverResponse { get; set; } = JsonSerializer.SerializeToElement<object?>(null);

	public JsonElement ReferencesResponse { get; set; }
	public JsonElement RenameResponse { get; set; }
	public JsonElement SignatureHelpResponse { get; set; }

	/// <summary>
	/// Gets or sets the text-document synchronization mode the fake negotiates on a successful start.
	/// Reads mirror the real client contract: <see cref="TextDocumentSyncKind.None"/> until a start
	/// succeeds, and again after the active transport becomes unhealthy.
	/// </summary>
	public TextDocumentSyncKind TextDocumentSyncKind
	{
		get => _capabilitySnapshotPublished ? _configuredTextDocumentSyncKind : TextDocumentSyncKind.None;
		set => _configuredTextDocumentSyncKind = value;
	}

	public IReadOnlyList<string> SemanticTokenTypes { get; set; } = [];
	public IReadOnlyList<string> SemanticTokenModifiers { get; set; } = [];

	/// <summary>
	/// Gets or sets a value indicating whether the fake negotiates completion-item resolve support on a
	/// successful start. Reads are <see langword="false"/> until a start succeeds, and again after the
	/// active transport becomes unhealthy, mirroring the real client contract.
	/// </summary>
	public bool SupportsCompletionResolve
	{
		get => _capabilitySnapshotPublished && _supportsCompletionResolve;
		set => _supportsCompletionResolve = value;
	}

	/// <inheritdoc cref="SupportsCompletionResolve"/>
	public bool SupportsDocumentSymbols
	{
		get => _capabilitySnapshotPublished && _supportsDocumentSymbols;
		set => _supportsDocumentSymbols = value;
	}

	/// <inheritdoc cref="SupportsCompletionResolve"/>
	public bool SupportsCodeActions
	{
		get => _capabilitySnapshotPublished && _supportsCodeActions;
		set => _supportsCodeActions = value;
	}

	/// <inheritdoc cref="SupportsCompletionResolve"/>
	public bool SupportsReferences
	{
		get => _capabilitySnapshotPublished && _supportsReferences;
		set => _supportsReferences = value;
	}

	/// <inheritdoc cref="SupportsCompletionResolve"/>
	public bool SupportsRename
	{
		get => _capabilitySnapshotPublished && _supportsRename;
		set => _supportsRename = value;
	}

	/// <inheritdoc cref="SupportsCompletionResolve"/>
	public bool SupportsFormatting
	{
		get => _capabilitySnapshotPublished && _supportsFormatting;
		set => _supportsFormatting = value;
	}

	/// <inheritdoc cref="SupportsCompletionResolve"/>
	public bool SupportsHover
	{
		get => _capabilitySnapshotPublished && _supportsHover;
		set => _supportsHover = value;
	}

	/// <inheritdoc cref="SupportsCompletionResolve"/>
	public bool SupportsDefinition
	{
		get => _capabilitySnapshotPublished && _supportsDefinition;
		set => _supportsDefinition = value;
	}

	/// <inheritdoc cref="SupportsCompletionResolve"/>
	public bool SupportsSignatureHelp
	{
		get => _capabilitySnapshotPublished && _supportsSignatureHelp;
		set => _supportsSignatureHelp = value;
	}

	/// <inheritdoc cref="SupportsCompletionResolve"/>
	public bool SupportsSemanticTokensFull
	{
		get => _capabilitySnapshotPublished && _supportsSemanticTokensFull;
		set => _supportsSemanticTokensFull = value;
	}

	/// <inheritdoc cref="SupportsCompletionResolve"/>
	public bool SupportsSemanticTokensDelta
	{
		get => _capabilitySnapshotPublished && _supportsSemanticTokensDelta;
		set => _supportsSemanticTokensDelta = value;
	}

	public bool FailStartWhenCancellationRequested { get; set; }
	public bool CancelNextHoverRequestWithoutTimeout { get; set; }
	public Action? BeforeReturningStartResult { get; set; }
	public Action? BeforeReturningHoverResponse { get; set; }
	public Action? BeforePublishingTransportUnavailable { get; set; }
	public int StartCallCount { get; private set; }
	public int MarkTransportUnhealthyCallCount { get; private set; }
	public int DisposeCallCount { get; private set; }
	public int TimedOutHoverRequestsRemaining { get; set; }
	public int TransportChangedRequestFailuresRemaining { get; set; }
	public string? ThrowIOExceptionOnNextRequestMethod { get; set; }
	public string? ThrowInvalidOperationOnNextRequestMethod { get; set; }
	public bool ThrowIOExceptionOnNextDidChange { get; set; }
	public bool ThrowInvalidOperationOnNextWatchedFilesNotification { get; set; }
	public bool ThrowIOExceptionOnNextWatchedFilesNotification { get; set; }
	public bool ThrowIOExceptionAfterWatchedFilesNotificationGateRelease { get; set; }
	public List<bool> StartCancellationTokenCanBeCanceled { get; } = [];

	public event EventHandler<DiagnosticsPublishedEventArgs>? DiagnosticsPublished;

	public event EventHandler? SemanticTokensRefreshRequested;

	public event EventHandler<TransportUnavailableEventArgs>? TransportUnavailable;

	public async Task<bool> StartAsync(CancellationToken cancellationToken)
	{
		ThrowIfDisposed();
		StartCallCount++;
		StartCancellationTokenCanBeCanceled.Add(cancellationToken.CanBeCanceled);

		TaskCompletionSource<bool>? startGate = _startGate;

		if (startGate is not null)
			await startGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

		if (FailStartWhenCancellationRequested && cancellationToken.IsCancellationRequested)
			throw new OperationCanceledException(cancellationToken);

		IsReady = StartResult;

		if (StartResult)
		{
			TransportGeneration = ++_lastIssuedTransportGeneration;
			_capabilitySnapshotPublished = true;
		}

		BeforeReturningStartResult?.Invoke();

		return StartResult;
	}

	private void MarkTransportUnhealthy()
	{
		MarkTransportUnhealthyCallCount++;

		bool wasReady = IsReady;
		IsReady = false;
		_capabilitySnapshotPublished = false;

		if (wasReady)
			TransportUnavailable?.Invoke(this, new TransportUnavailableEventArgs(TransportGeneration));
	}

	public bool TryMarkTransportUnhealthy(long transportGeneration)
	{
		if (transportGeneration != TransportGeneration)
			return false;

		MarkTransportUnhealthy();
		return true;
	}

	public Task SendNotificationAsync(string method, object? parameters, CancellationToken cancellationToken)
	{
		ThrowIfDisposed();

		// Like the real client, a send requires a ready transport session.
		if (!IsReady)
			throw new IOException("The language server transport is not ready.");

		if (cancellationToken.IsCancellationRequested)
			return Task.FromCanceled(cancellationToken);

		lock (_syncRoot)
		{
			_sentMethodNames.Add(method);
			_sentNotifications.Add((method, JsonSerializer.SerializeToElement(parameters)));
		}

		if (method == "textDocument/didChange")
		{
			_changeNotificationObserved.TrySetResult(true);

			if (ThrowIOExceptionOnNextDidChange)
			{
				ThrowIOExceptionOnNextDidChange = false;
				throw new IOException("Simulated didChange transport failure.");
			}

			TaskCompletionSource<bool>? changeNotificationGate = _changeNotificationGate;

			if (changeNotificationGate is not null)
				return changeNotificationGate.Task;
		}

		if (method == "workspace/didChangeWatchedFiles" && ThrowIOExceptionOnNextWatchedFilesNotification)
		{
			ThrowIOExceptionOnNextWatchedFilesNotification = false;
			throw new IOException("Simulated workspace watcher transport failure.");
		}

		if (method == "workspace/didChangeWatchedFiles" && ThrowInvalidOperationOnNextWatchedFilesNotification)
		{
			ThrowInvalidOperationOnNextWatchedFilesNotification = false;
			throw new InvalidOperationException("Simulated unexpected workspace watcher transport failure.");
		}

		if (method == "workspace/didChangeWatchedFiles" && _watchedFilesNotificationGate is not null)
			return WaitForWatchedFilesNotificationGateAsync();

		if (method == "textDocument/didClose")
			_closeNotificationObserved.TrySetResult(true);

		if (method == "textDocument/didOpen" && _openNotificationGate is not null)
			return _openNotificationGate.Task;

		return Task.CompletedTask;
	}

	public Task<TResult> SendRequestAsync<TResult>(string method, object parameters, CancellationToken cancellationToken)
	{
		ThrowIfDisposed();

		// Like the real client, a send requires a ready transport session.
		if (!IsReady)
			throw new IOException("The language server transport is not ready.");

		RecordRequest(method, parameters);

		if (string.Equals(ThrowIOExceptionOnNextRequestMethod, method, StringComparison.Ordinal))
		{
			ThrowIOExceptionOnNextRequestMethod = null;
			IsReady = false;

			throw new LanguageServerTransportUnavailableException($"Simulated {method} transport failure.");
		}

		if (string.Equals(ThrowInvalidOperationOnNextRequestMethod, method, StringComparison.Ordinal))
		{
			ThrowInvalidOperationOnNextRequestMethod = null;
			throw new InvalidOperationException($"Simulated {method} request failure.");
		}

		if (TransportChangedRequestFailuresRemaining > 0)
		{
			TransportChangedRequestFailuresRemaining--;
			IsReady = false;

			throw new LanguageServerTransportChangedException();
		}

		if (method == "textDocument/hover")
		{
			if (_hoverRequestGate is not null)
				return WaitForHoverRequestGateAsync<TResult>(cancellationToken);

			if (CancelNextHoverRequestWithoutTimeout)
			{
				CancelNextHoverRequestWithoutTimeout = false;
				throw new OperationCanceledException("Simulated internal hover cancellation.");
			}

			if (TimedOutHoverRequestsRemaining > 0)
			{
				TimedOutHoverRequestsRemaining--;
				return WaitForCancellationAsync<TResult>(cancellationToken);
			}

			if (HoverResponse.ValueKind == JsonValueKind.Null)
			{
				BeforeReturningHoverResponse?.Invoke();
				return Task.FromResult(default(TResult)!);
			}

			if (HoverResponse.ValueKind != JsonValueKind.Undefined)
			{
				BeforeReturningHoverResponse?.Invoke();
				return DeserializeResponseAsync<TResult>(HoverResponse);
			}
		}

		if (method == "textDocument/completion" && CompletionResponse.ValueKind != JsonValueKind.Undefined)
			return DeserializeResponseAsync<TResult>(CompletionResponse);

		if (method == "completionItem/resolve" && CompletionResolveResponse.ValueKind != JsonValueKind.Undefined)
			return DeserializeResponseAsync<TResult>(CompletionResolveResponse);

		if (method == "textDocument/definition" && DefinitionResponse.ValueKind != JsonValueKind.Undefined)
			return DeserializeResponseAsync<TResult>(DefinitionResponse);

		if (method == "textDocument/documentSymbol" && DocumentSymbolsResponse.ValueKind != JsonValueKind.Undefined)
			return DeserializeResponseAsync<TResult>(DocumentSymbolsResponse);

		if (method == "textDocument/codeAction" && CodeActionsResponse.ValueKind != JsonValueKind.Undefined)
			return DeserializeResponseAsync<TResult>(CodeActionsResponse);

		if (method == "textDocument/references" && ReferencesResponse.ValueKind != JsonValueKind.Undefined)
			return DeserializeResponseAsync<TResult>(ReferencesResponse);

		if (method == "textDocument/rename" && RenameResponse.ValueKind != JsonValueKind.Undefined)
			return DeserializeResponseAsync<TResult>(RenameResponse);

		if (method == "textDocument/formatting" && FormattingResponse.ValueKind != JsonValueKind.Undefined)
			return DeserializeResponseAsync<TResult>(FormattingResponse);

		if (method == "textDocument/semanticTokens/full")
		{
			bool hasSemanticTokensFullRequestGate;

			lock (_syncRoot)
				hasSemanticTokensFullRequestGate = _availableSemanticTokensFullRequestGates.Count > 0;

			if (hasSemanticTokensFullRequestGate)
				return WaitForSemanticTokensFullRequestGateAsync<TResult>();

			if (_semanticTokensFullResponses.Count > 0)
				return DeserializeResponseAsync<TResult>(_semanticTokensFullResponses.Dequeue());

			return DeserializeResponseAsync<TResult>(JsonSerializer.SerializeToElement(new
			{
				data = new[] { 0, 6, 5, 0, 0 },
				resultId = "tokens-1"
			}));
		}

		if (method == "textDocument/signatureHelp" && SignatureHelpResponse.ValueKind != JsonValueKind.Undefined)
			return DeserializeResponseAsync<TResult>(SignatureHelpResponse);

		if (typeof(TResult) == typeof(JsonElement))
			return Task.FromResult((TResult)(object)JsonSerializer.SerializeToElement(new { }));

		throw new InvalidOperationException(
			$"The fake language server client received an unconfigured request for '{method}'. Configure a response for it instead of relying on the silent default.");
	}

	public string[] GetSentMethodNames()
	{
		lock (_syncRoot)
			return [.. _sentMethodNames];
	}

	public void ClearSentMessages()
	{
		lock (_syncRoot)
		{
			_sentMethodNames.Clear();
			_sentNotifications.Clear();
			_sentRequests.Clear();
		}
	}

	public JsonElement GetLastNotificationParameters(string method)
	{
		lock (_syncRoot)
		{
			for (int i = _sentNotifications.Count - 1; i >= 0; i--)
			{
				if (string.Equals(_sentNotifications[i].Method, method, StringComparison.Ordinal))
					return _sentNotifications[i].Parameters;
			}
		}

		throw new InvalidOperationException($"Notification '{method}' was not observed.");
	}

	public JsonElement[] GetNotificationParameters(string method)
	{
		lock (_syncRoot)
		{
			var parameters = new List<JsonElement>();

			for (int i = 0; i < _sentNotifications.Count; i++)
			{
				if (string.Equals(_sentNotifications[i].Method, method, StringComparison.Ordinal))
					parameters.Add(_sentNotifications[i].Parameters);
			}

			return [.. parameters];
		}
	}

	public JsonElement GetLastRequestParameters(string method)
	{
		lock (_syncRoot)
		{
			for (int i = _sentRequests.Count - 1; i >= 0; i--)
			{
				if (string.Equals(_sentRequests[i].Method, method, StringComparison.Ordinal))
					return _sentRequests[i].Parameters;
			}
		}

		throw new InvalidOperationException($"Request '{method}' was not observed.");
	}

	public void BlockNextOpenNotification()
		=> _openNotificationGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

	public void BlockNextStartAsync()
		=> _startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

	public void BlockNextHoverRequest()
		=> _hoverRequestGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

	public void ReleaseOpenNotification()
		=> _openNotificationGate?.TrySetResult(true);

	public void ReleaseStartAsync()
	{
		TaskCompletionSource<bool>? startGate = _startGate;
		_startGate = null;

		startGate?.TrySetResult(true);
	}

	public void ReleaseHoverRequest()
	{
		TaskCompletionSource<bool>? hoverRequestGate = _hoverRequestGate;
		_hoverRequestGate = null;

		hoverRequestGate?.TrySetResult(true);
	}

	public void BlockNextChangeNotification()
		=> _changeNotificationGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

	public void BlockNextWatchedFilesNotification()
		=> _watchedFilesNotificationGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

	public void BlockNextSemanticTokensFullRequest()
		=> BlockNextSemanticTokensFullRequest(response: null);

	/// <summary>
	/// Blocks the next semantic-token full request and pins <paramref name="response"/> as its reply, so
	/// tests can resume gated requests in any order and still know which response each one receives.
	/// </summary>
	public void BlockNextSemanticTokensFullRequest(JsonElement? response)
	{
		lock (_syncRoot)
		{
			_availableSemanticTokensFullRequestGates.Enqueue((
				new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
				response));
		}
	}

	public void ReleaseChangeNotification()
	{
		TaskCompletionSource<bool>? changeNotificationGate = _changeNotificationGate;
		_changeNotificationGate = null;

		changeNotificationGate?.TrySetResult(true);
	}

	public void ReleaseWatchedFilesNotification()
	{
		TaskCompletionSource<bool>? watchedFilesNotificationGate = _watchedFilesNotificationGate;
		_watchedFilesNotificationGate = null;

		watchedFilesNotificationGate?.TrySetResult(true);
	}

	/// <summary>
	/// Releases the oldest semantic-token full request that is currently parked on a block gate, so
	/// gated requests resume in the order they arrived.
	/// </summary>
	public void ReleaseSemanticTokensFullRequest()
	{
		TaskCompletionSource<bool>? semanticTokensFullRequestGate;

		lock (_syncRoot)
		{
			semanticTokensFullRequestGate = _pendingSemanticTokensFullRequestGates.Count > 0
				? _pendingSemanticTokensFullRequestGates.Dequeue()
				: null;
		}

		semanticTokensFullRequestGate?.TrySetResult(true);
	}

	public async Task<bool> WaitForNotificationAsync(string method, TimeSpan timeout)
	{
		// Only notifications with an observation signal can be awaited; any other method would return
		// true immediately even though nothing was observed, making the await vacuous.
		Task observedNotification = method switch
		{
			"textDocument/didChange" => _changeNotificationObserved.Task,
			"textDocument/didClose" => _closeNotificationObserved.Task,
			_ => throw new InvalidOperationException(
				$"The fake language server client has no observation signal for notification '{method}'.")
		};

		Task completedTask = await Task.WhenAny(observedNotification, Task.Delay(timeout)).ConfigureAwait(false);
		return ReferenceEquals(completedTask, observedNotification);
	}

	public async Task<bool> WaitForMethodCountAsync(string method, int expectedCount, TimeSpan timeout)
	{
		DateTime deadline = DateTime.UtcNow + timeout;

		while (DateTime.UtcNow < deadline)
		{
			if (GetSentMethodCount(method) >= expectedCount)
				return true;

			await Task.Delay(10).ConfigureAwait(false);
		}

		return GetSentMethodCount(method) >= expectedCount;
	}

	public void PublishDiagnostics(PublishDiagnosticsParams parameters)
		=> DiagnosticsPublished?.Invoke(this, new DiagnosticsPublishedEventArgs(parameters));

	public void PublishSemanticTokensRefreshRequested()
		=> SemanticTokensRefreshRequested?.Invoke(this, EventArgs.Empty);

	public void PublishTransportUnavailable(long? transportGeneration = null)
	{
		long generation = transportGeneration ?? TransportGeneration;
		EventHandler<TransportUnavailableEventArgs>? handlers = TransportUnavailable;

		// The unhealthy reset keeps the generation number, mirroring the real client contract: a
		// transport that was marked unhealthy keeps its generation until it is detached.
		if (generation == TransportGeneration)
		{
			IsReady = false;
			_capabilitySnapshotPublished = false;
		}

		BeforePublishingTransportUnavailable?.Invoke();
		handlers?.Invoke(this, new TransportUnavailableEventArgs(generation));
	}

	public void EnqueueSemanticTokensFullResponse(JsonElement response)
		=> _semanticTokensFullResponses.Enqueue(response);

	private int GetSentMethodCount(string method)
	{
		int count = 0;

		lock (_syncRoot)
		{
			for (int i = 0; i < _sentMethodNames.Count; i++)
			{
				if (string.Equals(_sentMethodNames[i], method, StringComparison.Ordinal))
					count++;
			}
		}

		return count;
	}

	private void RecordRequest(string method, object parameters)
	{
		lock (_syncRoot)
		{
			_sentMethodNames.Add(method);
			_sentRequests.Add((method, JsonSerializer.SerializeToElement(parameters)));
		}
	}

	private static Task<TResult> DeserializeResponseAsync<TResult>(JsonElement response)
	{
		if (typeof(TResult) == typeof(JsonElement))
			return Task.FromResult((TResult)(object)response);

		TResult result = DeserializeResponse<TResult>(response);
		return Task.FromResult(result);
	}

	private static TResult DeserializeResponse<TResult>(JsonElement response)
	{
		// A configured JSON null models a server that answered null, which is the provider's documented
		// fallback path. The unconfigured case never reaches this method (its ValueKind is Undefined and
		// is rejected by the per-request configuration checks), so null is only observed here when a
		// test explicitly configured it.
		if (response.ValueKind == JsonValueKind.Null)
			return default!;

		TResult? result = JsonSerializer.Deserialize<TResult>(response.GetRawText(), s_responseDeserializationOptions);

		if (result is null)
			throw new InvalidOperationException("Expected a non-null JSON response.");

		return result;
	}

	private static TResult CreateDefaultResponse<TResult>()
		=> default!;

	private async Task WaitForWatchedFilesNotificationGateAsync()
	{
		TaskCompletionSource<bool>? watchedFilesNotificationGate = _watchedFilesNotificationGate;

		if (watchedFilesNotificationGate is not null)
			await watchedFilesNotificationGate.Task.ConfigureAwait(false);

		if (ThrowIOExceptionAfterWatchedFilesNotificationGateRelease)
		{
			ThrowIOExceptionAfterWatchedFilesNotificationGateRelease = false;
			throw new IOException("Simulated delayed workspace watcher transport failure.");
		}
	}

	private static async Task<TResult> WaitForCancellationAsync<TResult>(CancellationToken cancellationToken)
	{
		await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
		return CreateDefaultResponse<TResult>();
	}

	private async Task<TResult> WaitForSemanticTokensFullRequestGateAsync<TResult>()
	{
		(TaskCompletionSource<bool> Gate, JsonElement? Response) blockedRequest;

		lock (_syncRoot)
		{
			blockedRequest = _availableSemanticTokensFullRequestGates.Dequeue();
			_pendingSemanticTokensFullRequestGates.Enqueue(blockedRequest.Gate);
		}

		await blockedRequest.Gate.Task.ConfigureAwait(false);

		if (blockedRequest.Response is { } gatedResponse)
			return DeserializeResponse<TResult>(gatedResponse);

		if (_semanticTokensFullResponses.Count > 0)
			return DeserializeResponse<TResult>(_semanticTokensFullResponses.Dequeue());

		return DeserializeResponse<TResult>(JsonSerializer.SerializeToElement(new
		{
			data = new[] { 0, 6, 5, 0, 0 },
			resultId = "tokens-1"
		}));
	}

	private async Task<TResult> WaitForHoverRequestGateAsync<TResult>(CancellationToken cancellationToken)
	{
		TaskCompletionSource<bool>? hoverRequestGate = _hoverRequestGate;

		if (hoverRequestGate is not null)
			await hoverRequestGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

		if (HoverResponse.ValueKind == JsonValueKind.Null)
			return default!;

		if (HoverResponse.ValueKind != JsonValueKind.Undefined)
			return DeserializeResponse<TResult>(HoverResponse);

		return CreateDefaultResponse<TResult>();
	}

	/// <summary>
	/// Marks the fake as disposed and releases every parked operation, mirroring the real client's
	/// teardown: use after disposal throws <see cref="ObjectDisposedException"/>, and no blocked test
	/// operation can leak a permanently parked task.
	/// </summary>
	public void Dispose()
	{
		DisposeCallCount++;
		_isDisposed = true;
		IsReady = false;

		ReleaseOpenNotification();
		ReleaseStartAsync();
		ReleaseHoverRequest();
		ReleaseChangeNotification();
		ReleaseWatchedFilesNotification();

		TaskCompletionSource<bool>[] blockedSemanticTokensGates;

		lock (_syncRoot)
		{
			blockedSemanticTokensGates =
			[
				.. _availableSemanticTokensFullRequestGates.Select(blockedRequest => blockedRequest.Gate),
				.. _pendingSemanticTokensFullRequestGates
			];

			_availableSemanticTokensFullRequestGates.Clear();
			_pendingSemanticTokensFullRequestGates.Clear();
		}

		foreach (TaskCompletionSource<bool> gate in blockedSemanticTokensGates)
			gate.TrySetResult(true);
	}

	public ValueTask DisposeAsync()
	{
		// Mirror the real client's async disposal so an await-using consumer disposes the fake too.
		Dispose();
		return ValueTask.CompletedTask;
	}

	private void ThrowIfDisposed()
	{
		if (_isDisposed)
			throw new ObjectDisposedException(nameof(FakeLanguageServerClient));
	}
}

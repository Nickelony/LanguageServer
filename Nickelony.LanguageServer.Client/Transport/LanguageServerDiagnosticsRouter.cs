using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Routes diagnostics payloads and semantic-token refresh requests from transport callbacks to
/// <see cref="LanguageServerClient"/> subscribers through serialized background pumps.
/// </summary>
/// <remarks>
/// <para>
/// Diagnostics arrive on the LSP read loop and are stored as the latest payload per file URI. A bounded single-slot
/// channel acts only as a wake signal for the pump, so bursty notifications for the same file collapse to one queued
/// wake-up instead of building an unbounded backlog.
/// </para>
/// <para>
/// Both pumps run as observed background loops: their tasks are tracked and recreated after unexpected termination,
/// and an unexpected loop termination (faulted, canceled, or completed) marks the owning transport unhealthy while
/// the client is still active.
/// </para>
/// </remarks>
internal sealed class LanguageServerDiagnosticsRouter
{
	/// <summary>
	/// Stores one queued diagnostics payload together with the transport generation that produced it.
	/// </summary>
	/// <param name="TransportGeneration">The transport generation that published the diagnostics.</param>
	/// <param name="Parameters">The diagnostics payload.</param>
	private readonly record struct QueuedDiagnostics(long TransportGeneration, PublishDiagnosticsParams Parameters);

	/// <summary>
	/// Identifies one coalesced diagnostics queue slot.
	/// </summary>
	/// <param name="TransportGeneration">The transport generation that produced the diagnostics.</param>
	/// <param name="DocumentKey">The per-document coalescing key.</param>
	private readonly record struct DiagnosticsQueueKey(long TransportGeneration, string DocumentKey);

	/// <summary>
	/// Compares diagnostics queue keys by transport generation and the configured local-path document identity.
	/// </summary>
	private sealed class DiagnosticsQueueKeyComparer : IEqualityComparer<DiagnosticsQueueKey>
	{
		/// <summary>
		/// The comparer instance shared by the pending-diagnostics dictionary.
		/// </summary>
		public static readonly DiagnosticsQueueKeyComparer Instance = new();

		/// <inheritdoc/>
		public bool Equals(DiagnosticsQueueKey left, DiagnosticsQueueKey right) =>
			left.TransportGeneration == right.TransportGeneration
			&& LanguageServerPaths.LocalPathComparer.Equals(left.DocumentKey, right.DocumentKey);

		/// <inheritdoc/>
		public int GetHashCode(DiagnosticsQueueKey key) =>
			HashCode.Combine(key.TransportGeneration, LanguageServerPaths.LocalPathComparer.GetHashCode(key.DocumentKey));
	}

	private readonly ILogger _logger;

	/// <summary>
	/// The sender reported to <see cref="LanguageServerClient.SemanticTokensRefreshRequested"/> subscribers.
	/// </summary>
	private readonly object _eventSender;

	private readonly Func<long, bool> _canAcceptServerCallbacksForGeneration;
	private readonly Func<bool> _isDisposed;
	private readonly CancellationToken _lifetimeToken;
	private readonly Action _markTransportUnhealthy;

	// Queued diagnostics state. Both dictionaries compare document keys with the configured local-path identity so a
	// case-only path difference cannot produce duplicate coalescing slots on a case-insensitive host.
	private readonly ConcurrentDictionary<DiagnosticsQueueKey, QueuedDiagnostics> _pendingDiagnostics = new(DiagnosticsQueueKeyComparer.Instance);
	private readonly ConcurrentDictionary<string, QueuedDiagnostics> _pendingCallbackDiagnostics = new(LanguageServerPaths.LocalPathComparer);

	private readonly Channel<bool> _diagnosticsSignal = Channel.CreateBounded<bool>(
		new BoundedChannelOptions(1)
		{
			SingleReader = true,
			SingleWriter = false,
			AllowSynchronousContinuations = false,
			FullMode = BoundedChannelFullMode.DropWrite
		});

	// Background callback pump state.
	private Task _diagnosticsPumpTask = Task.CompletedTask;
	private int _pendingSemanticTokensRefresh;

	private readonly Channel<bool> _callbackSignal = Channel.CreateBounded<bool>(
		new BoundedChannelOptions(1)
		{
			SingleReader = true,
			SingleWriter = false,
			AllowSynchronousContinuations = false,
			FullMode = BoundedChannelFullMode.DropWrite
		});

	private Task _callbackPumpTask = Task.CompletedTask;
	private long _diagnosticsFallbackSequence;

	private readonly SerializedDiagnosticsSubscriberSet<EventHandler<DiagnosticsPublishedEventArgs>> _diagnosticsPublishedSubscribers;
	private readonly SerializedSignalSubscriberSet<EventHandler> _semanticTokensRefreshSubscribers;

	// Observed background loop state.
	private readonly object _observedBackgroundLoopSyncRoot = new();
	private readonly object _backgroundLoopSyncRoot = new();
	private readonly HashSet<Task> _observedBackgroundLoopTerminations = [];

	/// <summary>
	/// Initializes a new instance of the <see cref="LanguageServerDiagnosticsRouter"/> class.
	/// </summary>
	/// <param name="logger">The logger used for subscriber and background-loop diagnostics.</param>
	/// <param name="eventSender">The sender reported to diagnostics and semantic-token refresh subscribers.</param>
	/// <param name="canAcceptServerCallbacksForGeneration">
	/// Reports whether one transport generation may currently publish server callbacks; queued payloads that no
	/// longer qualify are dropped instead of delivered.
	/// </param>
	/// <param name="isDisposed">Reports whether the owning client started disposal.</param>
	/// <param name="markTransportUnhealthy">Marks the owning client transport unhealthy when a tracked pump terminates unexpectedly.</param>
	/// <param name="lifetimeToken">Signals client disposal to the background pumps.</param>
	internal LanguageServerDiagnosticsRouter(
		ILogger logger,
		object eventSender,
		Func<long, bool> canAcceptServerCallbacksForGeneration,
		Func<bool> isDisposed,
		Action markTransportUnhealthy,
		CancellationToken lifetimeToken)
	{
		_logger = logger;
		_eventSender = eventSender;
		_canAcceptServerCallbacksForGeneration = canAcceptServerCallbacksForGeneration;
		_isDisposed = isDisposed;
		_lifetimeToken = lifetimeToken;
		_markTransportUnhealthy = markTransportUnhealthy;

		_diagnosticsPublishedSubscribers = new(
			(handler, parameters) => handler(eventSender, new DiagnosticsPublishedEventArgs(parameters)),
			exception => _logger.LogWarning(exception, "Diagnostics handler threw; later subscribers will still be notified."));

		_semanticTokensRefreshSubscribers = new(
			handler => handler(_eventSender, EventArgs.Empty),
			exception => _logger.LogWarning(exception, "Semantic tokens refresh request handler threw; later subscribers will still be notified."));
	}

	/// <summary>
	/// Gets or replaces the tracked background callback pump task.
	/// </summary>
	internal Task CallbackPumpTask
	{
		get => Volatile.Read(ref _callbackPumpTask);
		set => Volatile.Write(ref _callbackPumpTask, value);
	}

	/// <summary>
	/// Gets or replaces the tracked background diagnostics pump task.
	/// </summary>
	internal Task DiagnosticsPumpTask
	{
		get => Volatile.Read(ref _diagnosticsPumpTask);
		set => Volatile.Write(ref _diagnosticsPumpTask, value);
	}

	/// <summary>
	/// Adds one subscriber to the serialized diagnostics-dispatch set.
	/// </summary>
	/// <param name="handler">The subscriber to add.</param>
	internal void AddDiagnosticsSubscriber(EventHandler<DiagnosticsPublishedEventArgs>? handler)
		=> _diagnosticsPublishedSubscribers.Add(handler);

	/// <summary>
	/// Removes one subscriber from the serialized diagnostics-dispatch set.
	/// </summary>
	/// <param name="handler">The subscriber to remove.</param>
	internal void RemoveDiagnosticsSubscriber(EventHandler<DiagnosticsPublishedEventArgs>? handler)
		=> _diagnosticsPublishedSubscribers.Remove(handler);

	/// <summary>
	/// Adds one subscriber to the serialized semantic-token refresh signal set.
	/// </summary>
	/// <param name="handler">The subscriber to add.</param>
	internal void AddSemanticTokensRefreshSubscriber(EventHandler? handler)
		=> _semanticTokensRefreshSubscribers.Add(handler);

	/// <summary>
	/// Removes one subscriber from the serialized semantic-token refresh signal set.
	/// </summary>
	/// <param name="handler">The subscriber to remove.</param>
	internal void RemoveSemanticTokensRefreshSubscriber(EventHandler? handler)
		=> _semanticTokensRefreshSubscribers.Remove(handler);

	/// <summary>
	/// Queues a diagnostics payload for later publication on the diagnostics pump.
	/// </summary>
	/// <param name="transportGeneration">The transport generation that published the diagnostics.</param>
	/// <param name="parameters">The diagnostics payload.</param>
	internal void RaiseDiagnosticsPublished(long transportGeneration, PublishDiagnosticsParams parameters)
	{
		// Keep only the newest diagnostics payload per file within one transport generation and wake the pump if it is idle.
		// First snapshot level: detach the queued payload from the caller's arrays so a caller that reuses or mutates its
		// payload after this call cannot change what the pump eventually observes. A second per-subscriber snapshot in
		// SerializedDiagnosticsSubscriberSet.Dispatch keeps subscribers from sharing payload instances with each other.
		_pendingDiagnostics[GetDiagnosticsQueueKey(transportGeneration, parameters)] = new QueuedDiagnostics(transportGeneration, parameters.CreateSnapshot());
		_diagnosticsSignal.Writer.TryWrite(true);
	}

	/// <summary>
	/// Publishes queued diagnostics for the current transport generation while coalescing repeated updates.
	/// </summary>
	internal async Task PumpDiagnosticsAsync()
	{
		ChannelReader<bool> reader = _diagnosticsSignal.Reader;

		try
		{
			while (await reader.WaitToReadAsync(_lifetimeToken).ConfigureAwait(false))
			{
				while (reader.TryRead(out _))
				{ }

				while (!_pendingDiagnostics.IsEmpty)
				{
					// The drain materializes the whole pending set per iteration. That is intentional at the routing
					// scale (one workspace with a bounded set of tracked documents): the snapshot keeps the iteration
					// race-free against concurrent raise calls and stays cheap for that document count.
					KeyValuePair<DiagnosticsQueueKey, QueuedDiagnostics>[] pendingDiagnostics = [.. _pendingDiagnostics];

					for (int i = 0; i < pendingDiagnostics.Length; i++)
					{
						if (!_pendingDiagnostics.TryRemove(pendingDiagnostics[i].Key, out QueuedDiagnostics queuedDiagnostics)
							|| !ShouldDeliverQueuedCallback(queuedDiagnostics))
						{
							continue;
						}

						QueueDiagnosticsCallback(pendingDiagnostics[i].Key.DocumentKey, queuedDiagnostics);
					}
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Expected on dispose.
		}
	}

	/// <summary>
	/// Queues a semantic tokens refresh callback for background subscriber dispatch.
	/// </summary>
	internal void QueueSemanticTokensRefreshCallback()
	{
		Interlocked.Exchange(ref _pendingSemanticTokensRefresh, 1);
		_callbackSignal.Writer.TryWrite(true);
	}

	/// <summary>
	/// Dispatches queued client callbacks on the background callback pump.
	/// </summary>
	internal async Task PumpCallbacksAsync()
	{
		ChannelReader<bool> reader = _callbackSignal.Reader;

		try
		{
			while (await reader.WaitToReadAsync(_lifetimeToken).ConfigureAwait(false))
			{
				while (reader.TryRead(out _))
				{ }

				while (true)
				{
					bool dispatchedCallbacks = false;

					if (Interlocked.Exchange(ref _pendingSemanticTokensRefresh, 0) != 0)
					{
						InvokeSemanticTokensRefreshRequested();
						dispatchedCallbacks = true;
					}

					if (!_pendingCallbackDiagnostics.IsEmpty)
					{
						// Same intentional whole-set snapshot as the diagnostics pump; the callback routing scale is one workspace.
						KeyValuePair<string, QueuedDiagnostics>[] pendingDiagnostics = [.. _pendingCallbackDiagnostics];

						for (int i = 0; i < pendingDiagnostics.Length; i++)
						{
							if (!_pendingCallbackDiagnostics.TryRemove(pendingDiagnostics[i].Key, out QueuedDiagnostics queuedDiagnostics)
								|| !ShouldDeliverQueuedCallback(queuedDiagnostics))
							{
								continue;
							}

							InvokeDiagnosticsPublished(pendingDiagnostics[i].Key, queuedDiagnostics.Parameters);
							dispatchedCallbacks = true;
						}
					}

					if (!dispatchedCallbacks
						&& Volatile.Read(ref _pendingSemanticTokensRefresh) == 0
						&& _pendingCallbackDiagnostics.IsEmpty)
					{
						break;
					}
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Expected on dispose.
		}
	}

	/// <summary>
	/// Dispatches one semantic tokens refresh signal to every current subscriber.
	/// </summary>
	internal void InvokeSemanticTokensRefreshRequested()
		=> _semanticTokensRefreshSubscribers.Dispatch();

	/// <summary>
	/// Dispatches one diagnostics payload to every current subscriber.
	/// </summary>
	/// <param name="documentKey">The document key used to coalesce repeated payloads per subscriber.</param>
	/// <param name="parameters">The diagnostics payload to dispatch.</param>
	internal void InvokeDiagnosticsPublished(string documentKey, PublishDiagnosticsParams parameters)
		=> _diagnosticsPublishedSubscribers.Dispatch(documentKey, parameters);

	/// <summary>
	/// Ensures the callback pump and, optionally, the diagnostics pump are running for the current client instance.
	/// Completed or faulted pumps are recreated so a transport restart can recover callback delivery.
	/// </summary>
	/// <param name="includeDiagnosticsPump">Whether the diagnostics pump should also be ensured.</param>
	internal void EnsurePumpsRunning(bool includeDiagnosticsPump)
	{
		lock (_backgroundLoopSyncRoot)
		{
			if (_callbackPumpTask.IsCompleted)
			{
				ForgetObservedBackgroundLoopTermination(_callbackPumpTask);

				_callbackPumpTask = StartObservedBackgroundLoop(
					PumpCallbacksAsync,
					"callback dispatcher",
					markTransportUnhealthyOnUnexpectedTermination: true);
			}

			if (includeDiagnosticsPump && _diagnosticsPumpTask.IsCompleted)
			{
				ForgetObservedBackgroundLoopTermination(_diagnosticsPumpTask);

				_diagnosticsPumpTask = StartObservedBackgroundLoop(
					PumpDiagnosticsAsync,
					"diagnostics pump",
					markTransportUnhealthyOnUnexpectedTermination: true);
			}
		}
	}

	/// <summary>
	/// Completes both pump wake signals so the background pumps drain and finish during disposal.
	/// </summary>
	internal void CompleteSignalChannels()
	{
		_callbackSignal.Writer.TryComplete();
		_diagnosticsSignal.Writer.TryComplete();
	}

	/// <summary>
	/// Observes one background loop task so unexpected termination is logged immediately while the client is still active.
	/// </summary>
	/// <param name="task">The background loop task to observe.</param>
	/// <param name="loopName">The logical loop name used for diagnostics.</param>
	/// <param name="markTransportUnhealthyOnUnexpectedTermination">Whether unexpected termination should mark the transport unhealthy.</param>
	internal void ObserveBackgroundLoop(
		Task task,
		string loopName,
		bool markTransportUnhealthyOnUnexpectedTermination)
	{
		task.ContinueWith(
			static (completedTask, state) =>
			{
				if (state is not BackgroundLoopObservation observation)
					return;

				observation.Owner.LogUnexpectedBackgroundLoopTermination(
					completedTask,
					observation.LoopName,
					observation.MarkTransportUnhealthyOnUnexpectedTermination);
			},
			new BackgroundLoopObservation(this, loopName, markTransportUnhealthyOnUnexpectedTermination),
			CancellationToken.None,
			TaskContinuationOptions.ExecuteSynchronously,
			TaskScheduler.Default);
	}

	/// <summary>
	/// Reports whether the supplied background loop task already logged its unexpected termination before disposal.
	/// </summary>
	/// <param name="task">The completed background loop task.</param>
	/// <returns><see langword="true"/> when the task already logged unexpected termination; otherwise, <see langword="false"/>.</returns>
	internal bool WasObservedBackgroundLoopTermination(Task task)
	{
		lock (_observedBackgroundLoopSyncRoot)
			return _observedBackgroundLoopTerminations.Contains(task);
	}

	/// <summary>
	/// Starts one background loop task and attaches immediate fault observation.
	/// </summary>
	/// <param name="backgroundLoop">The background loop delegate.</param>
	/// <param name="loopName">The logical loop name used for diagnostics.</param>
	/// <param name="markTransportUnhealthyOnUnexpectedTermination">Whether unexpected termination should mark the transport unhealthy.</param>
	/// <returns>The started background loop task.</returns>
	private Task StartObservedBackgroundLoop(
		Func<Task> backgroundLoop,
		string loopName,
		bool markTransportUnhealthyOnUnexpectedTermination)
	{
		Task task = Task.Run(backgroundLoop, CancellationToken.None);
		ObserveBackgroundLoop(task, loopName, markTransportUnhealthyOnUnexpectedTermination);
		return task;
	}

	/// <summary>
	/// Logs unexpected background loop termination while the client is still active.
	/// </summary>
	/// <param name="task">The completed background loop task.</param>
	/// <param name="loopName">The logical loop name used for diagnostics.</param>
	/// <param name="markTransportUnhealthyOnUnexpectedTermination">Whether unexpected termination should mark the transport unhealthy.</param>
	private void LogUnexpectedBackgroundLoopTermination(
		Task task,
		string loopName,
		bool markTransportUnhealthyOnUnexpectedTermination)
	{
		if (_isDisposed() || _lifetimeToken.IsCancellationRequested)
			return;

		if (!TryMarkObservedBackgroundLoopTermination(task))
			return;

		if (markTransportUnhealthyOnUnexpectedTermination)
			_markTransportUnhealthy();

		if (task.IsFaulted && task.Exception is { } aggregateException)
		{
			Exception loggedException = aggregateException.Flatten().InnerExceptions.Count == 1
				? aggregateException.Flatten().InnerExceptions[0]
				: aggregateException.Flatten();

			_logger.LogWarning(loggedException, "Language server background loop '{LoopName}' terminated unexpectedly while the client was still active.", loopName);
			return;
		}

		if (task.IsCanceled)
		{
			_logger.LogWarning("Language server background loop '{LoopName}' was canceled unexpectedly while the client was still active.", loopName);
			return;
		}

		_logger.LogWarning("Language server background loop '{LoopName}' completed unexpectedly while the client was still active.", loopName);
	}

	/// <summary>
	/// Records that one background loop termination has already been logged before disposal.
	/// </summary>
	/// <param name="task">The completed background loop task.</param>
	/// <returns><see langword="true"/> when the task was newly marked; otherwise, <see langword="false"/>.</returns>
	private bool TryMarkObservedBackgroundLoopTermination(Task task)
	{
		lock (_observedBackgroundLoopSyncRoot)
			return _observedBackgroundLoopTerminations.Add(task);
	}

	/// <summary>
	/// Clears the unexpected-termination marker for one completed background loop task before that loop is restarted.
	/// </summary>
	/// <param name="task">The completed background loop task to forget.</param>
	private void ForgetObservedBackgroundLoopTermination(Task task)
	{
		lock (_observedBackgroundLoopSyncRoot)
			_observedBackgroundLoopTerminations.Remove(task);
	}

	/// <summary>
	/// Builds the queue key used to coalesce diagnostics payloads.
	/// </summary>
	/// <param name="transportGeneration">The transport generation that published the diagnostics.</param>
	/// <param name="parameters">The diagnostics payload.</param>
	/// <returns>The queue key for the payload.</returns>
	private DiagnosticsQueueKey GetDiagnosticsQueueKey(long transportGeneration, PublishDiagnosticsParams parameters)
	{
		if (!string.IsNullOrWhiteSpace(parameters.Uri))
			return new(transportGeneration, GetDiagnosticsDocumentKey(parameters.Uri));

		return new(transportGeneration,
			"diagnostics:" + Interlocked.Increment(ref _diagnosticsFallbackSequence));
	}

	/// <summary>
	/// Gets the document identity used to coalesce diagnostics for one payload URI. Local file URIs collapse to
	/// their normalized local path; other URIs keep their raw text. Identity comparison happens in the queue
	/// comparers, so the key itself stays free of case folding work.
	/// </summary>
	/// <param name="uri">The payload URI.</param>
	/// <returns>The document identity.</returns>
	private static string GetDiagnosticsDocumentKey(string uri)
	{
		if (!LanguageServerPaths.TryGetLocalPath(uri, out string filePath))
			return uri;

		return filePath;
	}

	/// <summary>
	/// Reports whether one queued diagnostics callback may still be delivered to subscribers.
	/// </summary>
	/// <param name="queuedDiagnostics">The queued payload to inspect.</param>
	/// <returns><see langword="true"/> when the payload should be delivered; otherwise, <see langword="false"/>.</returns>
	private bool ShouldDeliverQueuedCallback(QueuedDiagnostics queuedDiagnostics)
	{
		// A zero generation means the payload did not originate from a transport session, so there is no
		// generation fence to apply. Real session payloads must still belong to a generation that accepts server
		// callbacks, which also drops payloads after their transport was marked unhealthy.
		return queuedDiagnostics.TransportGeneration == 0
			|| _canAcceptServerCallbacksForGeneration(queuedDiagnostics.TransportGeneration);
	}

	/// <summary>
	/// Queues a diagnostics callback for background subscriber dispatch.
	/// </summary>
	/// <param name="documentKey">The document key used to coalesce the callback payload.</param>
	/// <param name="parameters">The queued payload, including the transport generation that must still accept callbacks when it is delivered.</param>
	private void QueueDiagnosticsCallback(string documentKey, QueuedDiagnostics parameters)
	{
		_pendingCallbackDiagnostics[documentKey] = parameters;
		_callbackSignal.Writer.TryWrite(true);
	}

	/// <summary>
	/// Stores the state needed to log one observed background loop termination.
	/// </summary>
	/// <param name="Owner">The owning diagnostics router.</param>
	/// <param name="LoopName">The logical loop name.</param>
	/// <param name="MarkTransportUnhealthyOnUnexpectedTermination">Whether unexpected termination should mark the transport unhealthy.</param>
	private readonly record struct BackgroundLoopObservation(
		LanguageServerDiagnosticsRouter Owner,
		string LoopName,
		bool MarkTransportUnhealthyOnUnexpectedTermination);
}

using System.Collections.Concurrent;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Serializes diagnostics callback delivery per subscriber while coalescing repeated updates by document key.
/// </summary>
internal sealed class SerializedDiagnosticsSubscriberSet<THandler>
	where THandler : Delegate
{
	private readonly object _syncRoot = new();

	private readonly Action<THandler, PublishDiagnosticsParams> _invokeHandler;
	private readonly Action<Exception> _logHandlerFailure;
	private readonly Action<PublishDiagnosticsParams>? _beforePendingPayloadReplacement;

	private readonly List<Subscription> _subscriptions = [];

	/// <summary>
	/// Initializes a new instance of the <see cref="SerializedDiagnosticsSubscriberSet{THandler}"/> class.
	/// </summary>
	/// <param name="invokeHandler">Invokes one subscribed handler with one diagnostics payload.</param>
	/// <param name="logHandlerFailure">Logs one handler exception without interrupting later subscribers.</param>
	public SerializedDiagnosticsSubscriberSet(Action<THandler, PublishDiagnosticsParams> invokeHandler, Action<Exception> logHandlerFailure)
		: this(invokeHandler, logHandlerFailure, beforePendingPayloadReplacement: null)
	{ }

	/// <summary>
	/// Initializes a new instance of the <see cref="SerializedDiagnosticsSubscriberSet{THandler}"/> class.
	/// </summary>
	/// <param name="invokeHandler">Invokes one subscribed handler with one diagnostics payload.</param>
	/// <param name="logHandlerFailure">Logs one handler exception without interrupting later subscribers.</param>
	/// <param name="beforePendingPayloadReplacement">
	/// A test seam invoked just before a newer pending payload replaces an older one; production callers pass
	/// <see langword="null"/>. The concurrency test uses it to hold a replacement mid-flight deterministically.
	/// </param>
	internal SerializedDiagnosticsSubscriberSet(
		Action<THandler, PublishDiagnosticsParams> invokeHandler,
		Action<Exception> logHandlerFailure,
		Action<PublishDiagnosticsParams>? beforePendingPayloadReplacement)
	{
		_invokeHandler = invokeHandler;
		_logHandlerFailure = logHandlerFailure;
		_beforePendingPayloadReplacement = beforePendingPayloadReplacement;
	}

	/// <summary>
	/// Adds one subscriber to the serialized diagnostics-dispatch set.
	/// </summary>
	/// <param name="handler">The subscriber to add.</param>
	public void Add(THandler? handler)
	{
		if (handler is null)
			return;

		lock (_syncRoot)
			_subscriptions.Add(new Subscription(handler, _invokeHandler, _logHandlerFailure, _beforePendingPayloadReplacement));
	}

	/// <summary>
	/// Removes one subscriber from the serialized diagnostics-dispatch set.
	/// </summary>
	/// <param name="handler">The subscriber to remove.</param>
	public void Remove(THandler? handler)
	{
		if (handler is null)
			return;

		lock (_syncRoot)
		{
			for (int i = _subscriptions.Count - 1; i >= 0; i--)
			{
				if (!Equals(_subscriptions[i].Handler, handler))
					continue;

				_subscriptions[i].Dispose();
				_subscriptions.RemoveAt(i);

				break;
			}
		}
	}

	/// <summary>
	/// Queues one diagnostics payload for each current subscriber.
	/// </summary>
	/// <param name="documentKey">The document key used to coalesce repeated payloads per subscriber.</param>
	/// <param name="parameters">The diagnostics payload to dispatch.</param>
	public void Dispatch(string documentKey, PublishDiagnosticsParams parameters)
	{
		Subscription[] subscriptions;

		lock (_syncRoot)
		{
			if (_subscriptions.Count == 0)
				return;

			subscriptions = [.. _subscriptions];
		}

		for (int i = 0; i < subscriptions.Length; i++)
		{
			// Per-subscriber snapshot: each subscriber owns its payload instance so concurrent subscribers (or a still-busy
			// subscriber) cannot observe another subscriber's array mutations, even though the queued payload was already
			// detached once in LanguageServerDiagnosticsRouter.RaiseDiagnosticsPublished.
			subscriptions[i].Enqueue(documentKey, parameters.CreateSnapshot());
		}
	}

	/// <summary>
	/// Keeps only the newest diagnostics payload per document key for one subscriber and drains them in enqueue order.
	/// </summary>
	private sealed class Subscription : SerializedSubscriberDrain
	{
		private readonly Action<THandler, PublishDiagnosticsParams> _invokeHandler;
		private readonly Action<Exception> _logHandlerFailure;
		private readonly Action<PublishDiagnosticsParams>? _beforePendingPayloadReplacement;

		private readonly ConcurrentDictionary<string, PendingDiagnosticsPayload> _pendingPayloads = new(StringComparer.Ordinal);

		private long _nextSequence;

		private readonly record struct PendingDiagnosticsPayload(long Sequence, PublishDiagnosticsParams Parameters);
		private readonly record struct DrainedDiagnosticsPayload(long Sequence, PublishDiagnosticsParams Parameters);

		public Subscription(
			THandler handler,
			Action<THandler, PublishDiagnosticsParams> invokeHandler,
			Action<Exception> logHandlerFailure,
			Action<PublishDiagnosticsParams>? beforePendingPayloadReplacement)
		{
			Handler = handler;

			_invokeHandler = invokeHandler;
			_logHandlerFailure = logHandlerFailure;
			_beforePendingPayloadReplacement = beforePendingPayloadReplacement;
		}

		public THandler Handler { get; }

		protected override bool HasPendingWork => !_pendingPayloads.IsEmpty;

		public void Enqueue(string documentKey, PublishDiagnosticsParams parameters)
		{
			if (IsDisposed)
				return;

			// Allocate the sequence when this enqueue begins, even when the document already
			// has a pending payload. A retry must never allow an older caller to replace a
			// payload published later by another concurrent caller.
			long incomingSequence = Interlocked.Increment(ref _nextSequence);

			while (true)
			{
				if (!_pendingPayloads.TryGetValue(documentKey, out PendingDiagnosticsPayload existingPayload))
				{
					if (_pendingPayloads.TryAdd(documentKey, new PendingDiagnosticsPayload(incomingSequence, parameters)))
						break;

					continue;
				}

				if (incomingSequence <= existingPayload.Sequence)
					break;

				_beforePendingPayloadReplacement?.Invoke(parameters);

				if (_pendingPayloads.TryUpdate(documentKey,
					new PendingDiagnosticsPayload(incomingSequence, parameters),
					existingPayload))
				{
					break;
				}
			}

			TryScheduleDrain();
		}

		public void Dispose()
		{
			if (!TryMarkDisposed())
				return;

			_pendingPayloads.Clear();
		}

		protected override bool TryDrainNext()
		{
			if (_pendingPayloads.IsEmpty)
				return false;

			var drainedPayloads = new List<DrainedDiagnosticsPayload>();

			foreach (KeyValuePair<string, PendingDiagnosticsPayload> entry in _pendingPayloads)
			{
				if (_pendingPayloads.TryRemove(entry.Key, out PendingDiagnosticsPayload payload))
					drainedPayloads.Add(new DrainedDiagnosticsPayload(payload.Sequence, payload.Parameters));
			}

			drainedPayloads.Sort(static (left, right) => left.Sequence.CompareTo(right.Sequence));

			for (int i = 0; i < drainedPayloads.Count; i++)
			{
				if (IsDisposed)
					return false;

				try
				{
					_invokeHandler(Handler, drainedPayloads[i].Parameters);
				}
				catch (Exception exception)
				{
					TryLogHandlerFailure(_logHandlerFailure, exception);
				}
			}

			return drainedPayloads.Count > 0;
		}
	}
}

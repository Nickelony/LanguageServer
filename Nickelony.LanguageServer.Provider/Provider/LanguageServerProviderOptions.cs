namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Defines the tunable provider framework defaults used by <see cref="LanguageServerIntelliSenseProviderBase{TDocumentState}"/>.
/// </summary>
/// <remarks>
/// The defaults mirror the values the framework has been designed around: a ten-second request timeout with a
/// two-timeout restart threshold, a three-failure hard startup limit, and a sixteen-document cap for idle tracked
/// documents. Override individual values with an object initializer and pass the instance to the provider base
/// constructor. The provider base constructor validates the values and throws
/// <see cref="ArgumentOutOfRangeException"/> for a value outside its supported range.
/// </remarks>
public sealed class LanguageServerProviderOptions
{
	/// <summary>
	/// Gets the shared default options instance.
	/// </summary>
	public static LanguageServerProviderOptions Default { get; } = new();

	/// <summary>
	/// Gets the per-request timeout applied to language-server requests. Must be greater than zero and at most
	/// <see cref="int.MaxValue"/> milliseconds.
	/// </summary>
	/// <remarks>
	/// When a request times out, the provider returns the request's documented fallback value. Consecutive timeouts
	/// on one transport generation are counted and mark that generation unhealthy once the
	/// <see cref="RequestTimeoutRestartThreshold"/> is reached, so the next request restarts the transport.
	/// </remarks>
	public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);

	/// <summary>
	/// Gets the number of consecutive request timeouts on one transport generation before that transport is marked
	/// unhealthy and the next request triggers a restart. Must be at least one.
	/// </summary>
	public int RequestTimeoutRestartThreshold { get; init; } = 2;

	/// <summary>
	/// Gets the number of consecutive startup failures after which the provider enters the failed state and stops
	/// attempting to start until it is recreated. A failed restart replay (a start that succeeds but cannot reopen
	/// its tracked documents) counts as a failed startup attempt as well. Must be at least one.
	/// </summary>
	public int HardStartupFailureThreshold { get; init; } = 3;

	/// <summary>
	/// Gets the maximum number of idle tracked documents the provider keeps alive before it trims the least
	/// recently used idle documents and closes them on the server. Must not be negative; zero disables idle
	/// document retention, so an idle document is closed as soon as its last reference is released.
	/// </summary>
	/// <remarks>
	/// Idle means the document has no editor-open and no temporary request references. Editor-open documents are
	/// never trimmed, and a document whose close was deferred because a request reference was still active is
	/// closed when that reference is released regardless of this cap.
	/// </remarks>
	public int MaxTrackedIdleDocuments { get; init; } = 16;

	/// <summary>
	/// Validates the option values against the ranges the provider framework supports.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">One of the option values is outside its supported range.</exception>
	internal void Validate()
	{
		if (RequestTimeout <= TimeSpan.Zero || RequestTimeout.TotalMilliseconds > int.MaxValue)
		{
			throw new ArgumentOutOfRangeException(nameof(RequestTimeout), RequestTimeout,
				"The request timeout must be greater than zero and at most Int32.MaxValue milliseconds.");
		}

		if (RequestTimeoutRestartThreshold < 1)
		{
			throw new ArgumentOutOfRangeException(nameof(RequestTimeoutRestartThreshold), RequestTimeoutRestartThreshold,
				"The request-timeout restart threshold must be at least one.");
		}

		if (HardStartupFailureThreshold < 1)
		{
			throw new ArgumentOutOfRangeException(nameof(HardStartupFailureThreshold), HardStartupFailureThreshold,
				"The hard startup-failure threshold must be at least one.");
		}

		if (MaxTrackedIdleDocuments < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(MaxTrackedIdleDocuments), MaxTrackedIdleDocuments,
				"The tracked idle document limit must not be negative.");
		}
	}
}

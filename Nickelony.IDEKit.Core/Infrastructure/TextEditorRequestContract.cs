namespace Nickelony.IDEKit.Core.Infrastructure;

/// <summary>
/// Identifies the logical document and operation ownership captured when an asynchronous editor
/// request is admitted. <see cref="LogicalDocumentId"/> names the logical document the request was
/// admitted for; <see cref="DocumentVersion"/> identifies the logical content snapshot the request
/// was computed against (the default of zero when the request is content-snapshot based and has no
/// document version, as with background diagnostics); and <see cref="SessionGeneration"/>
/// identifies the operation ownership that invalidates asynchronous work. The document version and
/// the session generation are not interchangeable: a text change advances the document version,
/// while a load, replace, rename, or disposal boundary advances the session generation.
/// </summary>
/// <param name="LogicalDocumentId">The identity of the logical document the request was admitted for, when known.</param>
/// <param name="DocumentVersion">The logical content snapshot version the request was computed against.</param>
/// <param name="SessionGeneration">The operation ownership generation the request was admitted under.</param>
public readonly record struct TextEditorRequestIdentity(string? LogicalDocumentId, int DocumentVersion, int SessionGeneration);

/// <summary>
/// Describes how an admitted asynchronous editor request completed.
/// </summary>
public enum TextEditorRequestOutcome
{
	/// <summary>
	/// The request completed and its result was published.
	/// </summary>
	Completed,

	/// <summary>
	/// The request's provider work was aborted by cancellation.
	/// </summary>
	Cancelled,

	/// <summary>
	/// The request's provider work completed, but a newer request or an owner invalidation replaced
	/// it before the result could be published.
	/// </summary>
	Superseded,

	/// <summary>
	/// The request's provider work completed and the request is still the latest, but the session
	/// generation advanced or the logical document identity changed while the work was in flight.
	/// </summary>
	Stale,

	/// <summary>
	/// The request's provider work failed with a non-cancellation exception.
	/// </summary>
	Failed
}

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Describes the outcome of an explicit close call for a tracked document.
/// </summary>
public enum DocumentCloseResult
{
	/// <summary>
	/// The document was not tracked; the close was a no-op.
	/// </summary>
	Untracked = 0,

	/// <summary>
	/// The close released the last reference, or cleaned up an idle record that held none; the tracked record was
	/// removed and the server copy can be closed.
	/// </summary>
	Closed = 1,

	/// <summary>
	/// The close released one editor-open reference, but other editor-open references remain.
	/// </summary>
	StillOpen = 2,

	/// <summary>
	/// The close released the last editor-open reference, but temporary request references remain, so the tracked
	/// record is kept until the caller retries the close after the references drain.
	/// </summary>
	BusyWithRequests = 3
}

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Minimal tracked document store for framework tests.
/// </summary>
internal sealed class TestDocumentStore : TrackedDocumentStore<TestDocumentState>
{
	/// <inheritdoc/>
	protected override TestDocumentState CreateTrackedDocumentState(TrackedDocumentInitialState initialState)
		=> new(initialState);

	/// <inheritdoc/>
	protected override long GetLastAccessStamp(TestDocumentState state)
		=> state.LastAccessStamp;

	/// <inheritdoc/>
	protected override void TouchTrackedDocumentState(TestDocumentState state, long lastAccessStamp)
		=> state.Touch(lastAccessStamp);

	/// <inheritdoc/>
	protected override void ReopenTrackedDocumentState(TestDocumentState state, string content)
		=> state.Reopen(content);

	/// <inheritdoc/>
	protected override string ReplaceTrackedDocumentContent(TestDocumentState state, string content)
		=> state.UpdateContent(content);

	/// <inheritdoc/>
	protected override void RenameTrackedDocumentState(TestDocumentState state, string filePath, string uri)
		=> state.RenameTo(filePath, uri);

	/// <inheritdoc/>
	protected override void MarkTrackedDocumentClosed(TestDocumentState state)
		=> state.MarkClosed();

	/// <summary>
	/// Marks a tracked document as needing a fresh server-side open, mirroring the language-side invalidation the
	/// provider framework invokes when a synchronization delivery fails.
	/// </summary>
	/// <param name="filePath">The normalized document path to invalidate.</param>
	internal void Invalidate(string filePath)
		=> WithTrackedDocument(filePath, static state => state.MarkClosed());
}

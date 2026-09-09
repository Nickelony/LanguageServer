namespace Nickelony.LanguageServer.Client.Tests;

internal sealed class TestTrackedDocumentStore : TrackedDocumentStore<TestTrackedDocumentState>
{
	protected override TestTrackedDocumentState CreateTrackedDocumentState(TrackedDocumentInitialState initialState)
		=> new(initialState);

	protected override long GetLastAccessStamp(TestTrackedDocumentState state)
		=> state.LastAccessStamp;

	protected override void TouchTrackedDocumentState(TestTrackedDocumentState state, long lastAccessStamp)
		=> state.Touch(lastAccessStamp);

	protected override void ReopenTrackedDocumentState(TestTrackedDocumentState state, string content)
		=> state.Reopen(content);

	protected override string ReplaceTrackedDocumentContent(TestTrackedDocumentState state, string content)
		=> state.Update(content);

	protected override void RenameTrackedDocumentState(TestTrackedDocumentState state, string filePath, string uri)
		=> state.Rename(filePath, uri);

	protected override void MarkTrackedDocumentClosed(TestTrackedDocumentState state)
		=> state.Close();
}

internal sealed class TestTrackedDocumentState : TrackedDocumentState
{
	public TestTrackedDocumentState(TrackedDocumentInitialState initialState)
		: base(initialState)
	{ }

	public void Touch(long lastAccessStamp)
		=> SetLastAccessStamp(lastAccessStamp);

	public void Reopen(string content)
		=> ReopenDocument(content);

	public string Update(string content)
		=> ReplaceContent(content);

	public void Rename(string filePath, string uri)
		=> RenameDocument(filePath, uri);

	public void Close()
		=> MarkDocumentClosed();
}

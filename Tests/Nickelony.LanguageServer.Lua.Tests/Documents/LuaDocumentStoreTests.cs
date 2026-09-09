using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Diagnostics;

namespace Nickelony.LanguageServer.Lua.Tests;

[TestClass]
public sealed class LuaDocumentStoreTests
{
	[TestMethod]
	public void DiagnosticsCache_StoresReadOnlyCopyDetachedFromSourceCollection()
	{
		const string filePath = @"C:\Workspace\Scripts\diagnostics.lua";

		var store = new LuaDocumentStore();
		store.Synchronize(filePath, "return 1", acquireOpenReference: true);

		DocumentSnapshot? trackedDocument = store.GetDocumentSnapshot(filePath);
		Assert.IsNotNull(trackedDocument);

		var originalDiagnostic = new TextDiagnostic(TextDiagnosticSeverity.Warning, "Original", 0, 1);
		var replacementDiagnostic = new TextDiagnostic(TextDiagnosticSeverity.Warning, "Replacement", 1, 2);
		TextDiagnostic[] sourceDiagnostics = [originalDiagnostic];

		Assert.IsTrue(store.TryStoreDiagnostics(
			new LuaPublishedDiagnostics(filePath, sourceDiagnostics, version: trackedDocument.Version),
			expectedDocumentVersion: trackedDocument.Version,
			sourceContent: "return 1"));

		sourceDiagnostics[0] = replacementDiagnostic;

		IReadOnlyList<TextDiagnostic> storedDiagnostics = store.GetDiagnostics(filePath);

		Assert.AreEqual(1, storedDiagnostics.Count);
		Assert.AreSame(originalDiagnostic, storedDiagnostics[0]);
		Assert.ThrowsExactly<NotSupportedException>(() => ((IList<TextDiagnostic>)storedDiagnostics)[0] = replacementDiagnostic);

		(IReadOnlyList<TextDiagnostic> snapshotDiagnostics, string? snapshotContent) = store.GetDiagnosticsSnapshot(filePath);

		Assert.AreSame(storedDiagnostics[0], snapshotDiagnostics[0]);
		Assert.AreEqual("return 1", snapshotContent);
	}

	[TestMethod]
	public void DiagnosticsCache_DoesNotStorePayloadWhenTrackedDocumentVersionAdvanced()
	{
		const string filePath = @"C:\Workspace\Scripts\diagnostics.lua";

		var store = new LuaDocumentStore();
		store.Synchronize(filePath, "return 1", acquireOpenReference: true);

		DocumentSnapshot? staleDocument = store.GetDocumentSnapshot(filePath);

		store.Synchronize(filePath, "return 2");

		Assert.IsNotNull(staleDocument);

		bool stored = store.TryStoreDiagnostics(
			new LuaPublishedDiagnostics(
				filePath,
				[new TextDiagnostic(TextDiagnosticSeverity.Warning, "Stale", 0, 1)],
				version: staleDocument.Version),
			expectedDocumentVersion: staleDocument.Version,
			sourceContent: "return 2");

		Assert.IsFalse(stored);
		Assert.AreEqual(0, store.GetDiagnostics(filePath).Count);
	}

	[TestMethod]
	public void DiagnosticsCache_UnversionedPayloadIsStoredWithoutAdvancingTheCachedVersion()
	{
		const string filePath = @"C:\Workspace\Scripts\diagnostics.lua";

		var store = new LuaDocumentStore();
		store.Synchronize(filePath, "return 1", acquireOpenReference: true);
		store.Synchronize(filePath, "return 2");

		DocumentSnapshot? document = store.GetDocumentSnapshot(filePath);
		Assert.IsNotNull(document);
		Assert.AreEqual(2, document.Version);

		Assert.IsTrue(store.TryStoreDiagnostics(
			new LuaPublishedDiagnostics(
				filePath,
				[new TextDiagnostic(TextDiagnosticSeverity.Warning, "Versioned", 0, 1)],
				version: document.Version),
			expectedDocumentVersion: document.Version,
			sourceContent: "return 2"));

		// A payload without a server version is accepted but must not advance the cached version.
		Assert.IsTrue(store.TryStoreDiagnostics(
			new LuaPublishedDiagnostics(
				filePath,
				[new TextDiagnostic(TextDiagnosticSeverity.Warning, "Unversioned", 0, 1)],
				version: 0),
			expectedDocumentVersion: document.Version,
			sourceContent: "return 2"));

		Assert.AreEqual("Unversioned", store.GetDiagnostics(filePath)[0].Message);

		// Because the cached version stayed at 2, a payload for version 1 is still stale and rejected.
		Assert.IsFalse(store.TryStoreDiagnostics(
			new LuaPublishedDiagnostics(
				filePath,
				[new TextDiagnostic(TextDiagnosticSeverity.Warning, "Older", 0, 1)],
				version: 1),
			expectedDocumentVersion: document.Version,
			sourceContent: "return 2"));

		Assert.AreEqual("Unversioned", store.GetDiagnostics(filePath)[0].Message);
	}

	[TestMethod]
	public void SemanticTokensCache_ClonesStoredCollections()
	{
		const string filePath = @"C:\Workspace\Scripts\semantic.lua";

		var store = new LuaDocumentStore();
		store.Synchronize(filePath, "return 1", acquireOpenReference: true);

		var originalToken = new SemanticToken(0, 0, 6, "variable", []);
		var replacementToken = new SemanticToken(1, 0, 6, "function", []);
		SemanticToken[] sourceTokens = [originalToken];

		Assert.IsTrue(store.TryStoreSemanticTokens(filePath, version: 1, sourceTokens));

		sourceTokens[0] = replacementToken;

		IReadOnlyList<SemanticToken> storedTokens = store.GetSemanticTokens(filePath);

		Assert.AreEqual(1, storedTokens.Count);
		Assert.AreSame(originalToken, storedTokens[0]);
		Assert.ThrowsExactly<NotSupportedException>(() => ((IList<SemanticToken>)storedTokens)[0] = replacementToken);
	}

	[TestMethod]
	public void SemanticTokensCache_DoesNotStoreTokensWhenTrackedDocumentVersionAdvanced()
	{
		const string filePath = @"C:\Workspace\Scripts\semantic.lua";

		var store = new LuaDocumentStore();
		store.Synchronize(filePath, "return 1", acquireOpenReference: true);

		DocumentSnapshot? staleDocument = store.GetDocumentSnapshot(filePath);

		store.Synchronize(filePath, "return 2");

		Assert.IsNotNull(staleDocument);

		bool stored = store.TryStoreSemanticTokens(
			filePath,
			version: staleDocument.Version,
			[new SemanticToken(0, 0, 6, "variable", [])]);

		Assert.IsFalse(stored);
		Assert.AreEqual(0, store.GetSemanticTokens(filePath).Count);
	}

	[TestMethod]
	public void InvalidateServerSynchronization_MarksTheDocumentClosedAndReopensWithANewVersion()
	{
		const string filePath = @"C:\Workspace\Scripts\invalidate.lua";

		var store = new LuaDocumentStore();
		store.Synchronize(filePath, "return 1", acquireOpenReference: true);
		store.Synchronize(filePath, "return 2");

		Assert.IsTrue(store.TryStoreSemanticTokens(filePath, version: 2, [new SemanticToken(0, 0, 6, "variable", [])]));

		Assert.IsTrue(store.InvalidateServerSynchronization(filePath));

		// An invalidated document reopens through the next synchronization instead of reporting a
		// change against a server copy that may never have received the last edit; the reopened version
		// advances, and the invalidated token cache accepts the new payload instead of fencing it out
		// against the pre-failure stamp.
		DocumentSynchronizationRequest? reopenRequest = store.Synchronize(filePath, "return 2", acquireOpenReference: true);

		Assert.IsNotNull(reopenRequest);
		Assert.AreEqual(DocumentSynchronizationKind.Open, reopenRequest.Value.Kind);
		Assert.AreEqual(3, reopenRequest.Value.Document.Version);

		Assert.IsTrue(store.TryStoreSemanticTokens(filePath, version: reopenRequest.Value.Document.Version, [new SemanticToken(0, 0, 6, "function", [])]));
		Assert.AreEqual("function", store.GetSemanticTokens(filePath)[0].Type);

		Assert.IsFalse(store.InvalidateServerSynchronization(@"C:\Workspace\Scripts\unknown.lua"));
	}

	[TestMethod]
	public void CachedReads_NormalizeTheSuppliedPath()
	{
		const string filePath = @"C:\Workspace\Scripts\diagnostics.lua";

		var store = new LuaDocumentStore();
		store.Synchronize(filePath, "return 1", acquireOpenReference: true);

		DocumentSnapshot? document = store.GetDocumentSnapshot(filePath);
		Assert.IsNotNull(document);

		Assert.IsTrue(store.TryStoreDiagnostics(
			new LuaPublishedDiagnostics(
				filePath,
				[new TextDiagnostic(TextDiagnosticSeverity.Warning, "Cached", 0, 1)],
				version: document.Version),
			expectedDocumentVersion: document.Version,
			sourceContent: "return 1"));

		// A raw path that normalizes to the tracked path reads the same cached payload.
		Assert.AreEqual(1, store.GetDiagnostics(@"C:\Workspace\Scripts\..\Scripts\diagnostics.lua").Count);
	}

	[TestMethod]
	public void GetDiagnosticsSnapshot_UntrackedPath_ReturnsTheEmptyFallback()
	{
		var store = new LuaDocumentStore();

		(IReadOnlyList<TextDiagnostic> diagnostics, string? sourceContent) = store.GetDiagnosticsSnapshot(@"C:\Workspace\Scripts\unknown.lua");

		Assert.AreEqual(0, diagnostics.Count);
		Assert.IsNull(sourceContent);
	}

	[TestMethod]
	public void InvalidateServerSynchronization_ResetsTheDiagnosticsStampToo()
	{
		const string filePath = @"C:\Workspace\Scripts\invalidate-diagnostics.lua";

		var store = new LuaDocumentStore();
		store.Synchronize(filePath, "return 1", acquireOpenReference: true);
		store.Synchronize(filePath, "return 2");

		Assert.IsTrue(store.TryStoreDiagnostics(
			new LuaPublishedDiagnostics(
				filePath,
				[new TextDiagnostic(TextDiagnosticSeverity.Warning, "Newest", 0, 1)],
				version: 2),
			expectedDocumentVersion: 2,
			sourceContent: "return 2"));

		Assert.IsTrue(store.InvalidateServerSynchronization(filePath));

		// The invalidation resets both payload stamps, so the next diagnostics payload is accepted
		// even when it reports a version lower than the pre-failure stamp; the store-level expectation
		// is unknown here, which leaves the cache fence as the deciding guard.
		Assert.IsTrue(store.TryStoreDiagnostics(
			new LuaPublishedDiagnostics(
				filePath,
				[new TextDiagnostic(TextDiagnosticSeverity.Warning, "Reopened", 0, 1)],
				version: 1),
			expectedDocumentVersion: 0,
			sourceContent: "return 2"));

		Assert.AreEqual("Reopened", store.GetDiagnostics(filePath)[0].Message);
	}
}

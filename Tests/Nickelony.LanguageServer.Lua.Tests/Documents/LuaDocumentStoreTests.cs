using Nickelony.LanguageServer.Abstractions.Diagnostics;

namespace Nickelony.LanguageServer.Lua.Tests;

[TestClass]
public class LuaDocumentStoreTests
{
	[TestMethod]
	public void DiagnosticsCache_StoresReadOnlyCopyDetachedFromSourceCollection()
	{
		const string filePath = @"C:\Workspace\Scripts\diagnostics.lua";

		var manager = new LuaDocumentStore();
		manager.Synchronize(filePath, "return 1", acquireOpenReference: true);

		DocumentSnapshot? trackedDocument = manager.GetDocumentSnapshot(filePath);
		Assert.IsNotNull(trackedDocument);

		var originalDiagnostic = new TextEditorDiagnostic(TextEditorDiagnosticSeverity.Warning, "Original", 0, 1);
		var replacementDiagnostic = new TextEditorDiagnostic(TextEditorDiagnosticSeverity.Warning, "Replacement", 1, 2);
		TextEditorDiagnostic[] sourceDiagnostics = [originalDiagnostic];

		Assert.IsTrue(manager.TryStoreDiagnostics(
			new LuaPublishedDiagnostics(filePath, sourceDiagnostics, version: trackedDocument.Version),
			expectedDocumentVersion: trackedDocument.Version));

		sourceDiagnostics[0] = replacementDiagnostic;

		IReadOnlyList<TextEditorDiagnostic> storedDiagnostics = manager.GetDiagnostics(filePath);

		Assert.AreEqual(1, storedDiagnostics.Count);
		Assert.AreSame(originalDiagnostic, storedDiagnostics[0]);
		Assert.ThrowsExactly<NotSupportedException>(() => ((IList<TextEditorDiagnostic>)storedDiagnostics)[0] = replacementDiagnostic);
	}

	[TestMethod]
	public void DiagnosticsCache_DoesNotStorePayloadWhenTrackedDocumentVersionAdvanced()
	{
		const string filePath = @"C:\Workspace\Scripts\diagnostics.lua";

		var manager = new LuaDocumentStore();
		manager.Synchronize(filePath, "return 1", acquireOpenReference: true);

		DocumentSnapshot? staleDocument = manager.GetDocumentSnapshot(filePath);

		manager.Synchronize(filePath, "return 2");

		Assert.IsNotNull(staleDocument);

		bool stored = manager.TryStoreDiagnostics(
			new LuaPublishedDiagnostics(
				filePath,
				[new TextEditorDiagnostic(TextEditorDiagnosticSeverity.Warning, "Stale", 0, 1)],
				version: staleDocument.Version),
			expectedDocumentVersion: staleDocument.Version);

		Assert.IsFalse(stored);
		Assert.AreEqual(0, manager.GetDiagnostics(filePath).Count);
	}

	[TestMethod]
	public void SemanticTokensCache_ClonesStoredCollectionsAndDeltaState()
	{
		const string filePath = @"C:\Workspace\Scripts\semantic.lua";

		var manager = new LuaDocumentStore();
		manager.Synchronize(filePath, "return 1", acquireOpenReference: true);

		var originalToken = new LuaSemanticToken(0, 0, 6, "variable", []);
		var replacementToken = new LuaSemanticToken(1, 0, 6, "function", []);
		LuaSemanticToken[] sourceTokens = [originalToken];
		int[] sourceDeltaData = [1, 2, 3];

		Assert.IsTrue(manager.TryStoreSemanticTokens(filePath, version: 1, sourceTokens));
		manager.StoreSemanticTokensDeltaState(filePath, "tokens-1", sourceDeltaData);

		sourceTokens[0] = replacementToken;
		sourceDeltaData[0] = 99;

		IReadOnlyList<LuaSemanticToken> storedTokens = manager.GetSemanticTokens(filePath);
		SemanticTokensDeltaState deltaState = manager.GetSemanticTokensDeltaState(filePath);

		Assert.AreEqual(1, storedTokens.Count);
		Assert.AreSame(originalToken, storedTokens[0]);
		Assert.ThrowsExactly<NotSupportedException>(() => ((IList<LuaSemanticToken>)storedTokens)[0] = replacementToken);
		Assert.IsNotNull(deltaState.PreviousData);
		Assert.AreEqual(1, deltaState.PreviousData[0]);

		deltaState.PreviousData[1] = 77;

		SemanticTokensDeltaState reloadedDeltaState = manager.GetSemanticTokensDeltaState(filePath);

		Assert.IsNotNull(reloadedDeltaState.PreviousData);
		Assert.AreEqual(2, reloadedDeltaState.PreviousData[1]);
	}

	[TestMethod]
	public void SemanticTokensCache_DoesNotStoreTokensWhenTrackedDocumentVersionAdvanced()
	{
		const string filePath = @"C:\Workspace\Scripts\semantic.lua";

		var manager = new LuaDocumentStore();
		manager.Synchronize(filePath, "return 1", acquireOpenReference: true);

		DocumentSnapshot? staleDocument = manager.GetDocumentSnapshot(filePath);

		manager.Synchronize(filePath, "return 2");

		Assert.IsNotNull(staleDocument);

		bool stored = manager.TryStoreSemanticTokens(
			filePath,
			version: staleDocument.Version,
			[new LuaSemanticToken(0, 0, 6, "variable", [])]);

		Assert.IsFalse(stored);
		Assert.AreEqual(0, manager.GetSemanticTokens(filePath).Count);
	}
}

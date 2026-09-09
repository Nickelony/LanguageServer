namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class WorkspaceEditPayloadClonerTests
{
	[TestMethod]
	public void CloneChangeMap_ClonesNestedEditLists()
	{
		List<TextEditPayload> edits =
		[
			new TextEditPayload(
				new ProtocolRangePayload(
					new ProtocolPosition(0, 0),
					new ProtocolPosition(0, 1)),
				"x")
		];

		var source = new Dictionary<string, IReadOnlyList<TextEditPayload>?>
		{
			["file:///workspace/first.ext"] = edits
		};

		IReadOnlyDictionary<string, IReadOnlyList<TextEditPayload>?>? clone = WorkspaceEditPayloadCloner.CloneChangeMap(source);

		Assert.IsNotNull(clone);

		edits.Add(new TextEditPayload(
			new ProtocolRangePayload(
				new ProtocolPosition(1, 0),
				new ProtocolPosition(1, 1)),
			"y"));

		source["file:///workspace/second.ext"] = null;

		Assert.AreEqual(1, clone.Count);
		Assert.AreEqual(1, clone["file:///workspace/first.ext"]!.Count);
	}

	[TestMethod]
	public void CloneHelpers_ReturnNullForNullInputs()
	{
		Assert.IsNull(WorkspaceEditPayloadCloner.CloneChangeMap(null));
		Assert.IsNull(WorkspaceEditPayloadCloner.CloneDocumentChanges(null));
		Assert.IsNull(WorkspaceEditPayloadCloner.CloneEditList(null));
	}

	[TestMethod]
	public void CloneDocumentChanges_ClonesListContents()
	{
		List<WorkspaceDocumentChangePayload> documentChanges =
		[
			new WorkspaceDocumentChangePayload(
				new OptionalVersionedTextDocumentIdentifier("file:///workspace/first.ext", Version: null),
				edits: null,
				kind: null,
				uri: null,
				oldUri: null,
				newUri: null)
		];

		IReadOnlyList<WorkspaceDocumentChangePayload>? clone = WorkspaceEditPayloadCloner.CloneDocumentChanges(documentChanges);

		Assert.IsNotNull(clone);
		Assert.AreEqual(1, clone.Count);
		Assert.AreEqual("file:///workspace/first.ext", clone[0].TextDocument?.Uri);
	}
}

using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>
/// Covers the tolerant code-action response converter: literal actions with both workspace-edit
/// shapes, skipped command-only and malformed entries, and round-trip serialization.
/// </summary>
[TestClass]
public sealed class CodeActionsResponseTests
{
	[TestMethod]
	public void Deserialize_EditActions_MapTitleKindPreferredAndChangeMap()
	{
		CodeActionsResponse response = DeserializeRequired(
			"""
			[
			  {
			    "title": "Disable diagnostics on this line",
			    "kind": "quickfix",
			    "isPreferred": true,
			    "edit": {
			      "changes": {
			        "file:///C:/Workspace/Scripts/test.lua": [
			          {
			            "range": { "start": { "line": 4, "character": 0 }, "end": { "line": 4, "character": 0 } },
			            "newText": "---@diagnostic disable-next-line: undefined-global\n"
			          }
			        ]
			      }
			    }
			  },
			  {
			    "title": "Swap parameters",
			    "kind": "refactor.rewrite",
			    "edit": { "changes": {} }
			  }
			]
			""");

		Assert.AreEqual(2, response.CodeActions.Count);

		CodeActionPayload quickFix = response.CodeActions[0];

		Assert.AreEqual("Disable diagnostics on this line", quickFix.Title);
		Assert.AreEqual("quickfix", quickFix.Kind);
		Assert.IsTrue(quickFix.IsPreferred);
		Assert.IsNotNull(quickFix.Edit);

		IReadOnlyDictionary<string, IReadOnlyList<TextEditPayload>?> changes = quickFix.Edit.Value.Changes!;

		Assert.AreEqual(1, changes.Count);

		TextEditPayload textEdit = changes["file:///C:/Workspace/Scripts/test.lua"]![0];

		Assert.AreEqual(Range(4, 0, 4, 0), textEdit.Range);
		Assert.AreEqual("---@diagnostic disable-next-line: undefined-global\n", textEdit.NewText);

		CodeActionPayload refactor = response.CodeActions[1];

		Assert.AreEqual("refactor.rewrite", refactor.Kind);
		Assert.IsNull(refactor.IsPreferred);
		Assert.IsNotNull(refactor.Edit);
		Assert.AreEqual(0, refactor.Edit.Value.Changes!.Count);
	}

	[TestMethod]
	public void Deserialize_StructuredDocumentChanges_MapsTextEdits()
	{
		CodeActionsResponse response = DeserializeRequired(
			"""
			[
			  {
			    "title": "Swap parameters",
			    "kind": "refactor.rewrite",
			    "edit": {
			      "documentChanges": [
			        {
			          "textDocument": { "uri": "file:///C:/Workspace/Scripts/test.lua" },
			          "edits": [
			            {
			              "range": { "start": { "line": 1, "character": 10 }, "end": { "line": 1, "character": 14 } },
			              "newText": "second"
			            }
			          ]
			        }
			      ]
			    }
			  }
			]
			""");

		Assert.AreEqual(1, response.CodeActions.Count);
		Assert.IsNotNull(response.CodeActions[0].Edit);

		IReadOnlyList<WorkspaceDocumentChangePayload> documentChanges = response.CodeActions[0].Edit!.Value.DocumentChanges!;

		Assert.AreEqual(1, documentChanges.Count);

		WorkspaceDocumentChangePayload documentChange = documentChanges[0];

		Assert.AreEqual("file:///C:/Workspace/Scripts/test.lua", documentChange.TextDocument?.Uri);
		Assert.AreEqual(Range(1, 10, 1, 14), documentChange.Edits![0].Range);
		Assert.AreEqual("second", documentChange.Edits![0].NewText);
	}

	[TestMethod]
	public void Deserialize_CommandEntries_PreserveCommandAndData()
	{
		CodeActionsResponse response = DeserializeRequired(
			"""
			[
			  {
			    "title": "Disable (config)",
			    "kind": "quickfix",
			    "command": { "title": "Disable", "command": "lua.setConfig", "arguments": [1, "two"] }
			  },
			  {
			    "title": "Apply edit and command",
			    "kind": "quickfix",
			    "edit": { "changes": {} },
			    "command": { "title": "Run", "command": "lua.solve" },
			    "data": { "resolve": "later" }
			  },
			  {
			    "title": "No edit and no command",
			    "kind": "quickfix"
			  },
			  {
			    "title": "Explicit null edit",
			    "kind": "quickfix",
			    "edit": null
			  }
			]
			""");

		// A command-only action stays representable because the host owns the workspace/executeCommand policy; an
		// action that also carries an edit keeps both; an entry without either is skipped.
		Assert.AreEqual(2, response.CodeActions.Count);

		CodeActionPayload commandOnly = response.CodeActions[0];

		Assert.AreEqual("Disable (config)", commandOnly.Title);
		Assert.IsNull(commandOnly.Edit);
		Assert.IsNotNull(commandOnly.Command);
		Assert.AreEqual("Disable", commandOnly.Command!.Value.Title);
		Assert.AreEqual("lua.setConfig", commandOnly.Command.Value.Command);
		Assert.IsNotNull(commandOnly.Command.Value.Arguments);
		Assert.AreEqual(2, commandOnly.Command.Value.Arguments!.Count);
		Assert.AreEqual(1, commandOnly.Command.Value.Arguments![0].GetInt32());
		Assert.AreEqual("two", commandOnly.Command.Value.Arguments[1].GetString());

		CodeActionPayload withEdit = response.CodeActions[1];

		Assert.AreEqual("Apply edit and command", withEdit.Title);
		Assert.IsNotNull(withEdit.Edit);
		Assert.IsNotNull(withEdit.Command);
		Assert.AreEqual("lua.solve", withEdit.Command!.Value.Command);
		Assert.IsNull(withEdit.Command.Value.Arguments);
		Assert.IsNotNull(withEdit.Data);
		Assert.AreEqual("later", withEdit.Data!.Value.GetProperty("resolve").GetString());
	}

	[TestMethod]
	public void Deserialize_MalformedEntries_AreSkipped()
	{
		CodeActionsResponse response = DeserializeRequired(
			"""
			[
			  42,
			  "text",
			  null,
			  { "kind": "quickfix", "edit": { "changes": {} } },
			  { "title": "   ", "edit": { "changes": {} } },
			  { "title": "noEdit", "kind": "quickfix" },
			  { "title": "badEdit", "edit": { "changes": "not a map" } },
			  { "title": "usable", "kind": "quickfix", "edit": { "changes": {} } }
			]
			""");

		Assert.AreEqual(1, response.CodeActions.Count);
		Assert.AreEqual("usable", response.CodeActions[0].Title);
	}

	[TestMethod]
	public void Deserialize_JsonNull_ReturnsNullResponse()
		=> Assert.IsNull(JsonSerializer.Deserialize<CodeActionsResponse>("null"));

	[TestMethod]
	public void Deserialize_NonArrayPayload_ReturnsEmptyResponse()
	{
		CodeActionsResponse response = DeserializeRequired("""{ "codeActions": [] }""");

		Assert.AreEqual(0, response.CodeActions.Count);
	}

	[TestMethod]
	public void Serialize_RoundTripsEditActions()
	{
		var response = new CodeActionsResponse(
		[
			new CodeActionPayload
			{
				Title = "Add semicolon",
				Kind = "quickfix",
				IsPreferred = true,
				Edit = new WorkspaceEditResponse(
					changes: new Dictionary<string, IReadOnlyList<TextEditPayload>?>
					{
						["file:///C:/Workspace/Scripts/test.lua"] =
						[
							new TextEditPayload(Range(4, 0, 4, 0), ";")
						]
					},
					documentChanges: null),
				Command = new CodeActionCommandPayload(
					"Run formatter",
					"lua.format",
					[JsonSerializer.SerializeToElement(new { scope = "line" })]),
				Data = JsonSerializer.SerializeToElement(new { resolveKey = 7 })
			}
		]);

		string serialized = JsonSerializer.Serialize(response);

		StringAssert.Contains(serialized, "\"command\"");
		StringAssert.Contains(serialized, "\"data\"");

		CodeActionsResponse roundTripped = DeserializeRequired(serialized);

		Assert.AreEqual(1, roundTripped.CodeActions.Count);

		CodeActionPayload action = roundTripped.CodeActions[0];

		Assert.AreEqual("Add semicolon", action.Title);
		Assert.AreEqual("quickfix", action.Kind);
		Assert.IsTrue(action.IsPreferred);
		Assert.AreEqual(";", action.Edit!.Value.Changes!["file:///C:/Workspace/Scripts/test.lua"]![0].NewText);
		Assert.IsNotNull(action.Command);
		Assert.AreEqual("Run formatter", action.Command!.Value.Title);
		Assert.AreEqual("lua.format", action.Command.Value.Command);
		Assert.IsNotNull(action.Command.Value.Arguments);
		Assert.AreEqual("line", action.Command.Value.Arguments![0].GetProperty("scope").GetString());
		Assert.IsNotNull(action.Data);
		Assert.AreEqual(7, action.Data!.Value.GetProperty("resolveKey").GetInt32());
	}

	private static CodeActionsResponse DeserializeRequired(string json)
		=> JsonSerializer.Deserialize<CodeActionsResponse>(json)
			?? throw new InvalidOperationException("Failed to deserialize the code-action response test payload.");

	private static ProtocolRangePayload Range(int startLine, int startCharacter, int endLine, int endCharacter)
		=> new(new ProtocolPosition(startLine, startCharacter), new ProtocolPosition(endLine, endCharacter));
}

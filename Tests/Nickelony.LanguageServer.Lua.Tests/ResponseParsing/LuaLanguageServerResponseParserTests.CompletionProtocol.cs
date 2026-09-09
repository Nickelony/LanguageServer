using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.LanguageServer.Lua.Tests;

public sealed partial class LuaLanguageServerResponseParserTests
{
	[TestMethod]
	public void ParseCompletionItem_MapsDeprecatedTagAndIgnoresUnknownValues()
	{
		CompletionItemPayload deprecated = DeserializeCompletionItemPayload(new { label = "item", tags = new[] { 1 } });
		CompletionItemPayload unknown = DeserializeCompletionItemPayload(new { label = "item", tags = new[] { 99 } });
		CompletionItemPayload mixed = DeserializeCompletionItemPayload(new { label = "item", tags = new[] { 99, 1 } });

		TextCompletionItem? deprecatedItem = LuaLanguageServerResponseParser.ParseCompletionItem(deprecated, 0, "text");
		TextCompletionItem? unknownItem = LuaLanguageServerResponseParser.ParseCompletionItem(unknown, 0, "text");
		TextCompletionItem? mixedItem = LuaLanguageServerResponseParser.ParseCompletionItem(mixed, 0, "text");

		Assert.IsNotNull(deprecatedItem);
		Assert.IsNotNull(unknownItem);
		Assert.IsNotNull(mixedItem);
		CollectionAssert.AreEqual(new[] { TextCompletionTag.Deprecated }, deprecatedItem.Tags.ToArray());
		Assert.AreEqual(0, unknownItem.Tags.Count);
		CollectionAssert.AreEqual(new[] { TextCompletionTag.Deprecated }, mixedItem.Tags.ToArray());
	}

	[TestMethod]
	public void ParseCompletionItem_CarriesCommitCharactersVerbatim()
	{
		CompletionItemPayload payload = DeserializeCompletionItemPayload(new
		{
			label = "item",
			commitCharacters = new[] { "(", "," }
		});

		TextCompletionItem? item = LuaLanguageServerResponseParser.ParseCompletionItem(payload, 0, "text");

		Assert.IsNotNull(item);
		CollectionAssert.AreEqual(new[] { "(", "," }, item.CommitCharacters.ToArray());
	}

	[TestMethod]
	public void ParseCompletionItem_ResolvesAdditionalTextEditRangesToOffsets()
	{
		CompletionItemPayload payload = DeserializeCompletionItemPayload(new
		{
			label = "print",
			additionalTextEdits = new[]
			{
				new
				{
					range = new
					{
						start = new { line = 0, character = 6 },
						end = new { line = 0, character = 6 }
					},
					newText = "local print = print\n"
				}
			}
		});

		TextCompletionItem? item = LuaLanguageServerResponseParser.ParseCompletionItem(payload, 0, "print()");

		Assert.IsNotNull(item);
		Assert.AreEqual(1, item.AdditionalTextEdits.Count);

		TextCompletionTextEdit edit = item.AdditionalTextEdits[0];

		Assert.AreEqual(6, edit.ReplacementRange.Offset);
		Assert.AreEqual(0, edit.ReplacementRange.Length);
		Assert.AreEqual("local print = print\n", edit.NewText);
	}

	[TestMethod]
	public void ParseCompletionItem_SkipsMalformedAdditionalTextEditsIndividually()
	{
		CompletionItemPayload payload = DeserializeCompletionItemPayload(new
		{
			label = "print",
			additionalTextEdits = new object[]
			{
				new
				{
					range = new
					{
						start = new { line = 9, character = 0 },
						end = new { line = 9, character = 1 }
					},
					newText = "out-of-range-line"
				},
				new
				{
					range = new
					{
						start = new { line = 0, character = 0 },
						end = new { line = 0, character = 0 }
					}
				},
				new
				{
					range = new
					{
						start = new { line = 0, character = 0 },
						end = new { line = 0, character = 0 }
					},
					newText = "kept"
				}
			}
		});

		TextCompletionItem? item = LuaLanguageServerResponseParser.ParseCompletionItem(payload, 0, "print");

		Assert.IsNotNull(item);
		Assert.AreEqual(1, item.AdditionalTextEdits.Count);
		Assert.AreEqual("kept", item.AdditionalTextEdits[0].NewText);
	}

	[TestMethod]
	public void ParseCompletionItems_KeepsItemsThatDifferOnlyInAdditionalTextEdits()
	{
		CompletionItemPayload plain = DeserializeCompletionItemPayload(new { label = "print", kind = 3 });
		CompletionItemPayload withImport = DeserializeCompletionItemPayload(new
		{
			label = "print",
			kind = 3,
			additionalTextEdits = new[]
			{
				new
				{
					range = new
					{
						start = new { line = 0, character = 0 },
						end = new { line = 0, character = 0 }
					},
					newText = "local print = print\n"
				}
			}
		});

		IReadOnlyList<TextCompletionItem> items = LuaLanguageServerResponseParser.ParseCompletionItems([plain, withImport], "print");

		Assert.AreEqual(2, items.Count);
		Assert.AreEqual(0, items[0].AdditionalTextEdits.Count);
		Assert.AreEqual(1, items[1].AdditionalTextEdits.Count);
	}

	[TestMethod]
	public void ParseCompletionItems_KeepsItemsThatDifferOnlyInTags()
	{
		CompletionItemPayload plain = DeserializeCompletionItemPayload(new { label = "print", kind = 3 });
		CompletionItemPayload deprecated = DeserializeCompletionItemPayload(new { label = "print", kind = 3, tags = new[] { 1 } });

		IReadOnlyList<TextCompletionItem> items = LuaLanguageServerResponseParser.ParseCompletionItems([plain, deprecated], "print");

		Assert.AreEqual(2, items.Count);
	}

	[TestMethod]
	public void ParseCompletionItems_SkipsItemsWithoutAUsableLabel()
	{
		CompletionItemPayload blank = DeserializeCompletionItemPayload(new { label = "   ", kind = 3 });
		CompletionItemPayload missing = DeserializeCompletionItemPayload(new { kind = 3 });
		CompletionItemPayload valid = DeserializeCompletionItemPayload(new { label = "print", kind = 3 });

		IReadOnlyList<TextCompletionItem> items = LuaLanguageServerResponseParser.ParseCompletionItems([blank, missing, valid], "print");

		Assert.AreEqual(1, items.Count);
		Assert.AreEqual("print", items[0].Label);
	}

	[TestMethod]
	public void ParseCompletionItems_DetailWithoutDocumentation_StillRequestsResolve()
	{
		CompletionItemPayload payload = DeserializeCompletionItemPayload(new
		{
			label = "spawn",
			kind = 3,
			detail = "function spawn()"
		});
		int factoryCalls = 0;

		IReadOnlyList<TextCompletionItem> items = LuaLanguageServerResponseParser.ParseCompletionItems(
			[payload],
			"spa",
			(item, _, _) =>
			{
				factoryCalls++;
				return _ => Task.FromResult(item);
			});

		// A missing documentation field alone still benefits from a resolve round trip.
		Assert.AreEqual(1, items.Count);
		Assert.AreEqual(1, factoryCalls);
		Assert.IsTrue(items[0].CanResolve);
	}

	[TestMethod]
	public void ParseCompletionItems_KeepsItemsThatDifferOnlyInCommitCharacters()
	{
		CompletionItemPayload plain = DeserializeCompletionItemPayload(new { label = "print", kind = 3 });
		CompletionItemPayload withCommitCharacters = DeserializeCompletionItemPayload(new { label = "print", kind = 3, commitCharacters = new[] { "." } });

		IReadOnlyList<TextCompletionItem> items = LuaLanguageServerResponseParser.ParseCompletionItems([plain, withCommitCharacters], "print");

		Assert.AreEqual(2, items.Count);
		Assert.AreEqual(0, items[0].CommitCharacters.Count);
		Assert.AreEqual(1, items[1].CommitCharacters.Count);
	}

	[TestMethod]
	public void ParseCompletionItems_MergedDuplicateProducesExactlyOneResolveCallback()
	{
		CompletionItemPayload retained = DeserializeCompletionItemPayload(new { label = "spawn", kind = 3, detail = "function spawn()" });
		CompletionItemPayload duplicate = DeserializeCompletionItemPayload(new { label = "spawn", kind = 3, detail = "function spawn()", preselect = true });
		int factoryCalls = 0;

		IReadOnlyList<TextCompletionItem> items = LuaLanguageServerResponseParser.ParseCompletionItems(
			[retained, duplicate],
			"spa",
			(item, _, _) =>
			{
				factoryCalls++;
				return _ => Task.FromResult(item);
			});

		// The duplicate merges into the retained item, so the factory runs once and the merged item
		// carries exactly one resolve callback.
		Assert.AreEqual(1, items.Count);
		Assert.AreEqual(1, factoryCalls);
		Assert.IsTrue(items[0].CanResolve);
	}
}

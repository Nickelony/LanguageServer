using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Completion;
using System.Reflection;

namespace Nickelony.IDEKit.IntelliSense.Tests.Completion;

[TestClass]
public sealed class TextCompletionItemTests
{
	[TestMethod]
	public void InsertText_NotInitialized_UsesLabel()
	{
		var item = new TextCompletionItem("label");
		Assert.AreEqual("label", item.InsertText);
	}

	[TestMethod]
	public void InsertText_ExplicitNull_UsesLabel()
	{
		var item = new TextCompletionItem("label") { InsertText = null };
		Assert.AreEqual("label", item.InsertText);
	}

	[TestMethod]
	public void InsertText_Whitespace_IsPreserved()
	{
		// Only null falls back to the label; a whitespace-only insertion is a valid commit text.
		var item = new TextCompletionItem("label") { InsertText = "  " };
		Assert.AreEqual("  ", item.InsertText);
	}

	[TestMethod]
	public void InsertText_Explicit_IsPreserved()
	{
		var item = new TextCompletionItem("label") { InsertText = "inserted" };
		Assert.AreEqual("inserted", item.InsertText);
	}

	[TestMethod]
	public void Documentation_Blank_IsNull()
	{
		var item = new TextCompletionItem("label") { Documentation = "   " };
		Assert.IsNull(item.Documentation);
	}

	[TestMethod]
	public void Documentation_Padded_IsTrimmed()
	{
		var item = new TextCompletionItem("label") { Documentation = "  text  " };
		Assert.AreEqual("text", item.Documentation);
	}

	[TestMethod]
	public void Documentation_Markdown_IsNotTrimmed()
	{
		var item = new TextCompletionItem("label")
		{
			Documentation = "  **text**  ",
			DocumentationKind = TextMarkupKind.Markdown
		};

		Assert.AreEqual("  **text**  ", item.Documentation);
		Assert.AreEqual(TextMarkupKind.Markdown, item.DocumentationKind);
	}

	[TestMethod]
	public void Kind_NotInitialized_DefaultsToGeneric()
	{
		var item = new TextCompletionItem("label");
		Assert.AreSame(TextCompletionItemKind.Generic, item.Kind);
	}

	[TestMethod]
	public void Kind_Explicit_IsPreserved()
	{
		var item = new TextCompletionItem("label") { Kind = TextCompletionItemKind.Method };
		Assert.AreSame(TextCompletionItemKind.Method, item.Kind);
	}

	[TestMethod]
	public void Kind_Null_DefaultsToGeneric()
	{
		var item = new TextCompletionItem("label") { Kind = null! };
		Assert.AreSame(TextCompletionItemKind.Generic, item.Kind);
	}

	[TestMethod]
	public void Priority_NonFiniteValue_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextCompletionItem("label") { Priority = double.NaN });
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextCompletionItem("label") { Priority = double.PositiveInfinity });
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextCompletionItem("label") { Priority = double.NegativeInfinity });
	}

	[TestMethod]
	public void Priority_FiniteValue_IsStored()
	{
		var item = new TextCompletionItem("label") { Priority = -2.5 };

		Assert.AreEqual(-2.5, item.Priority);
	}

	[TestMethod]
	public void SortText_NotInitialized_IsNull()
	{
		var item = new TextCompletionItem("label");
		Assert.IsNull(item.SortText);
	}

	[TestMethod]
	public void SortText_Blank_IsNull()
	{
		var item = new TextCompletionItem("label") { SortText = "   " };
		Assert.IsNull(item.SortText);
	}

	[TestMethod]
	public void SortText_Explicit_IsPreservedVerbatim()
	{
		// Ordering is a lexicographic comparison, so a non-blank value is not trimmed.
		var item = new TextCompletionItem("label") { SortText = " 0002" };
		Assert.AreEqual(" 0002", item.SortText);
	}

	[TestMethod]
	public void IsPreselected_NotInitialized_IsFalse()
	{
		var item = new TextCompletionItem("label");
		Assert.IsFalse(item.IsPreselected);
	}

	[TestMethod]
	public void IsPreselected_Explicit_IsPreserved()
	{
		var item = new TextCompletionItem("label") { IsPreselected = true };
		Assert.IsTrue(item.IsPreselected);
	}

	[TestMethod]
	public void FilterText_NotInitialized_UsesLabel()
	{
		var item = new TextCompletionItem("label");
		Assert.AreEqual("label", item.FilterText);
	}

	[TestMethod]
	public void FilterText_BlankLabel_IsUsedAsTheFallbackUnchanged()
	{
		// Neither the label fallback nor the blank-filter normalization trims the result: an item with
		// a blank label and a blank filter text reports the blank label.
		var item = new TextCompletionItem("  ") { FilterText = "  " };

		Assert.AreEqual("  ", item.FilterText);
	}

	[TestMethod]
	public void FilterText_Whitespace_UsesLabel()
	{
		var item = new TextCompletionItem("label") { FilterText = "   " };
		Assert.AreEqual("label", item.FilterText);
	}

	[TestMethod]
	public void FilterText_Explicit_IsPreserved()
	{
		var item = new TextCompletionItem("label") { FilterText = "filtered" };
		Assert.AreEqual("filtered", item.FilterText);
	}

	[TestMethod]
	[DataRow(null, DisplayName = "Null")]
	[DataRow("   ", DisplayName = "Blank")]
	public void Detail_NullOrBlank_IsNull(string? detail)
	{
		var item = new TextCompletionItem("label") { Detail = detail };
		Assert.IsNull(item.Detail);
	}

	[TestMethod]
	public void Detail_Padded_IsTrimmed()
	{
		var item = new TextCompletionItem("label") { Detail = "  text  " };
		Assert.AreEqual("text", item.Detail);
	}

	[TestMethod]
	public void RequestMetadata_Initialized_IsPreserved()
	{
		var item = new TextCompletionItem("label")
		{
			RequestDocumentVersion = 3,
			RequestGeneration = 7,
			InsertTextFormat = TextCompletionInsertTextFormat.Snippet
		};

		Assert.AreEqual(3, item.RequestDocumentVersion);
		Assert.AreEqual(7, item.RequestGeneration);
		Assert.AreEqual(TextCompletionInsertTextFormat.Snippet, item.InsertTextFormat);
	}

	[TestMethod]
	public void InsertTextFormat_NotInitialized_DefaultsToPlainText()
	{
		var item = new TextCompletionItem("label");

		Assert.AreEqual(TextCompletionInsertTextFormat.PlainText, item.InsertTextFormat);
	}

	[TestMethod]
	public void ResolveCallback_NotInitialized_CannotResolve()
	{
		var item = new TextCompletionItem("label");
		Assert.IsFalse(item.CanResolve);
	}

	[TestMethod]
	public async Task ResolveAsync_WithoutResolveCallback_ReturnsSameItem()
	{
		var item = new TextCompletionItem("label");

		TextCompletionItem resolvedItem = await item.ResolveAsync().ConfigureAwait(false);

		Assert.AreSame(item, resolvedItem);
	}

	[TestMethod]
	public async Task ResolveAsync_WithResolveCallback_InvokesCallback()
	{
		var resolvedContent = new TextCompletionItem("label") { Detail = "resolved" };
		using var cancellation = new CancellationTokenSource();
		var item = new TextCompletionItem("label")
			.WithResolveCallback(cancellationToken =>
			{
				Assert.AreEqual(cancellation.Token, cancellationToken);
				return Task.FromResult(resolvedContent);
			});

		TextCompletionItem resolvedItem = await item.ResolveAsync(cancellation.Token).ConfigureAwait(false);

		Assert.IsTrue(item.CanResolve);
		Assert.AreSame(resolvedContent, resolvedItem);
	}

	[TestMethod]
	public async Task ResolveAsync_CallbackReturnsNullItem_Throws()
	{
		var item = new TextCompletionItem("label").WithResolveCallback(_ => Task.FromResult<TextCompletionItem>(null!));

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => item.ResolveAsync()).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task ResolveAsync_CallbackReturnsNullTask_Throws()
	{
		var item = new TextCompletionItem("label").WithResolveCallback(_ => null!);

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => item.ResolveAsync()).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task ResolveAsync_CallbackThrowsSynchronously_FaultsTheTask()
	{
		var expectedException = new InvalidOperationException("Resolve failed.");
		var item = new TextCompletionItem("label").WithResolveCallback(_ => throw expectedException);

		Task<TextCompletionItem> resolveTask = item.ResolveAsync();

		InvalidOperationException exception = await Assert
			.ThrowsExactlyAsync<InvalidOperationException>(() => resolveTask)
			.ConfigureAwait(false);

		Assert.AreSame(expectedException, exception);
	}

	[TestMethod]
	public void WithResolveCallback_ReturnsCopyWithResolveSupport()
	{
		var resolveCallback = new Func<CancellationToken, Task<TextCompletionItem>>(_ => Task.FromResult(new TextCompletionItem("label")));
		var item = new TextCompletionItem("label")
		{
			Detail = "detail",
			Priority = 2.0,
			Kind = TextCompletionItemKind.Field
		};

		TextCompletionItem resolved = item.WithResolveCallback(resolveCallback);

		Assert.IsFalse(item.CanResolve);
		Assert.IsTrue(resolved.CanResolve);
		Assert.AreNotSame(item, resolved);
		Assert.AreEqual(item.Label, resolved.Label);
		Assert.AreEqual(item.InsertText, resolved.InsertText);
		Assert.AreEqual(item.Detail, resolved.Detail);
		Assert.AreEqual(item.Priority, resolved.Priority);
		Assert.AreSame(item.Kind, resolved.Kind);
	}

	[TestMethod]
	public async Task WithRequestContext_StampsItemButNotRawResolveResult()
	{
		var item = new TextCompletionItem("label")
			.WithResolveCallback(_ => Task.FromResult(new TextCompletionItem("label") { Detail = "resolved" }));

		TextCompletionItem stampedItem = item.WithRequestContext(requestDocumentVersion: 4, requestGeneration: 9);
		TextCompletionItem resolvedItem = await stampedItem.ResolveAsync().ConfigureAwait(false);

		Assert.AreEqual(4, stampedItem.RequestDocumentVersion);
		Assert.AreEqual(9, stampedItem.RequestGeneration);

		// The raw resolve path does not stamp the resolved result; callers apply the stamps themselves
		// when they need them on a resolved item.
		Assert.IsNull(resolvedItem.RequestDocumentVersion);
		Assert.IsNull(resolvedItem.RequestGeneration);
		Assert.AreEqual("resolved", resolvedItem.Detail);
	}

	[TestMethod]
	public void WithRequestContext_SameContext_ReturnsSameItem()
	{
		var item = new TextCompletionItem("label") { RequestDocumentVersion = 4, RequestGeneration = 9 };

		Assert.AreSame(item, item.WithRequestContext(requestDocumentVersion: 4, requestGeneration: 9));
	}

	[TestMethod]
	public async Task WithoutTextEdit_RemovesTextEditButKeepsResolveCallback()
	{
		var textEdit = new TextCompletionTextEdit(new TextRange(2, 4), new TextRange(2, 10));
		var item = new TextCompletionItem("label") { TextEdit = textEdit }
			.WithResolveCallback(_ => Task.FromResult(new TextCompletionItem("label") { TextEdit = textEdit }));

		TextCompletionItem commitItem = item.WithoutTextEdit();
		TextCompletionItem resolvedItem = await commitItem.ResolveAsync().ConfigureAwait(false);

		Assert.IsNull(commitItem.TextEdit);
		Assert.IsTrue(commitItem.CanResolve);
		Assert.AreEqual(textEdit, resolvedItem.TextEdit);
	}

	[TestMethod]
	public void WithoutTextEdit_WithoutTextEdit_ReturnsSameItem()
	{
		var item = new TextCompletionItem("label") { RequestDocumentVersion = 4, RequestGeneration = 9 };

		Assert.AreSame(item, item.WithoutTextEdit());
	}

	[TestMethod]
	public void RequestDocumentVersion_NegativeValue_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextCompletionItem("label") { RequestDocumentVersion = -1 });
	}

	[TestMethod]
	public void RequestGeneration_NegativeValue_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextCompletionItem("label") { RequestGeneration = -1 });
	}

	[TestMethod]
	public void WithRequestContext_NegativeArguments_Throw()
	{
		var item = new TextCompletionItem("label");

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => item.WithRequestContext(-1, 0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => item.WithRequestContext(0, -1));
	}

	[TestMethod]
	public void WithResolvedContent_OverridesDetailDocumentationAndKind()
	{
		var item = new TextCompletionItem("label")
		{
			Documentation = "base",
			Detail = "base detail",
			Kind = TextCompletionItemKind.Field
		};
		var resolvedContent = new TextCompletionItem("label")
		{
			Documentation = "**resolved**",
			Detail = "resolved detail",
			Kind = TextCompletionItemKind.Method,
			DocumentationKind = TextMarkupKind.Markdown
		};

		TextCompletionItem merged = item.WithResolvedContent(resolvedContent);

		Assert.AreEqual("resolved detail", merged.Detail);
		Assert.AreEqual("**resolved**", merged.Documentation);
		Assert.AreEqual(TextMarkupKind.Markdown, merged.DocumentationKind);
		Assert.AreSame(TextCompletionItemKind.Method, merged.Kind);
	}

	[TestMethod]
	public void WithResolvedContent_GenericKind_IsFilledFromResolvedItem()
	{
		// A resolved kind that was set fills in when the current item never set one, so a resolved
		// item upgrades an unclassified item.
		var item = new TextCompletionItem("label");
		var resolvedContent = new TextCompletionItem("label") { Kind = TextCompletionItemKind.Method };

		TextCompletionItem merged = item.WithResolvedContent(resolvedContent);

		Assert.AreSame(TextCompletionItemKind.Method, merged.Kind);
	}

	[TestMethod]
	public void WithResolvedContent_ExplicitGenericKind_ResetsCurrentKind()
	{
		// An explicitly assigned category wins, including a reset to the Generic fallback.
		var item = new TextCompletionItem("label") { Kind = TextCompletionItemKind.Field };
		var resolvedContent = new TextCompletionItem("label") { Kind = TextCompletionItemKind.Generic };

		TextCompletionItem merged = item.WithResolvedContent(resolvedContent);

		Assert.AreSame(TextCompletionItemKind.Generic, merged.Kind);
	}

	[TestMethod]
	public void WithResolvedContent_ResolvedPriorityAndPreselect_KeepCurrentValues()
	{
		// Priority and preselect state are identity fields: a resolved item never overrides them.
		var item = new TextCompletionItem("label") { Priority = 5.0, IsPreselected = true };
		var resolvedContent = new TextCompletionItem("label") { Detail = "resolved detail", Priority = 9.0, IsPreselected = false };

		TextCompletionItem merged = item.WithResolvedContent(resolvedContent);

		Assert.AreEqual("resolved detail", merged.Detail);
		Assert.AreEqual(5.0, merged.Priority);
		Assert.IsTrue(merged.IsPreselected);
	}

	[TestMethod]
	public void WithResolvedContent_BlankResolvedFields_KeepCurrentValues()
	{
		var item = new TextCompletionItem("label")
		{
			Documentation = "base",
			Detail = "base detail",
			Kind = TextCompletionItemKind.Field
		};
		var resolvedContent = new TextCompletionItem("label") { Documentation = "   ", Detail = "  " };

		TextCompletionItem merged = item.WithResolvedContent(resolvedContent);

		Assert.AreEqual("base detail", merged.Detail);
		Assert.AreEqual("base", merged.Documentation);
		Assert.AreSame(TextCompletionItemKind.Field, merged.Kind);
	}

	[TestMethod]
	public void WithResolvedContent_AdoptsSortTextOnlyWhenMissing()
	{
		var resolvedContent = new TextCompletionItem("label") { SortText = "0001" };
		var withoutSortText = new TextCompletionItem("label");
		var withSortText = new TextCompletionItem("label") { SortText = "0002", IsPreselected = true };

		TextCompletionItem adopted = withoutSortText.WithResolvedContent(resolvedContent);
		TextCompletionItem kept = withSortText.WithResolvedContent(resolvedContent);

		Assert.AreEqual("0001", adopted.SortText);
		Assert.AreEqual("0002", kept.SortText);

		// Preselect state always comes from the original item; the resolved item cannot change it.
		Assert.IsTrue(kept.IsPreselected);
	}

	[TestMethod]
	public void WithResolvedContent_NoContribution_ReturnsTheSameInstance()
	{
		var item = new TextCompletionItem("label") { Detail = "detail", Documentation = "docs" };

		TextCompletionItem merged = item.WithResolvedContent(new TextCompletionItem("other"));

		Assert.AreSame(item, merged);
	}

	[TestMethod]
	public async Task WithResolvedContent_KeepsCommitMetadataAndDropsResolveCallback()
	{
		var textEdit = new TextCompletionTextEdit(new TextRange(2, 4));
		var item = new TextCompletionItem("label")
		{
			Priority = 3.0,
			FilterText = "filtered",
			TextEdit = textEdit,
			RequestDocumentVersion = 4,
			RequestGeneration = 9,
			InsertTextFormat = TextCompletionInsertTextFormat.Snippet
		}.WithResolveCallback(static _ => Task.FromResult(new TextCompletionItem("resolved")));

		TextCompletionItem merged = item.WithResolvedContent(new TextCompletionItem("other") { Detail = "resolved" });
		TextCompletionItem resolvedItem = await merged.ResolveAsync().ConfigureAwait(false);

		Assert.AreEqual("label", merged.Label);
		Assert.AreEqual("label", merged.InsertText);
		Assert.AreEqual("filtered", merged.FilterText);
		Assert.AreEqual(3.0, merged.Priority);
		Assert.AreEqual(textEdit, merged.TextEdit);
		Assert.AreEqual(4, merged.RequestDocumentVersion);
		Assert.AreEqual(9, merged.RequestGeneration);
		Assert.AreEqual(TextCompletionInsertTextFormat.Snippet, merged.InsertTextFormat);
		Assert.IsFalse(merged.CanResolve);
		Assert.AreSame(merged, resolvedItem);
	}

	[TestMethod]
	public void WithResolvedContent_ResolvedCommitFields_FillInWhenCurrentItemLacksThem()
	{
		var resolvedEdit = new TextCompletionTextEdit(new TextRange(2, 4), new TextRange(2, 10));
		var item = new TextCompletionItem("label");

		TextCompletionItem merged = item.WithResolvedContent(
			new TextCompletionItem("label")
			{
				TextEdit = resolvedEdit,
				InsertTextFormat = TextCompletionInsertTextFormat.Snippet
			});

		Assert.AreEqual(resolvedEdit, merged.TextEdit);
		Assert.AreEqual(TextCompletionInsertTextFormat.Snippet, merged.InsertTextFormat);
	}

	[TestMethod]
	public void WithResolvedContent_BothSidesCarryCommitFields_KeepsCurrentItemValues()
	{
		var currentEdit = new TextCompletionTextEdit(new TextRange(0, 3), new TextRange(0, 5), "current");
		var resolvedEdit = new TextCompletionTextEdit(new TextRange(1, 2), new TextRange(1, 4), "resolved");
		var item = new TextCompletionItem("label")
		{
			TextEdit = currentEdit,
			InsertTextFormat = TextCompletionInsertTextFormat.Snippet
		};
		var resolved = new TextCompletionItem("label")
		{
			TextEdit = resolvedEdit,
			InsertTextFormat = TextCompletionInsertTextFormat.PlainText
		};

		TextCompletionItem merged = item.WithResolvedContent(resolved);

		Assert.AreEqual(currentEdit, merged.TextEdit);

		// An explicitly assigned format wins, including a reset to plain text.
		Assert.AreEqual(TextCompletionInsertTextFormat.PlainText, merged.InsertTextFormat);
	}

	[TestMethod]
	public void WithResolvedContent_DistinctCurrentCommitText_StaysFromCurrentItem()
	{
		// Text that differs from the current label is treated as intentional and is never replaced.
		var item = new TextCompletionItem("label") { InsertText = "inserted", FilterText = "filtered" };
		var resolved = new TextCompletionItem("resolved-label") { InsertText = "resolved-insert", FilterText = "resolved-filter" };

		TextCompletionItem merged = item.WithResolvedContent(resolved);

		Assert.AreEqual("label", merged.Label);
		Assert.AreEqual("inserted", merged.InsertText);
		Assert.AreEqual("filtered", merged.FilterText);
	}

	[TestMethod]
	public void WithResolvedContent_LabelFallbackCommitText_AdoptsResolvedCommitText()
	{
		// The list item only had the label fallback, so resolve-time commit data is adopted.
		var item = new TextCompletionItem("label");
		var resolved = new TextCompletionItem("label") { InsertText = "resolved-insert", FilterText = "resolved-filter" };

		TextCompletionItem merged = item.WithResolvedContent(resolved);

		Assert.AreEqual("label", merged.Label);
		Assert.AreEqual("resolved-insert", merged.InsertText);
		Assert.AreEqual("resolved-filter", merged.FilterText);
	}

	[TestMethod]
	public void WithResolvedContent_LabelFallbackOnResolvedItem_KeepsCurrentCommitText()
	{
		// The resolved item only carries its own label fallback, so there is no commit data to adopt.
		var item = new TextCompletionItem("label");
		var resolved = new TextCompletionItem("resolved-label");

		TextCompletionItem merged = item.WithResolvedContent(resolved);

		Assert.AreEqual("label", merged.Label);
		Assert.AreEqual("label", merged.InsertText);
		Assert.AreEqual("label", merged.FilterText);
	}

	[TestMethod]
	public void WithResolvedContent_CurrentItemWithEditPayload_KeepsCommitText()
	{
		// An explicit edit payload makes the commit intentional even when the insertion text equals the label.
		var textEdit = new TextCompletionTextEdit(new TextRange(0, 3), new TextRange(0, 5));
		var item = new TextCompletionItem("spawn") { InsertText = "spawn", TextEdit = textEdit };
		var resolved = new TextCompletionItem("spawn") { InsertText = "resolved-spawn" };

		TextCompletionItem merged = item.WithResolvedContent(resolved);

		Assert.AreEqual("spawn", merged.InsertText);
		Assert.AreEqual(textEdit, merged.TextEdit);
	}

	[TestMethod]
	public void WithResolvedContent_EditPayloadVetoesResolvedInsertText_WhenInsertTextWasNeverSet()
	{
		// The current item never set an insertion text but carries an edit payload; the resolved
		// insertion text must not be adopted, because the edit payload owns the commit text.
		var textEdit = new TextCompletionTextEdit(new TextRange(0, 3), new TextRange(0, 5));
		var item = new TextCompletionItem("spawn") { TextEdit = textEdit };
		var resolved = new TextCompletionItem("spawn") { InsertText = "resolved-spawn" };

		TextCompletionItem merged = item.WithResolvedContent(resolved);

		Assert.AreEqual("spawn", merged.InsertText, "The label fallback must remain when an edit payload owns the commit text.");
		Assert.AreEqual(textEdit, merged.TextEdit);
	}

	[TestMethod]
	public void WithoutTextEdit_PreStampedItem_StillRemovesTextEdit()
	{
		var textEdit = new TextCompletionTextEdit(new TextRange(2, 4));
		var item = new TextCompletionItem("label") { TextEdit = textEdit, RequestDocumentVersion = 4, RequestGeneration = 9 };

		TextCompletionItem strippedItem = item.WithoutTextEdit();

		Assert.AreNotSame(item, strippedItem);
		Assert.IsNull(strippedItem.TextEdit);
		Assert.AreEqual(4, strippedItem.RequestDocumentVersion);
		Assert.AreEqual(9, strippedItem.RequestGeneration);
	}

	[TestMethod]
	public void Documentation_MarkdownKindInitializedBeforeDocumentation_IsNotTrimmed()
	{
		var item = new TextCompletionItem("label")
		{
			DocumentationKind = TextMarkupKind.Markdown,
			Documentation = "  **text**  "
		};

		Assert.AreEqual("  **text**  ", item.Documentation);
	}

	[TestMethod]
	public async Task ResolveCallback_Initialized_AttachesResolver()
	{
		var item = new TextCompletionItem("label")
		{
			ResolveCallback = _ => Task.FromResult(new TextCompletionItem("resolved"))
		};

		Assert.IsTrue(item.CanResolve);

		TextCompletionItem resolvedItem = await item.ResolveAsync().ConfigureAwait(false);

		Assert.AreEqual("resolved", resolvedItem.Label);
	}

	[TestMethod]
	public void WithResolvedContent_ResolvedEditWithNewText_FillsInWhenCurrentItemLacksEdit()
	{
		// The edit payload is adopted as a whole, including its replacement text, so a resolve response
		// can supply the commit text for items that only had a plain insertion text.
		var resolvedEdit = new TextCompletionTextEdit(new TextRange(2, 4), newText: "\t");
		var item = new TextCompletionItem("label");

		TextCompletionItem merged = item.WithResolvedContent(new TextCompletionItem("label") { TextEdit = resolvedEdit });

		Assert.AreEqual(resolvedEdit, merged.TextEdit);
		Assert.AreEqual("\t", merged.TextEdit?.NewText);
	}

	[TestMethod]
	public void WithResolvedContent_BlankPresentationFields_AdoptResolvedValuesAndIgnoreResolvedStamps()
	{
		// Fill-in direction: the current item has no detail or documentation yet, so the resolved values
		// apply; resolved request stamps never override the originating stamps.
		var item = new TextCompletionItem("label") { RequestDocumentVersion = 4, RequestGeneration = 9 };
		var resolved = new TextCompletionItem("label")
		{
			Detail = "resolved detail",
			Documentation = "resolved description",
			RequestDocumentVersion = 77,
			RequestGeneration = 88
		};

		TextCompletionItem merged = item.WithResolvedContent(resolved);

		Assert.AreEqual("resolved detail", merged.Detail);
		Assert.AreEqual("resolved description", merged.Documentation);
		Assert.AreEqual(4, merged.RequestDocumentVersion);
		Assert.AreEqual(9, merged.RequestGeneration);
	}

	[TestMethod]
	public void WithResolvedContent_ResolvedPlainDocumentation_ClearsMarkdownMode()
	{
		// The documentation and its kind are adopted together from a non-blank resolved value.
		var item = new TextCompletionItem("label")
		{
			Documentation = "**marked**",
			DocumentationKind = TextMarkupKind.Markdown
		};
		var resolved = new TextCompletionItem("label") { Documentation = "  plain text  " };

		TextCompletionItem merged = item.WithResolvedContent(resolved);

		Assert.AreEqual("plain text", merged.Documentation);
		Assert.AreEqual(TextMarkupKind.PlainText, merged.DocumentationKind);
	}

	[TestMethod]
	public void WithResolvedContent_ExplicitResolvedTextEqualToOneOwnLabel_IsAdopted()
	{
		// An explicitly assigned value is intentional even when it equals its own label; only unset
		// (label-fallback) text is filled in from the resolved item.
		var item = new TextCompletionItem("label");
		var resolved = new TextCompletionItem("spawn") { InsertText = "spawn", FilterText = "spawn" };

		TextCompletionItem merged = item.WithResolvedContent(resolved);

		Assert.AreEqual("spawn", merged.InsertText);
		Assert.AreEqual("spawn", merged.FilterText);
	}

	[TestMethod]
	public void WithResolvedContent_CurrentItemWithEditPayload_StillAdoptsFilterTextFallback()
	{
		// Filter text only drives matching, so an edit payload on this item does not block a
		// resolve-time filter text; the committed text stays untouched.
		var textEdit = new TextCompletionTextEdit(new TextRange(0, 3), new TextRange(0, 5));
		var item = new TextCompletionItem("spawn") { TextEdit = textEdit };
		var resolved = new TextCompletionItem("spawn") { FilterText = "spawn_func" };

		TextCompletionItem merged = item.WithResolvedContent(resolved);

		Assert.AreEqual("spawn_func", merged.FilterText);
		Assert.AreEqual("spawn", merged.InsertText);
		Assert.AreEqual(textEdit, merged.TextEdit);
	}

	[TestMethod]
	public void WithRequestContext_PreservesEveryOtherField()
	{
		TextCompletionItem item = CreateFullyPopulatedItem();

		TextCompletionItem stampedItem = item.WithRequestContext(requestDocumentVersion: 4, requestGeneration: 9);

		AssertItemsHaveSamePayload(
			item,
			stampedItem,
			nameof(TextCompletionItem.RequestDocumentVersion),
			nameof(TextCompletionItem.RequestGeneration));
		Assert.AreEqual(4, stampedItem.RequestDocumentVersion);
		Assert.AreEqual(9, stampedItem.RequestGeneration);
		Assert.AreEqual("0001", stampedItem.SortText);
		Assert.IsTrue(stampedItem.IsPreselected);
	}

	[TestMethod]
	public void WithoutTextEdit_PreservesEveryOtherField()
	{
		TextCompletionItem item = CreateFullyPopulatedItem();

		TextCompletionItem strippedItem = item.WithoutTextEdit();

		AssertItemsHaveSamePayload(item, strippedItem, nameof(TextCompletionItem.TextEdit));
		Assert.IsNull(strippedItem.TextEdit);
	}

	[TestMethod]
	public void WithResolveCallback_PreservesEveryOtherField()
	{
		TextCompletionItem item = CreateFullyPopulatedItem();

		TextCompletionItem resolvedItem = item.WithResolveCallback(static _ => Task.FromResult(new TextCompletionItem("resolved")));

		AssertItemsHaveSamePayload(item, resolvedItem, nameof(TextCompletionItem.ResolveCallback));
		Assert.IsNotNull(resolvedItem.ResolveCallback);
	}

	private static TextCompletionItem CreateFullyPopulatedItem()
	{
		return new TextCompletionItem("label")
		{
			InsertText = "inserted",
			Documentation = " description ",
			DocumentationKind = TextMarkupKind.PlainText,
			Detail = " detail ",
			Priority = 2.5,
			Kind = TextCompletionItemKind.Method,
			FilterText = "filtered",
			SortText = "0001",
			IsPreselected = true,
			TextEdit = new TextCompletionTextEdit(new TextRange(2, 4), new TextRange(2, 10), "replacement"),
			InsertTextFormat = TextCompletionInsertTextFormat.Snippet,
			Tags = [TextCompletionTag.Deprecated],
			CommitCharacters = ["(", ","],
			AdditionalTextEdits = [new TextCompletionTextEdit(new TextRange(0, 0), newText: "import")],
			RequestDocumentVersion = 1,
			RequestGeneration = 2,
			ResolveCallback = static _ => Task.FromResult(new TextCompletionItem("resolved"))
		};
	}

	[TestMethod]
	public void Tags_NotInitialized_IsEmpty()
	{
		var item = new TextCompletionItem("label");
		Assert.AreEqual(0, item.Tags.Count);
	}

	[TestMethod]
	public void Tags_Explicit_IsPreserved()
	{
		var item = new TextCompletionItem("label") { Tags = [TextCompletionTag.Deprecated] };
		CollectionAssert.AreEqual(new[] { TextCompletionTag.Deprecated }, item.Tags.ToArray());
	}

	[TestMethod]
	public void Tags_Null_IsEmpty()
	{
		var item = new TextCompletionItem("label") { Tags = null! };
		Assert.AreEqual(0, item.Tags.Count);
	}

	[TestMethod]
	public void Tags_AssignedList_IsStoredAsSnapshot()
	{
		var tags = new List<TextCompletionTag> { TextCompletionTag.Deprecated };
		var item = new TextCompletionItem("label") { Tags = tags };

		tags.Clear();

		Assert.AreEqual(1, item.Tags.Count);
	}

	[TestMethod]
	public void CommitCharacters_NotInitialized_IsEmpty()
	{
		var item = new TextCompletionItem("label");
		Assert.AreEqual(0, item.CommitCharacters.Count);
	}

	[TestMethod]
	public void CommitCharacters_Explicit_IsPreserved()
	{
		var item = new TextCompletionItem("label") { CommitCharacters = ["(", ","] };
		CollectionAssert.AreEqual(new[] { "(", "," }, item.CommitCharacters.ToArray());
	}

	[TestMethod]
	public void CommitCharacters_Null_IsEmpty()
	{
		var item = new TextCompletionItem("label") { CommitCharacters = null! };
		Assert.AreEqual(0, item.CommitCharacters.Count);
	}

	[TestMethod]
	public void CommitCharacters_AssignedList_IsStoredAsSnapshot()
	{
		var characters = new List<string> { "(", "," };
		var item = new TextCompletionItem("label") { CommitCharacters = characters };

		characters.Clear();

		Assert.AreEqual(2, item.CommitCharacters.Count);
	}

	[TestMethod]
	public void AdditionalTextEdits_NotInitialized_IsEmpty()
	{
		var item = new TextCompletionItem("label");
		Assert.AreEqual(0, item.AdditionalTextEdits.Count);
	}

	[TestMethod]
	public void AdditionalTextEdits_Explicit_IsPreserved()
	{
		var edit = new TextCompletionTextEdit(new TextRange(0, 0), newText: "local print = print\n");
		var item = new TextCompletionItem("label") { AdditionalTextEdits = [edit] };

		Assert.AreEqual(1, item.AdditionalTextEdits.Count);
		Assert.AreEqual("local print = print\n", item.AdditionalTextEdits[0].NewText);
	}

	[TestMethod]
	public void AdditionalTextEdits_Null_IsEmpty()
	{
		var item = new TextCompletionItem("label") { AdditionalTextEdits = null! };
		Assert.AreEqual(0, item.AdditionalTextEdits.Count);
	}

	[TestMethod]
	public void AdditionalTextEdits_AssignedList_IsStoredAsSnapshot()
	{
		var edits = new List<TextCompletionTextEdit> { new(new TextRange(0, 0), newText: "import") };
		var item = new TextCompletionItem("label") { AdditionalTextEdits = edits };

		edits.Clear();

		Assert.AreEqual(1, item.AdditionalTextEdits.Count);
		Assert.AreEqual("import", item.AdditionalTextEdits[0].NewText);
	}

	[TestMethod]
	public void WithResolvedContent_MergesTagsAsUnion()
	{
		var item = new TextCompletionItem("label");
		var resolved = new TextCompletionItem("label") { Tags = [TextCompletionTag.Deprecated] };

		TextCompletionItem merged = item.WithResolvedContent(resolved);

		CollectionAssert.AreEqual(new[] { TextCompletionTag.Deprecated }, merged.Tags.ToArray());
	}

	[TestMethod]
	public void WithResolvedContent_TagsFromBothSides_MergeWithoutDuplicates()
	{
		var item = new TextCompletionItem("label") { Tags = [TextCompletionTag.Deprecated] };
		var resolved = new TextCompletionItem("label") { Tags = [TextCompletionTag.Deprecated] };

		TextCompletionItem merged = item.WithResolvedContent(resolved);

		Assert.AreEqual(1, merged.Tags.Count);
	}

	[TestMethod]
	public void WithResolvedContent_AdoptsCommitCharactersOnlyWhenMissing()
	{
		var without = new TextCompletionItem("label");
		var with = new TextCompletionItem("label") { CommitCharacters = ["("] };
		var resolved = new TextCompletionItem("label") { CommitCharacters = [".", ":"] };

		TextCompletionItem adopted = without.WithResolvedContent(resolved);
		TextCompletionItem kept = with.WithResolvedContent(resolved);

		CollectionAssert.AreEqual(new[] { ".", ":" }, adopted.CommitCharacters.ToArray());
		CollectionAssert.AreEqual(new[] { "(" }, kept.CommitCharacters.ToArray());
	}

	[TestMethod]
	public void WithResolvedContent_AdoptsAdditionalTextEditsOnlyWhenMissing()
	{
		var keptEdit = new TextCompletionTextEdit(new TextRange(1, 1), newText: "kept");
		var resolvedEdit = new TextCompletionTextEdit(new TextRange(2, 2), newText: "resolved");

		var without = new TextCompletionItem("label");
		var with = new TextCompletionItem("label") { AdditionalTextEdits = [keptEdit] };

		TextCompletionItem adopted = without.WithResolvedContent(new TextCompletionItem("label") { AdditionalTextEdits = [resolvedEdit] });
		TextCompletionItem kept = with.WithResolvedContent(new TextCompletionItem("label") { AdditionalTextEdits = [resolvedEdit] });

		Assert.AreEqual(1, adopted.AdditionalTextEdits.Count);
		Assert.AreEqual("resolved", adopted.AdditionalTextEdits[0].NewText);
		Assert.AreEqual(1, kept.AdditionalTextEdits.Count);
		Assert.AreEqual("kept", kept.AdditionalTextEdits[0].NewText);
	}

	private static void AssertItemsHaveSamePayload(TextCompletionItem expected, TextCompletionItem actual, params string[] excludedProperties)
	{
		foreach (PropertyInfo property in typeof(TextCompletionItem).GetProperties(BindingFlags.Public | BindingFlags.Instance))
		{
			if (excludedProperties.Contains(property.Name))
				continue;

			Assert.AreEqual(
				property.GetValue(expected),
				property.GetValue(actual),
				$"The copy must preserve '{property.Name}'.");
		}
	}
}

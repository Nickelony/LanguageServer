using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.IDEKit.IntelliSense.Tests.Completion;

[TestClass]
public sealed class TextCompletionItemTests
{
	[TestMethod]
	public void Constructor_NullInsertText_UsesLabel()
	{
		var item = new TextCompletionItem("label");

		Assert.AreEqual("label", item.InsertText);
	}

	[TestMethod]
	public void Constructor_WhitespaceInsertText_UsesLabel()
	{
		var item = new TextCompletionItem("label", insertText: "  ");

		Assert.AreEqual("label", item.InsertText);
	}

	[TestMethod]
	public void Constructor_ExplicitInsertText_IsPreserved()
	{
		var item = new TextCompletionItem("label", insertText: "inserted");

		Assert.AreEqual("inserted", item.InsertText);
	}

	[TestMethod]
	public void Constructor_BlankDescription_IsNull()
	{
		var item = new TextCompletionItem("label", description: "   ");

		Assert.IsNull(item.Description);
	}

	[TestMethod]
	public void Constructor_Description_IsTrimmed()
	{
		var item = new TextCompletionItem("label", description: "  text  ");

		Assert.AreEqual("text", item.Description);
	}

	[TestMethod]
	public void Constructor_MarkdownDescription_IsNotTrimmed()
	{
		var item = new TextCompletionItem("label", description: "  **text**  ", isDescriptionMarkdown: true);

		Assert.AreEqual("  **text**  ", item.Description);
		Assert.IsTrue(item.IsDescriptionMarkdown);
	}

	[TestMethod]
	public void Constructor_NullKind_DefaultsToGeneric()
	{
		var item = new TextCompletionItem("label");

		Assert.AreSame(TextCompletionItemKind.Generic, item.Kind);
	}

	[TestMethod]
	public void Constructor_ExplicitKind_IsPreserved()
	{
		var item = new TextCompletionItem("label", kind: TextCompletionItemKind.Method);

		Assert.AreSame(TextCompletionItemKind.Method, item.Kind);
	}

	[TestMethod]
	public void Constructor_NullFilterText_UsesLabel()
	{
		var item = new TextCompletionItem("label");

		Assert.AreEqual("label", item.FilterText);
	}

	[TestMethod]
	public void Constructor_ExplicitFilterText_IsPreserved()
	{
		var item = new TextCompletionItem("label", filterText: "filtered");

		Assert.AreEqual("filtered", item.FilterText);
	}

	[TestMethod]
	public void Constructor_RequestMetadata_IsPreserved()
	{
		var item = new TextCompletionItem(
			"label",
			requestDocumentVersion: 3,
			requestGeneration: 7,
			insertCaretOffset: 42);

		Assert.AreEqual(3, item.RequestDocumentVersion);
		Assert.AreEqual(7, item.RequestGeneration);
		Assert.AreEqual(42, item.InsertCaretOffset);
	}
}

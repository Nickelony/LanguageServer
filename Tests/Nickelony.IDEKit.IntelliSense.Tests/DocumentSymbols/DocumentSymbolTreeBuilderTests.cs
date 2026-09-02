using Nickelony.IDEKit.IntelliSense.DocumentSymbols;

namespace Nickelony.IDEKit.IntelliSense.Tests.DocumentSymbols;

[TestClass]
public sealed class DocumentSymbolTreeBuilderTests
{
	private sealed record Node(string Text, string Payload);

	[TestMethod]
	public void BuildFlatNodes_ProducesOneSymbolPerNode()
	{
		IReadOnlyList<Node> nodes = [new Node("Alpha", "a"), new Node("Beta", "b")];

		IReadOnlyList<TextDocumentSymbol> symbols = DocumentSymbolTreeBuilder.BuildFlatNodes(nodes, node => node.Text, node => node.Payload);

		Assert.AreEqual(2, symbols.Count);
		Assert.AreEqual("Alpha", symbols[0].Name);
		Assert.AreEqual("a", symbols[0].Data);
		Assert.IsFalse(symbols[0].HasChildren);
		Assert.AreEqual(TextDocumentSymbolKind.Variable, symbols[0].Kind);
	}

	[TestMethod]
	public void BuildFlatNodes_WithoutDataSelector_KeepsDataNull()
	{
		IReadOnlyList<Node> nodes = [new Node("Alpha", "a")];

		IReadOnlyList<TextDocumentSymbol> symbols = DocumentSymbolTreeBuilder.BuildFlatNodes(nodes, node => node.Text);

		Assert.AreEqual(1, symbols.Count);
		Assert.IsNull(symbols[0].Data);
	}

	[TestMethod]
	public void BuildGroupedNodes_ProducesRootPerGroupWithChildren()
	{
		IReadOnlyList<string> groups = ["First", "Second"];

		IReadOnlyList<TextDocumentSymbol> symbols = DocumentSymbolTreeBuilder.BuildGroupedNodes(
			groups,
			group => group,
			SelectGroupNodes,
			node => node.Text,
			node => node.Payload);

		Assert.AreEqual(2, symbols.Count);

		Assert.AreEqual("First", symbols[0].Name);
		Assert.AreEqual(TextDocumentSymbolKind.Module, symbols[0].Kind);
		Assert.IsTrue(symbols[0].HasChildren);
		Assert.AreEqual(2, symbols[0].Children.Count);
		Assert.AreEqual("A", symbols[0].Children[0].Name);
		Assert.AreEqual("a", symbols[0].Children[0].Data);

		Assert.AreEqual("Second", symbols[1].Name);
		Assert.AreEqual(1, symbols[1].Children.Count);
		Assert.AreEqual("C", symbols[1].Children[0].Name);
	}

	[TestMethod]
	public void BuildGroupedNodes_EmptyGroup_ProducesRootWithoutChildren()
	{
		IReadOnlyList<string> groups = ["Empty"];

		IReadOnlyList<TextDocumentSymbol> symbols = DocumentSymbolTreeBuilder.BuildGroupedNodes<string, string>(
			groups,
			group => group,
			_ => [],
			node => node);

		Assert.AreEqual(1, symbols.Count);
		Assert.AreEqual("Empty", symbols[0].Name);
		Assert.IsFalse(symbols[0].HasChildren);
	}

	private static IReadOnlyList<Node> SelectGroupNodes(string group)
	{
		return group == "First"
			? [new Node("A", "a"), new Node("B", "b")]
			: [new Node("C", "c")];
	}
}

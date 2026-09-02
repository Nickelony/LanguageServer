using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.DocumentSymbols;

namespace Nickelony.IDEKit.IntelliSense.Tests.DocumentSymbols;

[TestClass]
public sealed class TextDocumentSymbolTests
{
	[TestMethod]
	public void Constructor_WithoutRanges_LeavesRangesNull()
	{
		var symbol = new TextDocumentSymbol("name", TextDocumentSymbolKind.Variable);

		Assert.AreEqual("name", symbol.Name);
		Assert.IsNull(symbol.SelectionRange);
		Assert.IsNull(symbol.Range);
		Assert.IsFalse(symbol.HasChildren);
	}

	[TestMethod]
	public void Constructor_SelectionRange_SetsFullRangeWhenOmitted()
	{
		var selection = new TextRange(4, 5);
		var symbol = new TextDocumentSymbol("name", TextDocumentSymbolKind.Method, selection);

		Assert.AreEqual(selection, symbol.SelectionRange);
		Assert.AreEqual(selection, symbol.Range);
	}

	[TestMethod]
	public void Constructor_FullRangeOverridesSelectionRange()
	{
		var selection = new TextRange(4, 5);
		var range = new TextRange(0, 20);
		var symbol = new TextDocumentSymbol("name", TextDocumentSymbolKind.Method, selection, range);

		Assert.AreEqual(selection, symbol.SelectionRange);
		Assert.AreEqual(range, symbol.Range);
	}

	[TestMethod]
	public void Constructor_BlankDetail_IsNull()
	{
		var symbol = new TextDocumentSymbol("name", TextDocumentSymbolKind.Class, detail: "   ");

		Assert.IsNull(symbol.Detail);
	}

	[TestMethod]
	public void Constructor_Children_ArePreserved()
	{
		var child = new TextDocumentSymbol("child", TextDocumentSymbolKind.Field);
		var symbol = new TextDocumentSymbol("parent", TextDocumentSymbolKind.Class, children: [child]);

		Assert.IsTrue(symbol.HasChildren);
		Assert.AreEqual(1, symbol.Children.Count);
		Assert.AreSame(child, symbol.Children[0]);
	}

	[TestMethod]
	public void Constructor_Data_IsPreserved()
	{
		var payload = new object();
		var symbol = new TextDocumentSymbol("name", TextDocumentSymbolKind.Variable, data: payload);

		Assert.AreSame(payload, symbol.Data);
	}
}

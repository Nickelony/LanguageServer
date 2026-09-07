using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Documents;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

[TestClass]
public sealed class TextDocumentExtensionsTests
{
	[TestMethod]
	public void ClampOffset_WithinBounds_ReturnsOffset()
	{
		var document = new TextDocument("abcdef");
		Assert.AreEqual(3, document.ClampOffset(3));
	}

	[TestMethod]
	public void ClampOffset_NegativeOffset_ReturnsZero()
	{
		var document = new TextDocument("abcdef");
		Assert.AreEqual(0, document.ClampOffset(-1));
	}

	[TestMethod]
	public void ClampOffset_BeyondEnd_ReturnsTextLength()
	{
		var document = new TextDocument("abcdef");

		Assert.AreEqual(6, document.ClampOffset(6));
		Assert.AreEqual(6, document.ClampOffset(100));
	}

	[TestMethod]
	public void ClampOffset_EmptyDocument_ReturnsZero()
	{
		var document = new TextDocument();
		Assert.AreEqual(0, document.ClampOffset(10));
	}
}

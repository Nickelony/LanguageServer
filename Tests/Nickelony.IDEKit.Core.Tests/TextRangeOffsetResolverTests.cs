namespace Nickelony.IDEKit.Core.Editing.Tests;

[TestClass]
public sealed class TextRangeOffsetResolverTests
{
	[TestMethod]
	public void TryResolveOffsets_ClampsStaleLineIndicesToDocumentBounds()
	{
		TextLineMap lineMap = TextLineMap.Build("\nvalue");

		bool resolved = TextRangeOffsetResolver.TryResolveOffsets(lineMap,
			startLineIndex: 99, startCharacter: 99, endLineIndex: 99, endCharacter: 99,
			out int startOffset, out int endOffset);

		Assert.IsTrue(resolved);
		Assert.AreEqual(1, startOffset);
		Assert.AreEqual(6, endOffset);
	}
}

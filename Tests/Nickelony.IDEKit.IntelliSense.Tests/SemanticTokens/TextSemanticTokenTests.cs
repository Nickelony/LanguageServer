using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.SemanticTokens;

namespace Nickelony.IDEKit.IntelliSense.Tests.SemanticTokens;

[TestClass]
public sealed class TextSemanticTokenTests
{
	[TestMethod]
	public void Constructor_StoresRangeAndType()
	{
		var token = new TextSemanticToken(new TextRange(6, 5), TextSemanticTokenTypes.Variable);

		Assert.AreEqual(6, token.Range.Offset);
		Assert.AreEqual(5, token.Range.Length);
		Assert.AreEqual("variable", token.Type);
		Assert.AreEqual(0, token.Modifiers.Count);
	}

	[TestMethod]
	public void Constructor_Modifiers_AreOwnedSnapshot()
	{
		var modifiers = new List<string> { TextSemanticTokenModifiers.Declaration, TextSemanticTokenModifiers.Static };
		var token = new TextSemanticToken(new TextRange(0, 4), TextSemanticTokenTypes.Method, modifiers);

		modifiers.Clear();

		Assert.AreEqual(2, token.Modifiers.Count);
		Assert.IsTrue(token.HasModifier(TextSemanticTokenModifiers.Declaration));
		Assert.IsTrue(token.HasModifier(TextSemanticTokenModifiers.Static));
	}

	[TestMethod]
	public void HasModifier_UnknownModifier_ReturnsFalse()
	{
		var token = new TextSemanticToken(new TextRange(0, 4), TextSemanticTokenTypes.Variable, [TextSemanticTokenModifiers.Global]);

		Assert.IsFalse(token.HasModifier(TextSemanticTokenModifiers.Deprecated));
		Assert.IsFalse(token.HasModifier(string.Empty));
		Assert.IsFalse(token.HasModifier("   "));
	}

	[TestMethod]
	public void HasModifier_NoModifiers_ReturnsFalse()
	{
		var token = new TextSemanticToken(new TextRange(0, 4), TextSemanticTokenTypes.Number);

		Assert.IsFalse(token.HasModifier(TextSemanticTokenModifiers.Declaration));
	}

	[TestMethod]
	public void HasModifier_IsOrdinalCaseSensitive()
	{
		var token = new TextSemanticToken(new TextRange(0, 4), TextSemanticTokenTypes.Variable, [TextSemanticTokenModifiers.Global]);

		Assert.IsFalse(token.HasModifier("Global"));
	}
}

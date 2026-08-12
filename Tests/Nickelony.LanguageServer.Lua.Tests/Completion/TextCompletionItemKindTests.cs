using Nickelony.LanguageServer.Abstractions.Completion;
using System.Text.Json;

namespace Nickelony.LanguageServer.Lua.Tests;

[TestClass]
public sealed class TextCompletionItemKindTests
{
	[TestMethod]
	public void WellKnownKindsAreRecognizedAndHaveUniqueIdentifiers()
	{
		TextCompletionItemKind[] kinds =
		[
			TextCompletionItemKind.Generic,
			TextCompletionItemKind.Property,
			TextCompletionItemKind.Array,
			TextCompletionItemKind.Section,
			TextCompletionItemKind.Directive,
			TextCompletionItemKind.Constant,
			TextCompletionItemKind.Keyword,
			TextCompletionItemKind.Method,
			TextCompletionItemKind.Variable,
			TextCompletionItemKind.Field,
			TextCompletionItemKind.Class,
			TextCompletionItemKind.Parameter,
			TextCompletionItemKind.Namespace,
			TextCompletionItemKind.File,
			TextCompletionItemKind.Folder
		];

		Assert.AreEqual(kinds.Length, kinds.Select(kind => kind.Identifier).Distinct(StringComparer.Ordinal).Count());
		Assert.IsTrue(kinds.All(kind => kind.IsWellKnown));
	}

	[TestMethod]
	public void CustomKindsNormalizeValidateAndCompareByIdentifier()
	{
		TextCompletionItemKind customKind = TextCompletionItemKind.CreateCustom("  host.lua-type  ");
		TextCompletionItemKind roundTrippedKind = TextCompletionItemKind.FromIdentifier(customKind.Identifier);

		Assert.AreEqual("host.lua-type", customKind.Identifier);
		Assert.IsFalse(customKind.IsWellKnown);
		Assert.AreEqual(customKind, roundTrippedKind);
		Assert.AreEqual(customKind.GetHashCode(), roundTrippedKind.GetHashCode());

		Assert.ThrowsExactly<ArgumentException>(() => TextCompletionItemKind.CreateCustom("Generic"));
		Assert.ThrowsExactly<ArgumentException>(() => TextCompletionItemKind.CreateCustom("9invalid"));
		Assert.ThrowsExactly<ArgumentException>(() => TextCompletionItemKind.CreateCustom("invalid kind"));
	}

	[TestMethod]
	public void CustomKindsSurviveStringJsonRoundTrip()
	{
		TextCompletionItemKind originalKind = TextCompletionItemKind.CreateCustom("editor.lua.special");
		string json = JsonSerializer.Serialize(originalKind);

		TextCompletionItemKind restoredKind = JsonSerializer.Deserialize<TextCompletionItemKind>(json)
			?? throw new AssertFailedException("Expected a completion kind after JSON deserialization.");

		Assert.AreEqual("\"editor.lua.special\"", json);
		Assert.AreEqual(originalKind, restoredKind);
		Assert.IsFalse(restoredKind.IsWellKnown);
	}
}

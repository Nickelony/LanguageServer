using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class HoverResponseTests
{
	[TestMethod]
	public void Deserialize_StringContents_ParsesPlainText()
	{
		HoverResponse? response = JsonSerializer.Deserialize<HoverResponse>(
			"""
			{ "contents": "Plain hover text" }
			""");

		Assert.IsNotNull(response);
		Assert.IsNotNull(response.Contents);
		Assert.AreEqual(JsonValueKind.String, response.Contents!.Value.ValueKind);
		Assert.AreEqual("Plain hover text", response.Contents.Value.GetString());
	}

	[TestMethod]
	public void Deserialize_MarkupObjectContents_IsReadableThroughMarkupContentReader()
	{
		HoverResponse? response = JsonSerializer.Deserialize<HoverResponse>(
			"""
			{
			  "contents": { "kind": "markdown", "value": "**bold**" }
			}
			""");

		Assert.IsNotNull(response);

		ProtocolMarkupContent content = MarkupContentReader.ExtractContent(response.Contents!.Value);

		Assert.IsTrue(content.IsMarkdown);
		Assert.AreEqual("**bold**", content.Text);
	}

	[TestMethod]
	public void Deserialize_MarkupArrayContents_CombinesEntries()
	{
		HoverResponse? response = JsonSerializer.Deserialize<HoverResponse>(
			"""
			{
			  "contents": [
			    "Summary",
			    { "kind": "markdown", "value": "Details" }
			  ]
			}
			""");

		Assert.IsNotNull(response);

		ProtocolMarkupContent content = MarkupContentReader.ExtractContent(response.Contents!.Value);

		Assert.AreEqual("Summary\n\nDetails", content.Text.Replace("\r\n", "\n", StringComparison.Ordinal));
	}

	[TestMethod]
	public void Deserialize_MissingContents_LeavesNullContents()
	{
		HoverResponse? response = JsonSerializer.Deserialize<HoverResponse>(
			"""
			{ }
			""");

		Assert.IsNotNull(response);
		Assert.IsNull(response.Contents);
	}
}

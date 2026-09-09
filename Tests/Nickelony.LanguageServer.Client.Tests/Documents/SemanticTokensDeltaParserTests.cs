using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class SemanticTokensDeltaParserTests
{
	[TestMethod]
	public void Parse_MissingStartReturnsNoChanges()
	{
		SemanticTokensWireResponse response = JsonSerializer.Deserialize<SemanticTokensWireResponse>(
			"""
			{
			  "resultId": "delta-1",
			  "edits": [
			    {
			      "deleteCount": 2,
			      "data": [1, 2, 3]
			    }
			  ]
			}
			""");

		SemanticTokensDeltaResponse result = SemanticTokensDeltaParser.Parse(response);

		Assert.AreEqual("delta-1", result.ResultId);
		Assert.IsNull(result.Data);
		Assert.IsNull(result.Edits);
	}

	[TestMethod]
	public void Parse_FullDataResponse_ReturnsDataStream()
	{
		int[] expectedData = [0, 0, 3, 0, 0, 0, 4, 2, 0, 1];

		SemanticTokensWireResponse response = JsonSerializer.Deserialize<SemanticTokensWireResponse>(
			"""
			{
			  "resultId": "full-1",
			  "data": [0, 0, 3, 0, 0, 0, 4, 2, 0, 1]
			}
			""");

		SemanticTokensDeltaResponse result = SemanticTokensDeltaParser.Parse(response);

		Assert.AreEqual("full-1", result.ResultId);
		Assert.IsNotNull(result.Data);
		CollectionAssert.AreEqual(expectedData, result.Data);
		Assert.IsNull(result.Edits);
	}

	[TestMethod]
	public void Parse_ValidEditsResponse_ReturnsEdits()
	{
		SemanticTokensWireResponse response = JsonSerializer.Deserialize<SemanticTokensWireResponse>(
			"""
			{
			  "resultId": "delta-2",
			  "edits": [
			    {
			      "start": 1,
			      "deleteCount": 2,
			      "data": [7, 8]
			    }
			  ]
			}
			""");

		SemanticTokensDeltaResponse result = SemanticTokensDeltaParser.Parse(response);

		Assert.AreEqual("delta-2", result.ResultId);
		Assert.IsNull(result.Data);
		Assert.IsNotNull(result.Edits);
		Assert.AreEqual(1, result.Edits!.Count);
		Assert.AreEqual(1, result.Edits[0].Start);
		Assert.AreEqual(2, result.Edits[0].DeleteCount);
		CollectionAssert.AreEqual(new[] { 7, 8 }, result.Edits[0].Data);
	}

	[TestMethod]
	public void Parse_NegativeCoordinatesDegradeToNoChanges()
	{
		SemanticTokensDeltaResponse result = SemanticTokensDeltaParser.Parse(DeserializeEdits(
			"""
			{
			  "resultId": "delta-3",
			  "edits": [ { "start": -1, "deleteCount": 2 } ]
			}
			"""));

		Assert.AreEqual("delta-3", result.ResultId);
		Assert.IsNull(result.Data);
		Assert.IsNull(result.Edits);

		result = SemanticTokensDeltaParser.Parse(DeserializeEdits(
			"""
			{
			  "resultId": "delta-4",
			  "edits": [ { "start": 1, "deleteCount": -2 } ]
			}
			"""));

		Assert.IsNull(result.Edits);
	}

	[TestMethod]
	public void Parse_UnorderedOrOverlappingEditsDegradeToNoChanges()
	{
		// The second edit starts before the first one ends, which violates the ordered, non-overlapping
		// edit contract.
		SemanticTokensDeltaResponse result = SemanticTokensDeltaParser.Parse(DeserializeEdits(
			"""
			{
			  "resultId": "delta-5",
			  "edits": [
			    { "start": 5, "deleteCount": 3 },
			    { "start": 6, "deleteCount": 0 }
			  ]
			}
			"""));

		Assert.AreEqual("delta-5", result.ResultId);
		Assert.IsNull(result.Data);
		Assert.IsNull(result.Edits);

		// A valid edit list with a gap between the edits stays accepted.
		result = SemanticTokensDeltaParser.Parse(DeserializeEdits(
			"""
			{
			  "resultId": "delta-6",
			  "edits": [
			    { "start": 1, "deleteCount": 2 },
			    { "start": 5, "deleteCount": 0, "data": [9] }
			  ]
			}
			"""));

		Assert.IsNull(result.Data);
		Assert.IsNotNull(result.Edits);
		Assert.AreEqual(2, result.Edits!.Count);
	}

	[TestMethod]
	public void Parse_WhenDataAndEditsAreBothPresent_PrefersTheFullDataStream()
	{
		SemanticTokensDeltaResponse result = SemanticTokensDeltaParser.Parse(DeserializeEdits(
			"""
			{
			  "resultId": "delta-7",
			  "data": [1, 2, 3],
			  "edits": [ { "start": 0, "deleteCount": 1 } ]
			}
			"""));

		Assert.IsNotNull(result.Data);
		Assert.IsNull(result.Edits);
		CollectionAssert.AreEqual(new[] { 1, 2, 3 }, result.Data);
	}

	private static SemanticTokensWireResponse? DeserializeEdits(string json)
		=> JsonSerializer.Deserialize<SemanticTokensWireResponse>(json);
}

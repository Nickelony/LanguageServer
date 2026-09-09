using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class PublishDiagnosticsParamsJsonConverterTests
{
	[TestMethod]
	public void Deserialize_WellFormedPayload_ParsesTypedEntries()
	{
		PublishDiagnosticsParams parameters = JsonSerializer.Deserialize<PublishDiagnosticsParams>(
			"""
			{
			  "uri": "file:///workspace/test.ext",
			  "version": 3,
			  "diagnostics": [
			    {
			      "range": {
			        "start": { "line": 0, "character": 0 },
			        "end": { "line": 0, "character": 5 }
			      },
			      "severity": 1,
			      "message": "broken",
			      "source": "example-language-server",
			      "code": "E1"
			    }
			  ]
			}
			""");

		Assert.AreEqual("file:///workspace/test.ext", parameters.Uri);
		Assert.AreEqual(3, parameters.Version);
		Assert.IsNotNull(parameters.Diagnostics);
		Assert.AreEqual(1, parameters.Diagnostics.Count);
		Assert.AreEqual(DiagnosticSeverity.Error, parameters.Diagnostics[0].Severity);
		Assert.AreEqual(5, parameters.Diagnostics[0].Range?.End.Character);
		Assert.AreEqual("broken", parameters.Diagnostics[0].Message);
		Assert.AreEqual("example-language-server", parameters.Diagnostics[0].Source);
	}

	[TestMethod]
	public void Deserialize_MalformedEntry_IsSkippedWithWarning()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);

		var options = new JsonSerializerOptions();
		options.Converters.Add(new PublishDiagnosticsParamsJsonConverter(logScope));

		PublishDiagnosticsParams parameters = JsonSerializer.Deserialize<PublishDiagnosticsParams>(
			"""
			{
			  "uri": "file:///workspace/test.ext",
			  "version": 3,
			  "diagnostics": [
			    {
			      "range": {
			        "start": { "line": 0, "character": 0 },
			        "end": { "line": 0, "character": 5 }
			      },
			      "severity": 1,
			      "message": "kept"
			    },
			    {
			      "range": { "start": { "line": 0, "character": 0 } },
			      "severity": 1,
			      "message": "dropped"
			    }
			  ]
			}
			""", options);

		Assert.IsNotNull(parameters.Diagnostics);
		Assert.AreEqual(1, parameters.Diagnostics.Count);
		Assert.AreEqual("kept", parameters.Diagnostics[0].Message);
		Assert.IsTrue(logScope.Logs.Any(log => log.Contains("Skipping malformed diagnostic", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void Deserialize_RootNotObject_LogsAndYieldsEmptyPayload()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);

		var options = new JsonSerializerOptions();
		options.Converters.Add(new PublishDiagnosticsParamsJsonConverter(logScope));

		PublishDiagnosticsParams parameters = JsonSerializer.Deserialize<PublishDiagnosticsParams>("[]", options);

		Assert.IsNull(parameters.Uri);
		Assert.IsNull(parameters.Version);
		Assert.IsNull(parameters.Diagnostics);
		Assert.IsTrue(logScope.Logs.Any(log => log.Contains("not an object", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void Deserialize_UnknownSeverity_StaysRepresentable()
	{
		PublishDiagnosticsParams parameters = JsonSerializer.Deserialize<PublishDiagnosticsParams>(
			"""
			{
			  "diagnostics": [
			    {
			      "range": {
			        "start": { "line": 0, "character": 0 },
			        "end": { "line": 0, "character": 1 }
			      },
			      "severity": 99
			    }
			  ]
			}
			""");

		Assert.IsNotNull(parameters.Diagnostics);
		Assert.AreEqual(1, parameters.Diagnostics.Count);
		Assert.AreEqual((DiagnosticSeverity)99, parameters.Diagnostics[0].Severity);
	}

	[TestMethod]
	public void Deserialize_MalformedRootMembers_AreIgnoredWithWarnings()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);

		var options = new JsonSerializerOptions();
		options.Converters.Add(new PublishDiagnosticsParamsJsonConverter(logScope));

		PublishDiagnosticsParams parameters = JsonSerializer.Deserialize<PublishDiagnosticsParams>(
			"""{ "uri": 7, "version": "newest", "diagnostics": null }""", options);

		Assert.IsNull(parameters.Uri);
		Assert.IsNull(parameters.Version);
		Assert.IsNull(parameters.Diagnostics);
		Assert.IsTrue(logScope.Logs.Any(log => log.Contains("'uri' property", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
		Assert.IsTrue(logScope.Logs.Any(log => log.Contains("'version' property", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void Deserialize_AbsentDiagnostics_LeavesNull()
	{
		PublishDiagnosticsParams parameters = JsonSerializer.Deserialize<PublishDiagnosticsParams>(
			"""{ "uri": "file:///workspace/test.ext" }""");

		Assert.AreEqual("file:///workspace/test.ext", parameters.Uri);
		Assert.IsNull(parameters.Version);
		Assert.IsNull(parameters.Diagnostics);
	}

	[TestMethod]
	public void Serialize_WritesStandardShape()
	{
		var parameters = new PublishDiagnosticsParams(
			"file:///workspace/test.ext",
			3,
			[
				new DiagnosticPayload(
					new ProtocolRangePayload(new ProtocolPosition(0, 0), new ProtocolPosition(0, 5)),
					DiagnosticSeverity.Error,
					"broken",
					"example-language-server",
					JsonSerializer.SerializeToElement("E1"))
			]);

		string json = JsonSerializer.Serialize(parameters);

		Assert.AreEqual(
			"""{"uri":"file:///workspace/test.ext","version":3,"diagnostics":[{"range":{"start":{"line":0,"character":0},"end":{"line":0,"character":5}},"severity":1,"message":"broken","source":"example-language-server","code":"E1"}]}""",
			json);
	}
}

using Nickelony.IDEKit.Core.Diagnostics;
using System.Text.Json;

namespace Nickelony.LanguageServer.Lua.Tests;

[TestClass]
public sealed class LuaLanguageServerDiagnosticsParserTests
{
	[TestMethod]
	public void TryParse_PreservesZeroWidthDiagnosticOnEmptyLine()
	{
		const string filePath = @"C:\Workspace\test.lua";
		const string content = "local value = 1\n\nnextLine = 2";

		bool parsed = LuaLanguageServerDiagnosticsParser.TryParse(
			CreateDiagnostics(line: 1, startCharacter: 0, endLine: 1, endCharacter: 0),
			filePath,
			content,
			documentVersion: 1,
			out LuaPublishedDiagnostics? publishedDiagnostics);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.AreEqual(1, publishedDiagnostics.Diagnostics.Count);

		// A zero-width diagnostic keeps its exact position (line 1, character 0) instead of being
		// anchored to visible text; widening an empty range is the rendering layer's concern.
		Assert.AreEqual(content.IndexOf("nextLine", StringComparison.Ordinal) - 1, publishedDiagnostics.Diagnostics[0].StartOffset);
		Assert.AreEqual(publishedDiagnostics.Diagnostics[0].StartOffset, publishedDiagnostics.Diagnostics[0].EndOffset);
	}

	[TestMethod]
	public void TryParse_PreservesZeroWidthDiagnosticOnTrailingEmptyLine()
	{
		const string filePath = @"C:\Workspace\test.lua";
		const string content = "return value\n";

		bool parsed = LuaLanguageServerDiagnosticsParser.TryParse(
			CreateDiagnostics(line: 1, startCharacter: 0, endLine: 1, endCharacter: 0),
			filePath,
			content,
			documentVersion: 1,
			out LuaPublishedDiagnostics? publishedDiagnostics);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.AreEqual(1, publishedDiagnostics.Diagnostics.Count);
		Assert.AreEqual(content.Length, publishedDiagnostics.Diagnostics[0].StartOffset);
		Assert.AreEqual(publishedDiagnostics.Diagnostics[0].StartOffset, publishedDiagnostics.Diagnostics[0].EndOffset);
	}

	[TestMethod]
	public void TryParse_PreservesZeroWidthDiagnosticInsideWordWithoutWidening()
	{
		const string filePath = @"C:\Workspace\test.lua";
		const string content = "local value = compute(1)";

		bool parsed = LuaLanguageServerDiagnosticsParser.TryParse(
			CreateDiagnostics(line: 0, startCharacter: 21, endLine: 0, endCharacter: 21),
			filePath,
			content,
			documentVersion: 1,
			out LuaPublishedDiagnostics? publishedDiagnostics);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.AreEqual(1, publishedDiagnostics.Diagnostics.Count);

		// A position-only diagnostic such as "expected ')' here" must keep the range the server
		// published: widening it to the enclosing word would echo a range LuaLS never sent back to
		// it in code-action requests.
		Assert.AreEqual(21, publishedDiagnostics.Diagnostics[0].StartOffset);
		Assert.AreEqual(21, publishedDiagnostics.Diagnostics[0].EndOffset);
	}

	[TestMethod]
	public void TryParse_IgnoresMalformedDiagnosticEntriesAndPreservesValidEntries()
	{
		const string filePath = @"C:\Workspace\test.lua";
		const string content = "local value = 1";

		bool parsed = LuaLanguageServerDiagnosticsParser.TryParse(
			new PublishDiagnosticsParams(
				Uri: null,
				Version: 1,
				Diagnostics:
				[
					new DiagnosticPayload(
						Range: null,
						Severity: DiagnosticSeverity.Error,
						Message: "Broken payload.",
						Source: null,
						Code: null),
					new DiagnosticPayload(
						new ProtocolRangePayload(
							new ProtocolPosition(0, 6),
							new ProtocolPosition(0, 11)),
						DiagnosticSeverity.Error,
						"Valid payload.",
						null,
						null)
				]),
			filePath,
			content,
			documentVersion: 1,
			out LuaPublishedDiagnostics? publishedDiagnostics);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.AreEqual(1, publishedDiagnostics.Diagnostics.Count);
		Assert.AreEqual(6, publishedDiagnostics.Diagnostics[0].StartOffset);
		Assert.AreEqual(11, publishedDiagnostics.Diagnostics[0].EndOffset);
	}

	[TestMethod]
	public void TryParse_PreservesInformationAndHintDiagnostics()
	{
		const string filePath = @"C:\Workspace\test.lua";
		const string content = "local value = 1";

		bool parsed = LuaLanguageServerDiagnosticsParser.TryParse(
			new PublishDiagnosticsParams(
				Uri: null,
				Version: 1,
				Diagnostics:
				[
					new DiagnosticPayload(
						new ProtocolRangePayload(
							new ProtocolPosition(0, 0),
							new ProtocolPosition(0, 5)),
						DiagnosticSeverity.Information,
						"Informational payload.",
						null,
						null),
					new DiagnosticPayload(
						new ProtocolRangePayload(
							new ProtocolPosition(0, 6),
							new ProtocolPosition(0, 11)),
						DiagnosticSeverity.Hint,
						"Hint payload.",
						null,
						null)
				]),
			filePath,
			content,
			documentVersion: 1,
			out LuaPublishedDiagnostics? publishedDiagnostics);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.AreEqual(2, publishedDiagnostics.Diagnostics.Count);
		Assert.AreEqual(TextDiagnosticSeverity.Information, publishedDiagnostics.Diagnostics[0].Severity);
		Assert.AreEqual(TextDiagnosticSeverity.Hint, publishedDiagnostics.Diagnostics[1].Severity);
	}

	[TestMethod]
	public void TryParse_StoresRawMessageAndFillsSourceAndCode()
	{
		const string filePath = @"C:\Workspace\test.lua";
		const string content = "local value = 1";

		bool parsed = LuaLanguageServerDiagnosticsParser.TryParse(
			new PublishDiagnosticsParams(
				Uri: null,
				Version: 1,
				Diagnostics:
				[
					new DiagnosticPayload(
						new ProtocolRangePayload(
							new ProtocolPosition(0, 0),
							new ProtocolPosition(0, 5)),
						DiagnosticSeverity.Warning,
						"  unused variable  ",
						"  LuaLS  ",
						JsonSerializer.SerializeToElement("W211"))
				]),
			filePath,
			content,
			documentVersion: 1,
			out LuaPublishedDiagnostics? publishedDiagnostics);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.AreEqual(1, publishedDiagnostics.Diagnostics.Count);
		Assert.AreEqual("unused variable", publishedDiagnostics.Diagnostics[0].Message);
		Assert.AreEqual("LuaLS", publishedDiagnostics.Diagnostics[0].Source);
		Assert.AreEqual("W211", publishedDiagnostics.Diagnostics[0].Code);
	}

	[TestMethod]
	public void TryParse_StoresNumericCodeAsRawText()
	{
		const string filePath = @"C:\Workspace\test.lua";
		const string content = "local value = 1";

		bool parsed = LuaLanguageServerDiagnosticsParser.TryParse(
			new PublishDiagnosticsParams(
				Uri: null,
				Version: 1,
				Diagnostics:
				[
					new DiagnosticPayload(
						new ProtocolRangePayload(
							new ProtocolPosition(0, 0),
							new ProtocolPosition(0, 5)),
						DiagnosticSeverity.Warning,
						"Unused variable.",
						null,
						JsonSerializer.SerializeToElement(211))
				]),
			filePath,
			content,
			documentVersion: 1,
			out LuaPublishedDiagnostics? publishedDiagnostics);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.AreEqual("211", publishedDiagnostics.Diagnostics[0].Code);
	}

	[TestMethod]
	public void TryParse_NormalizesBlankMessageSourceAndCode()
	{
		const string filePath = @"C:\Workspace\test.lua";
		const string content = "local value = 1";

		bool parsed = LuaLanguageServerDiagnosticsParser.TryParse(
			new PublishDiagnosticsParams(
				Uri: null,
				Version: 1,
				Diagnostics:
				[
					new DiagnosticPayload(
						new ProtocolRangePayload(
							new ProtocolPosition(0, 0),
							new ProtocolPosition(0, 5)),
						DiagnosticSeverity.Warning,
						"   ",
						"  ",
						JsonSerializer.SerializeToElement(string.Empty))
				]),
			filePath,
			content,
			documentVersion: 1,
			out LuaPublishedDiagnostics? publishedDiagnostics);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.AreEqual("Unknown Lua diagnostic.", publishedDiagnostics.Diagnostics[0].Message);
		Assert.IsNull(publishedDiagnostics.Diagnostics[0].Source);
		Assert.IsNull(publishedDiagnostics.Diagnostics[0].Code);
	}

	[TestMethod]
	public void TryParse_IgnoresCodeValuesOutsideStringAndNumber()
	{
		const string filePath = @"C:\Workspace\test.lua";
		const string content = "local value = 1";

		bool parsed = LuaLanguageServerDiagnosticsParser.TryParse(
			new PublishDiagnosticsParams(
				Uri: null,
				Version: 1,
				Diagnostics:
				[
					new DiagnosticPayload(
						new ProtocolRangePayload(
							new ProtocolPosition(0, 0),
							new ProtocolPosition(0, 5)),
						DiagnosticSeverity.Warning,
						"Unused variable.",
						null,
						JsonSerializer.SerializeToElement(true))
				]),
			filePath,
			content,
			documentVersion: 1,
			out LuaPublishedDiagnostics? publishedDiagnostics);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.IsNull(publishedDiagnostics.Diagnostics[0].Code);
	}

	[TestMethod]
	public void TryParse_UndefinedSeverities_FallBackToWarning()
	{
		const string filePath = @"C:\Workspace\test.lua";
		const string content = "local value = 1";

		bool parsed = LuaLanguageServerDiagnosticsParser.TryParse(
			new PublishDiagnosticsParams(
				Uri: null,
				Version: 1,
				Diagnostics:
				[
					new DiagnosticPayload(
						new ProtocolRangePayload(new ProtocolPosition(0, 0), new ProtocolPosition(0, 5)),
						(DiagnosticSeverity)9,
						"Future severity.",
						null,
						null),
					new DiagnosticPayload(
						new ProtocolRangePayload(new ProtocolPosition(0, 6), new ProtocolPosition(0, 11)),
						(DiagnosticSeverity)0,
						"Zero severity.",
						null,
						null),
					new DiagnosticPayload(
						new ProtocolRangePayload(new ProtocolPosition(0, 12), new ProtocolPosition(0, 13)),
						(DiagnosticSeverity)(-3),
						"Negative severity.",
						null,
						null)
				]),
			filePath,
			content,
			documentVersion: 1,
			out LuaPublishedDiagnostics? publishedDiagnostics);

		// Values outside the LSP 1-4 range must never leak undefined enum members into ordering.
		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.AreEqual(3, publishedDiagnostics.Diagnostics.Count);
		Assert.AreEqual(TextDiagnosticSeverity.Warning, publishedDiagnostics.Diagnostics[0].Severity);
		Assert.AreEqual(TextDiagnosticSeverity.Warning, publishedDiagnostics.Diagnostics[1].Severity);
		Assert.AreEqual(TextDiagnosticSeverity.Warning, publishedDiagnostics.Diagnostics[2].Severity);
	}

	[TestMethod]
	public void TryParse_MissingSeverity_FallsBackToError()
	{
		const string filePath = @"C:\Workspace\test.lua";
		const string content = "local value = 1";

		bool parsed = LuaLanguageServerDiagnosticsParser.TryParse(
			new PublishDiagnosticsParams(
				Uri: null,
				Version: 1,
				Diagnostics:
				[
					new DiagnosticPayload(
						new ProtocolRangePayload(new ProtocolPosition(0, 0), new ProtocolPosition(0, 5)),
						Severity: null,
						"Unclassified payload.",
						null,
						null)
				]),
			filePath,
			content,
			documentVersion: 1,
			out LuaPublishedDiagnostics? publishedDiagnostics);

		// The protocol defines no severity default; the provider presents an omitted severity as an
		// error (the highest-signal fallback).
		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.AreEqual(1, publishedDiagnostics.Diagnostics.Count);
		Assert.AreEqual(TextDiagnosticSeverity.Error, publishedDiagnostics.Diagnostics[0].Severity);
	}

	[TestMethod]
	public void TryParse_OrdersDiagnosticsByStartOffset()
	{
		const string filePath = @"C:\Workspace\test.lua";
		const string content = "local value = 1";

		bool parsed = LuaLanguageServerDiagnosticsParser.TryParse(
			new PublishDiagnosticsParams(
				Uri: null,
				Version: 1,
				Diagnostics:
				[
					new DiagnosticPayload(
						new ProtocolRangePayload(new ProtocolPosition(0, 6), new ProtocolPosition(0, 11)),
						DiagnosticSeverity.Warning,
						"Later warning.",
						null,
						null),
					new DiagnosticPayload(
						new ProtocolRangePayload(new ProtocolPosition(0, 0), new ProtocolPosition(0, 5)),
						DiagnosticSeverity.Hint,
						"Earlier hint.",
						null,
						null)
				]),
			filePath,
			content,
			documentVersion: 1,
			out LuaPublishedDiagnostics? publishedDiagnostics);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.AreEqual(2, publishedDiagnostics.Diagnostics.Count);
		Assert.AreEqual(0, publishedDiagnostics.Diagnostics[0].StartOffset);
		Assert.AreEqual(6, publishedDiagnostics.Diagnostics[1].StartOffset);
	}

	[TestMethod]
	public void TryParse_OutOfRangePositions_AreClampedToTheDocument()
	{
		const string filePath = @"C:\Workspace\test.lua";
		const string content = "local value = 1";

		bool parsed = LuaLanguageServerDiagnosticsParser.TryParse(
			new PublishDiagnosticsParams(
				Uri: null,
				Version: 1,
				Diagnostics:
				[
					new DiagnosticPayload(
						new ProtocolRangePayload(new ProtocolPosition(99, -4), new ProtocolPosition(120, -2)),
						DiagnosticSeverity.Warning,
						"Clamped warning.",
						null,
						null)
				]),
			filePath,
			content,
			documentVersion: 1,
			out LuaPublishedDiagnostics? publishedDiagnostics);

		// The last line and a zero column are used instead of failing the payload.
		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.AreEqual(1, publishedDiagnostics.Diagnostics.Count);
		Assert.AreEqual(0, publishedDiagnostics.Diagnostics[0].StartOffset);
		Assert.AreEqual(0, publishedDiagnostics.Diagnostics[0].EndOffset);
	}

	[TestMethod]
	public void TryParse_UnmappableRange_FallsBackToTheWordAnchor()
	{
		const string filePath = @"C:\Workspace\test.lua";
		const string content = "local value = 1";

		bool parsed = LuaLanguageServerDiagnosticsParser.TryParse(
			new PublishDiagnosticsParams(
				Uri: null,
				Version: 1,
				Diagnostics:
				[
					new DiagnosticPayload(
						// The end line lies beyond the document, so clamping collapses the range into an
						// inverted one; the diagnostic must survive through the word anchor instead of
						// being dropped.
						new ProtocolRangePayload(new ProtocolPosition(0, 6), new ProtocolPosition(5, 3)),
						DiagnosticSeverity.Warning,
						"Word anchored warning.",
						null,
						null)
				]),
			filePath,
			content,
			documentVersion: 1,
			out LuaPublishedDiagnostics? publishedDiagnostics);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.AreEqual(1, publishedDiagnostics.Diagnostics.Count);
		Assert.AreEqual("local value = 1".IndexOf("value", StringComparison.Ordinal), publishedDiagnostics.Diagnostics[0].StartOffset);
		Assert.AreEqual("local value = 1".IndexOf("value", StringComparison.Ordinal) + "value".Length, publishedDiagnostics.Diagnostics[0].EndOffset);
	}

	[TestMethod]
	public void TryParse_SameStartOffset_OrdersErrorBeforeWarning()
	{
		const string filePath = @"C:\Workspace\test.lua";
		const string content = "local value = 1";

		bool parsed = LuaLanguageServerDiagnosticsParser.TryParse(
			new PublishDiagnosticsParams(
				Uri: null,
				Version: 1,
				Diagnostics:
				[
					new DiagnosticPayload(
						new ProtocolRangePayload(new ProtocolPosition(0, 0), new ProtocolPosition(0, 5)),
						DiagnosticSeverity.Warning,
						"Warning first.",
						null,
						null),
					new DiagnosticPayload(
						new ProtocolRangePayload(new ProtocolPosition(0, 0), new ProtocolPosition(0, 5)),
						DiagnosticSeverity.Error,
						"Error second.",
						null,
						null)
				]),
			filePath,
			content,
			documentVersion: 1,
			out LuaPublishedDiagnostics? publishedDiagnostics);

		// Diagnostics with the same start offset are ordered by severity rank, error first.
		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.AreEqual(2, publishedDiagnostics.Diagnostics.Count);
		Assert.AreEqual("Error second.", publishedDiagnostics.Diagnostics[0].Message);
		Assert.AreEqual("Warning first.", publishedDiagnostics.Diagnostics[1].Message);
	}

	private static PublishDiagnosticsParams CreateDiagnostics(int line, int startCharacter, int endLine, int endCharacter) => new(
		Uri: null,
		Version: 1,
		Diagnostics:
		[
			new DiagnosticPayload(
				new ProtocolRangePayload(
					new ProtocolPosition(line, startCharacter),
					new ProtocolPosition(endLine, endCharacter)),
				DiagnosticSeverity.Error,
				"Syntax error.",
				null,
				null)
		]);
}

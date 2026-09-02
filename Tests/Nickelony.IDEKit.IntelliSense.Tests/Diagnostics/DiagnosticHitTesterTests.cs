using Nickelony.IDEKit.IntelliSense.Diagnostics;

namespace Nickelony.IDEKit.IntelliSense.Tests.Diagnostics;

/// <summary>
/// Tests for the <see cref="DiagnosticHitTester"/> hover hit-testing kernel.
/// </summary>
[TestClass]
public sealed class DiagnosticHitTesterTests
{
	private static readonly TextEditorDiagnostic s_errorDiagnostic = new(TextEditorDiagnosticSeverity.Error, "boom", 5, 10);
	private static readonly TextEditorDiagnostic s_warningDiagnostic = new(TextEditorDiagnosticSeverity.Warning, "careful", 20, 30);
	private static readonly TextEditorDiagnostic s_hintDiagnostic = new(TextEditorDiagnosticSeverity.Hint, "hint", 22, 24);

	private static readonly TextEditorDiagnostic[] s_allDiagnostics = [s_errorDiagnostic, s_warningDiagnostic, s_hintDiagnostic];

	[TestMethod]
	public void GetDiagnosticsAtOffset_InsideSpan_ReturnsDiagnostic()
	{
		IReadOnlyList<TextEditorDiagnostic> result = DiagnosticHitTester.GetDiagnosticsAtOffset(s_allDiagnostics, 7);

		Assert.AreEqual(1, result.Count);
		Assert.AreSame(s_errorDiagnostic, result[0]);
	}

	[TestMethod]
	public void GetDiagnosticsAtOffset_OutsideAllSpans_ReturnsEmpty()
	{
		IReadOnlyList<TextEditorDiagnostic> result = DiagnosticHitTester.GetDiagnosticsAtOffset(s_allDiagnostics, 15);

		Assert.AreEqual(0, result.Count);
	}

	[TestMethod]
	public void GetDiagnosticsForRange_IntersectingSpans_ReturnsOrderedBySeverity()
	{
		IReadOnlyList<TextEditorDiagnostic> result = DiagnosticHitTester.GetDiagnosticsForRange(s_allDiagnostics, 21, 25);

		// Warning (20-30) and Hint (22-24) both intersect 21-25; Warning sorts before Hint.
		// Severity is ordered Error < Warning < Information < Hint.
		Assert.AreEqual(2, result.Count);
		Assert.AreSame(s_warningDiagnostic, result[0]);
		Assert.AreSame(s_hintDiagnostic, result[1]);
	}

	[TestMethod]
	public void SelectHoverDiagnostics_ExactOffsetHit_WinsOverFallback()
	{
		var overlapping = new[]
		{
			new TextEditorDiagnostic(TextEditorDiagnosticSeverity.Error, "exact", 7, 9),
			s_warningDiagnostic,
		};

		IReadOnlyList<TextEditorDiagnostic> result = DiagnosticHitTester.SelectHoverDiagnostics(overlapping, 7, allowLineFallback: true, lineStartOffset: 0, lineEndOffset: 40);

		Assert.AreEqual(1, result.Count);
		Assert.AreSame(overlapping[0], result[0]);
	}

	[TestMethod]
	public void SelectHoverDiagnostics_OffsetInsideWarning_IncludesWarning()
	{
		IReadOnlyList<TextEditorDiagnostic> result = DiagnosticHitTester.SelectHoverDiagnostics(s_allDiagnostics, 25, allowLineFallback: true, lineStartOffset: 18, lineEndOffset: 32);

		// Offset 25 is inside Warning (20-30), so the result includes the warning diagnostic.
		Assert.IsTrue(result.Count >= 1);
		Assert.IsTrue(result.Contains(s_warningDiagnostic));
	}

	[TestMethod]
	public void SelectHoverDiagnostics_NoExactHit_NoFallback_ReturnsEmpty()
	{
		// Offset 15 falls in no diagnostic span, and line fallback is disabled.
		IReadOnlyList<TextEditorDiagnostic> result = DiagnosticHitTester.SelectHoverDiagnostics(s_allDiagnostics, 15, allowLineFallback: false, lineStartOffset: 18, lineEndOffset: 32);

		Assert.AreEqual(0, result.Count);
	}

	[TestMethod]
	public void FormatMessage_UnprefixedMessage_AddsSeverityLabel()
	{
		string result = DiagnosticHitTester.FormatMessage(s_warningDiagnostic, severity => severity.ToString());

		Assert.AreEqual("Warning:\ncareful", result);
	}

	[TestMethod]
	public void FormatMessage_AlreadyPrefixedMessage_IsLeftAlone()
	{
		var prefixed = new TextEditorDiagnostic(TextEditorDiagnosticSeverity.Error, "Warning: pre", 1, 5);
		string result = DiagnosticHitTester.FormatMessage(prefixed, severity => severity.ToString());

		Assert.AreEqual("Warning: pre", result);
	}

	[TestMethod]
	public void FormatMessage_NullLabel_ReturnsRawMessage()
	{
		string result = DiagnosticHitTester.FormatMessage(s_warningDiagnostic, severityLabel: null);

		Assert.AreEqual("careful", result);
	}

	[TestMethod]
	public void BuildCombinedMessage_DeduplicatesIdenticalFormattedMessages()
	{
		// Identical severity + message produce the same formatted text, so only one survives.
		var first = new TextEditorDiagnostic(TextEditorDiagnosticSeverity.Error, "same", 1, 2);
		var duplicate = new TextEditorDiagnostic(TextEditorDiagnosticSeverity.Error, "same", 4, 5);

		string? result = DiagnosticHitTester.BuildCombinedMessage([first, duplicate], severity => severity.ToString());

		Assert.AreEqual("Error:\nsame", result);
	}

	[TestMethod]
	public void BuildCombinedMessage_DifferentMessages_AreCombined()
	{
		var first = new TextEditorDiagnostic(TextEditorDiagnosticSeverity.Error, "first", 1, 2);
		var second = new TextEditorDiagnostic(TextEditorDiagnosticSeverity.Hint, "second", 4, 5);

		string? result = DiagnosticHitTester.BuildCombinedMessage([first, second], severity => severity.ToString());

		Assert.AreEqual("Error:\nfirst" + Environment.NewLine + Environment.NewLine + "Hint:\nsecond", result);
	}

	[TestMethod]
	public void BuildCombinedMessage_AllEmptyMessages_ReturnsNull()
	{
		var empty = new TextEditorDiagnostic(TextEditorDiagnosticSeverity.Error, " ", 1, 2);

		string? result = DiagnosticHitTester.BuildCombinedMessage([empty], severity => severity.ToString());

		Assert.IsNull(result);
	}
}

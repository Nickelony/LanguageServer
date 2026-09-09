using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Diagnostics;

namespace Nickelony.IDEKit.IntelliSense.Tests.Diagnostics;

[TestClass]
public sealed class DiagnosticHitTesterTests
{
	private static readonly TextDiagnostic s_errorDiagnostic = new(TextDiagnosticSeverity.Error, "boom", 5, 10);
	private static readonly TextDiagnostic s_warningDiagnostic = new(TextDiagnosticSeverity.Warning, "careful", 20, 30);
	private static readonly TextDiagnostic s_hintDiagnostic = new(TextDiagnosticSeverity.Hint, "hint", 22, 24);

	private static readonly TextDiagnostic[] s_allDiagnostics = [s_errorDiagnostic, s_warningDiagnostic, s_hintDiagnostic];

	[TestMethod]
	public void GetDiagnosticsAtOffset_InsideSpan_ReturnsDiagnostic()
	{
		IReadOnlyList<TextDiagnostic> result = DiagnosticHitTester.GetDiagnosticsAtOffset(s_allDiagnostics, 7);

		Assert.AreEqual(1, result.Count);
		Assert.AreSame(s_errorDiagnostic, result[0]);
	}

	[TestMethod]
	public void GetDiagnosticsAtOffset_OutsideAllSpans_ReturnsEmpty()
	{
		IReadOnlyList<TextDiagnostic> result = DiagnosticHitTester.GetDiagnosticsAtOffset(s_allDiagnostics, 15);

		Assert.AreEqual(0, result.Count);
	}

	[TestMethod]
	public void EmptySelections_ReuseTheSharedEmptyResult()
	{
		// The no-match paths return one shared empty result instead of allocating per call.
		IReadOnlyList<TextDiagnostic> offsetFirst = DiagnosticHitTester.GetDiagnosticsAtOffset(s_allDiagnostics, 15);
		IReadOnlyList<TextDiagnostic> offsetSecond = DiagnosticHitTester.GetDiagnosticsAtOffset(s_allDiagnostics, 15);
		IReadOnlyList<TextDiagnostic> rangeFirst = DiagnosticHitTester.GetDiagnosticsForRange(s_allDiagnostics, 15, 18);
		IReadOnlyList<TextDiagnostic> rangeSecond = DiagnosticHitTester.GetDiagnosticsForRange(s_allDiagnostics, 15, 18);

		Assert.AreEqual(0, offsetFirst.Count);
		Assert.AreEqual(0, rangeFirst.Count);
		Assert.AreSame(offsetFirst, offsetSecond);
		Assert.AreSame(rangeFirst, rangeSecond);
	}

	[TestMethod]
	public void GetDiagnosticsForRange_IntersectingSpans_ReturnsInDocumentOrder()
	{
		// Both spans intersect the queried range. Document order returns the warning (start 20)
		// before the error (start 24) even though the error has the higher severity value.
		var error = new TextDiagnostic(TextDiagnosticSeverity.Error, "error", 24, 28);
		var warning = new TextDiagnostic(TextDiagnosticSeverity.Warning, "warning", 20, 30);

		IReadOnlyList<TextDiagnostic> result = DiagnosticHitTester.GetDiagnosticsForRange([error, warning], 21, 25);

		Assert.AreEqual(2, result.Count);
		Assert.AreSame(warning, result[0]);
		Assert.AreSame(error, result[1]);
	}

	[TestMethod]
	public void GetDiagnosticsAtOffset_MixedSeverities_KeepsDocumentOrder()
	{
		// None (severity 0) starts after the error and must not be promoted ahead of it.
		var none = new TextDiagnostic(TextDiagnosticSeverity.None, "none", 6, 9);
		var error = new TextDiagnostic(TextDiagnosticSeverity.Error, "error", 5, 10);

		IReadOnlyList<TextDiagnostic> result = DiagnosticHitTester.GetDiagnosticsAtOffset([error, none], 7);

		Assert.AreEqual(2, result.Count);
		Assert.AreSame(error, result[0]);
		Assert.AreSame(none, result[1]);
	}

	[TestMethod]
	public void GetDiagnosticsAtOffset_SameStartOffset_OrdersByEndOffset()
	{
		var wider = new TextDiagnostic(TextDiagnosticSeverity.Warning, "wider", 5, 12);
		var narrower = new TextDiagnostic(TextDiagnosticSeverity.Error, "narrower", 5, 8);

		IReadOnlyList<TextDiagnostic> result = DiagnosticHitTester.GetDiagnosticsAtOffset([wider, narrower], 6);

		Assert.AreEqual(2, result.Count);
		Assert.AreSame(narrower, result[0]);
		Assert.AreSame(wider, result[1]);
	}

	[TestMethod]
	public void GetDiagnosticsAtOffset_EmptyDiagnosticSpan_MatchesExactlyItsOffset()
	{
		var empty = new TextDiagnostic(TextDiagnosticSeverity.Error, "empty", 7, 7);

		Assert.AreEqual(1, DiagnosticHitTester.GetDiagnosticsAtOffset([empty], 7).Count);
		Assert.AreEqual(0, DiagnosticHitTester.GetDiagnosticsAtOffset([empty], 6).Count);
		Assert.AreEqual(0, DiagnosticHitTester.GetDiagnosticsAtOffset([empty], 8).Count);
	}

	[TestMethod]
	public void GetDiagnosticsForRange_EmptyDiagnosticSpan_IntersectsWhenItsOffsetIsInside()
	{
		var empty = new TextDiagnostic(TextDiagnosticSeverity.Error, "empty", 7, 7);

		Assert.AreEqual(1, DiagnosticHitTester.GetDiagnosticsForRange([empty], 5, 10).Count);
		Assert.AreEqual(0, DiagnosticHitTester.GetDiagnosticsForRange([empty], 7, 7).Count);
		Assert.AreEqual(0, DiagnosticHitTester.GetDiagnosticsForRange([empty], 8, 10).Count);
	}

	[TestMethod]
	public void GetDiagnosticsForRange_EmptyOrReversedRange_SelectsNothing()
	{
		var diagnostic = new TextDiagnostic(TextDiagnosticSeverity.Error, "error", 5, 10);

		Assert.AreEqual(0, DiagnosticHitTester.GetDiagnosticsForRange([diagnostic], 8, 8).Count);
		Assert.AreEqual(0, DiagnosticHitTester.GetDiagnosticsForRange([diagnostic], 8, 6).Count);
	}

	[TestMethod]
	public void GetDiagnostics_NegativeArguments_Throw()
	{
		var diagnostic = new TextDiagnostic(TextDiagnosticSeverity.Error, "error", 5, 10);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DiagnosticHitTester.GetDiagnosticsAtOffset([diagnostic], -1));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DiagnosticHitTester.GetDiagnosticsForRange([diagnostic], -1, 5));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DiagnosticHitTester.GetDiagnosticsForRange([diagnostic], 0, -5));
	}
}

using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Hover;

namespace Nickelony.IDEKit.IntelliSense.Tests.Hover;

/// <summary>
/// Verifies the storage, defaults, and value equality of the hover evaluation-state record.
/// </summary>
[TestClass]
public sealed class TextHoverEvaluationStateTests
{
	[TestMethod]
	public void Constructor_StoresEveryComponent()
	{
		var diagnostic = new TextDiagnostic(TextDiagnosticSeverity.Warning, "message", 2, 4);
		var state = new TextHoverEvaluationState(
			ShouldRequestHover: true,
			RequestOffset: 3,
			CanShowHoverContent: true,
			CanShowDiagnosticFallback: false,
			DiagnosticInfo: diagnostic);

		Assert.IsTrue(state.ShouldRequestHover);
		Assert.AreEqual(3, state.RequestOffset);
		Assert.IsTrue(state.CanShowHoverContent);
		Assert.IsFalse(state.CanShowDiagnosticFallback);
		Assert.AreSame(diagnostic, state.DiagnosticInfo);
	}

	[TestMethod]
	public void Default_UsesSafeDefaults()
	{
		TextHoverEvaluationState state = default;

		Assert.IsFalse(state.ShouldRequestHover);
		Assert.AreEqual(0, state.RequestOffset);
		Assert.IsFalse(state.CanShowHoverContent);
		Assert.IsFalse(state.CanShowDiagnosticFallback);
		Assert.IsNull(state.DiagnosticInfo);
	}

	[TestMethod]
	public void Equality_ComparesEveryComponent()
	{
		var diagnostic = new TextDiagnostic(TextDiagnosticSeverity.Error, "boom", 1, 2);
		var state = new TextHoverEvaluationState(true, 3, true, true, diagnostic);

		Assert.AreEqual(state, new TextHoverEvaluationState(true, 3, true, true, diagnostic));
		Assert.AreNotEqual(state, new TextHoverEvaluationState(false, 3, true, true, diagnostic));
		Assert.AreNotEqual(state, new TextHoverEvaluationState(true, 4, true, true, diagnostic));
		Assert.AreNotEqual(state, new TextHoverEvaluationState(true, 3, false, true, diagnostic));
		Assert.AreNotEqual(state, new TextHoverEvaluationState(true, 3, true, false, diagnostic));
		Assert.AreNotEqual(state, new TextHoverEvaluationState(true, 3, true, true, null));
	}

	[TestMethod]
	public void DiagnosticFallback_FlagEnabled_ReturnsTheDiagnostic()
	{
		var diagnostic = new TextDiagnostic(TextDiagnosticSeverity.Warning, "message", 2, 4);
		var state = new TextHoverEvaluationState(true, 3, true, true, diagnostic);

		Assert.AreSame(diagnostic, state.DiagnosticFallback);
	}

	[TestMethod]
	public void DiagnosticFallback_FlagDisabled_IsNull()
	{
		var diagnostic = new TextDiagnostic(TextDiagnosticSeverity.Warning, "message", 2, 4);
		var state = new TextHoverEvaluationState(true, 3, true, false, diagnostic);

		Assert.IsNull(state.DiagnosticFallback);
	}

	[TestMethod]
	public void DiagnosticFallback_FlagEnabledWithoutDiagnostic_IsNull()
	{
		var state = new TextHoverEvaluationState(true, 3, true, true, null);

		Assert.IsNull(state.DiagnosticFallback);
	}
}

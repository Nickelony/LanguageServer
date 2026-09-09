using Nickelony.IDEKit.IntelliSense.Diagnostics;

namespace Nickelony.IDEKit.IntelliSense.Hover;

/// <summary>
/// Describes the host hover evaluation input and diagnostic-display state for a text editor.
/// </summary>
/// <remarks>
/// <para>
/// A host builds this state from the hovered offset (for example the identifier at that offset) and
/// its diagnostic information, then passes it to its hover logic, which uses the flags to decide
/// whether to request hover content or fall back to a diagnostic message. This is hover evaluation
/// input rather than a provider contract or a request snapshot: it describes what the editor may
/// show at one hover offset, not a document snapshot.
/// </para>
/// <para>
/// The host's hover logic applies one precedence rule: a completed request uses
/// <see cref="CanShowHoverContent"/> and combines the hover content with the diagnostic (either may be
/// absent); when the request was not made or failed, the diagnostic is shown alone only when
/// <see cref="CanShowDiagnosticFallback"/> allows it. The host selects which diagnostic to surface
/// when its diagnostics provider offers more than one (for example the first or the highest-severity
/// one), because the presentation renders exactly one diagnostic message.
/// </para>
/// <para>
/// The record uses value equality over all components, including <see cref="DiagnosticInfo"/>, which
/// itself uses structural equality.
/// </para>
/// </remarks>
/// <param name="ShouldRequestHover">Whether a hover request should be issued for the hovered offset.</param>
/// <param name="RequestOffset">
/// The zero-based UTF-16 offset for which hover is requested; meaningful when
/// <paramref name="ShouldRequestHover"/> is <see langword="true"/>. When the flag is set the offset
/// must address a position within the document snapshot, because the hover request path validates it.
/// </param>
/// <param name="CanShowHoverContent">Whether hover content may be shown for this hover state.</param>
/// <param name="CanShowDiagnosticFallback">
/// Whether diagnostic information may be shown when a hover request is not made or fails; successful requests
/// use <paramref name="CanShowHoverContent"/>.
/// </param>
/// <param name="DiagnosticInfo">The diagnostic information at the hovered offset, when available.</param>
public readonly record struct TextHoverEvaluationState(
	bool ShouldRequestHover,
	int RequestOffset,
	bool CanShowHoverContent,
	bool CanShowDiagnosticFallback,
	TextDiagnostic? DiagnosticInfo)
{
	/// <summary>
	/// Gets the diagnostic to surface when a hover request was not made or failed, or <see langword="null"/>
	/// when the state does not allow a diagnostic fallback.
	/// </summary>
	/// <remarks>
	/// The property applies the type's documented precedence rule so every host shares it: the diagnostic is
	/// shown alone only when <see cref="CanShowDiagnosticFallback"/> allows it; a successful request uses
	/// <see cref="CanShowHoverContent"/> and combines the hover content with the diagnostic instead.
	/// </remarks>
	public TextDiagnostic? DiagnosticFallback => CanShowDiagnosticFallback ? DiagnosticInfo : null;
}

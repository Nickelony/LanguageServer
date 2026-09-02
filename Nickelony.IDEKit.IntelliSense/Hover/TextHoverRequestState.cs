using Nickelony.IDEKit.IntelliSense.Diagnostics;

namespace Nickelony.IDEKit.IntelliSense.Hover;

/// <summary>
/// Describes the current hover request and diagnostic display state for a text editor.
/// </summary>
/// <param name="ShouldRequestHover">Whether a hover request should be issued for the hovered offset.</param>
/// <param name="RequestOffset">
/// The zero-based document offset for which hover is requested; meaningful when
/// <paramref name="ShouldRequestHover"/> is <see langword="true"/>.
/// </param>
/// <param name="CanShowToolTip">Whether tooltip content may be shown for this hover state.</param>
/// <param name="CanShowDiagnosticFallback">
/// Whether diagnostic information may be shown when a hover request is not made or fails; successful requests
/// use <paramref name="CanShowToolTip"/>.
/// </param>
/// <param name="DiagnosticInfo">The diagnostic information at the hovered offset, when available.</param>
public readonly record struct TextHoverRequestState(
	bool ShouldRequestHover,
	int RequestOffset,
	bool CanShowToolTip,
	bool CanShowDiagnosticFallback,
	TextEditorDiagnostic? DiagnosticInfo);

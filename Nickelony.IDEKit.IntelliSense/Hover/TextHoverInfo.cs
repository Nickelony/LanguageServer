namespace Nickelony.IDEKit.IntelliSense.Hover;

/// <summary>
/// Represents hover content resolved for a symbol or document location.
/// </summary>
/// <param name="Content">The hover text or markup supplied to the editor.</param>
/// <param name="ContentKind">The format used by <paramref name="Content"/>.</param>
/// <param name="SymbolName">The optional symbol name associated with the hover content.</param>
/// <param name="Identifier">An optional provider-specific identifier for follow-up resolution or caching.</param>
public sealed record TextHoverInfo(
	string Content,
	TextHoverContentKind ContentKind = TextHoverContentKind.PlainText,
	string? SymbolName = null,
	object? Identifier = null);

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents a single definition target parsed from an LSP location or location-link payload in protocol coordinates.
/// </summary>
/// <param name="Uri">
/// The target document URI. Definition parsing skips targets without a usable URI, so parsed
/// targets always carry one.
/// </param>
/// <param name="TargetRange">The zero-based protocol range covering the whole definition.</param>
/// <param name="SelectionRange">
/// The optional zero-based protocol range identifying the definition's name, or <see langword="null"/> when
/// the payload identified the target with a single range.
/// </param>
/// <param name="OriginSelectionRange">
/// The optional zero-based protocol range in the requesting document that the link refers to, or
/// <see langword="null"/> when the server sent none.
/// </param>
/// <remarks>
/// The payload stays in protocol coordinates; hosts convert it to their editor range model through
/// <see cref="ProtocolRangeConversion.TryGetTextPositionRange"/>.
/// </remarks>
public readonly record struct DefinitionTargetPayload(
	string Uri,
	ProtocolRangePayload TargetRange,
	ProtocolRangePayload? SelectionRange = null,
	ProtocolRangePayload? OriginSelectionRange = null);

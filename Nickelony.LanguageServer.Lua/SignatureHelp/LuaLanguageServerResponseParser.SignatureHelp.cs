using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Signatures;
using System.Text.Json;

namespace Nickelony.LanguageServer.Lua;

internal static partial class LuaLanguageServerResponseParser
{
	// The line separator passed to the plain-text normalizer that projects signature-help
	// documentation into the plain-text-only signature model.
	private const string DocumentationNewLine = "\n";

	/// <summary>
	/// Parses a signature-help payload from a LuaLS signature-help response.
	/// </summary>
	/// <param name="response">The signature help response payload, or <see langword="null"/> when unavailable.</param>
	/// <returns>
	/// The parsed signature help payload including every signature with a label, or
	/// <see langword="null"/> when no signature with a label is present.
	/// </returns>
	internal static TextSignatureHelp? ParseSignatureHelp(SignatureHelpResponse? response)
	{
		if (response?.Signatures is not { Length: > 0 } signaturePayloads)
			return null;

		int activeSignatureElementIndex = Math.Max(0, response.ActiveSignature ?? 0);
		int activeSignatureIndex = 0;

		var signatures = new List<TextSignatureInformation>(signaturePayloads.Length);

		for (int i = 0; i < signaturePayloads.Length; i++)
		{
			if (signaturePayloads[i] is not { } signaturePayload || string.IsNullOrWhiteSpace(signaturePayload.Label))
				continue;

			// Track where the active signature lands once unusable entries are skipped.
			if (i < activeSignatureElementIndex)
				activeSignatureIndex++;

			signatures.Add(CreateSignatureInformation(signaturePayload, signaturePayload.Label));
		}

		if (signatures.Count == 0)
			return null;

		activeSignatureIndex = Math.Min(activeSignatureIndex, signatures.Count - 1);

		return new(signatures, activeSignatureIndex, GetActiveParameterIndex(response.ActiveParameter));
	}

	/// <summary>
	/// Interprets an LSP <c>activeParameter</c> value into a parameter index, the not-specified
	/// sentinel, or the LSP 3.18 "no active parameter" state.
	/// </summary>
	/// <param name="activeParameterElement">The raw payload value.</param>
	/// <returns>The parameter index, the not-specified sentinel, or <see langword="null"/>.</returns>
	/// <remarks>
	/// An absent property keeps the sentinel so the next fallback level applies; an explicit
	/// <see langword="null"/> maps to the LSP 3.18 "no active parameter" state; a non-negative
	/// integer that fits <see cref="int"/> is used verbatim. A negative number follows the pre-3.18
	/// convention in which servers signaled "no active parameter" with <c>-1</c>, so it maps to
	/// <see langword="null"/> as well; any other number (fractional or out of range) falls back to
	/// the sentinel like an unknown value.
	/// </remarks>
	private static int? GetActiveParameterIndex(JsonElement activeParameterElement)
	{
		return activeParameterElement.ValueKind switch
		{
			JsonValueKind.Undefined => -1,
			JsonValueKind.Null => null,
			JsonValueKind.Number when activeParameterElement.TryGetInt32(out int index) => index < 0 ? null : index,
			_ => -1
		};
	}

	/// <summary>
	/// Extracts plain-text documentation from an LSP markup payload. Fence lines are removed so the
	/// result satisfies the plain-text-only signature model; inline markup is preserved as text.
	/// </summary>
	/// <param name="element">The markup payload to flatten.</param>
	/// <returns>The plain-text documentation, or <see langword="null"/> when the payload is blank.</returns>
	private static string? ExtractMarkupText(JsonElement element)
		=> BacktickFenceTextNormalizer.NormalizeForPlainText(MarkupContentReader.ExtractContent(element).Text, DocumentationNewLine);

	private static TextSignatureInformation CreateSignatureInformation(SignatureHelpSignaturePayload signaturePayload, string label)
	{
		string? documentation = signaturePayload.Documentation is { } documentationElement
			? ExtractMarkupText(documentationElement)
			: null;

		var parameters = new List<TextSignatureParameterInfo>();

		if (signaturePayload.Parameters is { Length: > 0 } parameterPayloads)
		{
			for (int i = 0; i < parameterPayloads.Length; i++)
			{
				SignatureHelpParameterPayload parameterPayload = parameterPayloads[i];

				string? parameterLabel = parameterPayload.Label.ValueKind == JsonValueKind.String
					? parameterPayload.Label.GetString()
					: SignatureLabelParser.TryExtractParameterLabel(label, parameterPayload.Label, out string? extractedLabel)
						? extractedLabel
						: null;

				string? parameterDocumentation = parameterPayload.Documentation is { } parameterDocumentationElement
					? ExtractMarkupText(parameterDocumentationElement)
					: null;

				parameters.Add(new TextSignatureParameterInfo(parameterLabel ?? string.Empty, parameterDocumentation));
			}
		}

		return new(label, documentation, GetActiveParameterIndex(signaturePayload.ActiveParameter), parameters);
	}
}

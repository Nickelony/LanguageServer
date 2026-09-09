using System.Collections.ObjectModel;

namespace Nickelony.IDEKit.IntelliSense.Signatures;

/// <summary>
/// Represents the signature-help payload for a callable item: the available signature options and
/// the active selection.
/// </summary>
/// <remarks>
/// <para>
/// The payload mirrors the LSP signature-help model. It carries one or more signatures, the active
/// signature index, and the active parameter index for the active signature, so a host can offer
/// "1 of N" overload navigation without another provider round-trip; see
/// <see cref="WithActiveSignature"/>.
/// </para>
/// <para>
/// <see cref="ActiveParameterIndex"/> resolves the effective active parameter with the LSP
/// precedence and default rules; see that property for the full resolution details.
/// </para>
/// </remarks>
public sealed class TextSignatureHelp
{
	private readonly ReadOnlyCollection<TextSignatureInformation> _signatures;
	private readonly int? _activeParameterIndex;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextSignatureHelp"/> class.
	/// </summary>
	/// <param name="signatures">
	/// The signature options; at least one entry is required because a payload without signatures
	/// has no content to present (providers return <see langword="null"/> instead). Elements must
	/// not be <see langword="null"/>.
	/// </param>
	/// <param name="activeSignatureIndex">
	/// The zero-based active signature index. A value outside the range of
	/// <paramref name="signatures"/> falls back to zero, which is the LSP default.
	/// </param>
	/// <param name="activeParameterIndex">
	/// The payload-level active parameter index for the active signature. The default value <c>-1</c>
	/// means the payload does not override the active signature's own index; when the active
	/// signature does not specify one either, the first parameter is selected for a signature
	/// that has parameters. <see langword="null"/> means no parameter is active (for example an
	/// unmatched named argument). Any other value outside the active signature's parameter range falls
	/// back to the first parameter, which is the LSP default; a signature without parameters has no
	/// active parameter regardless of the supplied value.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="signatures"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="signatures"/> is empty or contains a <see langword="null"/> element.
	/// </exception>
	public TextSignatureHelp(
		IReadOnlyList<TextSignatureInformation> signatures,
		int activeSignatureIndex = 0,
		int? activeParameterIndex = -1)
	{
		ArgumentNullException.ThrowIfNull(signatures);

		if (signatures.Count == 0)
		{
			throw new ArgumentException(
				"At least one signature is required; return null instead when no signature is available.",
				nameof(signatures));
		}

		for (int i = 0; i < signatures.Count; i++)
		{
			if (signatures[i] is null)
			{
				throw new ArgumentException(
					$"The signatures collection contains a null element at index {i}.",
					nameof(signatures));
			}
		}

		_signatures = Array.AsReadOnly([.. signatures]);
		ActiveSignatureIndex = NormalizeActiveSignatureIndex(activeSignatureIndex, _signatures.Count);
		_activeParameterIndex = activeParameterIndex;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="TextSignatureHelp"/> class from an owned
	/// signature snapshot. Used internally to change the active signature without copying the array
	/// again.
	/// </summary>
	private TextSignatureHelp(
		ReadOnlyCollection<TextSignatureInformation> signatures,
		int activeSignatureIndex,
		int? activeParameterIndex)
	{
		_signatures = signatures;
		ActiveSignatureIndex = activeSignatureIndex;
		_activeParameterIndex = activeParameterIndex;
	}

	/// <summary>
	/// Gets the owned immutable snapshot of signature options.
	/// </summary>
	public IReadOnlyList<TextSignatureInformation> Signatures => _signatures;

	/// <summary>
	/// Gets the zero-based index of the active signature within <see cref="Signatures"/>.
	/// </summary>
	public int ActiveSignatureIndex { get; }

	/// <summary>
	/// Gets the active signature option.
	/// </summary>
	public TextSignatureInformation ActiveSignature => Signatures[ActiveSignatureIndex];

	/// <summary>
	/// Gets the effective zero-based active parameter index for <see cref="ActiveSignature"/>, or
	/// <see langword="null"/> when no parameter is active or the active signature has no parameters.
	/// </summary>
	/// <remarks>
	/// The value is never a sentinel: it is either an index into the active signature's parameters
	/// or <see langword="null"/>. Resolution follows the LSP precedence and default rules documented
	/// on the constructor's <c>activeParameterIndex</c> parameter: the active signature's own index
	/// wins when it specifies one (a signature-level <see langword="null"/> reports no active
	/// parameter); otherwise the payload-level index applies, where the <c>-1</c> default and any
	/// out-of-range value select the first parameter and <see langword="null"/> reports none; and a
	/// signature without parameters always reports <see langword="null"/> (the LSP 3.18
	/// "no active parameter" state).
	/// </remarks>
	public int? ActiveParameterIndex
	{
		get
		{
			TextSignatureInformation activeSignature = ActiveSignature;

			return activeSignature.SpecifiesActiveParameterIndex
				? activeSignature.ActiveParameterIndex
				: ResolvePayloadActiveParameterIndex(_activeParameterIndex, activeSignature.Parameters);
		}
	}

	/// <summary>
	/// Returns a payload with the same signatures and payload-level parameter index but a different
	/// active signature, which a host uses for overload navigation.
	/// </summary>
	/// <param name="activeSignatureIndex">
	/// The zero-based active signature index. A value outside the range of <see cref="Signatures"/> falls
	/// back to zero, which is the LSP default.
	/// </param>
	/// <returns>
	/// The new payload; the active parameter index is resolved again for the selected signature
	/// using the same rules.
	/// </returns>
	public TextSignatureHelp WithActiveSignature(int activeSignatureIndex)
		=> new(_signatures, NormalizeActiveSignatureIndex(activeSignatureIndex, _signatures.Count), _activeParameterIndex);

	private static int NormalizeActiveSignatureIndex(int activeSignatureIndex, int signatureCount)
		=> activeSignatureIndex >= 0 && activeSignatureIndex < signatureCount ? activeSignatureIndex : 0;

	private static int? ResolvePayloadActiveParameterIndex(
		int? activeParameterIndex,
		IReadOnlyList<TextSignatureParameterInfo> parameters)
	{
		if (parameters.Count == 0)
			return null;

		return activeParameterIndex is int index
			? NormalizeParameterIndex(index, parameters.Count)
			: null;
	}

	/// <summary>
	/// Normalizes a parameter index into the range of a signature's parameters; a value outside the
	/// range falls back to the first parameter, which is the LSP default.
	/// </summary>
	internal static int NormalizeParameterIndex(int index, int parameterCount)
		=> index >= 0 && index < parameterCount ? index : 0;
}

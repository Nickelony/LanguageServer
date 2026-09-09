using System.Collections.ObjectModel;

namespace Nickelony.IDEKit.IntelliSense.Signatures;

/// <summary>
/// Represents one callable signature option within a signature-help payload.
/// </summary>
/// <remarks>
/// <para>
/// A payload can offer multiple signature options and a host can navigate between them; see
/// <see cref="TextSignatureHelp.WithActiveSignature"/>.
/// </para>
/// <para>
/// <see cref="Documentation"/> is plain text only; providers must flatten Markdown or other markup
/// before constructing the entry. Parameter labels are pre-resolved fragments of
/// <see cref="Label"/>: a label that is not a literal fragment of the signature label (for example
/// when the language server omits parameter text from the signature) cannot be located by a host
/// that highlights the active parameter. The label-search algorithm a host applies is documented in
/// <c>docs/EditorBindingGuide.md</c> (section 5.6).
/// </para>
/// </remarks>
public sealed class TextSignatureInformation
{
	private static readonly ReadOnlyCollection<TextSignatureParameterInfo> s_emptyParameters =
		Array.AsReadOnly<TextSignatureParameterInfo>([]);

	private readonly int? _activeParameterIndex;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextSignatureInformation"/> class.
	/// </summary>
	/// <param name="label">The full signature label shown to the user.</param>
	/// <param name="documentation">
	/// Optional documentation shown beside or below the signature; blank values are treated as absent
	/// and other values are trimmed.
	/// </param>
	/// <param name="activeParameterIndex">
	/// The zero-based active parameter index for this signature. The default value <c>-1</c> means the
	/// signature does not specify a parameter, so the payload-level index applies instead;
	/// <see langword="null"/> means no parameter is active (for example an unmatched named argument),
	/// and a signature without parameters reports no active parameter. Any other value outside the
	/// parameter range falls back to the first parameter. The value <c>-1</c> is reserved for the
	/// not-specified default: a producer that reports no active parameter must use
	/// <see langword="null"/> rather than a negative sentinel.
	/// </param>
	/// <param name="parameters">
	/// The parameters that compose the signature; the list must not contain
	/// <see langword="null"/> elements.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="label"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="parameters"/> contains a <see langword="null"/> element.
	/// </exception>
	public TextSignatureInformation(
		string label,
		string? documentation = null,
		int? activeParameterIndex = -1,
		IReadOnlyList<TextSignatureParameterInfo>? parameters = null)
	{
		ArgumentNullException.ThrowIfNull(label);

		if (parameters is { Count: > 0 })
		{
			for (int i = 0; i < parameters.Count; i++)
			{
				if (parameters[i] is null)
				{
					throw new ArgumentException(
						$"The parameters collection contains a null element at index {i}.",
						nameof(parameters));
				}
			}
		}

		Label = label;
		Documentation = string.IsNullOrWhiteSpace(documentation) ? null : documentation.Trim();

		Parameters = parameters is null or { Count: 0 }
			? s_emptyParameters
			: Array.AsReadOnly([.. parameters]);

		_activeParameterIndex = activeParameterIndex;
	}

	/// <summary>
	/// Gets the full signature label shown to the user.
	/// </summary>
	public string Label { get; }

	/// <summary>
	/// Gets optional documentation for the signature.
	/// </summary>
	public string? Documentation { get; }

	/// <summary>
	/// Gets the normalized active parameter index for this signature, or <see langword="null"/> when
	/// the signature does not specify one or has no parameters.
	/// </summary>
	/// <remarks>
	/// A specified value outside the parameter range falls back to the first parameter. The property
	/// ignores the payload-level fallback; see <see cref="TextSignatureHelp.ActiveParameterIndex"/> for
	/// the effective active parameter of the payload.
	/// </remarks>
	public int? ActiveParameterIndex => NormalizeActiveParameterIndex(_activeParameterIndex, Parameters);

	/// <summary>
	/// Gets the owned immutable snapshot of parameters that compose the signature.
	/// </summary>
	public IReadOnlyList<TextSignatureParameterInfo> Parameters { get; }

	/// <summary>
	/// Gets a value indicating whether the signature specifies an active parameter instead of
	/// relying on the payload-level fallback.
	/// </summary>
	internal bool SpecifiesActiveParameterIndex => _activeParameterIndex != -1;

	internal static int? NormalizeActiveParameterIndex(
		int? activeParameterIndex,
		IReadOnlyList<TextSignatureParameterInfo> parameters)
	{
		if (parameters.Count == 0 || activeParameterIndex is not int index || index == -1)
			return null;

		return TextSignatureHelp.NormalizeParameterIndex(index, parameters.Count);
	}
}

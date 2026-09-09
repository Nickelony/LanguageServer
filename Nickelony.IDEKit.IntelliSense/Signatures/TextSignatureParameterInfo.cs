namespace Nickelony.IDEKit.IntelliSense.Signatures;

/// <summary>
/// Represents one parameter entry within a signature-help payload.
/// </summary>
/// <remarks>
/// Providers supply pre-resolved labels: when the label could be extracted from the signature
/// label it is a plain-text fragment of that label, and when it could not be resolved it is empty.
/// </remarks>
public sealed class TextSignatureParameterInfo
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextSignatureParameterInfo"/> class.
	/// </summary>
	/// <param name="label">
	/// The parameter label shown in the signature UI, or an empty value when the provider could not
	/// resolve one. A blank label is never locatable inside the signature label, so a host that
	/// highlights the active parameter must treat it as unresolved and present the signature
	/// without a highlight (see <c>docs/EditorBindingGuide.md</c>, section 5.6).
	/// </param>
	/// <param name="documentation">
	/// Optional parameter documentation; blank values are treated as absent and other values are trimmed.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="label"/> is <see langword="null"/>.
	/// </exception>
	public TextSignatureParameterInfo(string label, string? documentation = null)
	{
		ArgumentNullException.ThrowIfNull(label);

		Label = label;
		Documentation = string.IsNullOrWhiteSpace(documentation) ? null : documentation.Trim();
	}

	/// <summary>
	/// Gets the parameter label shown in the signature UI.
	/// </summary>
	public string Label { get; }

	/// <summary>
	/// Gets optional documentation for the parameter.
	/// </summary>
	public string? Documentation { get; }
}

namespace Nickelony.LanguageServer.Abstractions.Signatures;

/// <summary>
/// Represents the active signature-help payload for a callable item.
/// </summary>
public sealed class TextSignatureHelpInfo
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextSignatureHelpInfo"/> class.
	/// </summary>
	/// <param name="label">The full signature label shown to the user.</param>
	/// <param name="activeParameterIndex">The zero-based active parameter index. Values outside the parameter range are clamped; -1 is retained when no parameters are provided.</param>
	/// <param name="documentation">Optional documentation shown beside or below the signature.</param>
	/// <param name="parameters">The parameters that compose the signature.</param>
	public TextSignatureHelpInfo(
		string label,
		int activeParameterIndex = -1,
		string? documentation = null,
		IReadOnlyList<TextSignatureParameterInfo>? parameters = null)
	{
		Label = label;
		Documentation = documentation;

		Parameters = parameters is null or { Count: 0 }
			? []
			: Array.AsReadOnly([.. parameters]);

		ActiveParameterIndex = Parameters.Count == 0
			? activeParameterIndex
			: Math.Clamp(activeParameterIndex, 0, Parameters.Count - 1);
	}

	/// <summary>
	/// Gets the full signature label shown to the user.
	/// </summary>
	public string Label { get; }

	/// <summary>
	/// Gets the zero-based active parameter index. When parameters are provided, the value is clamped to their range; -1 is returned only when no parameters are provided and no parameter is active.
	/// </summary>
	public int ActiveParameterIndex { get; }

	/// <summary>
	/// Gets optional documentation for the signature.
	/// </summary>
	public string? Documentation { get; }

	/// <summary>
	/// Gets the owned immutable snapshot of parameters that compose the signature.
	/// </summary>
	public IReadOnlyList<TextSignatureParameterInfo> Parameters { get; }
}

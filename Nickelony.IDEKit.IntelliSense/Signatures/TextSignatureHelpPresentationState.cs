namespace Nickelony.IDEKit.IntelliSense.Signatures;

/// <summary>
/// Describes the current state of the signature help presentation.
/// </summary>
/// <param name="SignatureHelp">The signature help information, when available.</param>
/// <param name="IsVisible">Whether the signature help popup is visible.</param>
/// <param name="IsRequestInFlight">Whether a signature help request is currently in flight.</param>
/// <param name="IsRefreshPending">Whether a signature help refresh is pending.</param>
/// <remarks>
/// The visibility union counts an in-flight request and a pending refresh: both are states a host may
/// present as pending activity.
/// </remarks>
public readonly record struct TextSignatureHelpPresentationState(
	TextSignatureHelp? SignatureHelp,
	bool IsVisible,
	bool IsRequestInFlight,
	bool IsRefreshPending)
{
	/// <summary>
	/// Gets the empty presentation state with no payload, visible state, in-flight request, or pending
	/// refresh.
	/// </summary>
	public static TextSignatureHelpPresentationState Empty { get; } = new(null, false, false, false);

	/// <summary>
	/// Gets a value indicating whether the signature help presentation is visible or a request is
	/// pending: the popup is visible, or a request is in flight or a refresh is pending.
	/// </summary>
	public bool IsPresentationVisibleOrRequestPending => IsVisible || IsRequestInFlight || IsRefreshPending;
}

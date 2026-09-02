namespace Nickelony.IDEKit.AvalonEdit.IntelliSense.Completion;

/// <summary>
/// Centralizes shared popup precedence rules across completion, hover, and signature help.
/// </summary>
public static class TextPopupInteractionRules
{
	/// <summary>
	/// Determines whether hover content may be shown while other transient popups are active.
	/// </summary>
	/// <param name="isCompletionWindowOpen">Whether a completion window is currently open.</param>
	/// <param name="isSignatureHelpOpen">Whether signature help is currently visible.</param>
	/// <returns><see langword="true"/> when hover may be shown; otherwise, <see langword="false"/>.</returns>
	public static bool CanShowHover(bool isCompletionWindowOpen, bool isSignatureHelpOpen)
		=> !isCompletionWindowOpen && !isSignatureHelpOpen;
}

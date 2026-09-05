using Nickelony.IDEKit.IntelliSense.Signatures;

namespace Nickelony.IDEKit.AvalonEdit.IntelliSense.Signatures;

/// <summary>
/// Provides the current signature syntax preview for an editor presentation.
/// </summary>
public interface ISyntaxPreviewSource
{
	/// <summary>
	/// Gets the current syntax preview, or <see langword="null"/> when none is available.
	/// </summary>
	TextSignatureHelpInfo? GetSyntaxPreview();
}

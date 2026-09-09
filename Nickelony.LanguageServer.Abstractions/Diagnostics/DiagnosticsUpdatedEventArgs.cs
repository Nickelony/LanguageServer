using Nickelony.IDEKit.IntelliSense.Diagnostics;

namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Provides the data for the <see cref="ILanguageServerIntelliSenseProvider.DiagnosticsUpdated"/> event.
/// </summary>
/// <remarks>
/// The diagnostics list is an owned immutable snapshot that remains valid after the callback returns.
/// </remarks>
public sealed class DiagnosticsUpdatedEventArgs : EventArgs
{
	/// <summary>
	/// Initializes a new instance of the <see cref="DiagnosticsUpdatedEventArgs"/> class.
	/// </summary>
	/// <param name="filePath">The local file path of the document whose diagnostics are updated.</param>
	/// <param name="diagnostics">The updated diagnostics snapshot for the document.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="diagnostics"/> is <see langword="null"/>.
	/// </exception>
	public DiagnosticsUpdatedEventArgs(string filePath, IReadOnlyList<TextDiagnostic> diagnostics)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(diagnostics);

		FilePath = filePath;
		Diagnostics = diagnostics;
	}

	/// <summary>
	/// Gets the local file path of the document whose diagnostics are updated.
	/// </summary>
	public string FilePath { get; }

	/// <summary>
	/// Gets the updated diagnostics snapshot for the document.
	/// </summary>
	public IReadOnlyList<TextDiagnostic> Diagnostics { get; }
}

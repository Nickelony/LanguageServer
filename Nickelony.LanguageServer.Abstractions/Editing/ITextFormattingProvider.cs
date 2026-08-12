namespace Nickelony.LanguageServer.Abstractions.Editing;

/// <summary>
/// Produces formatting edits for a document.
/// </summary>
public interface ITextFormattingProvider
{
	/// <summary>
	/// Gets a value indicating whether document formatting is supported by the current ready session.
	/// </summary>
	/// <remarks>Returns <see langword="false"/> until the provider is available and the server advertises formatting.</remarks>
	bool SupportsFormatting { get; }

	/// <summary>
	/// Produces formatting edits for the supplied document.
	/// </summary>
	/// <param name="request">The document and formatting options.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The workspace edit to apply, or <see langword="null"/> when formatting is unsupported or no changes are available.</returns>
	Task<TextWorkspaceEdit?> FormatDocumentAsync(TextFormatRequest request, CancellationToken cancellationToken = default);
}

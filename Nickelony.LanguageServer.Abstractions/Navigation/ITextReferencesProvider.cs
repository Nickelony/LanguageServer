namespace Nickelony.LanguageServer.Abstractions.Navigation;

/// <summary>
/// Resolves symbol reference locations from the current document context.
/// </summary>
public interface ITextReferencesProvider
{
	/// <summary>
	/// Gets a value indicating whether reference lookup is supported by the current ready session.
	/// </summary>
	/// <remarks>Returns <see langword="false"/> until the provider is available and the server advertises references.</remarks>
	bool SupportsReferences { get; }

	/// <summary>
	/// Resolves reference locations for the supplied request.
	/// </summary>
	/// <param name="request">The current document and caret-position request.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The resolved reference locations, or an empty list when references are unsupported or none are available.</returns>
	Task<IReadOnlyList<TextReferenceLocation>> GetReferencesAsync(
		TextReferenceRequest request, CancellationToken cancellationToken = default);
}
